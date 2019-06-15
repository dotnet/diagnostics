[cmdletbinding()]
param(
   [string]$SourceDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference="Stop"

$BinDir = "$SourceDirectory\artifacts\bin"
$SOSGalleryVersion = cat $BinDir\VersionPrefix.txt

$SOSNETCorePath = "$BinDir\SOS.NETCore\Release\netstandard2.0\publish"
$GalleryDir = "$BinDir\gallery\$SOSGalleryVersion"
$ZipFilePath = "$SourceDirectory\artifacts\packages\Release"
$ZipFile = Join-Path -Path $ZipFilePath "$SOSGalleryVersion.zip"

mkdir -Force "$GalleryDir\x64"
mkdir -Force "$GalleryDir\x86"
mkdir -Force "$GalleryDir\arm32"
mkdir -Force "$ZipFilePath"

Copy-Item $BinDir\Windows_NT.x64.Release\sos.dll $GalleryDir\x64
Copy-Item $SOSNETCorePath\*.dll $GalleryDir\x64
Copy-Item $BinDir\Windows_NT.x86.Release\sos.dll $GalleryDir\x86
Copy-Item $SOSNETCorePath\*.dll $GalleryDir\x86
Copy-Item $BinDir\Windows_NT.arm.Release\sos.dll $GalleryDir\arm32
Copy-Item $SOSNETCorePath\*.dll $GalleryDir\arm32
cat $SourceDirectory\eng\GalleryManifest.xml | %{$_ -replace "X.X.X.X","$SOSGalleryVersion"} | Set-Content $BinDir\gallery\GalleryManifest.xml

if (Test-Path $ZipFile) { rm "$ZipFile" }
Add-Type -assembly System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory("$BinDir\gallery", "$ZipFile")

