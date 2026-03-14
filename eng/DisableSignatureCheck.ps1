Param(
  [switch] $Restore,
  [string] $RepoRoot
)
$windowsNode = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\"
$relevantNodeName = "MiniDumpSettings"
$relevantNode = "$windowsNode\$relevantNodeName"
$propName = "DisableAuxProviderSignatureCheck"
$stateFileDirectory = "$RepoRoot\artifacts\tmp"
$stateFileName = "$stateFileDirectory\SignatureCheck.state"

if ($Restore)
{
    if (Test-Path $stateFileName)
    {
        Write-Host "Restoring state"
        $value = Get-Content -Path $stateFileName
        Write-Host "Restoring state: Set-ItemProperty $relevantNode -Name $propName -Value $value"
        Set-ItemProperty $relevantNode -Name $propName -Value $value -Type "DWORD"
        Write-Host "Restoring state: after Set-ItemProperty"
    }
}
else
{
    $value = 0
    if (Test-Path $relevantNode)
    {
        try
        {
            Write-Host "Disabling state: Get-ItemPropertyValue -Path $relevantNode -Name $propName"
            $value = Get-ItemPropertyValue -Path $relevantNode -Name $propName
        }
        catch
        {
            Write-Host "Disabling state: exception"
        }
    }
    else
    {
        Write-Host "Disabling state: New-Item -Path $windowsNode -Name $relevantNodeName"
        New-Item -Path $windowsNode -Name $relevantNodeName | Out-Null
    }
    New-Item -Path $stateFileDirectory -Force -ItemType 'Directory' | Out-Null 
    Write-Host "Disabling state: Writing state $value file to $stateFileName"
    Out-File -Encoding ascii -InputObject $value -FilePath $stateFileName
    Write-Host "Disabling state: Set-ItemProperty $relevantNode -Name $propName -Value 1"
    Set-ItemProperty $relevantNode -Name $propName -Value 1 -Type "DWORD"
    Write-Host "Disabling state: after Set-ItemProperty"
}
