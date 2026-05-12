Param(
  [switch] $Restore,
  [string] $RepoRoot
)

$windowsNode = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\"
$relevantNodeName = "MiniDumpSettings"
$relevantNode = "$windowsNode\$relevantNodeName"
$propName = "DisableAuxProviderSignatureCheck"

$auxiliaryNodeName = "MiniDumpAuxiliaryDlls"
$auxiliaryNode = "$windowsNode\$auxiliaryNodeName"
$knownNodeName = "KnownManagedDebuggingDlls"
$knownNode = "$windowsNode\$knownNodeName"

$stateFileDirectory = "$RepoRoot\artifacts\tmp"
$stateFileName = "$stateFileDirectory\SignatureCheck.state"

if ($Restore)
{
    if (Test-Path $stateFileName)
    {
        Write-Host "Restoring state from $stateFileName"
        $state = Get-Content -Path $stateFileName -Raw | ConvertFrom-Json

        # Restore DisableAuxProviderSignatureCheck
        Write-Host "Restoring state: Set-ItemProperty $relevantNode -Name $propName -Value $($state.DisableCheckPrior)"
        Set-ItemProperty $relevantNode -Name $propName -Value $state.DisableCheckPrior -Type "DWORD"

        # Remove added KnownManagedDebuggingDlls values
        if ($state.AddedKnownValues -and (Test-Path $knownNode))
        {
            foreach ($name in $state.AddedKnownValues)
            {
                Write-Host "Restoring state: Remove KnownManagedDebuggingDlls '$name'"
                Remove-ItemProperty -Path $knownNode -Name $name -ErrorAction SilentlyContinue
            }
        }

        # Remove added MiniDumpAuxiliaryDlls values
        if ($state.AddedAuxiliaryValues -and (Test-Path $auxiliaryNode))
        {
            foreach ($name in $state.AddedAuxiliaryValues)
            {
                Write-Host "Restoring state: Remove MiniDumpAuxiliaryDlls '$name'"
                Remove-ItemProperty -Path $auxiliaryNode -Name $name -ErrorAction SilentlyContinue
            }
        }

        Remove-Item -Path $stateFileName -Force
        Write-Host "Restoring state: complete"
    }
    else
    {
        Write-Host "No state file found at $stateFileName, nothing to restore"
    }
}
else
{
    # Save prior DisableAuxProviderSignatureCheck value
    $disableCheckPrior = 0
    if (Test-Path $relevantNode)
    {
        try
        {
            Write-Host "Disabling state: Get-ItemPropertyValue -Path $relevantNode -Name $propName"
            $disableCheckPrior = Get-ItemPropertyValue -Path $relevantNode -Name $propName
        }
        catch
        {
            Write-Host "Disabling state: property not found, defaulting to 0"
        }
    }
    else
    {
        Write-Host "Disabling state: New-Item -Path $windowsNode -Name $relevantNodeName"
        New-Item -Path $windowsNode -Name $relevantNodeName | Out-Null
    }

    # Find test runtime directories and register DACs
    $runtimeBasePath = "$RepoRoot\artifacts\dotnet-test\shared\Microsoft.NETCore.App"
    $addedKnown = @()
    $addedAux = @()

    if (Test-Path $runtimeBasePath)
    {
        # Ensure registry nodes exist
        if (-not (Test-Path $auxiliaryNode))
        {
            Write-Host "Disabling state: New-Item -Path $windowsNode -Name $auxiliaryNodeName"
            New-Item -Path $windowsNode -Name $auxiliaryNodeName | Out-Null
        }
        if (-not (Test-Path $knownNode))
        {
            Write-Host "Disabling state: New-Item -Path $windowsNode -Name $knownNodeName"
            New-Item -Path $windowsNode -Name $knownNodeName | Out-Null
        }

        foreach ($dir in (Get-ChildItem -Path $runtimeBasePath -Directory))
        {
            $dacPath = Join-Path $dir.FullName "mscordaccore.dll"
            $runtimeDllPath = Join-Path $dir.FullName "coreclr.dll"

            if (-not (Test-Path $dacPath))
            {
                continue
            }

            # Only add if not already present
            $existingKnown = Get-ItemProperty -Path $knownNode -Name $dacPath -ErrorAction SilentlyContinue
            if (-not ($existingKnown -and $existingKnown.PSObject.Properties[$dacPath]))
            {
                Write-Host "Disabling state: Register KnownManagedDebuggingDlls '$dacPath'"
                Set-ItemProperty -Path $knownNode -Name $dacPath -Value 0 -Type DWord
                $addedKnown += $dacPath
            }

            $existingAux = Get-ItemProperty -Path $auxiliaryNode -Name $runtimeDllPath -ErrorAction SilentlyContinue
            if (-not ($existingAux -and $existingAux.PSObject.Properties[$runtimeDllPath]))
            {
                Write-Host "Disabling state: Register MiniDumpAuxiliaryDlls '$runtimeDllPath' -> '$dacPath'"
                Set-ItemProperty -Path $auxiliaryNode -Name $runtimeDllPath -Value $dacPath -Type String
                $addedAux += $runtimeDllPath
            }
        }
    }
    else
    {
        Write-Host "Disabling state: Runtime path not found at $runtimeBasePath, skipping DAC registration"
    }

    # Save state
    New-Item -Path $stateFileDirectory -Force -ItemType 'Directory' | Out-Null
    $state = @{
        DisableCheckPrior = $disableCheckPrior
        AddedKnownValues = $addedKnown
        AddedAuxiliaryValues = $addedAux
    }
    $state | ConvertTo-Json -Depth 3 | Out-File -Encoding ascii -FilePath $stateFileName
    Write-Host "Disabling state: Saved state to $stateFileName"

    # Set the disable flag
    Write-Host "Disabling state: Set-ItemProperty $relevantNode -Name $propName -Value 1"
    Set-ItemProperty $relevantNode -Name $propName -Value 1 -Type "DWORD"
    Write-Host "Disabling state: complete"
}
