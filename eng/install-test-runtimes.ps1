[cmdletbinding()]
param(
   [string]$InstallDir,
   [string]$BuildArch,
   [switch]$DailyTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference="Stop"

$ConfigFile = Join-Path -Path $InstallDir Debugger.Tests.Versions.txt
[xml]$ConfigFileContents = Get-Content $ConfigFile

$RuntimeVersion21=$ConfigFileContents.Configuration.RuntimeVersion21
$AspNetCoreVersion21=$ConfigFileContents.Configuration.AspNetCoreVersion21
$RuntimeVersion30=$ConfigFileContents.Configuration.RuntimeVersion30
$AspNetCoreVersion30=$ConfigFileContents.Configuration.AspNetCoreVersion30
$RuntimeVersion31=$ConfigFileContents.Configuration.RuntimeVersion31
$AspNetCoreVersion31=$ConfigFileContents.Configuration.AspNetCoreVersion31
$RuntimeVersionLatest=$ConfigFileContents.Configuration.RuntimeVersionLatest
$AspNetCoreVersionLatest=$ConfigFileContents.Configuration.AspNetCoreVersionLatest

# Install the other versions of .NET Core runtime we are going to test. 2.1.x, 3.0.x, 3.1.x and latest.
. $InstallDir\dotnet-install.ps1 -Version $RuntimeVersion21 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $InstallDir
. $InstallDir\dotnet-install.ps1 -Version $AspNetCoreVersion21 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime aspnetcore -InstallDir $InstallDir

. $InstallDir\dotnet-install.ps1 -Version $RuntimeVersion31 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $InstallDir
. $InstallDir\dotnet-install.ps1 -Version $AspNetCoreVersion31 -Architecture $BuildArch -SkipNonVersionedFiles -Runtime aspnetcore -InstallDir $InstallDir

. $InstallDir\dotnet-install.ps1 -Version $RuntimeVersionLatest -Architecture $BuildArch -SkipNonVersionedFiles -Runtime dotnet -InstallDir $InstallDir
. $InstallDir\dotnet-install.ps1 -Version $AspNetCoreVersionLatest -Architecture $BuildArch -SkipNonVersionedFiles -Runtime aspnetcore -InstallDir $InstallDir

$OptionsConfigFile = Join-Path -Path $InstallDir Debugger.Tests.Options.txt

$DailyTestText="true"
if (!$DailyTest) {
    $DailyTestText="false"
}
'<Configuration>
  <DailyTest>' + $DailyTestText + '</DailyTest>
</Configuration>' | Set-Content $OptionsConfigFile
