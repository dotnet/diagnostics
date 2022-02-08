param(
  [Parameter(Mandatory=$true)][int] $BarBuildId,
  [Parameter(Mandatory=$true)][string] $ReleaseVersion,
  [Parameter(Mandatory=$true)][string] $DownloadTargetPath,
  [Parameter(Mandatory=$true)][string] $SasSuffixes,
  [Parameter(Mandatory=$true)][string] $AzdoToken,
  [Parameter(Mandatory=$true)][string] $MaestroToken,
  [Parameter(Mandatory=$true)][string] $GitHubToken,
  [Parameter(Mandatory=$false)][string] $MaestroApiEndPoint = 'https://maestro-prod.westus2.cloudapp.azure.com',
  [Parameter(Mandatory=$false)][string] $DarcVersion = $null,
  [switch] $help,
  [Parameter(ValueFromRemainingArguments=$true)][String[]]$properties
)
function Write-Help() {
    Write-Host "Common settings:"
    Write-Host "  -BarBuildId <value>               BAR Build ID of the diagnostics build to publish."
    Write-Host "  -ReleaseVersion <value>           Name to give the diagnostics release."
    Write-Host "  -DownloadTargetPath <value>       Path to download the build to."
    Write-Host "  -SasSuffixes <value>              Comma separated list of potential uri suffixes that can be used if anonymous access to a blob uri fails. Appended directly to the end of the URI. Use full SAS syntax with ?."
    Write-Host "  -AzdoToken <value>                Azure DevOps token to use for builds queries"
    Write-Host "  -MaestroToken <value>             Maestro token to use for querying BAR"
    Write-Host "  -GitHubToken <value>             GitHub token to use for querying repository information"
    Write-Host "  -MaestroApiEndPoint <value>       BAR endpoint to use for build queries."
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
        --sas-suffixes $SasSuffixes `
        --github-pat $GitHubToken `
        --azdev-pat $AzdoToken `
        --bar-uri $MaestroApiEndPoint `
        --password $MaestroToken `
        --separated `
        --verbose

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
