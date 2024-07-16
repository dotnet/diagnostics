# Symbol downloader dotnet cli extension #

This tool can download all the files needed for debugging (symbols, modules, SOS and DAC for the coreclr module given) for any given core dump, minidump or any supported platform's file formats like ELF, MachO, Windows DLLs, PDBs and portable PDBs. See [debugging coredumps](https://github.com/dotnet/diagnostics/blob/main/documentation/debugging-coredump.md) for more details.
      
    Usage: dotnet symbol [options] <FILES>
    
    Arguments:
      <FILES>   List of files. Can contain wildcards.

    Options:
      --microsoft-symbol-server                         Add 'https://msdl.microsoft.com/download/symbols' symbol server path (default).
      --server-path <symbol server path>                Add a http server path.
      --authenticated-server-path <pat> <server path>   Add a http PAT authenticated server path.
      --cache-directory <file cache directory>          Add a cache directory.
      --recurse-subdirectories                          Process input files in all subdirectories.
      --host-only                                       Download only the host program (i.e. dotnet) that lldb needs for loading coredumps.
      --symbols                                         Download the symbol files (.pdb, .dbg, .dwarf).
      --modules                                         Download the module files (.dll, .so, .dylib).
      --debugging                                       Download the special debugging modules (DAC, DBI, SOS).
      --windows-pdbs                                    Force the downloading of the Windows PDBs when Portable PDBs are also available.
      -o, --output <output directory>                   Set the output directory. Otherwise, write next to the input file (default).
      -d, --diagnostics                                 Enable diagnostic output.
      -h, --help                                        Show help information.

## Install ##

This is a dotnet global tool "extension" supported only by [.NET Core 2.1](https://www.microsoft.com/net/download/) or greater. The latest version of the downloader can be installed with the following command. Make sure you are not in any project directory with a NuGet.Config that doesn't include nuget.org as a source. See the Notes section about any errors. 

    dotnet tool install -g dotnet-symbol

If you already have dotnet-symbol installed you can update it with:

    dotnet tool update -g dotnet-symbol

## Examples ##

This will attempt to download all the modules, symbols and DAC/DBI files needed to debug the core dump including the managed assemblies and their PDBs if Linux/ELF core dump or Windows minidump:

    dotnet-symbol coredump.4507

This downloads just the host program needed to load a core dump on Linux or macOS under lldb. SOS under lldb can download the rest of the symbols and modules needed on demand or with the "loadsymbols" command. See [debugging coredumps](https://github.com/dotnet/diagnostics/blob/main/documentation/debugging-coredump.md) for more details.

    dotnet-symbol --host-only coredump.4507

To download the symbol files for a specific assembly:

    dotnet-symbol --symbols --cache-directory c:\temp\symcache --server-path https://msdl.microsoft.com/download/symbols --output c:\temp\symout System.Threading.dll

Downloads all the symbol files for the shared runtime:

    dotnet-symbol --symbols --output /tmp/symbols /usr/share/dotnet/shared/Microsoft.NETCore.App/2.0.3/*

After the symbols are downloaded to `/tmp/symbols` they can be copied back to the above runtime directory so the native debuggers like lldb or gdb can find them, but the copy needs to be superuser:

	sudo cp /tmp/symbols/* /usr/share/dotnet/shared/Microsoft.NETCore.App/2.0.3

To verify a symbol package on a local VSTS symbol server:

    dotnet-symbol --authenticated-server-path x349x9dfkdx33333livjit4wcvaiwc3v4wjyvnq https://mikemvsts.artifacts.visualstudio.com/defaultcollection/_apis/Symbol/symsrv coredump.45634

## Notes ##

Symbol download is only supported for official .NET Core runtime versions acquired through official channels such as [the official web site](https://dotnet.microsoft.com/download/dotnet-core) and the [default sources in the dotnet installation scripts](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-install-scripts). Runtimes obtained from community sites like [archlinux](https://www.archlinux.org/packages/community/x86_64/dotnet-runtime/) are not supported. 

Core dumps generated with gdb (generate-core-file command) or gcore (utility that comes with gdb) do not currently work with this utility (issue [#47](https://github.com/dotnet/symstore/issues/47)).

The best way to generate core dumps on Linux (not supported on Windows or MacOS) is to use the [createdump](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/xplat-minidump-generation.md) facility that is part of .NET Core 2.0 and greater. It can be setup (see [createdump](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/xplat-minidump-generation.md#configurationpolicy) for the details) to automatically generate a "minidump" like ELF core dump when your .NET Core app crashes. The coredump will contain all the necessary managed state to analyze with SOS or dotnet-dump. 

 Linux system core generation (enabled with `ulimit -c unlimited`) also works if the coredump_filter flags are set (see [core](http://man7.org/linux/man-pages/man5/core.5.html)) to at least 0x3f but they are usually a lot larger than necessary. 
```
echo 0x3f > /proc/self/coredump_filter
ulimit -c unlimited
```


If you receive the below error when installing the extension, you are in a project or directory that contains a NuGet.Config that doesn't contain nuget.org. 

    error NU1101: Unable to find package dotnet-symbol. No packages exist with this id in source(s): ...
    The tool package could not be restored.
    Tool 'dotnet-symbol' failed to install. This failure may have been caused by:
    
    * You are attempting to install a preview release and did not use the --version option to specify the version.
    * A package by this name was found, but it was not a .NET Core tool.
    * The required NuGet feed cannot be accessed, perhaps because of an Internet connection problem.
    * You mistyped the name of the tool.

You can either run the install command from your $HOME or %HOME% directory or override this behavior with the `--add-source` option:

`dotnet tool install -g --add-source https://api.nuget.org/v3/index.json dotnet-symbol` 
