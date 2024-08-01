param(
  [Parameter(Mandatory=$true)][string] $ManifestPath,
  [Parameter(Mandatory=$true)][string] $StagingPath,
  [Parameter(Mandatory=$false)][string] $DelegationSasToken,
  [switch] $help,
  [Parameter(ValueFromRemainingArguments=$true)][String[]]$properties
)

function Write-Help() {
    Write-Host "Publish packages specified in a manifest. This should not be used for large manifests."
    Write-Host "Common settings:"
    Write-Host "  -ManifestPath <value>     Path to a publishing manifest where the NuGet packages to publish can be found."
    Write-Host "  -StagingPath <value>      Directory containing the staged assets from blob storage."
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
    Write-Error "Error: unable to find manifest at '$ManifestPath'."
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

foreach ($nugetPack in $manifestJson.NugetAssets)
{
    $packagePath = Join-Path $StagingPath $nugetPack.PublishRelativePath
    if (!(Test-Path $packagePath))
    {
        Write-Error "Error: unable to find package at '$packagePath'."
        continue
    }
    Add-Member -InputObject $nugetPack -MemberType NoteProperty -Name LocalPath -Value $packagePath
}

$toolHashToLocalPath = @{}

foreach ($tool in $manifestJson.ToolBundleAssets)
{
    $toolPath = Join-Path $StagingPath $tool.PublishRelativePath
    if (!(Test-Path $toolPath))
    {
        Write-Error "Error: unable to find package at '$toolPath'."
        continue
    }
    Add-Member -InputObject $tool -MemberType NoteProperty -Name LocalPath -Value $toolPath
    $toolHashToLocalPath.Add($tool.Sha512, $toolPath)
}

foreach ($asset in $manifestJson.PublishInstructions)
{
    $remotePath = $asset.FilePath

    if ($DelegationSasToken -ne "")
    {
        $remotePath = "$remotePath?$DelegationSasToken"
    }

    Add-Member -InputObject $asset -MemberType NoteProperty -Name RemotePath -Value $remotePath
    $asset.FilePath = $toolHashToLocalPath[$asset.Sha512]
}

Copy-Item $ManifestPath "$ManifestPath.bak"
$manifestJson | ConvertTo-Json | %{ $_.Replace('\u0026' ,'&') } | Set-Content -Path $ManifestPath
