[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PayloadRoot,
    [string] $OutputDirectory = '',
    [string] $InnoCompiler = ''
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$payload = [IO.Path]::GetFullPath($PayloadRoot)
if (-not (Test-Path -LiteralPath $payload -PathType Container)) {
    throw "Portable release payload was not found: $payload"
}

$required = @(
    'VERSION.txt',
    'LICENSE',
    'PROTOTYPE-README.md',
    'cli\Ut4Recon.Cli.exe',
    'EditorPlugin\Ut4ReconEditor\Ut4ReconEditor.uplugin',
    'EditorPlugin\Ut4ReconEditor\Binaries\Win64\UE4Editor-Ut4ReconEditor.dll'
)
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $relative) -PathType Leaf)) {
        throw "Portable release payload is incomplete; missing $relative"
    }
}

$version = (Get-Content -Raw -LiteralPath (Join-Path $payload 'VERSION.txt')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "VERSION.txt does not contain a supported semantic version: $version"
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\release'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null

if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
    $InnoCompiler = $candidates | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Inno Setup 6 was not found. Install JRSoftware.InnoSetup with winget or pass -InnoCompiler.'
}

$script = Join-Path $PSScriptRoot 'installer\UT4Recon.iss'
& $InnoCompiler "/DPayloadRoot=$payload" "/DAppVersion=$version" "/DOutputDir=$output" $script
if ($LASTEXITCODE) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

$installer = Join-Path $output "UT4Recon-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Expected installer was not produced: $installer"
}

$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
Write-Host "Installer: $installer"
Write-Host "SHA-256: $hash"
