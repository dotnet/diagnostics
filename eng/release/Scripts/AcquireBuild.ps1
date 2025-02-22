param(
  [Parameter(Mandatory=$true)][int] $BarBuildId,
  [Parameter(Mandatory=$true)][string] $ReleaseVersion,
  [Parameter(Mandatory=$true)][string] $DownloadTargetPath,
  [Parameter(Mandatory=$true)][string] $AzdoToken,
  [Parameter(Mandatory=$false)][string] $DarcVersion = $null,
  [switch] $help,
  [Parameter(ValueFromRemainingArguments=$true)][String[]]$properties
)
function Write-Help() {
    Write-Host "Common settings:"
    Write-Host "  -BarBuildId <value>               BAR Build ID of the diagnostics build to publish."
    Write-Host "  -ReleaseVersion <value>           Name to give the diagnostics release."
    Write-Host "  -DownloadTargetPath <value>       Path to download the build to."
    Write-Host "  -AzdoToken <value>                Azure DevOps token to use for builds queries"
    Write-Host ""
}

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if ($help -or (($null -ne $properties) -and ($properties.Contains('/help') -or $properties.Contains('/?')))) {
    Write-Help
    exit 1
}

if ($null -ne $properties) {
    Write-Host "Unexpected extra parameters: $properties."
    exit 1
}

try {
    $ci = $true

    $darc = $null
    try {
        $darc = (Get-Command darc).Source
    }
    catch{
        . $PSScriptRoot\..\..\common\tools.ps1
        $darc = Get-Darc $DarcVersion
    }

    & $darc gather-drop `
        --id $BarBuildId `
        --release-name $ReleaseVersion `
        --output-dir $DownloadTargetPath `
        --overwrite `
        --use-azure-credential-for-blobs `
        --azdev-pat $AzdoToken `
        --separated `
        --continue-on-error `
        --ci

    if ($LastExitCode -ne 0) {
        Write-Host "Error: unable to gather the assets from build $BarBuildId to $DownloadTargetPath using darc."
        Write-Host $_
        exit 1
    }

    Write-Host 'done.'
}
catch {
    Write-Host $_
}
