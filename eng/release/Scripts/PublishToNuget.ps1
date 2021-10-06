param(
  [Parameter(Mandatory=$true)][string] $ManifestPath,
  [Parameter(Mandatory=$true)][string] $StagingPath,
  [Parameter(Mandatory=$true)][string] $FeedEndpoint,
  [Parameter(Mandatory=$true)][string] $FeedPat,
  [switch] $help,
  [Parameter(ValueFromRemainingArguments=$true)][String[]]$properties
)
function Write-Help() {
    Write-Host "Publish packages specified in a manifest. This should not be used for large manifests."
    Write-Host "Common settings:"
    Write-Host "  -ManifestPath <value>      Path to a publishing manifest where the NuGet packages to publish can be found."
    Write-Host "  -StagingPath <value>       Path where the assets in the manifests are laid out."
    Write-Host "  -FeedEndpoint <value>      NuGet feed to publish the packages to."
    Write-Host "  -FeedPat <value>           PAT to use in the publish process."
    Write-Host ""
}

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if ($help -or (($null -ne $properties) -and ($properties.Contains('/help') -or $properties.Contains('/?')))) {
    Write-Help
    exit 1
}

if ($null -ne $properties) {
    Write-Error "Unexpected extra parameters: $properties."
    exit 1
}

if (!(Test-Path $ManifestPath))
{
    Write-Error "Error: unable to find maifest at $ManifestPath."
    exit 1
}

$manifestSize = $(Get-ChildItem $ManifestPath).length / 1kb

# Limit size. For large manifests
if ($manifestSize -gt 500) 
{
    Write-Error "Error: Manifest $ManifestPath too large."
    exit 1
}

$manifestJson = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json

$failedToPublish = 0
foreach ($nugetPack in $manifestJson.NugetAssets)
{
    $packagePath = Join-Path $StagingPath $nugetPack.PublishRelativePath
    try
    {
        Write-Host "Downloading: $nugetPack."
        $progressPreference = 'silentlyContinue'
        Invoke-WebRequest -Uri $nugetPack.PublishedPath -OutFile (New-Item -Path $packagePath -Force)
        $progressPreference = 'Continue'

        if ($nugetPack.PSobject.Properties.Name.Contains("Sha512")-and $(Get-FileHash -Algorithm sha512 $packagePath).Hash -ne $nugetPack.Sha512) {
            Write-Host "Sha512 verification failed for $($nugetPack.PublishRelativePath)."
            $failedToPublish++
            continue
        }

        Write-Host "Publishing $packagePath."
        & "$PSScriptRoot/../../../dotnet.cmd" nuget push $packagePath --source $FeedEndpoint --api-key $FeedPat
        if ($LastExitCode -ne 0)
        {
            Write-Error "Error: unable to publish $($nugetPack.PublishRelativePath)."
            $failedToPublish++
        }
    }
    catch
    {
        Write-Error "Error: unable to publish $($nugetPack.PublishRelativePath)."
        $failedToPublish++
    }
}

if ($failedToPublish -ne 0)
{
    Write-Error "Error: $failedToPublish packages unpublished."
    exit 1
}
