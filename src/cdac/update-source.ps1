[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $RuntimeRoot,

    [string] $Commit = 'HEAD'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimeRoot = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$commit = (& git -C $runtimeRoot rev-parse "$Commit^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
    throw "Cannot resolve runtime commit '$Commit'."
}

$temporaryRoot = Join-Path $PSScriptRoot ".runtime-$([Guid]::NewGuid().ToString('N'))"
$snapshot = Join-Path $temporaryRoot 'snapshot'
$archive = Join-Path $temporaryRoot 'runtime.tar'

try {
    New-Item -ItemType Directory -Path $snapshot | Out-Null

    $includes = @(
        'src/native/managed/cdac'
        'eng/generatorProjects.targets'
        'src/libraries/Common/src/System/HResults.cs'
        'src/coreclr/tools/aot/ILCompiler.Reflection.ReadyToRun/NativeReader.cs'
        'src/coreclr/tools/aot/ILCompiler.Reflection.ReadyToRun/NibbleReader.cs'
        'src/coreclr/tools/aot/ILCompiler.Reflection.ReadyToRun/NativeArray.cs'
        'src/coreclr/tools/Common/Compiler/ObjectWriter/Dwarf/DwarfHelper.cs'
        'src/coreclr/tools/Common/CallingConvention'
        'src/coreclr/tools/Common/Internal/Runtime/CorConstants.cs'
        'src/coreclr/tools/Common/TypeSystem/Common/TargetArchitecture.cs'
    )

    & git -C $runtimeRoot archive --format=tar "--output=$archive" $commit -- $includes
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot archive the runtime source.'
    }

    & tar -xf $archive -C $snapshot
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot extract the runtime source.'
    }

    $cdacRoot = Join-Path $snapshot 'src\native\managed\cdac'
    @(
        'cmake'
        'inc'
        'scripts'
        'tests'
        'tools'
        'IData.md'
        'README.md'
        'cdac.slnx'
    ) | ForEach-Object {
        $path = Join-Path $cdacRoot $_
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }

    Get-ChildItem -LiteralPath $cdacRoot -Directory | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.md' | Remove-Item -Force
    }

    $callingConventionRoot = Join-Path $snapshot 'src\coreclr\tools\Common\CallingConvention'
    Get-ChildItem -LiteralPath $callingConventionRoot -Force |
        Where-Object { $_.PSIsContainer -or $_.Extension -ne '.cs' } |
        Remove-Item -Recurse -Force

    $destination = Join-Path $PSScriptRoot 'runtime'
    if (Test-Path -LiteralPath $destination) {
        if ((Get-Item -LiteralPath $destination -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing to replace linked destination '$destination'."
        }
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    Move-Item -LiteralPath $snapshot -Destination $destination
    [IO.File]::WriteAllText(
        (Join-Path $PSScriptRoot 'runtime-source.txt'),
        "$commit`n",
        [Text.UTF8Encoding]::new($false))
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host "Imported runtime source from commit $commit."
