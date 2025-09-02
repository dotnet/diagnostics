param(
    [switch]$skipbuild,
    [switch]$skipcopy,
    [string]$testName = "DbgShim"
)

# Save the starting directory
$startingDirectory = Get-Location

try {
    $diagnosticRepoPath="C:\Users\maxcharlamb\source\reposA\diagnostics"
    $helixFolder="C:\Users\maxcharlamb\temp\helix_folder"

    if (!$skipbuild) {
        Write-Host "Running build with tests and helix"
        & "$diagnosticRepoPath\build.cmd" "-withtests -bl -helix"
} else {
    Write-Host "Skipping build (skipbuild parameter specified)"
}

# Only run privatebuild if .dotnet-test folder doesn't exist
if (!(Test-Path "$diagnosticRepoPath\.dotnet-test")) {
    Write-Host "Running privatebuild to create .dotnet-test folder"
    & "$diagnosticRepoPath\build.cmd" "-installruntimes -skipmanaged -skipnative"
} else {
    Write-Host ".dotnet-test folder already exists, skipping privatebuild"
}

# Unzip $testName.UnitTests.zip into the helix folder
if (!$skipcopy)
{
    $zipPath = "$diagnosticRepoPath\artifacts\helix\tests\$testName.UnitTests.zip"
    if (Test-Path $zipPath) {
        # Clean up the helix folder if it exists
        if (Test-Path $helixFolder) {
            Write-Host "Cleaning up existing contents in $helixFolder"
            Remove-Item -Path "$helixFolder\*" -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            New-Item -ItemType Directory -Path $helixFolder -Force
        }
        Expand-Archive -Path $zipPath -DestinationPath $helixFolder -Force
        Write-Host "Successfully extracted $zipPath to $helixFolder"
    } else {
        Write-Warning "Zip file not found at: $zipPath"
    }
}

# Set DOTNET_ROOT environment variable
$env:DOTNET_ROOT = "$diagnosticRepoPath\.dotnet-test"

# Change to the helix folder before running dotnet
Set-Location $helixFolder
Write-Host "Changed directory to $helixFolder"

# Build dotnet arguments array for better readability
$dotnetArgs = @(
    "exec"
    "--depsfile", "$testName.UnitTests.deps.json"
    "--runtimeconfig", "$testName.UnitTests.runtimeconfig.json"
    "xunit.console.dll"
    "$testName.UnitTests.dll"
    "-noautoreporters"
    "-xml", "artifacts/TestResults/Release/$testName.UnitTests_net8.0_x64.xml"
    "-html", "artifacts/TestResults/Release/$testName.UnitTests_net8.0_x64.html"
)

# $dotnetArgs = @(
#     "test"
#     "$testName.UnitTests.dll"
#     "--results-directory", "./testresults/"
#     "--logger", "console;verbosity=detailed"
#     "--logger", "html;logfilename=testResults.html"
#     "--filter", "FullyQualifiedName~OtherCommands"
# )



# Print the command that will be executed
Write-Host "Executing command: $env:DOTNET_ROOT\dotnet.exe $($dotnetArgs -join ' ')"

& "$env:DOTNET_ROOT\dotnet.exe" @dotnetArgs

} finally {
    # Always return to the starting directory
    Set-Location $startingDirectory
    Write-Host "Returned to starting directory: $startingDirectory"
}
