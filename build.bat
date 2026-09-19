@echo off
setlocal
cd /d "%~dp0"

set "VERSION=0.9.2

echo Building Ceprkac v%VERSION%...
echo.

echo Step 1: Publishing application (framework-dependent net48, small installer)...
dotnet publish Ceprkac.csproj -c Release -o bin\publish
if errorlevel 1 goto error

echo.
echo Step 2: Copying icon and x64 WebView2 loader...
copy /Y Ceprkac.ico bin\publish\
if exist bin\publish\runtimes\win-x64\native\WebView2Loader.dll copy /Y bin\publish\runtimes\win-x64\native\WebView2Loader.dll bin\publish\

echo.
echo Step 3: Building installer...
REM Inno Setup outputs to releases\%VERSION%\ (defined in Ceprkac.iss OutputDir)
set "ISCC=C:\Program Files\Inno Setup 7\ISCC.exe"
if not exist "%ISCC%" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
"%ISCC%" Ceprkac.iss
if errorlevel 1 goto error

echo.
echo Build complete!
echo Output: releases\%VERSION%\Ceprkac-%VERSION%-Setup.exe
goto end

:error
echo.
echo BUILD FAILED!
exit /b 1

:end
