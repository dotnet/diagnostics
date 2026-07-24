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

        # Build the test filter argument if provided.
        # Tests run as xUnit v3 / Microsoft.Testing.Platform executables, so use the MTP
        # filter options (--filter-method / --filter-class) instead of the old xunit.console
        # -method / -class flags.
        # Use backslash-escaped quotes so they survive the additional quoting in tools.ps1.
        #
        # A filter is applied to EVERY project in the test traversal, so projects that don't
        # contain a matching test legitimately run zero tests. MTP returns exit code 8 ("zero
        # tests ran") in that case, which Arcade would otherwise treat as a failure. Append
        # --ignore-exit-code 8 so those non-matching projects don't fail the run. The trade-off
        # (a mistyped filter that matches nothing anywhere would pass silently) is covered by the
        # "at least one test ran" guard after the test run below.
        $testFilterActive = $false
        $testFilterArg = ''
        if ($methodfilter -ne '') {
            $testFilterActive = $true
            $testFilterArg = "/p:TestRunnerAdditionalArguments=\`"--filter-method $methodfilter --ignore-exit-code 8\`""
        }
        elseif ($classfilter -ne '') {
            $testFilterActive = $true
            $testFilterArg = "/p:TestRunnerAdditionalArguments=\`"--filter-class $classfilter --ignore-exit-code 8\`""
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

        # When a filter is active it is applied to every project in the traversal, so non-matching
        # projects run zero tests. --ignore-exit-code 8 (added to $testFilterArg above) keeps those
        # from failing the run; to still catch a filter that matches nothing ANYWHERE, count the
        # tests that ran after the build. Clear this run's result XMLs first so the post-run count
        # only reflects the current run (result file names embed the target framework, so stale
        # files from a previous run would not otherwise be overwritten).
        $resultsDir = Join-Path (Join-Path $artifactsdir "TestResults") $configuration
        if ($testFilterActive -and (Test-Path $resultsDir)) {
            Remove-Item (Join-Path $resultsDir "*.xml") -Force -ErrorAction SilentlyContinue
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

        # Guard against a filter that silently matches nothing (see note above): sum the test
        # counts from the xUnit result XMLs this run produced and fail if nothing ran.
        if ($testFilterActive) {
            $testsRan = 0
            if (Test-Path $resultsDir) {
                foreach ($xml in Get-ChildItem $resultsDir -Filter *.xml -File -ErrorAction SilentlyContinue) {
                    try {
                        [xml]$doc = Get-Content -LiteralPath $xml.FullName -Raw
                        foreach ($asm in @($doc.assemblies.assembly)) {
                            if ($asm -and $asm.total) { $testsRan += [int]$asm.total }
                        }
                    } catch { }
                }
            }
            if ($testsRan -eq 0) {
                Write-Host "ERROR: The test filter matched zero tests across all projects. Check the -methodfilter/-classfilter value." -ForegroundColor Red
                exit 1
            }
            Write-Host "Test filter matched $testsRan test(s) across the run." -ForegroundColor Green
        }
    }
}
