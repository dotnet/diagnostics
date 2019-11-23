[cmdletbinding()]
param(
   [string]$DotNetDir,
   [string]$TempDir,
   [string]$BuildArch,
   [switch]$DailyTest,
   [string]$Branch="master",
   [string]$UncachedFeed="https://dotnetcli.blob.core.windows.net/dotnet"
)

Set-StrictMode -Version Latest
$ErrorActionPreference="Stop"

function Get-Latest-Version([string]$channel, [string]$runtime = "", [bool]$coherent = $false) {

    $VersionFileUrl = $null
    if ($runtime -eq "dotnet") {
        $VersionFileUrl = "$UncachedFeed/Runtime/$channel/latest.version"
    }
    elseif ($runtime -eq "aspnetcore") {
        $VersionFileUrl = "$UncachedFeed/aspnetcore/Runtime/$channel/latest.version"
    }
    elseif ($runtime -eq "") {
        if ($coherent) {
            $VersionFileUrl = "$UncachedFeed/Sdk/$Channel/latest.coherent.version"
        }
        else {
            $VersionFileUrl = "$UncachedFeed/Sdk/$Channel/latest.version"
        }
    }
    else {
        throw "Invalid value for $runtime"
    }

    $VersionFile = Join-Path -Path $TempDir latest.version

    try {
        Invoke-WebRequest $VersionFileUrl -OutFile $VersionFile
    }
    catch {
        return ""
    }

    if (Test-Path $VersionFile) {
        $VersionText = cat $VersionFile
        $Data = @($VersionText.Split([char[]]@(), [StringSplitOptions]::RemoveEmptyEntries));
        return $Data[1].Trim()
    }

    return ""
}

$ConfigFile = Join-Path -Path $DotNetDir Debugger.Tests.Versions.txt

$RuntimeVersion21="2.1.12"
$AspNetCoreVersion21="2.1.12"
$RuntimeVersion30="3.0.0"
$AspNetCoreVersion30="3.0.0"
$RuntimeVersion31="3.1.0"
$AspNetCoreVersion31="3.1.0"
$RuntimeVersionLatest=""
$AspNetCoreVersionLatest=""

$DailyTestText="true"
if (!$DailyTest) {
    $DailyTestText="false"
}

# Get the latest runtime versions for master and create a config file containing it
try {
    $RuntimeVersionLatest = Get-Latest-Version $Branch dotnet
    $AspNetCoreVersionLatest = Get-Latest-Version $Branch aspnetcore
    Write-Host "Latest $Branch runtime: $RuntimeVersionLatest aspnetcore: $AspNetCoreVersionLatest"
}
catch {
    Write-Host "Could not download latest $Branch runtime version"
}

# Install the other versions of .NET Core runtime we are going to test. 2.1.x, 3.0.x, 3.1.x and latest.
. $DotNetDir\dotnet-install.ps1 -Version $RuntimeVersion21 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $DotNetDir
. $DotNetDir\dotnet-install.ps1 -Version $AspNetCoreVersion21 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime aspnetcore -InstallDir $DotNetDir

. $DotNetDir\dotnet-install.ps1 -Version $RuntimeVersion31 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $DotNetDir
. $DotNetDir\dotnet-install.ps1 -Version $AspNetCoreVersion31 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime aspnetcore -InstallDir $DotNetDir

. $DotNetDir\dotnet-install.ps1 -Version $RuntimeVersionLatest -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $DotNetDir
. $DotNetDir\dotnet-install.ps1 -Version $AspNetCoreVersionLatest -Architecture $BuildArch -SkipNonVersionedFiles -Runtime aspnetcore -InstallDir $DotNetDir

'<Configuration>
  <DailyTest>' + $DailyTestText  +'</DailyTest>
  <RuntimeVersion21>' + $RuntimeVersion21 + '</RuntimeVersion21>
  <AspNetCoreVersion21>' + $AspNetCoreVersion21 + '</AspNetCoreVersion21>
  <RuntimeVersion30>' + $RuntimeVersion30 + '</RuntimeVersion30>
  <AspNetCoreVersion30>' + $AspNetCoreVersion30 + '</AspNetCoreVersion30>
  <RuntimeVersion31>' + $RuntimeVersion31 + '</RuntimeVersion31>
  <AspNetCoreVersion31>' + $AspNetCoreVersion31 + '</AspNetCoreVersion31>
  <RuntimeVersionLatest>' + $RuntimeVersionLatest + '</RuntimeVersionLatest>
  <AspNetCoreVersionLatest>' + $AspNetCoreVersionLatest + '</AspNetCoreVersionLatest>
</Configuration>' | Set-Content $ConfigFile
