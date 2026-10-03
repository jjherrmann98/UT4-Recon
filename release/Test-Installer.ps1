[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Installer,
    [Parameter(Mandatory)][string] $PayloadRoot,
    [string] $TestRoot = ''
)

$ErrorActionPreference = 'Stop'
$installerFull = [IO.Path]::GetFullPath($Installer)
$payload = [IO.Path]::GetFullPath($PayloadRoot)
if (-not $TestRoot) {
    $TestRoot = Join-Path ([IO.Path]::GetTempPath()) ('ut4recon-installer-test-' + [guid]::NewGuid().ToString('N'))
}
$test = [IO.Path]::GetFullPath($TestRoot)
if (Test-Path -LiteralPath $test) { throw "Test root already exists: $test" }

$app = Join-Path $test 'app'
$editor = Join-Path $test 'editor'
$modules = Join-Path $editor 'Engine\Binaries\Win64\UE4Editor.modules'
New-Item -ItemType Directory -Path (Split-Path $modules) -Force | Out-Null
Set-Content -LiteralPath $modules -Encoding ascii -Value '{"CompatibleChangelist":3525109}'

function Invoke-Process([string] $FilePath, [string[]] $Arguments, [int[]] $ExpectedExitCodes = @(0)) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -notin $ExpectedExitCodes) {
        throw "$FilePath exited with $($process.ExitCode); expected $($ExpectedExitCodes -join ', ')."
    }
    return $process.ExitCode
}

try {
    Invoke-Process $installerFull @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        "/DIR=$app", "/EDITORROOT=$editor", "/LOG=$(Join-Path $test 'install.log')"
    ) | Out-Null

    $installedCli = Join-Path $app 'cli\Ut4Recon.Cli.exe'
    $installedPlugin = Join-Path $editor 'Engine\Plugins\Marketplace\Ut4ReconEditor\Binaries\Win64\UE4Editor-Ut4ReconEditor.dll'
    foreach ($path in @($installedCli, $installedPlugin, (Join-Path $app 'unins000.exe'))) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Installed file is missing: $path" }
    }

    foreach ($sourceFile in Get-ChildItem -LiteralPath $payload -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($payload, $sourceFile.FullName)
        $installedFile = Join-Path $app $relative
        if (-not (Test-Path -LiteralPath $installedFile -PathType Leaf)) {
            throw "Application payload file is missing after install: $relative"
        }
        if ((Get-FileHash -LiteralPath $sourceFile.FullName).Hash -ne (Get-FileHash -LiteralPath $installedFile).Hash) {
            throw "Application payload file changed during install: $relative"
        }
    }

    $sourcePlugin = Join-Path $payload 'EditorPlugin\Ut4ReconEditor\Binaries\Win64\UE4Editor-Ut4ReconEditor.dll'
    if ((Get-FileHash -LiteralPath $sourcePlugin).Hash -ne (Get-FileHash -LiteralPath $installedPlugin).Hash) {
        throw 'Installed editor plugin does not match the release payload.'
    }
    Invoke-Process $installedCli @('--help') | Out-Null

    $badEditor = Join-Path $test 'wrong-editor'
    $badModules = Join-Path $badEditor 'Engine\Binaries\Win64\UE4Editor.modules'
    New-Item -ItemType Directory -Path (Split-Path $badModules) -Force | Out-Null
    Set-Content -LiteralPath $badModules -Encoding ascii -Value '{"CompatibleChangelist":1234567}'
    $badApp = Join-Path $test 'bad-app'
    $badExit = Invoke-Process $installerFull @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        "/DIR=$badApp", "/EDITORROOT=$badEditor", "/LOG=$(Join-Path $test 'wrong-version.log')"
    ) @(1, 2, 5)
    if ($badExit -eq 0 -or (Test-Path -LiteralPath (Join-Path $badApp 'cli\Ut4Recon.Cli.exe'))) {
        throw 'Installer did not reject an incompatible editor.'
    }

    $fakeEditorExecutable = Join-Path $test 'UE4Editor.exe'
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\ping.exe') -Destination $fakeEditorExecutable
    $fakeEditorProcess = Start-Process -FilePath $fakeEditorExecutable -ArgumentList @('-t', '127.0.0.1') -PassThru -WindowStyle Hidden
    try {
        Start-Sleep -Milliseconds 500
        $runningApp = Join-Path $test 'running-editor-app'
        $runningExit = Invoke-Process $installerFull @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
            "/DIR=$runningApp", "/EDITORROOT=$editor", "/LOG=$(Join-Path $test 'running-editor.log')"
        ) @(1, 2, 5, 7)
        if ($runningExit -eq 0 -or (Test-Path -LiteralPath (Join-Path $runningApp 'cli\Ut4Recon.Cli.exe'))) {
            throw 'Installer did not refuse to update files while UE4Editor.exe was running.'
        }
    }
    finally {
        Stop-Process -Id $fakeEditorProcess.Id -Force -ErrorAction SilentlyContinue
    }

    Invoke-Process (Join-Path $app 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') | Out-Null
    if (Test-Path -LiteralPath $installedPlugin) { throw 'Uninstall left the editor plugin behind.' }
    if (Test-Path -LiteralPath $installedCli) { throw 'Uninstall left the application payload behind.' }
    if (-not (Test-Path -LiteralPath $modules)) { throw 'Uninstall modified files owned by the editor.' }

    Write-Host "Installer smoke test passed: $test"
}
catch {
    Write-Host "Installer smoke test artifacts retained at: $test"
    throw
}

