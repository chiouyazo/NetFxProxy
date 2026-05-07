@echo off
setlocal

set VCVARS=
for /f "tokens=*" %%i in ('"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath 2^>nul') do set VCVARS=%%i\VC\Auxiliary\Build\vcvarsall.bat

if not exist "%VCVARS%" (
    echo ERROR: vcvarsall.bat not found. Install Visual Studio with C++ workload.
    exit /b 1
)

call "%VCVARS%" x64

set OUT_DIR=%~dp0

cl /clr /nologo /EHa /LD /MD /O2 ^
    "%~dp0NetFxProxy.cpp" ^
    /Fe:"%OUT_DIR%NetFxProxy.dll" ^
    /Fo:"%OUT_DIR%NetFxProxy.obj" ^
    /link /DLL

if %ERRORLEVEL% == 0 (
    echo BUILD SUCCEEDED: %OUT_DIR%NetFxProxy.dll
) else (
    echo BUILD FAILED
    exit /b 1
)
