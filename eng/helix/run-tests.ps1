param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("x86", "x64", "arm64")]
    [string] $Architecture,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Windows_NT")]
    [string] $TargetOS
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
$artifactsDir = Join-Path $repoRoot "artifacts"
$bootstrapRoot = Join-Path $artifactsDir "dotnet-bootstrap"
$dotnetRoot = Join-Path $artifactsDir "dotnet-test"
$uploadRoot = if ($env:HELIX_WORKITEM_UPLOAD_ROOT) { $env:HELIX_WORKITEM_UPLOAD_ROOT } else { Join-Path $artifactsDir "helix-results" }

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:NUGET_PACKAGES = Join-Path $repoRoot ".packages"
$targetRid = "win-$Architecture"

$logDir = Join-Path $artifactsDir "log\$Configuration"
New-Item -ItemType Directory -Force -Path $logDir, $uploadRoot | Out-Null

$bootstrapSdkVersion = (Get-Content (Join-Path $repoRoot "global.json") -Raw | ConvertFrom-Json).sdk.version
& (Join-Path $repoRoot "eng\dotnet-install.ps1") `
    -NoPath `
    -Version $bootstrapSdkVersion `
    -InstallDir $bootstrapRoot
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$bootstrapDotNet = Join-Path $bootstrapRoot "dotnet.exe"
& $bootstrapDotNet msbuild (Join-Path $repoRoot "eng\InstallRuntimes.proj") `
    /restore `
    /t:InstallTestRuntimes `
    /p:Configuration=$Configuration `
    /p:ContinuousIntegrationBuild=true `
    /p:SkipBuildSdkInstall=true `
    /p:TargetArch=$Architecture `
    /p:TargetOS=$TargetOS `
    /p:TargetRid=$targetRid `
    /bl:"$logDir\InstallRuntimes.binlog"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_HOST_PATH = Join-Path $dotnetRoot "dotnet.exe"
$env:PATH = "$dotnetRoot;$env:PATH"

& $env:DOTNET_HOST_PATH msbuild (Join-Path $repoRoot "build.proj") `
    /restore `
    /t:Test `
    /p:Configuration=$Configuration `
    /p:ContinuousIntegrationBuild=true `
    /p:SkipTestArtifactsBuild=true `
    /p:TargetArch=$Architecture `
    /p:TargetOS=$TargetOS `
    /p:TargetRid=$targetRid `
    /p:TestArchitectures=$Architecture `
    /bl:"$logDir\Test.binlog"
$exitCode = $LASTEXITCODE

foreach ($result in Get-ChildItem (Join-Path $artifactsDir "TestResults") -Filter "*.xml" -File -Recurse -ErrorAction SilentlyContinue) {
    Copy-Item -Force $result.FullName (Join-Path $uploadRoot "$($result.BaseName).testResults.xml")
}

foreach ($directory in "TestResults", "log") {
    $sourceDirectory = Join-Path $artifactsDir $directory
    if (Test-Path $sourceDirectory) {
        Copy-Item -Recurse -Force $sourceDirectory $uploadRoot
    }
}

if ($exitCode -ne 0) {
    $diagnosticPaths = @(
        (Join-Path $artifactsDir "tmp\$Configuration\dumps"),
        (Join-Path $artifactsDir "tmp\$Configuration\streams")
    ) | Where-Object { Test-Path $_ }

    if ($diagnosticPaths.Count -gt 0) {
        Compress-Archive -Path $diagnosticPaths -DestinationPath (Join-Path $uploadRoot "diagnostics-dumps.zip")
    }
}

exit $exitCode
