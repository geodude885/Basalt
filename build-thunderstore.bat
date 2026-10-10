@echo off
setlocal enabledelayedexpansion
REM Thunderstore package builder. Reads name/version from Version.props.

echo Syncing version from Version.props...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0UpdateVersion.ps1"
if errorlevel 1 ( echo Version update failed! & pause & exit /b 1 )

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content '%~dp0Version.props')).Project.PropertyGroup.ModVersion"`) do set MOD_VERSION=%%v
for /f "usebackq delims=" %%n in (`powershell -NoProfile -Command "([xml](Get-Content '%~dp0Version.props')).Project.PropertyGroup.ModName"`) do set MOD_NAME=%%n
for /f "usebackq delims=" %%a in (`powershell -NoProfile -Command "([xml](Get-Content '%~dp0Version.props')).Project.PropertyGroup.ModAuthor"`) do set MOD_AUTHOR=%%a

echo.
echo Building %MOD_NAME% v%MOD_VERSION% (Release)...
dotnet build "%~dp0%MOD_NAME%\%MOD_NAME%.csproj" -c Release
if errorlevel 1 ( echo Build failed! & pause & exit /b 1 )

REM Never ship a package that fails its own tests.
echo.
echo Running tests...
dotnet test "%~dp0%MOD_NAME%.slnx" --nologo -v q
if errorlevel 1 ( echo Tests failed - not packaging! & pause & exit /b 1 )

echo.
echo Creating Thunderstore package...
if exist "%~dp0ThunderstorePackage" rmdir /s /q "%~dp0ThunderstorePackage"
mkdir "%~dp0ThunderstorePackage\Mods"

set "BUILT=%~dp0%MOD_NAME%\bin\Release\net6.0\%MOD_NAME%.dll"
if not exist "%BUILT%" ( echo Built DLL not found: %BUILT% & pause & exit /b 1 )
copy "%BUILT%" "%~dp0ThunderstorePackage\Mods\" >nul
REM The song decoders the mod uses, where MelonLoader loads libraries from.
mkdir "%~dp0ThunderstorePackage\UserLibs"
copy "%USERPROFILE%\.nuget\packages\nlayer\1.16.0\lib\netstandard2.0\NLayer.dll" "%~dp0ThunderstorePackage\UserLibs\" >nul
copy "%USERPROFILE%\.nuget\packages\nvorbis\0.10.5\lib\netstandard2.0\NVorbis.dll" "%~dp0ThunderstorePackage\UserLibs\" >nul
copy "%~dp0THIRD-PARTY-NOTICES.md" "%~dp0ThunderstorePackage\" >nul
copy "%~dp0Thunderstore\README.md" "%~dp0ThunderstorePackage\" >nul
REM manifest.json is copied, not regenerated: UpdateVersion.ps1 has already stamped the version
REM into it, and a second generated copy here would let the two drift apart.
copy "%~dp0Thunderstore\manifest.json" "%~dp0ThunderstorePackage\" >nul
copy "%~dp0Thunderstore\CHANGELOG.md" "%~dp0ThunderstorePackage\" >nul
if exist "%~dp0Thunderstore\icon.png" (
	copy "%~dp0Thunderstore\icon.png" "%~dp0ThunderstorePackage\" >nul
	echo [OK] Icon included
) else (
	echo [WARNING] icon.png not found. Add a 256x256 PNG at Thunderstore\icon.png before publishing.
)

set ZIP_NAME=%MOD_AUTHOR%-%MOD_NAME%-%MOD_VERSION%.zip
if exist "%~dp0%ZIP_NAME%" del "%~dp0%ZIP_NAME%"
powershell -NoProfile -Command "Compress-Archive -Path '%~dp0ThunderstorePackage\*' -DestinationPath '%~dp0%ZIP_NAME%'"
if errorlevel 1 ( echo Packaging failed! & pause & exit /b 1 )

echo.
echo ========================================
echo Package created: %ZIP_NAME%
echo ========================================
pause
