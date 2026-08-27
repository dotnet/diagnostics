Param(
  [switch] $Restore,
  [string] $RepoRoot,
  [string] $StateDirectory,
  [ValidateSet("x64", "x86")]
  [string] $TargetArchitecture = "x64"
)

$ErrorActionPreference = "Stop"

$softwareNode = if ($TargetArchitecture -eq "x86") { "HKLM:\SOFTWARE\WOW6432Node" } else { "HKLM:\SOFTWARE" }
$windowsNode = "$softwareNode\Microsoft\Windows NT\CurrentVersion\"
$relevantNodeName = "MiniDumpSettings"
$relevantNode = "$windowsNode\$relevantNodeName"
$propName = "DisableAuxProviderSignatureCheck"

$auxiliaryNodeName = "MiniDumpAuxiliaryDlls"
$auxiliaryNode = "$windowsNode\$auxiliaryNodeName"
$knownNodeName = "KnownManagedDebuggingDlls"
$knownNode = "$windowsNode\$knownNodeName"

$stateFileDirectory = if ($StateDirectory) { $StateDirectory } else { "$RepoRoot\artifacts\tmp" }
$stateFileName = "$stateFileDirectory\SignatureCheck.$TargetArchitecture.state"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
{
    throw "Administrative privileges are required to configure DbgHelp dump settings."
}

if ($Restore)
{
    if (Test-Path $stateFileName)
    {
        Write-Host "Restoring state from $stateFileName"
        $state = Get-Content -Path $stateFileName -Raw | ConvertFrom-Json

        # Restore DisableAuxProviderSignatureCheck
        if ($state.DisableCheckExisted)
        {
            Write-Host "Restoring state: Set-ItemProperty $relevantNode -Name $propName -Value $($state.DisableCheckPrior)"
            Set-ItemProperty $relevantNode -Name $propName -Value $state.DisableCheckPrior -Type "DWORD"
            if ((Get-ItemPropertyValue -Path $relevantNode -Name $propName) -ne $state.DisableCheckPrior)
            {
                throw "Failed to restore $relevantNode\$propName"
            }
        }
        else
        {
            Write-Host "Restoring state: Remove-ItemProperty $relevantNode -Name $propName"
            Remove-ItemProperty -Path $relevantNode -Name $propName -ErrorAction SilentlyContinue
        }

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
    $disableCheckExisted = $false
    if (Test-Path $relevantNode)
    {
        $existingSetting = Get-ItemProperty -Path $relevantNode -Name $propName -ErrorAction SilentlyContinue
        if ($existingSetting -and $existingSetting.PSObject.Properties[$propName])
        {
            $disableCheckExisted = $true
            $disableCheckPrior = $existingSetting.$propName
        }
    }

    if ($disableCheckExisted)
    {
        Write-Host "Disabling state: captured prior $relevantNode\$propName value $disableCheckPrior"
    }
    else
    {
        Write-Host "Disabling state: registry value not found"
    }

    $addedKnown = @()
    $addedAux = @()
    $state = @{
        DisableCheckExisted = $disableCheckExisted
        DisableCheckPrior = $disableCheckPrior
        AddedKnownValues = $addedKnown
        AddedAuxiliaryValues = $addedAux
    }

    function Save-State
    {
        New-Item -Path $stateFileDirectory -Force -ItemType "Directory" | Out-Null
        $state.AddedKnownValues = $addedKnown
        $state.AddedAuxiliaryValues = $addedAux
        $state | ConvertTo-Json -Depth 3 | Out-File -Encoding ascii -FilePath $stateFileName
    }

    # Persist rollback state before the first registry mutation.
    Save-State
    Write-Host "Disabling state: Saved state to $stateFileName"

    if (-not (Test-Path $relevantNode))
    {
        Write-Host "Disabling state: New-Item -Path $windowsNode -Name $relevantNodeName"
        New-Item -Path $windowsNode -Name $relevantNodeName | Out-Null
    }

    # Find test runtime directories and register DACs
    $runtimeBasePath = "$RepoRoot\artifacts\dotnet-test\shared\Microsoft.NETCore.App"

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
                $addedKnown += $dacPath
                Save-State
                Set-ItemProperty -Path $knownNode -Name $dacPath -Value 0 -Type DWord
            }

            $existingAux = Get-ItemProperty -Path $auxiliaryNode -Name $runtimeDllPath -ErrorAction SilentlyContinue
            if (-not ($existingAux -and $existingAux.PSObject.Properties[$runtimeDllPath]))
            {
                Write-Host "Disabling state: Register MiniDumpAuxiliaryDlls '$runtimeDllPath' -> '$dacPath'"
                $addedAux += $runtimeDllPath
                Save-State
                Set-ItemProperty -Path $auxiliaryNode -Name $runtimeDllPath -Value $dacPath -Type String
            }
        }
    }
    else
    {
        Write-Host "Disabling state: Runtime path not found at $runtimeBasePath, skipping DAC registration"
    }

    # Set the disable flag
    Write-Host "Disabling state: Set-ItemProperty $relevantNode -Name $propName -Value 1"
    Set-ItemProperty $relevantNode -Name $propName -Value 1 -Type "DWORD"
    if ((Get-ItemPropertyValue -Path $relevantNode -Name $propName) -ne 1)
    {
        throw "Failed to set $relevantNode\$propName"
    }
    Write-Host "Disabling state: complete"
}
