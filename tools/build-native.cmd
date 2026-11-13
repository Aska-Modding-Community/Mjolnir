@echo off
rem Builds src\Mjolnir.Native (version.dll) with MSVC x64 + Ninja.
rem Usage: tools\build-native.cmd [x64-release|x64-debug]
setlocal
set "PRESET=%~1"
if "%PRESET%"=="" set "PRESET=x64-release"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (echo vswhere.exe not found & exit /b 1)
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%i"
if not defined VSDIR (echo MSVC x64 build tools not found & exit /b 1)

rem vcvarsall locates vswhere through PATH.
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

rem CMake/Ninja from PATH, falling back to the per-user copies.
where cmake >nul 2>nul || set "PATH=%USERPROFILE%\.cmake-deps\cmake\win\x64\bin;%PATH%"
where ninja >nul 2>nul || set "PATH=%USERPROFILE%\.cmake-deps\ninja\win\x64;%PATH%"

cd /d "%~dp0..\src\Mjolnir.Native" || exit /b 1
cmake --preset %PRESET% || exit /b 1
cmake --build --preset %PRESET% || exit /b 1
