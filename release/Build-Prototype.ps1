[CmdletBinding()]
param(
    [string] $Output = '',
    [Parameter(Mandatory)][string] $EditorRoot,
    [Parameter(Mandatory)][string] $SourceRoot,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$versionXml = [xml](Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'Directory.Build.props'))
$version = [string]$versionXml.Project.PropertyGroup.Version
if (-not $Output) { $Output = Join-Path $repoRoot "artifacts\release\ut4recon-$version-win-x64" }
$outputRoot = [IO.Path]::GetFullPath($Output)
$zip = "$outputRoot.zip"
$partialZip = "$outputRoot.partial.zip"
if (Test-Path -LiteralPath $outputRoot) { throw "Release output already exists: $outputRoot" }
if (Test-Path -LiteralPath $zip) { throw "Release archive already exists: $zip" }
if (Test-Path -LiteralPath $partialZip) { throw "Partial release archive already exists: $partialZip" }
New-Item -ItemType Directory -Path $outputRoot | Out-Null

if (-not $SkipTests) { & dotnet test (Join-Path $repoRoot 'Ut4Recon.sln') --no-restore --verbosity minimal -p:IncludePrivateFixtureTests=true; if ($LASTEXITCODE) { throw 'Tests failed.' } }

$cliRoot = Join-Path $outputRoot 'cli'
& dotnet publish (Join-Path $repoRoot 'src\Ut4Recon.Cli\Ut4Recon.Cli.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $cliRoot
if ($LASTEXITCODE) { throw 'CLI publish failed.' }

$nativeBuild = Join-Path $outputRoot '.native-build'
& (Join-Path $repoRoot 'native\Ut4ReconEditor\Build-BinaryPlugin.ps1') -SourceRoot $SourceRoot -EditorRoot $EditorRoot -OutputRoot $nativeBuild
if ($LASTEXITCODE) { throw 'Native plugin build failed.' }
Copy-Item -LiteralPath (Join-Path $nativeBuild 'Plugin') -Destination (Join-Path $outputRoot 'EditorPlugin') -Recurse
$nativeFull = [IO.Path]::GetFullPath($nativeBuild)
if (-not $nativeFull.StartsWith($outputRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe native staging path.' }
Remove-Item -LiteralPath $nativeFull -Recurse -Force

Copy-Item -LiteralPath (Join-Path $repoRoot 'schemas') -Destination (Join-Path $outputRoot 'schemas') -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'fixtures') -Destination (Join-Path $outputRoot 'fixtures') -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'IMPLEMENTATION_PLAN.md') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'NOTICE.md') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'CONTRIBUTING.md') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'RELEASING.md') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination (Join-Path $outputRoot 'docs') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-EditorPlugin.ps1') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PROTOTYPE-README.md') -Destination $outputRoot
Set-Content -LiteralPath (Join-Path $outputRoot 'VERSION.txt') -Encoding utf8 -Value $version

$dependencyJson = Join-Path $outputRoot 'third-party-packages.json'
& dotnet list (Join-Path $repoRoot 'src\Ut4Recon.Cli\Ut4Recon.Cli.csproj') package --include-transitive --format json | Set-Content -LiteralPath $dependencyJson -Encoding utf8
if ($LASTEXITCODE) { throw 'Dependency inventory failed.' }
$inventory = Get-Content -Raw -LiteralPath $dependencyJson | ConvertFrom-Json
$packages = @($inventory.projects.frameworks.topLevelPackages + $inventory.projects.frameworks.transitivePackages | Where-Object id | Sort-Object id -Unique)
$licenseRoot = Join-Path $outputRoot 'ThirdPartyLicenses'
New-Item -ItemType Directory -Path $licenseRoot | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'third-party-licenses\*') -Destination $licenseRoot

