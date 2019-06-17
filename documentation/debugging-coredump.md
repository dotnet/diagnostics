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

    ~$ dotnet-symbol /tmp/dump/coredump.32232

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
    (lldb) target create --core /cores/core.32232

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
