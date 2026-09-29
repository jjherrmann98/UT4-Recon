[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $SourceRoot,
    [Parameter(Mandatory)][string] $EditorRoot,
    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\..\artifacts\ut4-native-sdk'),
    [switch] $Install
)

$ErrorActionPreference = 'Stop'

function Assert-Path([string] $Path, [string] $Description) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description was not found at $Path"
    }
}

$sourceVersionPath = Join-Path $SourceRoot 'Engine\Build\Build.version'
$editorModulesPath = Join-Path $EditorRoot 'Engine\Binaries\Win64\UE4Editor.modules'
$editorBin = Join-Path $EditorRoot 'Engine\Binaries\Win64'
Assert-Path $sourceVersionPath 'Recovered source version file'
Assert-Path $editorModulesPath 'Installed editor module manifest'

$sourceVersion = Get-Content -Raw -LiteralPath $sourceVersionPath | ConvertFrom-Json
$editorModules = Get-Content -Raw -LiteralPath $editorModulesPath | ConvertFrom-Json
if ($sourceVersion.MajorVersion -ne 4 -or $sourceVersion.MinorVersion -ne 15) {
    throw "The recovered headers are $($sourceVersion.MajorVersion).$($sourceVersion.MinorVersion); UT4 4.15 headers are required."
}
if ($editorModules.CompatibleChangelist -ne 3525109) {
    throw "The installed editor API is $($editorModules.CompatibleChangelist); this bootstrap is certified only for UT4 CL 3525360 / API 3525109."
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
Assert-Path $vswhere 'vswhere'
$vsRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsRoot) { throw 'A Visual Studio C++ toolchain was not found.' }
$vcRoot = Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
$toolBin = Join-Path $vcRoot 'bin\Hostx64\x64'

