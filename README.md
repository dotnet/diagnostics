.NET Core Diagnostics Repo
==========================

This repository contains the source code for various .NET Core runtime diagnostic tools. It currently contains SOS, the managed portion of SOS, the lldb SOS plugin and various global diagnostic tools. The goals of this repo is to build SOS and the lldb SOS plugin for the portable (glibc based) Linux platform (Centos 7) and the platforms not supported by the portable (musl based) build (Centos 6, Alpine, and macOS) and to test across various indexes in a very large matrix: OSs/distros (Centos 6/7, Ubuntu, Alpine, Fedora, Debian, RHEL 7.2), architectures (x64, x86, arm, arm64), lldb versions (3.9 to 9.0) and .NET Core versions (2.1, 3.1, 5.0.x).

Another goal to make it easier to obtain a version of lldb (currently 3.9) with scripts and documentation for platforms/distros like Centos, Alpine, Fedora, etc. that by default provide really old versions.

This repo will also allow out of band development of new SOS and lldb plugin features like symbol server support for the .NET Core runtime and solve the source build problem having SOS.NETCore (managed portion of SOS) in the runtime repo.

See the [GitHub Release tab](https://github.com/dotnet/diagnostics/releases) for notes on SOS and diagnostic tools releases.

--------------------------
## Building the Repository

The build depends on Git, CMake, Python and of course a C++ compiler.  Once these prerequisites are installed
the build is simply a matter of invoking the 'build' script (`build.cmd` or `build.sh`) at the base of the
repository.

The details of installing the components differ depending on the operating system.  See the following
pages based on your OS.  There is no cross-building across OS (only for ARM, which is built on x64).
You have to be on the particular platform to build that platform.

To install the platform's prerequisites and build:

 * [Windows Instructions](documentation/building/windows-instructions.md)
 * [Linux Instructions](documentation/building/linux-instructions.md)
 * [MacOS Instructions](documentation/building/osx-instructions.md)
 * [FreeBSD Instructions](documentation/building/freebsd-instructions.md)
 * [NetBSD Instructions](documentation/building/netbsd-instructions.md)
 * [Testing on private runtime builds](documentation/privatebuildtesting.md)

## SOS and Other Diagnostic Tools

* [SOS](documentation/sos.md) - About the SOS debugger extension.
* [dotnet-dump](documentation/dotnet-dump-instructions.md) - Dump collection and analysis utility.
* [dotnet-gcdump](documentation/dotnet-gcdump-instructions.md) - Heap analysis tool that collects gcdumps of live .NET processes.
* [dotnet-trace](documentation/dotnet-trace-instructions.md) - Enable the collection of events for a running .NET Core Application to a local trace file.
* [dotnet-counters](documentation/dotnet-counters-instructions.md) - Monitor performance counters of a .NET Core application in real time. 

## Useful Links

* [FAQ](documentation/FAQ.md) - Frequently asked questions.
* [The LLDB Debugger](http://lldb.llvm.org/index.html) - More information about lldb.
* [SOS](https://msdn.microsoft.com/en-us/library/bb190764(v=vs.110).aspx) - More information about SOS.
* [Debugging CoreCLR](https://github.com/dotnet/runtime/blob/main/docs/workflow/debugging/coreclr/debugging.md) - Instructions for debugging .NET Core and the CoreCLR runtime.
* [dotnet/runtime](https://github.com/dotnet/runtime) - Source for the .NET Core runtime.
* [Official Build Instructions](documentation/building/official-build-instructions.md) - Internal official build instructions.

[//]: # (Begin current test results)

## Build Status

[![Build Status](https://dnceng.visualstudio.com/public/_apis/build/status/dotnet/diagnostics/diagnostics-public-ci?branchName=main)](https://dnceng.visualstudio.com/public/_build/latest?definitionId=72&branchName=main)

[//]: # (End current test results)


## License

The diagnostics repository is licensed under the [MIT license](LICENSE.TXT).
