Frequently Asked Questions
==========================

* If SOS or dotnet-dump analyze commands display "UNKNOWN" for types or functions names, your core dump may not have all the managed state. Dumps created with gdb or gcore have this problem. Linux system generated core dumps need the `coredump_filter` for the process to be set to at least 0x3f. See [core](http://man7.org/linux/man-pages/man5/core.5.html) for more information.

* If dump collection (`dotnet-dump collect` or `createdump`) doesn't work in a docker container, try adding the SYS\_TRACE capability with --cap-add=SYS\_PTRACE or --privileged.
 
* If dump analysis (`dotnet-dump analyze`) on Microsoft .NET Core SDK Linux docker images fails with an`Unhandled exception: System.DllNotFoundException: Unable to load shared library 'libdl.so' or one of its dependencies` exception. Try installing the "libc6-dev" package.
 
* During dump collection (`dotnet-dump collect`) a failure ending in a message like `Permission denied /tmp/dotnet-diagnostic-19668-22628141-socket error` hints you don't have access to use such a socket. Verify the target process is owned by the user trying to create the dump, or trigger dump creation command with `sudo`. If you use `sudo` to collect the dump, make sure the dump file output path is accessible by the target process/user (via the --output option). The default dump path is the in the current directory and may not be the same user as the target process.

* If dump collection (`dotnet-dump collect`) fails with `Core dump generation FAILED 0x80004005` look for error message output on the target process's console (not the console executing the dotnet-dump collect). This error may be caused by writing the core dump to a protected, inaccessible or non-existent location. To get more information about the core dump generation add the `--diag` option the dotnet-dump collect command and look for the diagnostic logging on the target process's console.

* If you receive the following error message executing a SOS command under `lldb` or `dotnet-dump analyze`, SOS cannot find the DAC module (`libmscordaccore.so` or `libmscordaccore.dylib`) in the same directory as the runtime (libcoreclr.so or libcoreclr.dylib) module.
    ```
    (lldb) clrstack
    Failed to load data access module, 0x80131c64
    Can not load or initialize libmscordaccore.so. The target runtime may not be initialized.
    ClrStack  failed
    ```
    or
    ```
    Failed to load data access module, 0x80131c4f
    You can run the debugger command 'setclrpath ' to control the load path of libmscordaccore.so.
    If that succeeds, the SOS command should work on retry.
    For more information see https://go.microsoft.com/fwlink/?linkid=2135652
    ```
    First try enabling the symbol downloading with `setsymbolserver -ms`. This is already enabled for `dotnet-dump analyze` and if SOS for lldb was installed with `dotnet-sos install`.

    If that doesn't work, try using the `setclrpath <directory>` command with a directory that contains the matching version of the DAC module. This is useful for private runtimes or debug builds that haven't been published to our symbol servers.

    If this is a dump, the problem could also be that the dump is missing some memory required by SOS. Try generating a "full" dump (the default with `dotnet-dump collect` without a `--type` option) or add setting the crash dump generation (createdump) environment variable `COMPlus_DbgMiniDumpType=4`. For more details on crash dump generation see [here](https://docs.microsoft.com/en-us/dotnet/core/diagnostics/dumps#collecting-dumps-on-crash).

* If you receive this error message executing a SOS command:
    ```
    Failed to find runtime module (libcoreclr.so), 0x80004005
    Extension commands need it in order to have something to do.
    ClrStack  failed
    ```
    The following could be the problem:
    * The process or core dump hasn't loaded the .NET Core runtime yet.
    * The coredump was loaded under lldb without specifying the host (i.e `dotnet`). `target modules list` doesn't display `libcoreclr.so` or `libcoreclr.dylib`. Start lldb with the host as the target program and the core file, for example `lldb --core coredump /usr/share/dotnet/dotnet`. In case you don't have the host available, `dotnet symbol` is will be able to download them.
    * If a coredump was loaded under lldb, a host was specified, and `target modules list` displays the runtime module but you still get that message lldb needs the correct version of libcoreclr.so/dylib next to the coredump. You can use `dotnet-symbol --modules <coredump>` to download the needed binaries.

* If you receive one of these error messages executing a SOS command running on Windows:
    ```
    SOS does not support the current target architecture 0x0000014c
    ```
   or 
    ```
    SOS does not support the current target architecture 'arm32' (0x01c4). A 32 bit target may require a 32 bit debugger or vice versa. In general, try to use the same bitness for the debugger and target process.
    ```

    You may need a different bitness of the Windows (windbg/cdb) debugger or dotnet-dump. If you are running an x64 (64 bit), try an x86 (32 bit) version. The easiest way to get an x86 version of dotnet-dump is installing the "single-file" version [here](https://aka.ms/dotnet-dump/win-x86). For more information on single-file tools see [here](https://github.com/dotnet/diagnostics/blob/main/documentation/single-file-tools.md#single-file-diagnostic-tools).
