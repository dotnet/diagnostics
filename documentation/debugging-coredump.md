Debugging Linux or MacOS Core Dump
==================================

These instructions will lead you through getting symbols, loading and debugging a Linux or MacOS core dump. The best way to generate a core dump on Linux (only) is through the [createdump](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/xplat-minidump-generation.md#configurationpolicy) facility.

Dumps created with gdb or gcore do not have all the managed state so various SOS or dotnet-dump commands may display "UNKNOWN" for type and function names. This can also happen with Linux system generated core dumps if the `coredump_filter` for the process is not set to at least 0x3f. See [core](http://man7.org/linux/man-pages/man5/core.5.html) for more information.

### Getting symbols ###

Because SOS now has symbol download support (both managed PDBs and native symbols via `loadsymbols`) all that lldb requires is the host program and a few other binaries. The host is usually `dotnet` but for self-contained applications it the .NET Core `apphost` renamed to the program/project name. These steps will handle either case and download the host lldb needs to properly diagnose a core dump. There are also cases that the runtime module (i.e. libcoreclr.so) is need by lldb.

First install or update the dotnet CLI symbol tool. This only needs to be done once. See this [link](https://github.com/dotnet/symstore/tree/main/src/dotnet-symbol#install) for more details. We need version 1.0.142101 or greater of dotnet-symbol installed.

    ~$ dotnet tool install -g dotnet-symbol
    You can invoke the tool using the following command: dotnet-symbol
    Tool 'dotnet-symbol' (version '1.0.142101') was successfully installed.

Or update if already installed:

    ~$ dotnet tool update -g dotnet-symbol
    Tool 'dotnet-symbol' was successfully updated from version '1.0.51501' to version '1.0.142101'.

Copy the core dump to a tmp directory.

    ~$ mkdir /tmp/dump
    ~$ cp ~/coredump.32232 /tmp/dump

Download the host program, modules and symbols for the core dump:

    ~$ dotnet-symbol /tmp/dump/coredump.32232

If your project/program binaries are not on the machine the core dump is being loaded on, copy them to a temporary directory. You can use the lldb/SOS command `setsymbolserver -directory <temp-dir>` to add this directory to the search path.

Alternatively, you can download just the host program for the core dump (this all lldb needs) if you only need symbols for the managed modules. The `loadsymbols` command in SOS will attempt to download the native runtime symbols.

    ~$ dotnet-symbol --host-only --debugging /tmp/dump/coredump.32232

If the `--host-only` option is not found, update dotnet-symbol to the latest with the above step.

### Install lldb ###

See the instructions [here](sos.md#getting-lldb) on installing lldb.

### Install the latest SOS ###

See the instructions [here](sos.md#installing-sos) on installing SOS.

### Launch lldb under Linux ###

    ~$ lldb --core /tmp/dump/coredump.32232 <host-program>
    Core file '/tmp/dump/coredump.32232' (x86_64) was loaded.
    (lldb)

The `<host-program>` is the native program that started the .NET Core application. It is usually `dotnet` unless the application is self contained and then it is the name of application without the .dll.

Add the directory with the core dump and symbols to the symbol search path:

     (lldb) setsymbolserver -directory /tmp/dump
     Added symbol directory path: /tmp/dump
     (lldb)

Optionally load the native symbols. The managed PDBs will be loaded on demand when needed:

     (lldb) loadsymbols

Even if the core dump was not generated on this machine, the native and managed .NET Core symbols should be available along with all the SOS commands.

### Launch lldb under MacOS ###

    ~$ lldb --core /cores/core.32232 <host-program>
    (lldb)

Follow the rest of the above Linux steps to set the symbol server and load native symbols.

The MacOS lldb has a bug that prevents SOS clrstack from properly working. Because of this bug SOS can't properly match the lldb native with with the managed thread OSID displayed by `clrthreads`. The `setsostid` command is a work around for this lldb bug. This command maps the OSID from this command:

```
(lldb) clrthreads
ThreadCount:      2
UnstartedThread:  0
BackgroundThread: 1
PendingThread:    0
DeadThread:       0
Hosted Runtime:   no
                                                                                                        Lock
 DBG   ID OSID ThreadOBJ           State GC Mode     GC Alloc Context                  Domain           Count Apt Exception
XXXX    1 1fbf31 00007FBEC9007200    20020 Preemptive  0000000190191710:0000000190191FD0 00007FBEC981F200 0     Ukn System.IO.DirectoryNotFoundException 0000000190172b88
XXXX    2 1fbf39 00007FBEC9008000    21220 Preemptive  0000000000000000:0000000000000000 00007FBEC981F200 0     Ukn (Finalizer)
```
To one of the native thread indexes from this command:

```
(lldb) thread list
Process 0 stopped
* thread #1: tid = 0x0000, 0x00007fffb5595d42 libsystem_kernel.dylib`__pthread_kill + 10, stop reason = signal SIGSTOP
  thread #2: tid = 0x0001, 0x00007fffb558e34a libsystem_kernel.dylib`mach_msg_trap + 10, stop reason = signal SIGSTOP
  thread #3: tid = 0x0002, 0x00007fffb559719e libsystem_kernel.dylib`poll + 10, stop reason = signal SIGSTOP
  thread #4: tid = 0x0003, 0x00007fffb5595a3e libsystem_kernel.dylib`__open + 10, stop reason = signal SIGSTOP
  thread #5: tid = 0x0004, 0x00007fffb5595bf2 libsystem_kernel.dylib`__psynch_cvwait + 10, stop reason = signal SIGSTOP
  thread #6: tid = 0x0005, 0x00007fffb5595bf2 libsystem_kernel.dylib`__psynch_cvwait + 10, stop reason = signal SIGSTOP
  thread #7: tid = 0x0006, 0x00007fffb558e34a libsystem_kernel.dylib`mach_msg_trap + 10, stop reason = signal SIGSTOP
```

Map the main managed thread `1fbf31` to native thread index `1`:

```
(lldb) setsostid 1fbf31 1
```
