[CmdletBinding()]
param([Parameter(Mandatory)][string] $EditorRoot)

$ErrorActionPreference = 'Stop'
if (Get-Process UE4Editor -ErrorAction SilentlyContinue) {
    throw 'Close UT4 Editor before installing the plugin.'
}
$editorRootFull = [IO.Path]::GetFullPath($EditorRoot)
$modulesPath = Join-Path $editorRootFull 'Engine\Binaries\Win64\UE4Editor.modules'
if (-not (Test-Path -LiteralPath $modulesPath)) { throw "UT4 Editor was not found at $editorRootFull" }
$modules = Get-Content -Raw -LiteralPath $modulesPath | ConvertFrom-Json
if ($modules.CompatibleChangelist -ne 3525109) { throw "This plugin requires UT4 Editor API 3525109; the selected editor reports $($modules.CompatibleChangelist)." }
$source = Join-Path $PSScriptRoot 'EditorPlugin\Ut4ReconEditor'
if (-not (Test-Path -LiteralPath (Join-Path $source 'Binaries\Win64\UE4Editor-Ut4ReconEditor.dll'))) { throw 'The packaged editor plugin is incomplete.' }
$destination = Join-Path $editorRootFull 'Engine\Plugins\Marketplace\Ut4ReconEditor'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
Write-Host "Installed UT4 Recon Editor to $destination"
