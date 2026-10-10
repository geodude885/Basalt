# Syncs version/author/name from Version.props into ModEntry.cs and the Thunderstore manifest.
param([switch]$Build)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

[xml]$versionProps = Get-Content (Join-Path $root "Version.props")
$version = $versionProps.Project.PropertyGroup.ModVersion
$author  = $versionProps.Project.PropertyGroup.ModAuthor
$modName = $versionProps.Project.PropertyGroup.ModName

Write-Host "Mod:     $modName" -ForegroundColor Cyan
Write-Host "Version: $version" -ForegroundColor Cyan
Write-Host "Author:  $author"  -ForegroundColor Cyan

# ModEntry.cs - update the MelonInfo attribute (name, version, author).
# Each replacement is verified: a silently-missed regex would ship a package whose in-game
# version disagrees with its manifest, which is worse than failing here.
$modCsPath = Join-Path $root "$modName\ModEntry.cs"
if (-not (Test-Path $modCsPath)) { throw "Entry point not found: $modCsPath" }
$modCs = Get-Content $modCsPath -Raw
$melonInfo = "MelonInfo\(typeof\($modName\.ModEntry\),\s*"".*?"",\s*"".*?"",\s*"".*?""\)"
if ($modCs -notmatch $melonInfo) { throw "MelonInfo attribute not found in $modCsPath" }
$modCs = $modCs -replace $melonInfo, "MelonInfo(typeof($modName.ModEntry), `"$modName`", `"$version`", `"$author`")"
Set-Content $modCsPath -Value $modCs -NoNewline
Write-Host "[OK] Updated ModEntry.cs" -ForegroundColor Green

# The dev tools carry the same version.
$devCsPath = Join-Path $root "$modName.DevTools\DevToolsEntry.cs"
$devCs = Get-Content $devCsPath -Raw
$devInfo = "MelonInfo\(typeof\($modName\.DevTools\.DevToolsEntry\),\s*"".*?"",\s*"".*?"",\s*"".*?""\)"
if ($devCs -notmatch $devInfo) { throw "MelonInfo attribute not found in $devCsPath" }
$devCs = $devCs -replace $devInfo, "MelonInfo(typeof($modName.DevTools.DevToolsEntry), `"$modName.DevTools`", `"$version`", `"$author`")"
Set-Content $devCsPath -Value $devCs -NoNewline
Write-Host "[OK] Updated DevToolsEntry.cs" -ForegroundColor Green

# Thunderstore manifest — update name, version, author
$manifestPath = Join-Path $root "Thunderstore\manifest.json"
$manifest = Get-Content $manifestPath -Raw
$manifest = $manifest -replace '"name"\s*:\s*"[^"]*"', "`"name`": `"$modName`""
$manifest = $manifest -replace '"version_number"\s*:\s*"[^"]*"', "`"version_number`": `"$version`""
Set-Content $manifestPath -Value $manifest -NoNewline
Write-Host "[OK] Updated manifest.json" -ForegroundColor Green

Write-Host ""
Write-Host "Done. Remember to update Thunderstore\CHANGELOG.md manually." -ForegroundColor Yellow

if ($Build) {
    dotnet build (Join-Path $root "$modName\$modName.csproj") -c Release
}
