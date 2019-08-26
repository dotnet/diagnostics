Frequently Asked Questions
==========================

* If SOS or dotnet-dump analyze commands display "UNKNOWN" for types or functions names, your core dump may not have all the managed state. Dumps created with gdb or gcore have this problem. Linux system generated core dumps need the `coredump_filter` for the process to be set to at least 0x3f. See [core](http://man7.org/linux/man-pages/man5/core.5.html) for more information.

* If dump collection (`dotnet-dump collect` or `createdump`) doesn't work in a docker container, try adding the SYS\_TRACE capablity with --cap-add=SYS\_PTRACE or --privileged.
 
* If dump analysis (`dotnet-dump analyze`) on Microsoft .NET Core SDK Linux docker images fails with an`Unhandled exception: System.DllNotFoundException: Unable to load shared library 'libdl.so' or one of its dependencies` exception. Try installing the "libc6-dev" package.
 
* If dump collection (`dotnet-dump collect`) fails with something like `Permission denied /tmp/dotnet-diagnostic-19668-22628141-socket error`, the target process is a different user or root so `sudo` is needed to run the command.

* If dump collection (`dotnet-dump collect`) fails with `Core dump generation FAILED 0x80004005`, look for error message output on the target process's console (not the console executing the dotnet-dump collect). To enable dump collection logging add the `--diag` option the dotnet-dump collect command and also look for diagnostic logging on the target process's console.
