# Copies the user's local anatomy into a mod package; never builds or downloads assets.
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
    if (-not $GameDirectory) { throw 'Specify -AnatomyDirectory, -GameDirectory, KENSHI_PATH, or kenshiPath in meitou.local.json.' }
    $AnatomyDirectory = Join-Path $GameDirectory 'mods/Gauntlet of Crowns'
}
$taskSource = (Resolve-Path -LiteralPath $AnatomyDirectory).Path
if (-not (Test-Path -LiteralPath $taskSource -PathType Container)) { throw 'AnatomyDirectory must be a directory.' }
$taskAssetDirectory = Get-ContainedPath $taskOutput 'assets/anatomy'
$taskFiles = @('Ecorche.anatomy', 'Brain.anatomy', 'Mouth.anatomy')
foreach ($name in $taskFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskSource $name) -PathType Leaf)) { throw "Missing source anatomy: $name" }
}
# Validate every input and destination before changing the package.
foreach ($name in $taskFiles) {
    $source = [IO.Path]::GetFullPath((Join-Path $taskSource $name))
    $destination = Get-ContainedPath $taskOutput ('assets/anatomy/' + $name)
    if ([string]::Equals($source, $destination, $taskComparison)) { throw 'Package destination must differ from the anatomy source.' }
}
$taskAssembly = $null
if ($AssemblyPath) {
    if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) { throw 'AssemblyPath must name the built mod DLL.' }
    $taskAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
    $name = [IO.Path]::GetFileName($taskAssembly)
    if (-not [string]::Equals($name, 'Brutality.dll', [StringComparison]::OrdinalIgnoreCase)) { throw 'AssemblyPath must name Brutality.dll, as declared by meitou.json.' }
}
New-Item -ItemType Directory -Force -Path $taskAssetDirectory | Out-Null
$taskEntries = @()
foreach ($name in $taskFiles) {
    $source = Join-Path $taskSource $name
    $destination = Get-ContainedPath $taskOutput ('assets/anatomy/' + $name)
    if ([string]::Equals([IO.Path]::GetFullPath($source), $destination, $taskComparison)) { throw 'Package destination must differ from the anatomy source.' }
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    Copy-Item -LiteralPath $source -Destination $destination -Force
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Copied anatomy checksum differs: $name" }
    $taskEntries += [ordered]@{
        path = 'assets/anatomy/' + $name
        bytes = (Get-Item -LiteralPath $destination).Length
        sha256 = $hash
        licenseStatus = 'unconfirmed'
        redistributionStatus = 'unconfirmed-local-copy-only'
    }
}
$taskEvidence = @()
$taskLicense = Join-Path $taskSource 'Ecorche-LICENSE.md'
if (Test-Path -LiteralPath $taskLicense -PathType Leaf) {
    $taskLicenseDirectory = Get-ContainedPath $taskOutput 'assets/anatomy/licenses'
    New-Item -ItemType Directory -Force -Path $taskLicenseDirectory | Out-Null
    $taskLicenseCopy = Get-ContainedPath $taskOutput 'assets/anatomy/licenses/Ecorche-LICENSE.md'
    Copy-Item -LiteralPath $taskLicense -Destination $taskLicenseCopy -Force
    $taskEvidence += [ordered]@{
        path = 'assets/anatomy/licenses/Ecorche-LICENSE.md'
        sha256 = (Get-FileHash -LiteralPath $taskLicenseCopy -Algorithm SHA256).Hash.ToLowerInvariant()
        status = 'local-source-declaration-not-byte-provenance-verification'
    }
}
$taskManifest = [ordered]@{
    formatVersion = 1
    purpose = 'user-authorized-local-package'
    goreSourceCommit = '3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa'
    assetProvenance = 'local copies; the code reference does not establish asset origin or permission'
    source = 'local anatomy directory; absolute source path intentionally omitted'
    redistributionStatus = 'unconfirmed'
    files = $taskEntries
    licenseEvidence = $taskEvidence
    note = 'The local Ecorche declaration names CC BY-SA 4.0, but its referenced full attribution/license files and byte provenance are unverified. Brain and Mouth have no confirmed per-file declaration. Local bundling does not establish redistribution permission.'
}
$taskManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Get-ContainedPath $taskOutput 'assets/anatomy/manifest.json') -Encoding utf8

foreach ($name in @('meitou.json', 'README.md', 'ASSETS.md')) {
    $source = Join-Path $taskRepo ('mods/Brutality/' + $name)
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
$taskAuthoredMod = Join-Path $taskRepo 'mods/Brutality/Brutality.mod'
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
Write-Output "Packaged three local anatomy files and verified SHA256 checksums in $taskAssetDirectory"
