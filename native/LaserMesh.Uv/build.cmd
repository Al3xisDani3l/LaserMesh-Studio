@echo off
setlocal
if defined VCToolsInstallDir goto compile
for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSROOT=%%i"
if not exist "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" exit /b 2
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat"
:compile
if errorlevel 1 exit /b 1
if not exist "%~dp0bin" mkdir "%~dp0bin"
pushd "%~dp0bin"
cl /nologo /O2 /std:c++17 /EHsc /MT /LD /I"%~dp0..\..\external\xatlas\source\xatlas" "%~dp0bridge.cpp" "%~dp0..\..\external\xatlas\source\xatlas\xatlas.cpp" /Fe:LaserMesh.Uv.Native.dll
set code=%errorlevel%
popd
exit /b %code%
