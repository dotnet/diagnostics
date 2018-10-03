[cmdletbinding()]
param(
   [string]$DotNetDir,
   [string]$RuntimeVersion21,
   [string]$TempDir,
   [string]$BuildArch,
   [switch]$DailyTest,
   [string]$Branch="master",
   [string]$UncachedFeed="https://dotnetcli.blob.core.windows.net/dotnet"
)

Set-StrictMode -Version Latest
$ErrorActionPreference="Stop"

$RuntimeVersion11="1.1.9"
$RuntimeVersion20="2.0.9"
$DailyTestText="true"

# Install the other versions of .NET Core runtime we are going to test. 1.1.x, 2.0.x, 2.1.x (installed with the CLI)
# and latest. Only install the latest master for daily jobs and leave the RuntimeVersion* config properties blank.
if (!$DailyTest) {
    $DailyTestText="false"
    . $DotNetDir\dotnet-install.ps1 -Version $RuntimeVersion11 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $DotNetDir
    . $DotNetDir\dotnet-install.ps1 -Version $RuntimeVersion20 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $DotNetDir
}

. $DotNetDir\dotnet-install.ps1 -Channel $Branch -Version latest -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $DotNetDir

# Now download the latest runtime version and create a config file containing it
$VersionFileUrl = "$UncachedFeed/Runtime/$Branch/latest.version"
$VersionFile = Join-Path -Path $TempDir latest.version
$ConfigFile = Join-Path -Path $DotNetDir Debugger.Tests.Versions.txt

Invoke-WebRequest $VersionFileUrl -OutFile $VersionFile

if (Test-Path $VersionFile) {
    $VersionText = cat $VersionFile
    $Data = @($VersionText.Split([char[]]@(), [StringSplitOptions]::RemoveEmptyEntries));
    $RuntimeVersionLatest = $Data[1].Trim()

    Write-Host "Latest version: $RuntimeVersionLatest"

    '<Configuration>
<DailyTest>' + $DailyTestText  +'</DailyTest>
<RuntimeVersion11>' + $RuntimeVersion11 + '</RuntimeVersion11>
<RuntimeVersion20>' + $RuntimeVersion20 + '</RuntimeVersion20>
<RuntimeVersion21>' + $RuntimeVersion21 + '</RuntimeVersion21>
<RuntimeVersionLatest>' + $RuntimeVersionLatest + '</RuntimeVersionLatest>
</Configuration>' | Set-Content $ConfigFile

}
else {
    Write-Host "Could not download latest runtime version file"
}
