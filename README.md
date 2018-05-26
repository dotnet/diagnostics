.NET Core Diagnostics Repo
==========================

**Currently under construction**

This repository contains the source code for various .NET Core runtime diagnostic tools. It currently contains SOS, the managed portion of SOS and the lldb SOS plugin. One of goals of this repo is to build SOS and the lldb SOS plugin for the portable Linux platform (Centos 7) and the platforms not supported by the portable build (Centos 6, Alpine, eventually macOS) and to test across various indexes in a very large matrix: OSs/distros (Centos 6/7, Ubuntu, Alpine, Fedora, Debian, RHEL 7.2), architectures (x64, x86, arm, arm64), lldb versions (3.9, 4.0, 5.0, 6.0) and even .NET Core versions (1.1, 2.0.x, 2.1). 

Another goal to make it easier to obtain a version of lldb (currently 3.9) with scripts and documentation for platforms/distros like Centos, Alpine, Fedora, etc. that by default provide really old versions. 

This repo will also allow out of band development of new SOS and lldb plugin features like symbol server support for the .NET Core runtime and solve the source build problem having SOS.NETCore (managed portion of SOS) in the coreclr repo.

--------------------------
## Building the Repository

The build depends on Git, CMake, Python and of course a C++ compiler.  Once these prerequisites are installed
the build is simply a matter of invoking the 'build' script (`build.cmd` or `build.sh`) at the base of the 
repository.  

The details of installing the components differ depending on the operating system.  See the following
pages based on your OS.  There is no cross-building across OS (only for ARM, which is built on X64). 
You have to be on the particular platform to build that platform.  

To install the platform's prerequisites:

 * [Windows Instructions](Documentation/building/windows-instructions.md)
 * [Linux Instructions](Documentation/building/linux-instructions.md)
 * [macOS Instructions](Documentation/building/osx-instructions.md)
 * [FreeBSD Instructions](Documentation/building/freebsd-instructions.md) 
 * [NetBSD Instructions](Documentation/building/netbsd-instructions.md)

To build under Windows, run build.cmd from the root of the repository:

```bat
.\build.cmd 

[Lots of build spew]

BUILD: Repo sucessfully built.
BUILD: Product binaries are available at c:\git\diagnostics\artifacts\bin\Windows_NT.x64.Debug	
```

To build under Linux, MacOS, FreeBSD, or NetBSD, run build.sh from the root of the repository:

```sh
$ ./build.sh

[Lots of build spew]

BUILD: Repo sucessfully built.
BUILD: Product binaries are available at /home/mikem/diagnostics/artifacts/bin/Linux.x64.Debug
```

To test the resulting SOS (and plugin if xplat):

```sh
$ ./test.sh
```

## Useful Links

* [dotnet/coreclr](https://github.com/dotnet/coreclr) - Source for the .NET Core runtime.

* [Debugging CoreCLR](https://github.com/dotnet/diagnostics/blob/master/Documentation/building/debugging-instructions.md) - Instructions for debugging .NET Core and the CoreCLR runtime.

[//]: # (Begin current test results)

## Daily Builds

|    | x64 Debug|x64 Release|
|:--:|:--:|:--:|
|**Windows**|[![Build Status](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/Windows_NT_Debug/badge/icon)](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/Windows_NT_Debug/)|[![Build Status](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/Windows_NT_Release/badge/icon)](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/Windows_NT_Release/)|
|**Ubuntu 16.04**|[![Build Status](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/Ubuntu16.04_Debug/badge/icon)](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/Ubuntu16.04_Debug/)|[![Build Status](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/Ubuntu16.04_Release/badge/icon)](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/Ubuntu16.04_Release/)|
|**CentOS 7**|[![Build Status](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/CentOS7.1_Debug/badge/icon)](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/CentOS7.1_Debug/)|[![Build Status](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/CentOS7.1_Release/badge/icon)](https://ci.dot.net/job/dotnet_diagnostics/job/master/job/CentOS7.1_Release/)|

[//]: # (End current test results)



## License

The diagnostics repository is licensed under the [MIT license](LICENSE.TXT). This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).  For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

