#include <string.h>
#include <vcclr.h>

#using <mscorlib.dll>
#using <System.dll>

using namespace System;
using namespace System::IO;
using namespace System::Reflection;
using namespace System::Runtime::InteropServices;
using namespace System::Collections::Generic;
using namespace System::Collections::Concurrent;
using namespace System::Threading;

#define ARG_NULL         0
#define ARG_INT32        1
#define ARG_INT64        2
#define ARG_STRING       3
#define ARG_BOOL         4
#define ARG_DOUBLE       5
#define ARG_HANDLE       6
#define ARG_REF_HANDLE   7
#define ARG_FLOAT        8
#define ARG_DECIMAL      9

#pragma pack(push, 8)
struct NativeArg
{
    int type;
    long long intVal;
    double floatVal;
    const wchar_t* strVal;
};

struct NativeResult
{
    int type;
    long long intVal;
    double floatVal;
    wchar_t strBuf[4096];
    int errorCode;
};
#pragma pack(pop)

static gcroot<ConcurrentDictionary<int, Object^>^> g_handles;
static int g_nextHandle = 0;
static bool g_initialized = false;
static gcroot<String^> g_assemblyDir;
static gcroot<ConcurrentDictionary<String^, Assembly^>^> g_loadedAssemblies;
static gcroot<ConcurrentDictionary<String^, MethodInfo^>^> g_methodCache;

static wchar_t g_lastError[4096] = { 0 };

static void SetError(String^ msg)
{
    pin_ptr<const wchar_t> pinned = PtrToStringChars(msg);
    wcsncpy_s(g_lastError, 4096, pinned, _TRUNCATE);
}

static void SetError(Exception^ ex)
{
    String^ msg = ex->InnerException != nullptr ? ex->InnerException->ToString() : ex->ToString();
    SetError(msg);
}

static int StoreHandle(Object^ obj)
{
    if (obj == nullptr) return 0;
    int id = Interlocked::Increment(g_nextHandle);
    ((ConcurrentDictionary<int, Object^>^)g_handles)->TryAdd(id, obj);
    return id;
}

static Object^ GetHandle(int id)
{
    if (id == 0) return nullptr;
    Object^ val = nullptr;
    ((ConcurrentDictionary<int, Object^>^)g_handles)->TryGetValue(id, val);
    return val;
}

static Assembly^ ResolveAssembly(Object^ sender, ResolveEventArgs^ args)
{
    String^ name = (gcnew AssemblyName(args->Name))->Name;
    ConcurrentDictionary<String^, Assembly^>^ loaded = (ConcurrentDictionary<String^, Assembly^>^)g_loadedAssemblies;

    Assembly^ existing = nullptr;
    if (loaded->TryGetValue(name, existing))
        return existing;

    String^ path = Path::Combine((String^)g_assemblyDir, name + ".dll");
    if (File::Exists(path))
    {
        Assembly^ asmObj = Assembly::LoadFrom(path);
        loaded->TryAdd(name, asmObj);
        return asmObj;
    }
    return nullptr;
}

static Object^ ArgToManaged(const NativeArg& arg)
{
    switch (arg.type)
    {
        case ARG_NULL:       return nullptr;
        case ARG_INT32:      return static_cast<int>(arg.intVal);
        case ARG_INT64:      return static_cast<long long>(arg.intVal);
        case ARG_STRING:     return arg.strVal ? gcnew String(arg.strVal) : nullptr;
        case ARG_BOOL:       return arg.intVal != 0;
        case ARG_DOUBLE:     return arg.floatVal;
        case ARG_FLOAT:      return static_cast<float>(arg.floatVal);
        case ARG_HANDLE:
        case ARG_REF_HANDLE: return GetHandle(static_cast<int>(arg.intVal));
        case ARG_DECIMAL:    return static_cast<Decimal>(arg.floatVal);
        default:             return nullptr;
    }
}

static void WriteResult(Object^ obj, NativeResult* out)
{
    out->errorCode = 0;
    out->intVal = 0;
    out->floatVal = 0;
    out->strBuf[0] = L'\0';

    if (obj == nullptr)
    {
        out->type = ARG_NULL;
        return;
    }

    Type^ t = obj->GetType();

    if      (t == Int32::typeid)   { out->type = ARG_INT32;   out->intVal = safe_cast<int>(obj); }
    else if (t == Int64::typeid)   { out->type = ARG_INT64;   out->intVal = safe_cast<long long>(obj); }
    else if (t == Boolean::typeid) { out->type = ARG_BOOL;    out->intVal = safe_cast<bool>(obj) ? 1 : 0; }
    else if (t == Double::typeid)  { out->type = ARG_DOUBLE;  out->floatVal = safe_cast<double>(obj); }
    else if (t == Single::typeid)  { out->type = ARG_FLOAT;   out->floatVal = safe_cast<float>(obj); }
    else if (t == Decimal::typeid) { out->type = ARG_DECIMAL; out->floatVal = static_cast<double>(safe_cast<Decimal>(obj)); }
    else if (t == String::typeid)
    {
        out->type = ARG_STRING;
        pin_ptr<const wchar_t> pinned = PtrToStringChars(safe_cast<String^>(obj));
        wcsncpy_s(out->strBuf, 4096, pinned, _TRUNCATE);
    }
    else
    {
        out->type = ARG_HANDLE;
        out->intVal = StoreHandle(obj);
    }
}