$dotnetRoot = Split-Path (Get-Command dotnet).Source
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination (Join-Path $licenseRoot 'dotnet-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $licenseRoot 'dotnet-ThirdPartyNotices.txt')
$nugetRoot = ((& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
$legacyLicenseExpressions = @{
    'K4os.Compression.LZ4' = 'MIT'
    'K4os.Compression.LZ4.Streams' = 'MIT'
    'K4os.Hash.xxHash' = 'MIT'
}
$notice = @(
    '# Third-party notices',
    '',
    'The self-contained prototype includes the .NET runtime and the NuGet packages below. Copies of packaged license and notice texts are under `ThirdPartyLicenses`.',
    '',
    '- [.NET runtime](https://github.com/dotnet/runtime): `ThirdPartyLicenses/dotnet-LICENSE.txt` and `ThirdPartyLicenses/dotnet-ThirdPartyNotices.txt`',
    '',
    '| Package | Version | Declared license | Included text |',
    '| --- | --- | --- | --- |'
)
foreach ($package in $packages) {
    $packageDirectory = Join-Path $nugetRoot ($package.id.ToLowerInvariant() + '\' + $package.resolvedVersion.ToLowerInvariant())
    $nuspec = Get-ChildItem -LiteralPath $packageDirectory -Filter *.nuspec | Select-Object -First 1
    [xml] $metadata = Get-Content -Raw -LiteralPath $nuspec.FullName
    $licenseNode = $metadata.package.metadata.license
    $declared = if ($licenseNode -and $licenseNode.InnerText) { $licenseNode.InnerText.Trim() } elseif ($legacyLicenseExpressions.ContainsKey($package.id)) { $legacyLicenseExpressions[$package.id] } else { 'See package metadata' }
    $packageLicenseRoot = Join-Path $licenseRoot ($package.id + '-' + $package.resolvedVersion)
    $licenseFiles = @(Get-ChildItem -LiteralPath $packageDirectory -File -Recurse | Where-Object Name -Match '^(LICENSE|COPYING|NOTICE)(\..+)?$' | Sort-Object FullName -Unique)
    $included = @()
    if ($licenseFiles.Count -gt 0) {
        New-Item -ItemType Directory -Path $packageLicenseRoot | Out-Null
        foreach ($licenseFile in $licenseFiles) {
            $destinationName = $licenseFile.Name
            if (Test-Path -LiteralPath (Join-Path $packageLicenseRoot $destinationName)) { $destinationName = ([IO.Path]::GetFileNameWithoutExtension($licenseFile.Name) + '-' + $included.Count + $licenseFile.Extension) }
            Copy-Item -LiteralPath $licenseFile.FullName -Destination (Join-Path $packageLicenseRoot $destinationName)
            $included += "ThirdPartyLicenses/$($package.id)-$($package.resolvedVersion)/$destinationName"
        }
    }
    elseif ($declared -in @('MIT', 'Apache-2.0', 'BSD-2-Clause')) {
        $included += "ThirdPartyLicenses/SPDX-$declared.txt"
    }
    $includedText = if ($included.Count) { '`' + ($included -join '`, `') + '`' } else { '[package metadata](https://www.nuget.org/packages/' + $package.id + '/' + $package.resolvedVersion + ')' }
    $notice += "| [$($package.id)](https://www.nuget.org/packages/$($package.id)/$($package.resolvedVersion)) | $($package.resolvedVersion) | $declared | $includedText |"
}
Set-Content -LiteralPath (Join-Path $outputRoot 'THIRD-PARTY-NOTICES.md') -Encoding utf8 -Value $notice

$hashLines = Get-ChildItem -LiteralPath $outputRoot -File -Recurse | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($outputRoot, $_.FullName).Replace('\', '/')
    "$(Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName | Select-Object -ExpandProperty Hash)  $relative"
}
Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding ascii -Value $hashLines
try {
    Compress-Archive -LiteralPath $outputRoot -DestinationPath $partialZip -CompressionLevel Optimal
    Move-Item -LiteralPath $partialZip -Destination $zip
}
finally {
    if (Test-Path -LiteralPath $partialZip) { Remove-Item -LiteralPath $partialZip -Force }
}
Write-Host "Prototype: $outputRoot"
Write-Host "Archive: $zip"
Write-Host "SHA-256: $((Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash)"
