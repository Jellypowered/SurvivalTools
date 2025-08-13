echo off

REM Set output path
set "OUTDIR=F:\SteamLibrary\steamapps\common\RimWorld\Mods\SurvivalTools\1.6\Assemblies"

REM Only delete assemblies if they exist
if exist "%OUTDIR%\*.dll" (
    echo Removing existing DLLs...
    del /q "%OUTDIR%\*.dll"
) else (
    echo No existing DLLs to remove.
)

REM Build DLL to output directory
dotnet build .vscode -o "%OUTDIR%"

REM Only delete assemblies if they exist
if exist "%OUTDIR%\0Harmony.dll" (
    echo Removing existing Harmony DLL...
    del /q "%OUTDIR%\0Harmony.dll"
) else (
    echo No existing Harmony DLL to remove.
)