static void WriteError(NativeResult* out, String^ msg)
{
    out->type = 0;
    out->intVal = 0;
    out->floatVal = 0;
    out->errorCode = -1;
    pin_ptr<const wchar_t> pinned = PtrToStringChars(msg);
    wcsncpy_s(out->strBuf, 4096, pinned, _TRUNCATE);
}

static void WriteError(NativeResult* out, Exception^ ex)
{
    String^ msg = ex->Message;
    if (TargetInvocationException^ tie = dynamic_cast<TargetInvocationException^>(ex))
        if (tie->InnerException != nullptr)
            msg = tie->InnerException->Message;
    WriteError(out, msg);
}

static String^ MakeMethodCacheKey(String^ typeName, String^ methodName, int argCount)
{
    return String::Concat(typeName, "::", methodName, "::", argCount.ToString());
}

static MethodInfo^ FindMethod(Type^ type, String^ methodName, int argCount, BindingFlags flags)
{
    String^ cacheKey = MakeMethodCacheKey(type->FullName, methodName, argCount);
    ConcurrentDictionary<String^, MethodInfo^>^ cache = (ConcurrentDictionary<String^, MethodInfo^>^)g_methodCache;

    MethodInfo^ cached = nullptr;
    if (cache->TryGetValue(cacheKey, cached))
        return cached;

    for each (MethodInfo^ m in type->GetMethods(flags))
    {
        if (m->Name == methodName && m->GetParameters()->Length == argCount)
        {
            cache->TryAdd(cacheKey, m);
            return m;
        }
    }
    return nullptr;
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_Init(const wchar_t* bridgeDllPath)
{
    try
    {
        if (g_initialized) return 0;

        g_handles = gcnew ConcurrentDictionary<int, Object^>();
        g_loadedAssemblies = gcnew ConcurrentDictionary<String^, Assembly^>();
        g_methodCache = gcnew ConcurrentDictionary<String^, MethodInfo^>();

        String^ path = gcnew String(bridgeDllPath);
        g_assemblyDir = Path::GetDirectoryName(path);
        AppDomain::CurrentDomain->AssemblyResolve += gcnew ResolveEventHandler(&ResolveAssembly);

        g_initialized = true;
        return 0;
    }
    catch (Exception^ ex) { SetError(ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_GetLastError(wchar_t* buffer, int maxLen)
{
    wcsncpy_s(buffer, maxLen, g_lastError, _TRUNCATE);
    return 0;
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_GetClrVersion(wchar_t* buffer, int maxLen)
{
    try
    {
        pin_ptr<const wchar_t> pinned = PtrToStringChars(Environment::Version->ToString());
        wcsncpy_s(buffer, maxLen, pinned, _TRUNCATE);
        return 0;
    }
    catch (Exception^ ex) { SetError(ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_Ping()
{
    return g_initialized ? 0 : -1;
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_LoadAssembly(const wchar_t* path, int* outAssemblyHandle)
{
    try
    {
        String^ fullPath = gcnew String(path);
        if (!Path::IsPathRooted(fullPath))
            fullPath = Path::Combine((String^)g_assemblyDir, fullPath);

        Assembly^ asmObj = Assembly::LoadFrom(fullPath);
        *outAssemblyHandle = StoreHandle(asmObj);

        ((ConcurrentDictionary<String^, Assembly^>^)g_loadedAssemblies)->TryAdd(asmObj->GetName()->Name, asmObj);
        return 0;
    }
    catch (Exception^ ex) { SetError(ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_ResolveType(int assemblyHandle, const wchar_t* typeName, int* outTypeHandle)
{
    try
    {
        Assembly^ asmObj = safe_cast<Assembly^>(GetHandle(assemblyHandle));
        if (asmObj == nullptr) { SetError(L"Invalid assembly handle"); return -1; }

        Type^ type = asmObj->GetType(gcnew String(typeName), true);
        *outTypeHandle = StoreHandle(type);
        return 0;
    }
    catch (Exception^ ex) { SetError(ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_CreateInstance(int typeHandle, const NativeArg* args, int argCount, int* outObjectHandle)
{
    try
    {
        Type^ type = safe_cast<Type^>(GetHandle(typeHandle));
        if (type == nullptr) { SetError(L"Invalid type handle"); return -1; }

        array<Object^>^ managedArgs = gcnew array<Object^>(argCount);
        for (int i = 0; i < argCount; i++)
            managedArgs[i] = ArgToManaged(args[i]);

        *outObjectHandle = StoreHandle(Activator::CreateInstance(type, managedArgs));
        return 0;
    }
    catch (Exception^ ex) { SetError(ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_InvokeStatic(
    int typeHandle, const wchar_t* methodName,
    const NativeArg* args, int argCount, NativeResult* outResult)
{
    try
    {
        Type^ type = safe_cast<Type^>(GetHandle(typeHandle));
        if (type == nullptr) { WriteError(outResult, L"Invalid type handle"); return -1; }

        array<Object^>^ managedArgs = gcnew array<Object^>(argCount);
        for (int i = 0; i < argCount; i++)
            managedArgs[i] = ArgToManaged(args[i]);

        MethodInfo^ method = FindMethod(type, gcnew String(methodName), argCount, BindingFlags::Public | BindingFlags::Static);
        if (method == nullptr) { WriteError(outResult, "Method not found: " + gcnew String(methodName)); return -1; }

        WriteResult(method->Invoke(nullptr, managedArgs), outResult);
        return 0;
    }
    catch (Exception^ ex) { WriteError(outResult, ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_InvokeInstance(
    int objectHandle, const wchar_t* methodName,
    const NativeArg* args, int argCount,
    NativeResult* outResult, NativeResult* outRefResults)
{
    try
    {
        Object^ target = GetHandle(objectHandle);
        if (target == nullptr) { WriteError(outResult, L"Invalid object handle"); return -1; }

        Type^ type = target->GetType();
        String^ mName = gcnew String(methodName);

        array<Object^>^ managedArgs = gcnew array<Object^>(argCount);
        for (int i = 0; i < argCount; i++)
            managedArgs[i] = ArgToManaged(args[i]);

        MethodInfo^ method = FindMethod(type, mName, argCount, BindingFlags::Public | BindingFlags::Instance);
        if (method == nullptr) { WriteError(outResult, "Method not found: " + mName); return -1; }

        WriteResult(method->Invoke(target, managedArgs), outResult);

        if (outRefResults != nullptr)
        {
            for (int i = 0; i < argCount; i++)
            {
                if (args[i].type == ARG_REF_HANDLE)
                    WriteResult(managedArgs[i], &outRefResults[i]);
                else
                {
                    outRefResults[i].type = -1;
                    outRefResults[i].errorCode = 0;
                }
            }
        }

        return 0;
    }
    catch (Exception^ ex) { WriteError(outResult, ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_GetProperty(int objectHandle, const wchar_t* propertyName, NativeResult* outResult)
{
    try
    {
        Object^ target = GetHandle(objectHandle);
        if (target == nullptr) { WriteError(outResult, L"Invalid object handle"); return -1; }

        PropertyInfo^ prop = target->GetType()->GetProperty(gcnew String(propertyName), BindingFlags::Public | BindingFlags::Instance);
        if (prop == nullptr) { WriteError(outResult, "Property not found: " + gcnew String(propertyName)); return -1; }

        WriteResult(prop->GetValue(target), outResult);
        return 0;
    }
    catch (Exception^ ex) { WriteError(outResult, ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_SetProperty(int objectHandle, const wchar_t* propertyName, const NativeArg* value)
{
    try
    {
        Object^ target = GetHandle(objectHandle);
        if (target == nullptr) { SetError(L"Invalid object handle"); return -1; }

        PropertyInfo^ prop = target->GetType()->GetProperty(gcnew String(propertyName), BindingFlags::Public | BindingFlags::Instance);
        if (prop == nullptr) { SetError("Property not found: " + gcnew String(propertyName)); return -1; }

        prop->SetValue(target, ArgToManaged(*value));
        return 0;
    }
    catch (Exception^ ex) { SetError(ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_ReleaseHandle(int handle)
{
    try
    {
        if (handle == 0) return 0;
        Object^ obj = nullptr;
        if (((ConcurrentDictionary<int, Object^>^)g_handles)->TryRemove(handle, obj))
        {
            IDisposable^ disposable = dynamic_cast<IDisposable^>(obj);
            if (disposable != nullptr)
                try { delete disposable; } catch (Exception^) { }
        }
        return 0;
    }
    catch (Exception^ ex) { SetError(ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_GetTypeName(int objectHandle, wchar_t* buffer, int maxLen)
{
    try
    {
        Object^ obj = GetHandle(objectHandle);
        if (obj == nullptr) { wcsncpy_s(buffer, maxLen, L"null", _TRUNCATE); return 0; }

        pin_ptr<const wchar_t> pinned = PtrToStringChars(obj->GetType()->FullName);
        wcsncpy_s(buffer, maxLen, pinned, _TRUNCATE);
        return 0;
    }
    catch (Exception^ ex) { SetError(ex); return -1; }
}

extern "C" __declspec(dllexport) int __stdcall NetFxProxy_HandleCount()
{
    ConcurrentDictionary<int, Object^>^ handles = (ConcurrentDictionary<int, Object^>^)g_handles;
    return handles != nullptr ? handles->Count : 0;
}
