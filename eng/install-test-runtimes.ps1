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

$RuntimeVersion11="1.1.13"
$RuntimeVersion21="2.1.12"
$RuntimeVersion22="2.2.6"
$RuntimeVersion30="3.0.0"
$AspNetCoreVersion30="3.0.0"
$DailyTestText="true"

# Always install 2.1 for the daily test (scheduled builds) scenario because xunit needs it
. $DotNetDir\dotnet-install.ps1 -Version $RuntimeVersion21 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $DotNetDir
. $DotNetDir\dotnet-install.ps1 -Version $RuntimeVersion21 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime aspnetcore -InstallDir $DotNetDir

# Install the other versions of .NET Core runtime we are going to test. 1.1.x, 2.1.x, 2.2.x, 3.0.x
# and latest. Only install the latest master for daily jobs and leave the RuntimeVersion* 
# config properties blank.
if (!$DailyTest) {
    $DailyTestText="false"
    . $DotNetDir\dotnet-install.ps1 -Version $RuntimeVersion11 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $DotNetDir
    . $DotNetDir\dotnet-install.ps1 -Version $RuntimeVersion22 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $DotNetDir
    . $DotNetDir\dotnet-install.ps1 -Version $RuntimeVersion22 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime aspnetcore -InstallDir $DotNetDir
}

. $DotNetDir\dotnet-install.ps1 -Channel $Branch -Version latest -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $DotNetDir
. $DotNetDir\dotnet-install.ps1 -Channel $Branch -Version latest -Architecture $BuildArch -SkipNonVersionedFiles -Runtime aspnetcore -InstallDir $DotNetDir

# Now download the latest runtime version and create a config file containing it
$VersionFileUrl = "$UncachedFeed/Runtime/$Branch/latest.version"
$VersionFile = Join-Path -Path $TempDir latest.version
$ConfigFile = Join-Path -Path $DotNetDir Debugger.Tests.Versions.txt

Invoke-WebRequest $VersionFileUrl -OutFile $VersionFile

if (Test-Path $VersionFile) {
    $VersionText = cat $VersionFile
    $Data = @($VersionText.Split([char[]]@(), [StringSplitOptions]::RemoveEmptyEntries));
    $RuntimeVersionLatest = $Data[1].Trim()

    Write-Host "Latest $Branch version: $RuntimeVersionLatest"

    '<Configuration>
<DailyTest>' + $DailyTestText  +'</DailyTest>
<RuntimeVersion11>' + $RuntimeVersion11 + '</RuntimeVersion11>
<RuntimeVersion21>' + $RuntimeVersion21 + '</RuntimeVersion21>
<RuntimeVersion22>' + $RuntimeVersion22 + '</RuntimeVersion22>
<RuntimeVersion30>' + $RuntimeVersion30 + '</RuntimeVersion30>
<AspNetCoreVersion30>' + $AspNetCoreVersion30 + '</AspNetCoreVersion30>
<RuntimeVersionLatest>' + $RuntimeVersionLatest + '</RuntimeVersionLatest>
</Configuration>' | Set-Content $ConfigFile

}
else {
    Write-Host "Could not download latest runtime version file"
}
