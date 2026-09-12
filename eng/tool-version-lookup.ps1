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

    Decodes the version and resolves the embedded commit SHA.

.EXAMPLE
    eng\tool-version-lookup.ps1 before bda9ea7b

    Finds the latest daily build version published before a given commit.

.EXAMPLE
    eng\tool-version-lookup.ps1 after 18cf9d1 -Tool dotnet-dump

    Finds the earliest daily build version published after a given commit.

.EXAMPLE
    eng\tool-version-lookup.ps1 verify 10.0.711601

    Checks whether a specific version exists on the dotnet-tools feed.

.EXAMPLE
    eng\tool-version-lookup.ps1 list -Last 5

    Lists the 5 most recent daily build versions on the feed.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, Position=0)]
    [ValidateSet("decode", "before", "after", "verify", "list")]
    [string]$Command,

    [Parameter(Position=1)]
    [string]$Ref,

    [string]$Date,

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

# Restrict to hex SHAs to prevent shell injection via git arguments.
function Validate-CommitRef([string]$CommitRef) {
    if ($CommitRef -notmatch '^[a-fA-F0-9]{4,40}$') {
        Write-Error "Invalid commit ref: '$CommitRef'. Expected a hex SHA."
        exit 1
    }
}

function Get-CommitDate([string]$CommitRef) {
    Validate-CommitRef $CommitRef
    $result = git log -1 --format="%cI" $CommitRef 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Could not find commit $CommitRef"
        exit 1
    }
    return [DateTimeOffset]::Parse($result.Trim())
}

function Get-CommitInfo([string]$CommitRef) {
    Validate-CommitRef $CommitRef
    $result = git log -1 --format="%h %ai %s" $CommitRef 2>&1
    if ($LASTEXITCODE -ne 0) {
        return "(could not resolve $CommitRef)"
    }
    return $result.Trim()
}

function Resolve-MajorMinor([string[]]$Versions) {
    if ($MajorMinor) { return $MajorMinor }
    $detected = Get-DetectedMajorMinor $Versions
    if (-not $detected) {
        Write-Error "Could not determine major.minor version from feed."
        exit 1
    }
    return $detected
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
        $info = Get-CommitInfo $sha
        Write-Host "Commit:           $info"
    }
}

