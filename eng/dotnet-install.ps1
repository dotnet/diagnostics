[CmdletBinding(PositionalBinding=$false)]
Param(
  [string] $InstallDir="<auto>",
  [string] $Architecture="<auto>",
  [string] $Version = "Latest",
  [string] $Runtime,
  [string] $RuntimeSourceFeed = "",
  [string] $RuntimeSourceFeedKey = "",
  [switch] $SkipNonVersionedFiles,
  [switch] $NoPath
)

. $PSScriptRoot\common\tools.ps1

try {
  if ($Runtime) {
    InstallDotNet $InstallDir $Version $Architecture $Runtime $SkipNonVersionedFiles -RuntimeSourceFeed $RuntimeSourceFeed -RuntimeSourceFeedKey $RuntimeSourceFeedKey
  }
  else {
    InstallDotNetSdk $InstallDir $Version $Architecture
  }
}
catch {
  Write-Host $_.ScriptStackTrace
  Write-PipelineTelemetryError -Category 'InitializeToolset' -Message $_
  ExitWithExitCode 1
}

ExitWithExitCode 0
