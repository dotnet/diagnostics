# Single File Diagnostic Tools

All of our diagnostic tools have been distributed so far as [global tools](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools). These provide a great user experience as they provide clear ways to easily download, update, and configure the scope of the tools. However, these require a full .NET SDK which is often not available in some scenarios like production machines. For these scenarios we provide a single file distribution mechanism that only requires a runtime to be available on the target machine. Other than the installation instructions, all functionality and usage remains the same. If you need help on how to use them, please refer to the [.NET Core diagnostic global tools](https://docs.microsoft.com/en-us/dotnet/core/diagnostics/#net-core-diagnostic-global-tools) documentation.

## Requirements to Run the Tools

- The tools distributed in a single file format are framework dependent applications. As such, they require a 3.1+ .NET Core runtime to be available. This means one of the following:
  - Installing the runtime globally in a well-known location using one of the different installer technologies documented in the [official .NET download page](https://dotnet.microsoft.com/download).
  - Downloading the binary archives from the [official .NET download page](https://dotnet.microsoft.com/download) and extracting them to a directory. Afterwards, please set `DOTNET_ROOT` to the path of the extraction.
  - Using the [dotnet-install scripts](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script) and setting `DOTNET_ROOT` to the installation directory. This mechanism is not recommended for development or production environments, but it's ideal for environments like CI machines.
  - The workload is being run in a container with the runtime available. This could be either because the workload is running on one of the [official .NET runtime containers](https://hub.docker.com/_/microsoft-dotnet-runtime) or on a custom-built image that installs it at build time.
- These tools are bundles that will inflate on the target machine and will extract files to disk as necessary. The extraction folder is by default within the `TEMP` directory. In case `TEMP` is not available in the target environment or there's a desire to control such directory for reasons such as clean up, set `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to a path that can be used in lieu of `TEMP`.

## Obtaining the Tools

### Downloading the latest available release of a tool

The latest release of the tools is always available at a link that follows the following schema:

```
https://aka.ms/<TOOL NAME>/<TARGET PLATFORM RUNTIME IDENTIFIER>
```

The tools we support are:
- `dotnet-counters`
- `dotnet-dump`
- `dotnet-gcdump`
- `dotnet-sos`
- `dotnet-trace` 
  
The supported [runtime identifiers](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog) are:

- `win-arm`
- `win-arm64`
- `win-x64`
- `win-x86`
- `osx-x64`
- `linux-arm`.
- `linux-arm64`
- `linux-musl-arm64`
- `linux-x64`
- `linux-musl-x64`

For example, the latest release of `dotnet-trace` for Windows x64 will be available at https://aka.ms/dotnet-trace/win-x64.

To download these tools you can either use a browser or use a command line utility like `wget` or `curl`. It's important to note that the file name is returned in the `content-disposition` header of the request. If the utility you use to download the tools respects the header, the file will be saved with the name of the tool downloaded; otherwise you might need to rename the tool. A few examples are:

1. *`curl` (available on Windows after 1706, macOS, and several distributions of Linux)*
```sh
curl -JLO https://aka.ms/dotnet-dump/win-x64
```

2. *`wget`*
```sh
wget --content-disposition https://aka.ms/dotnet-dump/linux-x64
```

3. *`pwsh` (PowerShell core)*
```powershell
$resp = Invoke-WebRequest -Uri "https://aka.ms/dotnet-dump/win-x86"
$header = [System.Net.Http.Headers.ContentDispositionHeaderValue]::Parse($resp.Headers.'content-disposition')
[System.IO.File]::WriteAllBytes($header.FileName, $resp.content)
```

### Past Releases and Checksum Validation

Each release in the [releases section](https://github.com/dotnet/diagnostics/releases) of the repository contains a table of stable links for our tools starting release `v5.0.152202`. Additionally, there's a CSV available as an attachment containing all the stable links and SHA512 checksums in case the downloaded files need to be validated.
