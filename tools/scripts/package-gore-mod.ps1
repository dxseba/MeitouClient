# Packages the committed licensed anatomy by default; never builds or downloads assets.
# pwsh -File tools/scripts/package-gore-mod.ps1 -OutputDirectory <package> [-AnatomyDirectory <source>] [-GameDirectory <Kenshi>] [-AssemblyPath <mod DLL>]
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $OutputDirectory,
    [string] $AnatomyDirectory,
    [string] $GameDirectory = $env:KENSHI_PATH,
    [string] $AssemblyPath
)
$ErrorActionPreference = 'Stop'
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
$taskComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }

function Get-ContainedPath([string] $base, [string] $relative) {
    $canonicalBase = [IO.Path]::GetFullPath($base).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $candidate = [IO.Path]::GetFullPath((Join-Path $canonicalBase $relative))
    if (-not $candidate.StartsWith($canonicalBase + [IO.Path]::DirectorySeparatorChar, $taskComparison)) {
        throw "Package path escapes its destination: $relative"
    }
    return $candidate
}

$taskBundled = Join-Path $taskRepo 'mods/Brutality/assets/anatomy'
$taskBundledManifestPath = Join-Path $taskBundled 'manifest.json'
if (-not (Test-Path -LiteralPath $taskBundledManifestPath -PathType Leaf)) { throw 'The committed anatomy manifest is missing.' }
$taskBundledManifest = Get-Content -LiteralPath $taskBundledManifestPath -Raw | ConvertFrom-Json
if (-not $AnatomyDirectory -and -not $PSBoundParameters.ContainsKey('GameDirectory')) {
    $AnatomyDirectory = $taskBundled
}
if (-not $AnatomyDirectory) {
    if (-not $GameDirectory) {
        foreach ($start in @((Get-Location).Path, $taskRepo)) {
            for ($directory = [IO.DirectoryInfo]::new($start); $null -ne $directory; $directory = $directory.Parent) {
                $config = Join-Path $directory.FullName 'meitou.local.json'
                if (Test-Path -LiteralPath $config -PathType Leaf) {
                    $GameDirectory = (Get-Content -LiteralPath $config -Raw | ConvertFrom-Json).kenshiPath
                    if ($GameDirectory) { break }
                }
            }
            if ($GameDirectory) { break }
        }
    }
    if (-not $GameDirectory) { throw 'Specify a nonempty -GameDirectory or use -AnatomyDirectory for an override.' }
    $AnatomyDirectory = Join-Path $GameDirectory 'mods/Gauntlet of Crowns'
}
$taskSource = (Resolve-Path -LiteralPath $AnatomyDirectory).Path
if (-not (Test-Path -LiteralPath $taskSource -PathType Container)) { throw 'AnatomyDirectory must be a directory.' }
$taskAssetDirectory = Get-ContainedPath $taskOutput 'assets/anatomy'
$taskFiles = @('Ecorche.anatomy', 'Brain.anatomy', 'Mouth.anatomy')
$taskEntries = @()
$taskConfirmed = $true
foreach ($name in $taskFiles) {
    $source = Get-ContainedPath $taskSource $name
    $destination = Get-ContainedPath $taskOutput ('assets/anatomy/' + $name)
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing source anatomy: $name" }
    if ([string]::Equals($source, $destination, $taskComparison)) { throw 'Package destination must differ from the anatomy source.' }
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $bytes = (Get-Item -LiteralPath $source).Length
    $expected = @($taskBundledManifest.files | Where-Object path -CEQ $name)
    if ($expected.Count -ne 1) { throw "Committed manifest must describe exactly one $name." }
    if ($hash -ne $expected[0].sha256 -or $bytes -ne $expected[0].bytes) { $taskConfirmed = $false }
    $taskEntries += [ordered]@{ path = $name; bytes = $bytes; sha256 = $hash }
}
if ([string]::Equals([IO.Path]::GetFullPath($taskSource), [IO.Path]::GetFullPath($taskBundled), $taskComparison) -and -not $taskConfirmed) {
    throw 'Committed anatomy differs from its provenance manifest. Restore the assets or explicitly select a separate override directory.'
}
$taskNotices = if ($taskConfirmed) { @($taskBundledManifest.notices) } else { @(
    'LICENSE.md', 'ATTRIBUTION.md', 'SUPPLEMENTAL-ATTRIBUTION.md', 'Ecorche-CC-BY-SA-4.0.txt',
    'Ecorche-MIT.txt', 'Ecorche-LICENSE.md', 'Ecorche-ATTRIBUTION.md'
) }
$taskNoticeSource = if ($taskConfirmed) { $taskBundled } else { $taskSource }
$taskCopies = @()
foreach ($name in $taskFiles) { $taskCopies += [ordered]@{ source = (Get-ContainedPath $taskSource $name); relative = $name } }
foreach ($name in $taskNotices) {
    $source = Get-ContainedPath $taskNoticeSource $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        if ($taskConfirmed) { throw "Missing committed anatomy notice: $name" }
        continue
    }
    $taskCopies += [ordered]@{ source = $source; relative = $name }
}
if ($taskConfirmed) {
    foreach ($name in @('README.md', 'manifest.json')) {
        $source = Get-ContainedPath $taskBundled $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing committed anatomy metadata: $name" }
        $taskCopies += [ordered]@{ source = $source; relative = $name }
    }
}
# Validate every source/destination pair before changing the package.
foreach ($copy in $taskCopies) {
    $destination = Get-ContainedPath $taskOutput ('assets/anatomy/' + $copy.relative)
    if ([string]::Equals($copy.source, $destination, $taskComparison)) { throw 'Package destination must differ from the anatomy source.' }
}
$taskAssembly = $null
if ($AssemblyPath) {
    if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) { throw 'AssemblyPath must name the built mod DLL.' }
    $taskAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
    $name = [IO.Path]::GetFileName($taskAssembly)
    if (-not [string]::Equals($name, 'Brutality.dll', [StringComparison]::OrdinalIgnoreCase)) { throw 'AssemblyPath must name Brutality.dll, as declared by meitou.json.' }
    if ([string]::Equals($taskAssembly, (Get-ContainedPath $taskOutput 'Brutality.dll'), $taskComparison)) { throw 'Package destination must differ from the assembly source.' }
}
New-Item -ItemType Directory -Force -Path $taskAssetDirectory | Out-Null
foreach ($copy in $taskCopies) {
    $destination = Get-ContainedPath $taskOutput ('assets/anatomy/' + $copy.relative)
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath $copy.source -Destination $destination -Force
    if ((Get-FileHash -LiteralPath $copy.source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
        throw "Copied anatomy or notice checksum differs: $($copy.relative)"
    }
}
if (-not $taskConfirmed) {
    $taskEvidence = @($taskCopies | Where-Object { $_.relative -notin $taskFiles } | ForEach-Object { $_.relative })
    [ordered]@{
        version = 1
        format = 'ECR1'
        importedFrom = 'Explicit local anatomy override; absolute source path intentionally omitted.'
        goreSourceCommit = '3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa'
        redistributionStatus = 'unconfirmed'
        note = 'These bytes differ from the licensed committed bundle. Its verified provenance and permission do not automatically apply to this override.'
        notices = $taskEvidence
        files = $taskEntries
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Get-ContainedPath $taskOutput 'assets/anatomy/manifest.json') -Encoding utf8
    'Custom local anatomy override. Redistribution provenance is unconfirmed; inspect manifest.json and the source notices.' |
        Set-Content -LiteralPath (Get-ContainedPath $taskOutput 'assets/anatomy/README.md') -Encoding utf8
    Write-Warning 'Anatomy override differs from the committed bundle; its redistribution provenance remains unconfirmed.'
}

$taskModSource = Join-Path $taskRepo 'mods/Brutality.Stock'
foreach ($name in @('meitou.json', 'README.md')) {
    $source = Join-Path $taskModSource $name
    if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination (Get-ContainedPath $taskOutput $name) -Force }
}
$taskEffects = Join-Path $taskRepo 'mods/Brutality/assets/effects'
$taskEffectDirectory = Get-ContainedPath $taskOutput 'assets/effects'
New-Item -ItemType Directory -Force -Path $taskEffectDirectory | Out-Null
foreach ($name in @('robot-electric-arc.png', 'robot-spark.png', 'CREDITS.md')) {
    $source = Join-Path $taskEffects $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing bundled effect asset: $name" }
    $destination = Get-ContainedPath $taskOutput ('assets/effects/' + $name)
    Copy-Item -LiteralPath $source -Destination $destination -Force
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
        throw "Effect asset hash mismatch: $name"
    }
}
Copy-Item -LiteralPath (Join-Path $taskRepo 'LICENSE') -Destination (Get-ContainedPath $taskOutput 'LICENSE') -Force
$taskException = Join-Path $taskRepo 'LICENSE-EXCEPTION.md'
if (Test-Path -LiteralPath $taskException -PathType Leaf) {
    Copy-Item -LiteralPath $taskException -Destination (Get-ContainedPath $taskOutput 'LICENSE-EXCEPTION.md') -Force
}
# An empty data mod is still required by ModList.Discover. This is exactly FcsWriter.Write(new FcsFile()):
# V17 type, 20-byte header (version + four empty strings), next-id and zero records.
$taskDataMod = Get-ContainedPath $taskOutput 'Brutality.mod'
$taskAuthoredMod = Join-Path $taskModSource 'Brutality.mod'
if (Test-Path -LiteralPath $taskAuthoredMod -PathType Leaf) {
    Copy-Item -LiteralPath $taskAuthoredMod -Destination $taskDataMod -Force
} else {
    $taskDataStream = [IO.MemoryStream]::new()
    $taskDataWriter = [IO.BinaryWriter]::new($taskDataStream, [Text.Encoding]::UTF8, $true)
    try {
        $taskDataWriter.Write([int]17)
        $taskDataWriter.Write([int]20)
        $taskDataWriter.Write([int]1)
        for ($i = 0; $i -lt 6; $i++) { $taskDataWriter.Write([int]0) }
        $taskDataWriter.Flush()
        [IO.File]::WriteAllBytes($taskDataMod, $taskDataStream.ToArray())
    } finally {
        $taskDataWriter.Dispose()
        $taskDataStream.Dispose()
    }
}
if ($taskAssembly) {
    Copy-Item -LiteralPath $taskAssembly -Destination (Get-ContainedPath $taskOutput 'Brutality.dll') -Force
}
Write-Output "Packaged three anatomy files with verified SHA256 checksums and their notices in $taskAssetDirectory"