$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdkVersion = Get-ChildItem -LiteralPath (Join-Path $kitsRoot 'Include') -Directory | Where-Object { Test-Path (Join-Path $kitsRoot "Lib\$($_.Name)\um\x64\kernel32.lib") } | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name
if (-not $sdkVersion) { throw 'A Windows 10 SDK with x64 libraries was not found.' }
$rc = Join-Path $kitsRoot "bin\$sdkVersion\x64\rc.exe"
if (-not (Test-Path -LiteralPath $rc)) {
    $rc = Get-ChildItem -LiteralPath (Join-Path $kitsRoot 'bin') -Filter rc.exe -Recurse | Where-Object FullName -Match '\\x64\\rc\.exe$' | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
Assert-Path $rc 'Windows resource compiler'

$pluginRoot = $PSScriptRoot
$moduleSource = Join-Path $pluginRoot 'Source\Ut4ReconEditor\Private\Ut4ReconEditorModule.cpp'
$packageRoot = Join-Path $OutputRoot 'Plugin\Ut4ReconEditor'
$binaryRoot = Join-Path $packageRoot 'Binaries\Win64'
$libRoot = Join-Path $OutputRoot 'Lib\Win64'
New-Item -ItemType Directory -Force -Path $binaryRoot, $libRoot | Out-Null

function New-ImportLibrary([string] $ModuleName) {
    $dllName = "UE4Editor-$ModuleName.dll"
    $dllPath = Join-Path $editorBin $dllName
    Assert-Path $dllPath "Installed $ModuleName module"
    $defPath = Join-Path $libRoot "UE4Editor-$ModuleName.def"
    $libPath = Join-Path $libRoot "UE4Editor-$ModuleName.lib"
    $dump = & (Join-Path $toolBin 'dumpbin.exe') /nologo /exports $dllPath
    $exports = @($dump | ForEach-Object {
        if ($_ -match '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]+\s+(\S+)') { $matches[1] }
    })
    if ($exports.Count -eq 0) { throw "$dllName contains no named exports." }
    $definition = 'LIBRARY "' + $dllName + '"' + "`r`nEXPORTS`r`n" + ($exports -join "`r`n")
    Set-Content -LiteralPath $defPath -Encoding ascii -Value $definition
    $libOutput = & (Join-Path $toolBin 'lib.exe') /nologo /machine:x64 "/def:$defPath" "/out:$libPath"
    if ($LASTEXITCODE) { throw "lib.exe failed for $dllName." }
    $libOutput | Write-Host
    return $libPath
}

$dependencyModules = @('Core', 'CoreUObject', 'Engine', 'InputCore', 'Slate', 'SlateCore', 'Json', 'Projects', 'UnrealEd', 'WorkspaceMenuStructure', 'AssetTools')
$importLibraries = @($dependencyModules | ForEach-Object { New-ImportLibrary $_ })
$objectPath = Join-Path $OutputRoot 'Ut4ReconEditorModule.obj'
$stubRoot = Join-Path $OutputRoot 'GeneratedHeaderStubs'
New-Item -ItemType Directory -Force -Path $stubRoot | Out-Null
Set-Content -LiteralPath (Join-Path $stubRoot 'AutomatedAssetImportData.generated.h') -Encoding ascii -Value '// Reflection declarations intentionally suppressed for the non-reflected adapter.'
$stubScanRoots = @(
    (Join-Path $SourceRoot 'Engine\Source\Runtime\InputCore'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\Slate'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\SlateCore')
)
foreach ($header in Get-ChildItem -LiteralPath $stubScanRoots -Filter '*.h' -Recurse) {
    foreach ($line in Get-Content -LiteralPath $header.FullName) {
        if ($line -match '#include\s+"([^/"]+\.generated\.h)"') {
            $stub = Join-Path $stubRoot $matches[1]
            if (-not (Test-Path -LiteralPath $stub)) { Set-Content -LiteralPath $stub -Encoding ascii -Value '// Reflection declarations intentionally suppressed for the non-reflected adapter.' }
        }
    }
}
$includePaths = @(
    $stubRoot,
    (Join-Path $vcRoot 'include'),
    (Join-Path $kitsRoot "Include\$sdkVersion\ucrt"),
    (Join-Path $kitsRoot "Include\$sdkVersion\shared"),
    (Join-Path $kitsRoot "Include\$sdkVersion\um"),
    (Join-Path $kitsRoot "Include\$sdkVersion\winrt"),
    (Join-Path $SourceRoot 'Engine\Source'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\Core\Public'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\Core\Public\Templates'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\Core'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\CoreUObject\Public'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\CoreUObject\Classes'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\Engine\Public'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\Engine\Classes'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\InputCore\Classes'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\InputCore\Public'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\Slate\Public'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\SlateCore\Public'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\Json\Public'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\AssetRegistry\Public'),
    (Join-Path $SourceRoot 'Engine\Source\Runtime\Projects\Public'),
    (Join-Path $SourceRoot 'Engine\Source\Editor\UnrealEd\Public'),
    (Join-Path $SourceRoot 'Engine\Source\Editor\UnrealEd\Classes'),
    (Join-Path $SourceRoot 'Engine\Source\Editor\WorkspaceMenuStructure\Public')
    (Join-Path $SourceRoot 'Engine\Source\Developer\AssetTools\Public')
)
$definitions = @(
    'WIN32', '_WINDOWS', 'UNICODE', '_UNICODE', 'PLATFORM_WINDOWS=1', 'PLATFORM_64BITS=1',
    'UE_BUILD_DEVELOPMENT=1', 'WITH_ENGINE=1', 'WITH_UNREAL_DEVELOPER_TOOLS=1', 'WITH_EDITOR=1',
    'WITH_PLUGIN_SUPPORT=1', 'WITH_PERFCOUNTERS=1', 'WITH_EDITORONLY_DATA=1', 'WITH_SERVER_CODE=1',
    'UE_BUILD_MINIMAL=0', 'IS_MONOLITHIC=0', 'IS_PROGRAM=0',
    'CORE_API=DLLIMPORT', 'COREUOBJECT_API=DLLIMPORT', 'ENGINE_API=DLLIMPORT', 'INPUTCORE_API=DLLIMPORT',
    'SLATE_API=DLLIMPORT', 'SLATECORE_API=DLLIMPORT', 'JSON_API=DLLIMPORT',
    'PROJECTS_API=DLLIMPORT', 'UNREALED_API=DLLIMPORT', 'WORKSPACEMENUSTRUCTURE_API=DLLIMPORT', 'ASSETTOOLS_API=DLLIMPORT',
    'UT4RECONEDITOR_API=DLLEXPORT'
)
$compileArgs = @('/nologo', '/c', '/std:c++14', '/EHsc', '/MD', '/O2', '/W3', '/wd4668', "/Fo$objectPath")
$compileArgs += $definitions | ForEach-Object { "/D$_" }
$compileArgs += $includePaths | ForEach-Object { "/I$_" }
$compileArgs += $moduleSource
& (Join-Path $toolBin 'cl.exe') @compileArgs
if ($LASTEXITCODE) { throw 'Native adapter compilation failed.' }

$resourceSource = Join-Path $OutputRoot 'ModuleVersion.rc'
$resourceObject = Join-Path $OutputRoot 'ModuleVersion.res'
Set-Content -LiteralPath $resourceSource -Encoding ascii -Value "LANGUAGE 0,0`r`n191 10 { `"$($editorModules.CompatibleChangelist)`" 0 }"
& $rc /nologo "/fo$resourceObject" $resourceSource
if ($LASTEXITCODE) { throw 'Module API resource compilation failed.' }

$dllPath = Join-Path $binaryRoot 'UE4Editor-Ut4ReconEditor.dll'
$libraryPaths = @(
    (Join-Path $vcRoot 'lib\x64'),
    (Join-Path $kitsRoot "Lib\$sdkVersion\ucrt\x64"),
    (Join-Path $kitsRoot "Lib\$sdkVersion\um\x64"),
    $libRoot
)
$linkArgs = @('/nologo', '/dll', '/machine:x64', "/out:$dllPath", $objectPath, $resourceObject) + $importLibraries
$linkArgs += $libraryPaths | ForEach-Object { "/libpath:$_" }
& (Join-Path $toolBin 'link.exe') @linkArgs
if ($LASTEXITCODE) { throw 'Native adapter link failed.' }

Copy-Item -LiteralPath (Join-Path $pluginRoot 'Ut4ReconEditor.uplugin') -Destination (Join-Path $packageRoot 'Ut4ReconEditor.uplugin') -Force
$outputModules = [ordered]@{
    Changelist = [int] $editorModules.Changelist
    CompatibleChangelist = [int] $editorModules.CompatibleChangelist
    BuildId = [string] $editorModules.BuildId
    Modules = [ordered]@{ Ut4ReconEditor = 'UE4Editor-Ut4ReconEditor.dll' }
}
$outputModules | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $binaryRoot 'UE4Editor.modules') -Encoding utf8

if ($Install) {
    $installRoot = Join-Path $EditorRoot 'Engine\Plugins\Marketplace\Ut4ReconEditor'
    New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
    Copy-Item -Path (Join-Path $packageRoot '*') -Destination $installRoot -Recurse -Force
    Write-Host "Installed UT4 Recon Editor to $installRoot"
}

Write-Host "Built $dllPath"
Write-Host "Headers: $($sourceVersion.MajorVersion).$($sourceVersion.MinorVersion).$($sourceVersion.PatchVersion)-CL-$($sourceVersion.Changelist)"
Write-Host "Runtime API: $($editorModules.CompatibleChangelist) (editor CL $($editorModules.Changelist))"