function Invoke-BeforeOrAfter([bool]$IsBefore) {
    $direction = if ($IsBefore) { "before" } else { "after" }
    $label = if ($IsBefore) { "latest" } else { "earliest" }

    if ($Date) {
        $targetDate = [DateTimeOffset]::Parse($Date)
        Write-Host "Finding $label $Tool version built $direction $Date..."
    }
    elseif ($Ref) {
        $info = Get-CommitInfo $Ref
        $targetDate = Get-CommitDate $Ref
        Write-Host "Commit:  $info"
        Write-Host "Date:    $($targetDate.ToString('yyyy-MM-dd HH:mm zzz'))"
        Write-Host ""
        Write-Host "Finding $label $Tool version built $direction $($targetDate.ToString('yyyy-MM-dd'))..."
    }
    else {
        Write-Error "Usage: tool-version-lookup.ps1 $direction <commit-sha> [-Date yyyy-MM-dd]"
        exit 1
    }

    $targetYY = $targetDate.Year - 2000
    $targetMM = $targetDate.Month
    $targetDD = $targetDate.Day

    # For "before": use revision 0 so any build from that day is excluded
    # (the build may or may not include the commit depending on timing).
    # For "after": use revision 99 so any build from that day is excluded.
    if ($IsBefore) {
        $targetPatch = Encode-Patch $targetYY $targetMM $targetDD -Revision 0
    }
    else {
        $targetPatch = Encode-Patch $targetYY $targetMM $targetDD -Revision 99
    }

    $versions = Get-FeedVersions $Tool
    $mm = Resolve-MajorMinor $versions

    $candidates = @()
    foreach ($v in $versions) {
        if (-not $v.StartsWith("$mm.")) { continue }
        $parsed = Parse-ToolVersion $v
        if (-not $parsed) { continue }
        if ($IsBefore -and $parsed.Patch -lt $targetPatch) {
            $candidates += @{ Version = $v; Patch = $parsed.Patch }
        }
        elseif (-not $IsBefore -and $parsed.Patch -gt $targetPatch) {
            $candidates += @{ Version = $v; Patch = $parsed.Patch }
        }
    }

    if ($candidates.Length -eq 0) {
        Write-Host ""
        if (-not $IsBefore) {
            Write-Host "The fix may not have been published yet." -ForegroundColor Yellow
        }
        Write-Error "No $Tool $mm.x versions found $direction that date."
        exit 1
    }

    # @() wrapper is required: Sort-Object unwraps single-element arrays in PowerShell.
    $candidates = @($candidates | Sort-Object { $_.Patch })

    if ($IsBefore) {
        $recommended = $candidates[$candidates.Length - 1]
        $othersLabel = "Other recent options:"
        if ($candidates.Length -ge 2) {
            $start = [math]::Max(0, $candidates.Length - 4)
            $end = $candidates.Length - 2
            $others = @($candidates[$start..$end])
        }
        else {
            $others = @()
        }
    }
    else {
        $recommended = $candidates[0]
        $othersLabel = "Other options (newer):"
        if ($candidates.Length -ge 2) {
            $end = [math]::Min(3, $candidates.Length - 1)
            $others = @($candidates[1..$end])
        }
        else {
            $others = @()
        }
    }

    $feedUrl = $FeedBase
    Write-Host ""
    Write-Host "Recommended version: $($recommended.Version)"
    Write-Host "  Built: $(Format-BuildDate $recommended.Patch)"
    Write-Host ""
    Write-Host "Install with:"
    Write-Host "  dotnet tool update $Tool -g --version $($recommended.Version) ``"
    Write-Host "    --add-source $feedUrl"

    if ($others -and $others.Length -gt 0) {
        Write-Host ""
        Write-Host $othersLabel
        foreach ($c in $others) {
            if ($c) {
                Write-Host ("  {0,-20}  built {1}" -f $c.Version, (Format-BuildDate $c.Patch))
            }
        }
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

function Invoke-Verify {
    if (-not $Ref) {
        Write-Error "Usage: tool-version-lookup.ps1 verify <version>"
        exit 1
    }
    $versions = Get-FeedVersions $Tool

    if ($versions -contains $Ref) {
        $parsed = Parse-ToolVersion $Ref
        if ($parsed) {
            Write-Host "[OK] $Tool $Ref exists on the feed"
            Write-Host "  Built: $(Format-BuildDate $parsed.Patch)"
        }
        else {
            Write-Host "[OK] $Tool $Ref exists on the feed"
        }
    }
    else {
        Write-Host "[NOT FOUND] $Tool $Ref NOT found on the feed" -ForegroundColor Red

        $parsed = Parse-ToolVersion $Ref
        if ($parsed) {
            $nearby = @()
            foreach ($v in $versions) {
                $vp = Parse-ToolVersion $v
                if ($vp -and $vp.Major -eq $parsed.Major -and $vp.Minor -eq $parsed.Minor) {
                    if ([math]::Abs($vp.Patch - $parsed.Patch) -lt 500) {
                        $nearby += $v
                    }
                }
            }
            if ($nearby.Length -gt 0) {
                Write-Host ""
                Write-Host "  Nearby versions:"
                $nearby | Select-Object -Last 5 | ForEach-Object {
                    $vp = Parse-ToolVersion $_
                    Write-Host ("    {0,-20}  built {1}" -f $_, (Format-BuildDate $vp.Patch))
                }
            }
        }
        exit 1
    }
}

switch ($Command) {
    "decode"  { Invoke-Decode }
    "before"  { Invoke-BeforeOrAfter -IsBefore $true }
    "after"   { Invoke-BeforeOrAfter -IsBefore $false }
    "verify"  { Invoke-Verify }
    "list"    { Invoke-List }
}
