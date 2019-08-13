.NET Core Diagnostics Repo
==========================

This repository contains the source code for various .NET Core runtime diagnostic tools. It currently contains SOS, the managed portion of SOS, the lldb SOS plugin and various global diagnostic tools. The goals of this repo is to build SOS and the lldb SOS plugin for the portable (glibc based) Linux platform (Centos 7) and the platforms not supported by the portable (musl based) build (Centos 6, Alpine, and macOS) and to test across various indexes in a very large matrix: OSs/distros (Centos 6/7, Ubuntu, Alpine, Fedora, Debian, RHEL 7.2), architectures (x64, x86, arm, arm64), lldb versions (3.9, 4.0, 5.0, 6.0) and .NET Core versions (1.1, 2.0.x, 2.1).

Another goal to make it easier to obtain a version of lldb (currently 3.9) with scripts and documentation for platforms/distros like Centos, Alpine, Fedora, etc. that by default provide really old versions.

This repo will also allow out of band development of new SOS and lldb plugin features like symbol server support for the .NET Core runtime and solve the source build problem having SOS.NETCore (managed portion of SOS) in the coreclr repo.

See the [GitHub Release tab](https://github.com/dotnet/diagnostics/releases) for notes on SOS and diagnostic tools releases.

--------------------------
## Building the Repository

The build depends on Git, CMake, Python and of course a C++ compiler.  Once these prerequisites are installed
the build is simply a matter of invoking the 'build' script (`build.cmd` or `build.sh`) at the base of the
repository.

The details of installing the components differ depending on the operating system.  See the following
pages based on your OS.  There is no cross-building across OS (only for ARM, which is built on X64).
You have to be on the particular platform to build that platform.

To install the platform's prerequisites and build:

 * [Windows Instructions](documentation/building/windows-instructions.md)
 * [Linux Instructions](documentation/building/linux-instructions.md)
 * [MacOS Instructions](documentation/building/osx-instructions.md)
 * [FreeBSD Instructions](documentation/building/freebsd-instructions.md)
 * [NetBSD Instructions](documentation/building/netbsd-instructions.md)

## Getting lldb

Getting a version of lldb that works for your platform can be a problem sometimes. The version has to be at least 3.9 or greater because of a bug running SOS on a core dump that was fixed. Some Linux distros like Ubuntu it is easy as `sudo apt-get install lldb-3.9 python-lldb-3.9`. On other distros, you will need to build lldb. The directions below should give you some guidance.

* [Linux Instructions](documentation/lldb/linux-instructions.md)
* [MacOS Instructions](documentation/lldb/osx-instructions.md)
* [FreeBSD Instructions](documentation/lldb/freebsd-instructions.md)
* [NetBSD Instructions](documentation/lldb/netbsd-instructions.md)

## Installing SOS

* [Linux and MacOS Instructions](documentation/installing-sos-instructions.md)
* [Windows Instructions](documentation/installing-sos-windows-instructions.md)

## Using SOS

* [SOS debugging for Linux/MacOS](documentation/sos-debugging-extension.md)
* [SOS debugging for Windows](documentation/sos-debugging-extension-windows.md)
* [Debugging a core dump](documentation/debugging-coredump.md)

## Tools

* [dotnet-dump](documentation/dotnet-dump-instructions.md) - Dump collection and analysis utility.
* [dotnet-trace](documentation/dotnet-trace-instructions.md) - Enable the collection of events for a running .NET Core Application to a local trace file.
* [dotnet-counters](documentation/dotnet-counters-instructions.md) - Monitor performance counters of a .NET Core application in real time. 

## New Features

Symbol server support - The `setsymbolserver` command enables downloading the symbol files (portable PDBs) for managed assemblies during commands like `clrstack`, etc. See `soshelp setsymbolserver` for more details.

    (lldb) setsymbolserver -ms

Before executing the "bt" command to dump native frames to load the native symbols (for live debugging only):

    (lldb) loadsymbols

## Useful Links

* [FAQ](documentation/FAQ.md) - Frequently asked questions.
* [The LLDB Debugger](http://lldb.llvm.org/index.html) - More information about lldb.
* [SOS](https://msdn.microsoft.com/en-us/library/bb190764(v=vs.110).aspx) - More information about SOS.
* [Debugging CoreCLR](https://github.com/dotnet/coreclr/blob/master/Documentation/building/debugging-instructions.md) - Instructions for debugging .NET Core and the CoreCLR runtime.
* [dotnet/coreclr](https://github.com/dotnet/coreclr) - Source for the .NET Core runtime.
* [Official Build Instructions](documentation/building/official-build-instructions.md) - Internal official build instructions.

[//]: # (Begin current test results)

## Build Status

[![Build Status](https://dnceng.visualstudio.com/public/_apis/build/status/dotnet/diagnostics/diagnostics-public-ci?branchName=master)](https://dnceng.visualstudio.com/public/_build/latest?definitionId=72&branchName=master)

[//]: # (End current test results)


## License

The diagnostics repository is licensed under the [MIT license](LICENSE.TXT). This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).  For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

