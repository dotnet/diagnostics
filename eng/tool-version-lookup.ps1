<#
.SYNOPSIS
    Correlates diagnostics tool versions with git commits and build dates.

.DESCRIPTION
    Daily builds of dotnet-trace, dotnet-dump, dotnet-counters, and dotnet-gcdump
    are published to the dotnet-tools NuGet feed with stable version numbers like
    10.0.715501. The patch component encodes the build date using the Arcade SDK
    formula, and the informational version (shown by --version) includes the commit
    SHA after '+'.

.EXAMPLE
    eng\tool-version-lookup.ps1 decode 10.0.715501

    Decodes the version to show its build date and OfficialBuildId.

.EXAMPLE
    eng\tool-version-lookup.ps1 decode "10.0.715501+86150ac0275658c5efc6035269499a86dee68e54"

    Decodes the version and shows the embedded commit SHA.

.EXAMPLE
    eng\tool-version-lookup.ps1 list -Last 5

    Lists the 5 most recent daily build versions on the feed.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, Position=0)]
    [ValidateSet("decode", "list")]
    [string]$Command,

    [Parameter(Position=1)]
    [string]$Ref,

    [ValidateSet("dotnet-trace", "dotnet-dump", "dotnet-counters", "dotnet-gcdump")]
    [string]$Tool = "dotnet-trace",

    [string]$MajorMinor,

    [int]$Last = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# NuGet V3 flat container API — the simplest endpoint for listing all versions of a package.
$FeedFlat2Base = "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/flat2"
# Full feed URL used in the 'dotnet tool update --add-source' install command printed for users.
$FeedBase = "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/index.json"
# Arcade SDK epoch constant from Version.BeforeCommonTargets.targets.
# If Arcade changes this value, it must be updated here as well.
$VersionBaseShortDate = 19000

# The Arcade SDK (Version.BeforeCommonTargets.targets) encodes the OfficialBuildId
# (format: yyyyMMdd.revision) into the patch component of the version number:
#   SHORT_DATE = YY*1000 + MM*50 + DD
#   PATCH = (SHORT_DATE - VersionBaseShortDate) * 100 + revision
# MM*50 is used instead of MM*100 because months max at 12 (12*50=600), leaving
# room for days 1-31 without overflow into the next month's range.
function Decode-Patch([int]$Patch) {
    [int]$revision = $Patch % 100
    [int]$shortDate = [math]::Floor($Patch / 100) + $VersionBaseShortDate
    [int]$yy = [math]::Floor($shortDate / 1000)
    [int]$remainder = $shortDate - ($yy * 1000)
    [int]$mm = [math]::Floor($remainder / 50)
    [int]$dd = $remainder - ($mm * 50)
    return @{ Year = $yy; Month = $mm; Day = $dd; Revision = $revision }
}

function Encode-Patch([int]$Year, [int]$Month, [int]$Day, [int]$Revision = 1) {
    [int]$shortDate = $Year * 1000 + $Month * 50 + $Day
    return ($shortDate - $VersionBaseShortDate) * 100 + $Revision
}

function Format-BuildDate([int]$Patch) {
    $d = Decode-Patch $Patch
    return "20{0:D2}-{1:D2}-{2:D2} (rev {3})" -f $d.Year, $d.Month, $d.Day, $d.Revision
}

# Strips the "+commitsha" metadata suffix and splits "Major.Minor.Patch".
function Parse-ToolVersion([string]$Version) {
    $clean = $Version.Split("+")[0]
    $parts = $clean.Split(".")
    if ($parts.Length -ne 3) { return $null }
    try {
        return @{
            Major = [int]$parts[0]
            Minor = [int]$parts[1]
            Patch = [int]$parts[2]
        }
    }
    catch {
        return $null
    }
}

function Get-FeedVersions([string]$ToolName) {
    $url = "$FeedFlat2Base/$ToolName/index.json"
    try {
        $response = Invoke-RestMethod -Uri $url
        return $response.versions
    }
    catch {
        Write-Error "Failed to query feed for ${ToolName}: $_"
        exit 1
    }
}

# Auto-detects the active major.minor series by finding the version with the
# highest patch number (most recent build), since the feed contains versions
# from multiple release branches (e.g., 6.0.x, 9.0.x, 10.0.x).
function Get-DetectedMajorMinor([string[]]$Versions) {
    $bestPatch = -1
    $bestPrefix = $null
    foreach ($v in $Versions) {
        $parsed = Parse-ToolVersion $v
        if ($parsed -and $parsed.Patch -gt $bestPatch) {
            $bestPatch = $parsed.Patch
            $bestPrefix = "$($parsed.Major).$($parsed.Minor)"
        }
    }
    return $bestPrefix
}

function Invoke-Decode {
    if (-not $Ref) {
        Write-Error "Usage: tool-version-lookup.ps1 decode <version>"
        exit 1
    }
    $parsed = Parse-ToolVersion $Ref
    if (-not $parsed) {
        Write-Error "Could not parse version '$Ref'."
        exit 1
    }

    $d = Decode-Patch $parsed.Patch
    Write-Host "Version:          $Ref"
    Write-Host ("Build date:       20{0:D2}-{1:D2}-{2:D2}" -f $d.Year, $d.Month, $d.Day)
    Write-Host "Build revision:   $($d.Revision)"
    Write-Host ("OfficialBuildId:  20{0:D2}{1:D2}{2:D2}.{3}" -f $d.Year, $d.Month, $d.Day, $d.Revision)

    if ($Ref.Contains("+")) {
        $sha = $Ref.Split("+")[1]
        Write-Host "Commit SHA:       $sha"
    }
}

function Invoke-List {
    $versions = Get-FeedVersions $Tool
    $prefix = if ($MajorMinor) { $MajorMinor } else { Get-DetectedMajorMinor $versions }

    $filtered = $versions | Where-Object { $_.StartsWith("$prefix.") }
    $filtered = $filtered | Sort-Object { (Parse-ToolVersion $_).Patch }
    $show = $filtered | Select-Object -Last $Last

    Write-Host "Recent $Tool $prefix.x versions on dotnet-tools feed:"
    Write-Host ""
    Write-Host ("{0,-20}  {1,-16}  {2}" -f "Version", "Build Date", "OfficialBuildId")
    Write-Host ("{0,-20}  {1,-16}  {2}" -f ("-" * 20), ("-" * 16), ("-" * 15))
    foreach ($v in $show) {
        $parsed = Parse-ToolVersion $v
        if ($parsed) {
            $d = Decode-Patch $parsed.Patch
            $dateStr = "20{0:D2}-{1:D2}-{2:D2}" -f $d.Year, $d.Month, $d.Day
            $buildId = "20{0:D2}{1:D2}{2:D2}.{3}" -f $d.Year, $d.Month, $d.Day, $d.Revision
            Write-Host ("{0,-20}  {1,-16}  {2}" -f $v, $dateStr, $buildId)
        }
    }
}

switch ($Command) {
    "decode"  { Invoke-Decode }
    "list"    { Invoke-List }
}
