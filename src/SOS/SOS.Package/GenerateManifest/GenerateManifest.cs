// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Xml.Linq;
using System.Xml.XPath;

const string ExpectedParameters = "<GalleryManifestBaseTemplate> <GalleryManifestPath> <UncompressedManifestPath> <ExtensionVersion>";

const string FilesXpathInPackageElement = "Components/BinaryComponent/Files";
const string CompressedGalleryManifestTemplate = """
<?xml version="1.0" encoding="utf-8"?>
<ExtensionPackages Version="1.0.0.0" Compression="none">
</ExtensionPackages>
""";

if (args is null || args.Length != 4)
{
    Console.Error.WriteLine("Expected parameters: {0}", ExpectedParameters);
    return -1;
}

string galleryManifestBaseTemplate = args[0];
string galleryManifestPath = args[1];
string uncompressedManifestPath = args[2];
string extensionVersion = args[3];

// Compressed and uncompressed manifests are the same for SOS except for two main differences:
// - The compressed manifest has the ExtensionPackage as a top level element, while the uncompressed manifest has ExtensionPackages as the top level element
//   with Compression="none" attribute. The uncompressed manifest contains a single ExtensionPackage element with the ExtensionPackage that would appear in
//   the compressed manifest as a child element.
// - The uncompressed manifest's ExtensionPackage contains a single File element pointing to the architecture-specific extension installed by the win SDK installer.
//   The compressed manifest's ExtensionPackage contains a File element per architecture as the extension gallery ships all of them in one.
Console.WriteLine("Generating manifests for SOS version {0}", extensionVersion);

XDocument galleryManifest = XDocument.Load(galleryManifestBaseTemplate);
XElement extensionPackageElement = galleryManifest.Root!;
extensionPackageElement.XPathSelectElement("Version")!.Value = extensionVersion;

// Create uncompressed manifest with the updated version.
XDocument uncompressedGalleryManifest = XDocument.Parse(CompressedGalleryManifestTemplate);
XElement extensionPackageInCompressedManifest = new(extensionPackageElement);
uncompressedGalleryManifest.Root!.Add(extensionPackageInCompressedManifest);

Console.WriteLine("Generating extension gallery manifest.");
// Update compressed/regular manifest to have a File element per RID since the gallery ships all architectures in one package.
XElement files = extensionPackageElement.XPathSelectElement(FilesXpathInPackageElement)!;
files.RemoveNodes();
files.Add(
    new XElement("File", new XAttribute("Architecture", "amd64"), new XAttribute("Module", "win-x64\\sos.dll")),
    new XElement("File", new XAttribute("Architecture", "x86"), new XAttribute("Module", "win-x86\\sos.dll")),
    new XElement("File", new XAttribute("Architecture", "arm64"), new XAttribute("Module", "win-arm64\\sos.dll")));

// Create folder if it doesn't exist and save the gallery manifest.
Directory.CreateDirectory(Path.GetDirectoryName(galleryManifestPath)!);
galleryManifest.Save(galleryManifestPath);

Console.WriteLine("Generating debugger layout (uncompressed) manifest.");
// Update uncompressed manifest to have a single File element pointing to the winext\sos\sos.dll path.
// The installer is responsible for installing the correct architecture-specific DLL.
XElement uncompressedFiles = extensionPackageInCompressedManifest.XPathSelectElement(FilesXpathInPackageElement)!;
uncompressedFiles.RemoveNodes();
uncompressedFiles.Add(
    new XElement("File", new XAttribute("Architecture", "Any"), new XAttribute("Module", "winext\\sos\\sos.dll"), new XAttribute("FilePathKind", "RepositoryRelative")));
Directory.CreateDirectory(Path.GetDirectoryName(uncompressedManifestPath)!);
uncompressedGalleryManifest.Save(uncompressedManifestPath);

Console.WriteLine("Done.");
return 0;
