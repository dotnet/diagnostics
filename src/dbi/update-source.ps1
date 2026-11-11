[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $RuntimeRoot,

    [string] $Commit = 'HEAD'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimeRoot = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$commit = (& git -C $runtimeRoot rev-parse "$Commit^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
    throw "Cannot resolve runtime commit '$Commit'."
}

$runtimeManifestPath = 'src/coreclr/dlls/mscordbi_universal/SourceManifest.props'
$sourceManifestContents = & git -C $runtimeRoot show "${commit}:$runtimeManifestPath"
if ($LASTEXITCODE -ne 0) {
    throw "Cannot read '$runtimeManifestPath' from runtime commit '$commit'."
}

[xml] $sourceManifest = $sourceManifestContents -join "`n"
$includes = @(
    $sourceManifest.SelectNodes('/Project/ItemGroup/UniversalDbiSourceManifestSource') |
        ForEach-Object { $_.GetAttribute('Include') }
)
if ($includes.Count -eq 0) {
    throw "The source manifest does not contain any UniversalDbiSourceManifestSource items."
}

foreach ($include in $includes) {
    $segments = $include -split '[\\/]'
    if ([string]::IsNullOrWhiteSpace($include) -or
        [IO.Path]::IsPathRooted($include) -or
        $include.StartsWith(':', [StringComparison]::Ordinal) -or
        $segments -contains '..') {
        throw "Invalid source mirror path '$include' in the source manifest."
    }
}

$temporaryRoot = Join-Path $PSScriptRoot ".runtime-$([Guid]::NewGuid().ToString('N'))"
$snapshot = Join-Path $temporaryRoot 'snapshot'
$archive = Join-Path $temporaryRoot 'runtime.tar'

try {
    New-Item -ItemType Directory -Path $snapshot | Out-Null

    & git -C $runtimeRoot archive --format=tar "--output=$archive" $commit -- $includes
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot archive the runtime source.'
    }

    & tar -xf $archive -C $snapshot
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot extract the runtime source.'
    }

    $destination = Join-Path $PSScriptRoot 'runtime'
    if (Test-Path -LiteralPath $destination) {
        if ((Get-Item -LiteralPath $destination -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing to replace linked destination '$destination'."
        }
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    Move-Item -LiteralPath $snapshot -Destination $destination
    [IO.File]::WriteAllText(
        (Join-Path $PSScriptRoot 'runtime-source.txt'),
        "$commit`n",
        [Text.UTF8Encoding]::new($false))
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host "Imported runtime source from commit $commit."
