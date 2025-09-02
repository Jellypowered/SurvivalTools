@echo off
setlocal

REM ================================
REM Config
REM ================================
set "CURRENTVER=1.6"
set "CONFIG=Release"

REM Local mirror output (second output)
set "BASE=.\%CURRENTVER%\Assemblies"

REM Hardcoded fallback (your known-good location)
set "FALLBACK_MODS=F:\SteamLibrary\steamapps\common\RimWorld\Mods"

REM ================================
REM RimWorld Mods path auto-detect (multi-library aware, brace-safe)
REM ================================
set "MODS_PATH="
set "TMPPS=%TEMP%\rw_findmods_%RANDOM%.ps1"

REM Build a small PowerShell script that prints the Mods path and exits
> "%TMPPS%" echo $steam = (Get-ItemProperty -Path HKCU:\Software\Valve\Steam -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
>>"%TMPPS%" echo if ($steam) {
>>"%TMPPS%" echo ^    $paths = @($steam)
>>"%TMPPS%" echo ^    $default = Join-Path $steam 'steamapps\common\RimWorld\Mods'
>>"%TMPPS%" echo ^    if (Test-Path $default) { $default; exit }
>>"%TMPPS%" echo ^    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
>>"%TMPPS%" echo ^    if (Test-Path $vdf) {
>>"%TMPPS%" echo ^        $txt = Get-Content -Raw $vdf
>>"%TMPPS%" echo ^        foreach ($line in $txt -split "`n") {
>>"%TMPPS%" echo ^            if ($line -match '"path"\s+"([^"]+)"') { $paths += $Matches[1] }
>>"%TMPPS%" echo ^        }
>>"%TMPPS%" echo ^    }
>>"%TMPPS%" echo ^    foreach ($p in $paths) {
>>"%TMPPS%" echo ^        $mods = Join-Path $p 'steamapps\common\RimWorld\Mods'
>>"%TMPPS%" echo ^        if (Test-Path $mods) { $mods; exit }
>>"%TMPPS%" echo ^    }
>>"%TMPPS%" echo }

for /f "usebackq delims=" %%P in (`powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%TMPPS%"`) do (
    set "MODS_PATH=%%P"
)

del /q "%TMPPS%" >nul 2>&1

if not defined MODS_PATH (
    set "MODS_PATH=%FALLBACK_MODS%"
)

echo RimWorld Mods path is: "%MODS_PATH%"

REM ================================
REM Read mod name from About/About.xml
REM ================================
set "MODNAME="
for /f "delims=" %%a in ('powershell -NoLogo -NoProfile -Command ^
  "[xml]$about=Get-Content '.\About\About.xml'; $about.ModMetaData.name"') do (
    set "MODNAME=%%a"
)
if not defined MODNAME (
    echo Failed to read mod name from About\About.xml. Aborting.
    exit /b 1
)
echo Mod name is: %MODNAME%

REM Destination root = Mods\<ModName>\
set "MODROOT=%MODS_PATH%\%MODNAME%"
set "MODVER=%MODROOT%\%CURRENTVER%"
set "OUTDIR=%MODVER%\Assemblies"

REM ================================
REM Prepare output folders
REM ================================
if exist "%OUTDIR%" (
    echo Removing existing output directory: "%OUTDIR%"
    rmdir /s /q "%OUTDIR%"
)
if not exist "%OUTDIR%" (
    echo Creating output dir: "%OUTDIR%"
    mkdir "%OUTDIR%"
)
if not exist "%BASE%" (
    echo Creating local mirror dir: "%BASE%"
    mkdir "%BASE%"
)

REM ================================
REM Clean old DLLs
REM ================================
if exist "%OUTDIR%\*.dll" (
    echo Removing existing DLLs in OUTDIR...
    del /q "%OUTDIR%\*.dll"
) else (
    echo No existing DLLs in OUTDIR.
)

if exist "%BASE%\*.dll" (
    echo Removing existing DLLs in BASE mirror...
    del /q "%BASE%\*.dll"
) else (
    echo No existing DLLs in BASE mirror.
)

REM ================================
REM Build
REM ================================
echo Building %MODNAME%...
dotnet build .vscode -c Release -o "%OUTDIR%"
if errorlevel 1 (
    echo Build failed. Aborting.
    exit /b %errorlevel%
)

REM Remove bundled Harmony if it appears
if exist "%OUTDIR%\0Harmony.dll" (
    echo Removing bundled Harmony DLL...
    del /q "%OUTDIR%\0Harmony.dll"
)

REM Remove base bundled Harmony if it appears
if exist "%BASE%\0Harmony.dll" (
    echo Removing base bundled Harmony DLL...
    del /q "%BASE%\0Harmony.dll"
)

REM Mirror build to BASE
echo Mirroring build to BASE...
xcopy /Y /E "%OUTDIR%\*" "%BASE%\" >nul

setlocal EnableExtensions EnableDelayedExpansion

REM ================================
REM Copy assets into Mod folder
REM ================================
for %%V in (1.0 1.1 1.2 1.3 1.4 1.5 1.6) do if exist "%%V" (
    echo Copying version folder %%V...
    xcopy "%%V" "!MODROOT!\%%V" /E /I /Y >nul
)

if exist "About" (
    echo Copying About...
    xcopy "About" "!MODROOT!\About" /E /I /Y >nul
)

if exist "Assemblies" (
    echo Copying root Assemblies...
    xcopy "Assemblies" "!MODROOT!\Assemblies" /E /I /Y >nul
)

if exist "LoadFolders.xml" (
    echo Copying LoadFolders.xml...
    copy /Y "LoadFolders.xml" "!MODROOT!\LoadFolders.xml" >nul
)

for %%D in ("Languages" "Textures" "Defs" "Patches") do if exist "%%~fD" (
    echo Copying %%~nxD to "!MODROOT!\%%~nxD" ...
    xcopy "%%~fD" "!MODROOT!\%%~nxD" /E /I /Y >nul
)

setlocal EnableExtensions EnableDelayedExpansion

set "ZIPNAME=!MODNAME!_!CURRENTVER!.zip"
set "ZIPSOURCE=!MODROOT!"
set "ZIPDEST=%CD%\!ZIPNAME!"

if exist "!ZIPDEST!" (
    echo Removing existing ZIP...
    del /q "!ZIPDEST!"
)

echo Creating ZIP archive (tar)...
tar.exe -a -cf "!ZIPDEST!" -C "!ZIPSOURCE!" .

if exist "!ZIPDEST!" (
    echo ZIP created at: "!ZIPDEST!"
) else (
    echo ZIP creation failed.
    exit /b 1
)

echo Done.
endlocal
