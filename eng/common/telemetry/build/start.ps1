[CmdletBinding()]
param(
  [string]$BuildUri
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"


try {
  $workItemId = Invoke-RestMethod -Uri "https://helix.dot.net/api/2018-03-14/telemetry/job/build?buildUri=$([Net.WebUtility]::UrlEncode($BuildUri))" -Method Post -ContentType "application/json" -Body "" `
    -Headers @{ 'X-Helix-Job-Token'=$env:Helix_JobToken }

  $env:Helix_WorkItemId = $workItemId
  if (& "$PSScriptRoot/../../is-vsts.ps1") {
    Write-Host "##vso[task.setvariable variable=Helix_WorkItemId]$env:Helix_WorkItemId"
  }
}
catch {
  Write-Error $_
  Write-Error $_.Exception
  exit 1
}
