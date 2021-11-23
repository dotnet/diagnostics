Installing SOS on Linux and MacOS
=================================

The first step is to install the dotnet-sos CLI global tool. This requires at least the 2.1 or greater .NET Core SDK to be installed. If you see the error message `Tool 'dotnet-sos' is already installed`, you will need to uninstall the global tool (see below). 

    $ dotnet tool install -g dotnet-sos
    You can invoke the tool using the following command: dotnet-sos
    Tool 'dotnet-sos' (version '3.0.47001') was successfully installed.

The next step is use this global tool to install SOS. 

    $ dotnet-sos install
    Installing SOS to /home/mikem/.dotnet/sos from /home/mikem/.dotnet/tools/.store/dotnet-sos/3.0.47001/dotnet-sos/3.0.47001/tools/netcoreapp2.1/any/linux-x64
    Creating installation directory...
    Copying files...
    Updating existing /home/mikem/.lldbinit file - LLDB will load SOS automatically at startup
    SOS install succeeded

Now any time you run lldb, SOS will automatically be loaded and the symbol downloading enabled. This requires at least lldb 3.9 installed. See [Getting lldb](../README.md) section.

    $ lldb
    (lldb) soshelp
    -------------------------------------------------------------------------------
    SOS is a debugger extension DLL designed to aid in the debugging of managed
    programs. Functions are listed by category, then roughly in order of
    importance. Shortcut names for popular functions are listed in parenthesis.
    Type "soshelp <functionname>" for detailed info on that function.

    Object Inspection                  Examining code and stacks
    -----------------------------      -----------------------------
    DumpObj (dumpobj)                  Threads (clrthreads)
    DumpArray                          ThreadState
    DumpAsync (dumpasync)              IP2MD (ip2md)
    DumpDelegate (dumpdelegate)        u (clru)
    DumpStackObjects (dso)             DumpStack (dumpstack)
    DumpHeap (dumpheap)                EEStack (eestack)
    DumpVC                             CLRStack (clrstack)
    FinalizeQueue (finalizequeue)      GCInfo
    GCRoot (gcroot)                    EHInfo
    PrintException (pe)                bpmd (bpmd)

    Examining CLR data structures      Diagnostic Utilities
    -----------------------------      -----------------------------
    DumpDomain (dumpdomain)            VerifyHeap
    EEHeap (eeheap)                    FindAppDomain
    Name2EE (name2ee)                  DumpLog (dumplog)
    SyncBlk (syncblk)
    DumpMT (dumpmt)
    DumpClass (dumpclass)
    DumpMD (dumpmd)
    Token2EE
    DumpModule (dumpmodule)
    DumpAssembly
    DumpRuntimeTypes
    DumpIL (dumpil)
    DumpSig
    DumpSigElem

    Examining the GC history           Other
    -----------------------------      -----------------------------
    HistInit (histinit)                SetHostRuntime (sethostruntime)
    HistRoot (histroot)                SetSymbolServer (setsymbolserver, loadsymbols)
    HistObj  (histobj)                 FAQ
    HistObjFind (histobjfind)          SOSFlush
    HistClear (histclear)              Help (soshelp)
    (lldb)

## Updating SOS

    $ dotnet tool update -g dotnet-sos

The installer needs to be run again:

    $ dotnet-sos install
    Installing SOS to /home/mikem/.dotnet/sos from /home/mikem/.dotnet/tools/.store/dotnet-sos/3.0.47001/dotnet-sos/3.0.47001/tools/netcoreapp2.1/any/linux-x64
    Installing over existing installation...
    Creating installation directory...
    Copying files...
    Updating existing /home/mikem/.lldbinit file - LLDB will load SOS automatically at startup
    Cleaning up...
    SOS install succeeded

## Uninstalling SOS

To uninstall and remove the lldb configuration run this command:

    $ dotnet-sos uninstall
    Uninstalling SOS from /home/mikem/.dotnet/sos
    Reverting /home/mikem/.lldbinit file - LLDB will no longer load SOS at startup
    SOS uninstall succeeded

To remove the SOS installer global tool:

    $ dotnet tool uninstall -g dotnet-sos
    Tool 'dotnet-sos' (version '3.0.47001') was successfully uninstalled.
