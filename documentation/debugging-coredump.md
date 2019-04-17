Debugging Linux or MacOS Core Dump
==================================

These instructions will lead you through getting symbols, loading and debugging a Linux or MacOS core dump. The best way to generate a core dump on Linux (only) is through the [createdump](https://github.com/dotnet/coreclr/blob/master/Documentation/botr/xplat-minidump-generation.md#configurationpolicy) facility.

### Getting symbols ###

First install the dotnet CLI symbol tool. This only needs to be down once. See this [link](https://github.com/dotnet/symstore/tree/master/src/dotnet-symbol#install) for more details.

    ~$ dotnet tool install -g dotnet-symbol

Copy the core dump to a tmp directory.

    ~$ mkdir /tmp/dump
    ~$ cp ~/coredump.32232 /tmp/dump

Download the modules and symbols for the core dump:

    ~$ dotnet symbol /tmp/dump/coredump.32232

Remove (Linux only) the the libcoreclrtraceptprovider to avoid crashing lldb to avoid issue #[20205](https://github.com/dotnet/coreclr/issues/20205).

    ~$ rm /tmp/dump/libcoreclrtraceptprovider.*

### Install lldb and build the diagnostic repo ###

See the instructions on the main README.md.

### Launch lldb under Linux ###

    ~$ lldb
    (lldb) plugin load $(HOME)/diagnostics/artifacts/bin/Linux.x64.Debug/libsosplugin.so
    (lldb) sethostruntime /usr/share/dotnet/shared/Microsoft.NETCore.App/2.1.0
    (lldb) target create --core /tmp/dump/coredump.32232

Even if the core dump was not generated on this machine, the native and managed .NET Core symbols should be available along with all the SOS commands. Note that path passed to the `sethostruntime` needs to be a .NET Core runtime installed on the machine. See `dotnet --info` for information on the installed runtimes and SDKs.

### Launch lldb under MacOS ###

    ~$ lldb
    (lldb) plugin load $(HOME)/diagnostics/artifacts/bin/OSX.x64.Debug/libsosplugin.dylib
    (lldb) sethostruntime /usr/share/dotnet/shared/Microsoft.NETCore.App/2.1.0
    (lldb) target create --core /tmp/dump/coredump.32232

The MacOS lldb has a bug that prevents SOS clrstack from properly working. Because of this bug SOS can't properly match the lldb native with with the managed thread OSID displayed by `clrthreads`. The `setsostid` command is a work around for this lldb bug.
