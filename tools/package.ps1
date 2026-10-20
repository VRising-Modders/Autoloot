<#
.SYNOPSIS
  Build the Autoloot Thunderstore package: packaging/out/Autoloot-<version>.zip.
.DESCRIPTION
  Builds src/Autoloot in Release with the version from packaging/thunderstore/manifest.json, writes
  progenitor.json from the built DLL with Progenitor's publish checker, assembles the zip (manifest,
  README, CHANGELOG, icon, LICENSE, Autoloot.dll, progenitor.json at the root, '/' separators) and
  runs the checker on the finished zip.

  -ProgenitorRepo is a checkout of github.com/VRising-Modders/Progenitor (for tools/publish-check.ps1).
  -ProgenitorDll is the Progenitor.dll to compile against: use the one from the Progenitor release
  the manifest depends on (BepInEx/progenitor-core/Progenitor.dll in that release's zip).
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$ProgenitorRepo,
  [Parameter(Mandatory = $true)][string]$ProgenitorDll
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$ts = Join-Path $repo "packaging\thunderstore"
$out = Join-Path $repo "packaging\out"
$utf8 = New-Object System.Text.UTF8Encoding $false
$manifestText = [System.IO.File]::ReadAllText((Join-Path $ts "manifest.json"), $utf8)
$manifest = $manifestText | ConvertFrom-Json
$version = [string]$manifest.version_number
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "manifest version_number '$version' is not major.minor.patch" }

$proj = Join-Path $repo "src\Autoloot\Autoloot.csproj"
& dotnet build $proj -c Release -nologo -v q "-p:ReleaseVersion=$version" "-p:ProgenitorDll=$ProgenitorDll"
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$dll = Join-Path $repo "src\Autoloot\bin\Release\net6.0\Autoloot.dll"

$stage = Join-Path $out "_stage"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
[System.IO.File]::WriteAllText((Join-Path $stage "manifest.json"), $manifestText, $utf8)
foreach ($f in @("README.md", "CHANGELOG.md", "icon.png")) { Copy-Item -LiteralPath (Join-Path $ts $f) -Destination $stage }
Copy-Item -LiteralPath (Join-Path $repo "LICENSE") -Destination $stage
Copy-Item -LiteralPath $dll -Destination $stage

$check = Join-Path $ProgenitorRepo "tools\publish-check.ps1"
& powershell -NoProfile -ExecutionPolicy Bypass -File $check -Dll (Join-Path $stage "Autoloot.dll") -WriteProgenitorJson (Join-Path $stage "progenitor.json") -HotLoadable
if ($LASTEXITCODE -ne 0) { throw "progenitor.json was not written" }

$zip = Join-Path $out ("Autoloot-" + $version + ".zip")
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($file in @(Get-ChildItem -LiteralPath $stage -File | Sort-Object Name)) {
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $file.Name, [System.IO.Compression.CompressionLevel]::Optimal)
  }
} finally { $archive.Dispose() }
Remove-Item -LiteralPath $stage -Recurse -Force

& powershell -NoProfile -ExecutionPolicy Bypass -File $check -Package $zip
if ($LASTEXITCODE -ne 0) { throw "the package check failed" }
Write-Host "wrote $zip"
