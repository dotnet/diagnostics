# Diagnostics Release Tool

The diagnostics release tool consists of two components. A reusable core, and the specific logic necessary for publishing the tools in this repo.

## The Core functionality

This can be shared by anyone who needs to release files to CDN and generate release manifests. It's comprised of the files in the `Common` and `Core` directories. The main concepts needed to understand.

1. The orchestrator, the `Release` class takes a list of layout workers, a list of verifiers, a publisher, and a manifest generator, and a base directory to publish. Any unhandled exception by any of the components with be logged and the orchestration process will finish.
2. The layout workers - Implement `ILayoutWorker`, and through `HandleFileAsync` they are expected to return a `LayoutWorkerResult`. It represents both if the file was handled by the worker or an error status, and an enumerable representing the set of source files to relative paths to publish along with some metadata (file type, hash, if it needs to be put in CDN, a category string, a Rid to disambiguate between platform specific assets). This allows for a single file to generate several publishing locations, or to unpack archives and distribute files the files to their respective target destinations. There's a couple caveats:
   1. Each local file should only be handled by one layout worker. If the file needs to be published to several locations, a single publisher should do it.
   2. Each file should be handled by at least one worker.
   3. There are 4 simple implementations of workers that come by default: one for NuGet packages, one for symbol packages, one for pass through blobs (take it from a location and publish it somewhere), and one that unzips and tries to place all files within.
3. Verifiers - they get a lot of all files that will get published with metadata and target path. The idea is to provide an extension point to verify all and only expected files get published. Currently this functionality is not hooked up.
4. The publishers - These are supposed take a map of local source path to relative path from publishing location and publish the file. They return the string of the URI where the file got published. A sample one provided published to a network share. 
5. A manifest generator (`IManifestGenerator`) will receive all the files will have the opportunity to produce a manifest in the format sees fit with all the information provided by the publishing process in a stream that's handed back to the orchestrator. The orchestrator will save the contents of such manifest to file and save it at the root of the publishing location.

## The Diagnostics Specifics.

1. The diagnostics repo uses only the publishers and layout workers that are included in the core.
2. The repository uses a `darc` generated drop to collect the assets to release. It's important to note only assets marked as shipping will be considered.
  - Anything that's a NuGet package will get stored in the `NugetAssets` folder of the release.
  - Anything that's a symbol package will get stored in the `SymbolNugetAssets` folder of the release.
  - Single file global tools will get stored in the `ToolBundleAssets` directory under the specific tool's RID.
3. The manifest generator is specific to the diagnostics repo, but it could easily be used in other places with some modification. Particularly the `PublishInstructions` sections can be used to publish files to CDN using the ESRP infrastructure after proper onboarding, as the repository uses the generic format the engineering team requires.  
 
   The manifest in the diagnostics repo consists of a JSON file with four sections:
    - A generic metadata section consisting of key value pairs of useful build data (commit, branch, version, date, etc).
    - A `PublishInstructions` section - A list containing each file to be published to our CDN. Each file has the path where it got published, its content's SHA512, the relative path to be stored in the CDN, and if needed the AKA.ms link to use. We specify the schema for our the download links in the `tool-list.json`, which we pass in to the manifest generator constructor.
    - A `ToolBundleAssets` section - A list containing each single file tool published. Each item containing the name of the tool, its RID, the path relative to publish root, and the URI where it got published.
    - A `NugetAssets` section - A list of all the packages to publish, with each having the relative publishing path and the URI where it got published.

    A sample miniature manifest would be:

    ```json
    {
        "ReleaseVersion": "68656-20201030-04",
        "RepoUrl": "https://github.com/dotnet/diagnostics",
        "Branch": "refs/heads/release/stable",
        "Commit": "4d281c71a14e6226ab0bf0c98687db4a5c4217e3",
        "DateProduced": "2020-10-23T13:21:45\u002B00:00",
        "BuildNumber": "20201022.2",
        "BarBuildId": 68656,
        "PublishInstructions": [
            {
                "FilePath": "\\\\random\\share\\68656-20201030-04\\ToolBundleAssets\\linux-arm\\dotnet-counters",
                "Sha512": "D6722205EA22BC0D1AA2CDCBF4BAF171A1D7D727E4804749DE74C9099230B8EE643A51541085B631EF276DAF85EEC61C2340461A3B37BAE9C26BE7291BE621CC",
                "PublishUrlSubPath": "68656-20201030-04/4A413B1991AD089C3E1A2E921A4BB24F4DA1051695EABA03163E7A8A93B7A29F/dotnet-counters",
                "AkaMsLink": "https://aka.ms/dotnet-counters/linux-arm"
            }
        ],
        "ToolBundleAssets": [
            {
                "ToolName": "dotnet-counters",
                "Rid": "linux-arm",
                "PublishRelativePath": "ToolBundleAssets\\linux-arm\\dotnet-counters",
                "PublishedPath": "\\\\random\\share\\68656-20201030-04\\ToolBundleAssets\\linux-arm\\dotnet-counters"
            }
        ],
        "NugetAssets": [
            {
                "PublishRelativePath": "NugetAssets\\dotnet-counters.5.0.152202.nupkg",
                "PublishedPath": "\\\\random\\share\\68656-20201030-04\\ToolBundleAssets\\linux-arm\\dotnet-counters"
            }
        ]
    }
    ```
