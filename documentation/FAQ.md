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
    First try enabling the symbol downloading with `setsymbolserver -ms`. This is already enabled for `dotnet-dump analyze` and if SOS for lldb was installed with `dotnet-sos install`.

    If that doesn't work, try using the `setclrpath <directory>` command with a directory that contains the matching version of the DAC module. This is useful for private runtimes or debug builds that haven't been published to our symbol servers.
