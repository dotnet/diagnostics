This documentation is now being maintained at [dotnet-sos](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-sos). This doc is no longer being updated.

SOS 
===

SOS is a debugger extension that allows a developer to inspect the managed state of a .NET Core and desktop runtime process. SOS can be loaded by WinDbg/cdb debuggers on Windows and lldb on Linux and MacOS.

## Getting lldb

Getting a version of lldb that works for your platform can be a problem sometimes. The version has to be at least 3.9 or greater because of a bug running SOS on a core dump that was fixed. Some Linux distros like Ubuntu it is easy as `sudo apt-get install lldb-3.9 python-lldb-3.9`. On other distros, you will need to build lldb. The directions below should give you some guidance.

* [Linux Instructions](lldb/linux-instructions.md)
* [MacOS Instructions](lldb/osx-instructions.md)
* [FreeBSD Instructions](lldb/freebsd-instructions.md)
* [NetBSD Instructions](lldb/netbsd-instructions.md)

## Installing SOS

* [Linux and MacOS Instructions](installing-sos-instructions.md)
* [Windows Instructions](installing-sos-windows-instructions.md)

## Using SOS

* [SOS debugging](https://learn.microsoft.com/dotnet/core/diagnostics/sos-debugging-extension)
* [Debugging a core dump](debugging-coredump.md)

## New SOS Features

The `bpmd` command can now be used before the runtime is loaded. You can load SOS or the sos plugin on Linux and execute bpmd. Always add the module extension for the first parameter.

    bpmd SymbolTestApp.dll SymbolTestApp.Program.Main

You can set a source file/line number breakpoint like this (the fully qualified source file path is usually not necessary):

    bpmd SymbolTestApp.cs:24

Symbol server support - The `setsymbolserver` command enables downloading the symbol files (portable PDBs) for managed assemblies during commands like `clrstack`, etc. See `soshelp setsymbolserver` for more details.

    (lldb) setsymbolserver -ms

Before executing the "bt" command to dump native frames to load the native symbols (for live debugging only):

    (lldb) loadsymbols

To add a local directory to search for symbols:

    (lldb) setsymbolserver -directory /tmp/symbols
