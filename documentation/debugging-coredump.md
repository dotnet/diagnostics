Debugging Linux or MacOS Core Dump
==================================

These instructions will lead you through getting symbols, loading and debugging a Linux or MacOS core dump. The best way to generate a core dump on Linux (only) is through the [createdump](https://github.com/dotnet/coreclr/blob/master/Documentation/botr/xplat-minidump-generation.md#configurationpolicy) facility.

Dumps created with gdb or gcore do not have all the managed state so various SOS or dotnet-dump commands may display "UNKNOWN" for type and function names. This can also happen with Linux system generated core dumps if the `coredump_filter` for the process is not set to at least 0x3f. See [core](http://man7.org/linux/man-pages/man5/core.5.html) for more information.

### Getting symbols ###

First install the dotnet CLI symbol tool. This only needs to be down once. See this [link](https://github.com/dotnet/symstore/tree/master/src/dotnet-symbol#install) for more details.

    ~$ dotnet tool install -g dotnet-symbol

Copy the core dump to a tmp directory.

    ~$ mkdir /tmp/dump
    ~$ cp ~/coredump.32232 /tmp/dump

Download the modules and symbols for the core dump:

    ~$ dotnet symbol /tmp/dump/coredump.32232

### Install lldb ###

See the instructions on the main [README.md](../README.md) under "Getting lldb".

### Install the latest SOS ###

See the instructions on the main [README.md](../README.md) under "Installing SOS".

### Launch lldb under Linux ###

    ~$ lldb
    (lldb) target create --core /tmp/dump/coredump.32232

Even if the core dump was not generated on this machine, the native and managed .NET Core symbols should be available along with all the SOS commands.

### Launch lldb under MacOS ###

    ~$ lldb
    (lldb) target create --core /tmp/dump/coredump.32232

The MacOS lldb has a bug that prevents SOS clrstack from properly working. Because of this bug SOS can't properly match the lldb native with with the managed thread OSID displayed by `clrthreads`. The `setsostid` command is a work around for this lldb bug.
