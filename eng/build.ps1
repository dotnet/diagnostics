[CmdletBinding(PositionalBinding=$false)]
Param(
    [ValidateSet("x86","x64","arm","arm64")][string][Alias('a', "platform")]$architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant(),
    [ValidateSet("Debug","Release")][string][Alias('c')] $configuration = "Debug",
    [string][Alias('v')] $verbosity = "minimal",
    [switch][Alias('t')] $test,
    [switch] $privatebuild,
    [switch] $ci,
    [switch][Alias('bl')]$binaryLog,
    [switch] $skipmanaged,
    [switch] $skipnative,
    [switch] $bundletools,
    [ValidateSet("", "cdac", "cdacfallback", "cdacverify", "dac")][string] $dacMode = '',
    [string] $cdacPath = '',
    [switch] $testInterpreter,
    [string] $methodfilter = '',
    [string] $classfilter = '',
    [ValidatePattern("(default|\d+\.\d+.\d+(-[a-z0-9\.]+)?)")][string] $dotnetruntimeversion = 'default',
    [ValidatePattern("(default|\d+\.\d+.\d+(-[a-z0-9\.]+)?)")][string] $dotnetruntimedownloadversion= 'default',
    [string] $runtimesourcefeed = '',
    [string] $runtimesourcefeedkey = '',
    [string] $liveRuntimeDir = '',
    [Parameter(ValueFromRemainingArguments=$true)][String[]] $remainingargs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($cdacPath -ne '' -and $dacMode -ne 'cdac') {
    Write-Error "-cdacPath is only valid with -dacMode cdac."
    exit 1
}


$crossbuild = $false
if (($architecture -eq "arm") -or ($architecture -eq "arm64")) {
    $processor = @([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())
    if ($architecture -ne $processor) {
        $crossbuild = $true
    }
}

switch ($configuration.ToLower()) {
    { $_ -eq "debug" } { $configuration = "Debug" }
    { $_ -eq "release" } { $configuration = "Release" }
}

$reporoot = Join-Path $PSScriptRoot ".."
$engroot = Join-Path $reporoot "eng"
$artifactsdir = Join-Path $reporoot "artifacts"
$os = "Windows_NT"
$logdir = Join-Path $artifactsdir "log"
$logdir = Join-Path $logdir Windows_NT.$architecture.$configuration

$bl = if ($binaryLog) { '-binaryLog' } else { '' }

if ($ci) {
    $remainingargs = "-ci " + $remainingargs
}

if ($bundletools) {
    $remainingargs = "/p:BundleTools=true " + $remainingargs
    $remainingargs = '/bl:"$logdir\BundleTools.binlog" ' + $remainingargs
    $remainingargs = '-noBl ' + $remainingargs
    $skipnative = $True
    $test = $False
}

# Build native components
if (-not $skipnative) {
    Invoke-Expression "& `"$engroot\Build-Native.cmd`" -architecture $architecture -configuration $configuration -verbosity $verbosity $remainingargs"
    if ($lastExitCode -ne 0) {
        exit $lastExitCode
    }
}

# Overlay an externally-provided cDAC (mscordaccore_universal) next to the freshly built sos.dll. SOS
# resolves the cDAC from its own native binaries directory, so this is the only spot it is picked up
# from. Used by the cdac DacMode to exercise the runtime-under-test's own cDAC instead of the copy
# restored from a referenced runtime package.
if ($cdacPath -ne '') {
    if (-not (Test-Path $cdacPath)) {
        Write-Error "-cdacPath '$cdacPath' does not exist."
        exit 1
    }
    $cdacDest = Join-Path (Join-Path $artifactsdir "bin\$os.$architecture.$configuration") "mscordaccore_universal.dll"
    New-Item -ItemType Directory -Force -Path (Split-Path $cdacDest -Parent) | Out-Null
    Write-Host "Overlaying cDAC: $cdacPath -> $cdacDest"
    Copy-Item $cdacPath $cdacDest -Force
}

# Install sdk for building, restore and build managed components.
# Test runtime installation and debuggee building is handled by src/tests/dirs.proj targets.
if (-not $skipmanaged) {
    $privatebuildtesting = "false"
    if ($privatebuild) {
        $privatebuildtesting = "true"
    }
    Invoke-Expression "& `"$engroot\common\build.ps1`" -configuration $configuration -verbosity $verbosity $bl /p:TargetOS=$os /p:TargetArch=$architecture /p:TestArchitectures=$architecture /p:PrivateBuildTesting=$privatebuildtesting /p:LiveRuntimeDir=`"$liveRuntimeDir`" $remainingargs"

    if ($lastExitCode -ne 0) {
        exit $lastExitCode
    }
}

# Run the xunit tests
if ($test) {
    if (-not $crossbuild) {
        if ($dacMode -ne '') {
            $env:SOS_TEST_DAC_MODE=$dacMode
        }

        if ($testInterpreter) {
            $env:SOS_TEST_INTERPRETER="true"
        }

        # Build the test filter argument if provided
        # Use backslash-escaped quotes so they survive the additional quoting in tools.ps1
        $testFilterArg = ''
        if ($methodfilter -ne '') {
            $testFilterArg = "/p:TestRunnerAdditionalArguments=\`"-method $methodfilter\`""
        }
        elseif ($classfilter -ne '') {
            $testFilterArg = "/p:TestRunnerAdditionalArguments=\`"-class $classfilter\`""
        }

        # When the managed build was skipped (e.g. the test-only CI legs that download prebuilt
        # product binaries), the debuggees built by BuildDebuggees in src/tests/dirs.proj were
        # downloaded as part of TestArtifacts. Skip rebuilding them so this leg only runs tests.
        # Test runtimes are still installed locally below (cheap, ensures correct file permissions).
        $skipTestArtifactsBuild = if ($skipmanaged) { 'true' } else { 'false' }

        # The managed build normally installs the test SDK/runtimes via an InstallRuntimes.proj
        # ProjectReference. The -test step runs with Build=false, so install them explicitly here.
        if ($skipmanaged) {
            & "$engroot\common\build.ps1" `
              -restore -build `
              -projects "$engroot\InstallRuntimes.proj" `
              -configuration $configuration `
              -verbosity $verbosity `
              -ci:$ci `
              /p:TargetOS=$os `
              /p:TargetArch=$architecture
            if ($lastExitCode -ne 0) {
                exit $lastExitCode
            }
        }

        & "$engroot\common\build.ps1" `
          -test `
          -restore:$skipmanaged `
          -configuration $configuration `
          -verbosity $verbosity `
          -ci:$ci `
          /bl:$logdir\Test.binlog `
          /p:TargetOS=$os `
          /p:TargetArch=$architecture `
          /p:TestArchitectures=$architecture `
          /p:SkipTestArtifactsBuild=$skipTestArtifactsBuild `
          /p:DotnetRuntimeVersion="$dotnetruntimeversion" `
          /p:DotnetRuntimeDownloadVersion="$dotnetruntimedownloadversion" `
          /p:RuntimeSourceFeed="$runtimesourcefeed" `
          /p:RuntimeSourceFeedKey="$runtimesourcefeedkey" `
          /p:LiveRuntimeDir="$liveRuntimeDir" `
          $testFilterArg

        if ($lastExitCode -ne 0) {
            exit $lastExitCode
        }
    }
}
