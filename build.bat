@echo off
rem Builds SD2MinimalFix.dll from SD2MinimalFix.cs with the C# compiler that ships with Windows.
rem Everything is taken from this folder: the source and the reference DLLs in refs\ (the game is not needed).
rem
rem   build.bat           build SD2MinimalFix.dll here
rem   build.bat install   build and copy it into the game
rem   build.bat refs      refresh refs\ from the game (after a game or BepInEx update)
setlocal
cd /d "%~dp0"

rem ---- game folder: only used by "install" and "refs"
set GAME=D:\SteamLibrary\steamapps\common\Soundodger 2

if /i "%~1"=="refs" goto :refs

set R=%~dp0refs
if not exist "%R%\Assembly-CSharp.dll" (
    echo refs\ is missing. Run:  build.bat refs
    pause
    exit /b 1
)
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REFS=-r:"%R%\mscorlib.dll" -r:"%R%\System.dll" -r:"%R%\System.Core.dll" -r:"%R%\netstandard.dll" -r:"%R%\UnityEngine.dll" -r:"%R%\UnityEngine.CoreModule.dll" -r:"%R%\UnityEngine.AudioModule.dll" -r:"%R%\UnityEngine.IMGUIModule.dll" -r:"%R%\Assembly-CSharp.dll" -r:"%R%\BepInEx.dll" -r:"%R%\0Harmony.dll"

"%CSC%" -nologo -noconfig -nostdlib -target:library -optimize -out:SD2MinimalFix.dll %REFS% SD2MinimalFix.cs
if errorlevel 1 (
    echo.
    echo BUILD FAILED - see the lines above:  SD2MinimalFix.cs^(line,column^): error ...
    pause
    exit /b 1
)
echo.
echo Built: %~dp0SD2MinimalFix.dll

if /i "%~1"=="install" (
    copy /y SD2MinimalFix.dll "%GAME%\BepInEx\plugins\SD2MinimalFix\SD2MinimalFix.dll" >nul && echo Installed into the game. || echo Could not copy: close the game and try again.
)
pause
exit /b 0

:refs
set M=%GAME%\Soundodger 2_Data\Managed
set C=%GAME%\BepInEx\core
if not exist refs mkdir refs
for %%f in (mscorlib System System.Core netstandard UnityEngine UnityEngine.CoreModule UnityEngine.AudioModule UnityEngine.IMGUIModule Assembly-CSharp) do copy /y "%M%\%%f.dll" refs\ >nul || echo Missing: %M%\%%f.dll
for %%f in (BepInEx 0Harmony) do copy /y "%C%\%%f.dll" refs\ >nul || echo Missing: %C%\%%f.dll
echo refs\ refreshed from the game.
pause
