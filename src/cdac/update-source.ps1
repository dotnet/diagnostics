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

$sourceManifestPath = 'src/native/managed/cdac/SourceManifest.props'
$sourceManifestContents = & git -C $runtimeRoot show "${commit}:$sourceManifestPath"
if ($LASTEXITCODE -ne 0) {
    throw "Cannot read '$sourceManifestPath' from runtime commit '$commit'."
}

[xml] $sourceManifest = $sourceManifestContents -join "`n"
$includes = @(
    $sourceManifest.SelectNodes('/Project/ItemGroup/CDacSourceManifestSource') |
        ForEach-Object { $_.GetAttribute('Include') }
)
if ($includes.Count -eq 0) {
    throw "'$sourceManifestPath' does not contain any CDacSourceManifestSource items."
}

foreach ($include in $includes) {
    $segments = $include -split '[\\/]'
    if ([string]::IsNullOrWhiteSpace($include) -or
        [IO.Path]::IsPathRooted($include) -or
        $include.StartsWith(':', [StringComparison]::Ordinal) -or
        $segments -contains '..') {
        throw "Invalid source mirror path '$include' in '$sourceManifestPath'."
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

    $cdacRoot = Join-Path $snapshot 'src/native/managed/cdac'
    @(
        'cmake'
        'inc'
        'scripts'
        'IData.md'
        'README.md'
        'cdac.slnx'
    ) | ForEach-Object {
        $path = Join-Path $cdacRoot $_
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }

    @(
        @{ Path = 'tests'; Keep = 'TestInfrastructure' }
        @{ Path = 'tools'; Keep = 'CdacUsageGraph' }
        @{ Path = 'tools\CdacUsageGraph'; Keep = 'src' }
    ) | ForEach-Object {
        $path = Join-Path $cdacRoot $_.Path
        $keep = $_.Keep
        Get-ChildItem -LiteralPath $path -Force |
            Where-Object { $_.Name -ne $keep } |
            Remove-Item -Recurse -Force
    }

    Get-ChildItem -LiteralPath $cdacRoot -Directory | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.md' | Remove-Item -Force
    }

    $callingConventionRoot = Join-Path $snapshot 'src/coreclr/tools/Common/CallingConvention'
    Get-ChildItem -LiteralPath $callingConventionRoot -Force |
        Where-Object { $_.PSIsContainer -or $_.Extension -ne '.cs' } |
        Remove-Item -Recurse -Force

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
