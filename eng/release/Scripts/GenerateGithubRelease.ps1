param(
  [Parameter(Mandatory=$true)][string] $ManifestPath,
  [Parameter(Mandatory=$false)][string] $ReleaseNotes,
  [Parameter(Mandatory=$true)][string] $GhOrganization,
  [Parameter(Mandatory=$true)][string] $GhRepository,
  [Parameter(Mandatory=$false)][string] $GhCliLink = "https://github.com/cli/cli/releases/download/v1.2.0/gh_1.2.0_windows_amd64.zip",
  [Parameter(Mandatory=$true)][string] $TagName,
  [bool] $DraftRelease = $false,
  [switch] $help,
  [Parameter(ValueFromRemainingArguments=$true)][String[]]$properties
)
function Write-Help() {
    Write-Host "Publish release to GitHub. Expects an environtment variable GITHUB_TOKEN to perform auth."
    Write-Host "Common settings:"
    Write-Host "  -ManifestPath <value>       Path to a publishing manifest."
    Write-Host "  -ReleaseNotes <value>       Path to release notes."
    Write-Host "  -GhOrganization <value>     GitHub organization the repository lives in."
    Write-Host "  -GhRepository <value>       GitHub repository in the organization to create the release on."
    Write-Host "  -GhCliLink <value>          GitHub CLI download link."
    Write-Host "  -TagName <value>            Tag to use for the release."
    Write-Host "  -DraftRelease               Stage the release, but don't make it public yet."
    Write-Host ""
}
function Get-ReleaseNotes()
{
    if ($ReleaseNotes)
    {
        if (!(Test-Path $ReleaseNotes))
        {
            Write-Error "Error: unable to find notes at $ReleaseNotes."
            exit 1
        }

        return Get-Content -Raw -Path $ReleaseNotes
    }
}

function Get-DownloadLinksAndChecksums($manifest)
{

    $linkTable = "<details>`n"
    $linkTable += "<summary>Packages released to NuGet</summary>`n`n"

    foreach ($nugetPackage in $manifest.NugetAssets)
    {
        $packageName = Split-Path $nugetPackage.PublishRelativePath -Leaf
        $linkTable += "- ``" + $packageName  + "```n"
    }

    $linkTable += "</details>`n`n"

    $filePublishData = @{}
    $manifest.PublishInstructions | %{ $filePublishData.Add($_.FilePath, $_) }

    $sortedTools = $manifest.ToolBundleAssets | Sort-Object -Property @{ Expression = "Rid" }, @{ Expression = "ToolName" }

    $linkTable += "<details>`n"
    $linkTable += "<summary>Global Tools - Single File Links</summary>`n`n"
    $linkTable += "*Note*: All Windows assets are signed with a trusted Microsoft Authenticode Certificate. To verify `
        integrity for Linux and macOS assets check the CSV in the assets section of the release for their SHA512 hashes.`n"
    $linkTable += "| Tool | Platform | Download Link |`n"
    $linkTable += "|:---:|:---:|:---:|`n"

    $checksumCsv = "`"ToolName`",`"Rid`",`"DownloadLink`",`"Sha512`"`n"

    foreach ($toolBundle in $sortedTools)
    {
        $hash = $filePublishData[$toolBundle.PublishedPath].Sha512
        $name = $toolBundle.ToolName
        $rid = $toolBundle.Rid

        $link = "https://download.visualstudio.microsoft.com/download/pr/" + $filePublishData[$toolBundle.PublishedPath].PublishUrlSubPath
        $linkTable += "| $name | $rid | [Download]($link) |`n";

        $checksumCsv += "`"$name`",`"$rid`",`"$link`",`"$hash`"`n"
    }

    $linkTable += "</details>`n"
    return $linkTable, $checksumCsv
}

function Post-GithubRelease($manifest, [string]$releaseBody, [string]$checksumCsvBody)
{
    $extractionPath = New-TemporaryFile | % { Remove-Item $_; New-Item -ItemType Directory -Path $_ }
    $zipPath = Join-Path $extractionPath "ghcli.zip"
    $ghTool = [IO.Path]::Combine($extractionPath, "bin", "gh.exe")

    Write-Host "Downloading GitHub CLI from $GhCliLink."
    try
    {
        $progressPreference = 'silentlyContinue'
        Invoke-WebRequest $GhCliLink -OutFile $zipPath
        Expand-Archive -Path $zipPath -DestinationPath $extractionPath
        $progressPreference = 'Continue'
    }
    catch 
    {
        Write-Error "Unable to get GitHub CLI for release"
        exit 1
    }

    if (!(Test-Path $ghTool))
    {
        Write-Error "Error: unable to find GitHub tool at expected location."
        exit 1
    }

    if (!(Test-Path env:GITHUB_TOKEN))
    {
        Write-Error "Error: unable to find GitHub PAT. Please set in GITHUB_TOKEN."
        exit 1
    }

    $extraParameters = @()

    if ($DraftRelease -eq $true)
    {
        $extraParameters += '-d'
    }

    $releaseNotes = "release_notes.md"
    $csvManifest = "checksums.csv"

    Set-Content -Path $releaseNotes -Value $releaseBody
    Set-Content -Path $csvManifest -Value $checksumCsvBody

    if (-Not (Test-Path $releaseNotes)) {
        Write-Error "Unable to find release notes"
    }

    if (-Not (Test-Path $csvManifest)) {
        Write-Error "Unable to find release notes"
    }

    $releaseNotes = $(Get-ChildItem $releaseNotes).FullName
    $csvManifest = $(Get-ChildItem $csvManifest).FullName
    & $ghTool release create $TagName `
        "`"$csvManifest#File Links And Checksums CSV`"" `
        --repo "`"$GhOrganization/$GhRepository`"" `
        --title "`"Diagnostics Release - $TagName`"" `
        --notes-file "`"$releaseNotes`"" `
        --target $manifest.Commit `
        ($extraParameters -join ' ')

    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Write-Error "Something failed in creating the release."
        exit 1
    }
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
$linkCollection, $checksumCsvContent = Get-DownloadLinksAndChecksums $manifestJson

$releaseNotesText = Get-ReleaseNotes
$releaseNotesText += "`n`n" + $linkCollection

Post-GithubRelease -manifest $manifestJson `
                -releaseBody $releaseNotesText `
                -checksumCsvBody $checksumCsvContent
