This documentation is now being maintained at [dotnet-sos](https://github.com/dotnet/docs/blob/a201d22d8c33fcb77af093edb96d0fe669e0e491/docs/core/diagnostics/dotnet-sos.md). This doc is no longer being updated.

Installing SOS on Windows
=========================

There are three ways to install the Windows Debugger:

* The Microsoft Windows SDK. See [Debugging Tools for Windows 10 (WinDbg)](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/debugger-download-tools#small-classic-windbg-preview-logo-debugging-tools-for-windows-10-windbg) for more information. SOS will need to be manually installed with dotnet-sos.
* The WinDbg Preview. See [Download WinDbg Preview](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/debugger-download-tools#small-windbg-preview-logo-download-windbg-preview). SOS will be automatically loaded for .NET Core apps.
* The Microsoft internal version of the Windows Debugger. The latest SOS will automatically be loaded from the internal Microsoft extension gallery. For more details see below.

### Manually Installing SOS on Windows ###

To install the latest released SOS manually, use the dotnet-sos CLI global tool. This applies to any of the ways the Windows debugger was installed. You may have to `.unload sos` a version of SOS that was automatically loaded.

    C:\Users\mikem>dotnet tool install -g dotnet-sos
    You can invoke the tool using the following command: dotnet-sos
    Tool 'dotnet-sos' (version '5.0.160202') was successfully installed.

Run the installer:

    C:\Users\mikem>dotnet-sos install
    Installing SOS to C:\Users\mikem\.dotnet\sos from C:\Users\mikem\.dotnet\tools\.store\dotnet-sos\5.0.251802\dotnet-sos\5.0.251802\tools\netcoreapp3.1\any\win-x64
    Installing over existing installation...
    Creating installation directory...
    Copying files...
    Execute '.load C:\Users\mikem\.dotnet\sos\sos.dll' to load SOS in your Windows debugger.
    Cleaning up...
    SOS install succeeded

SOS will need to be loaded manually with the above ".load" command:

    C:\Users\mikem>"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe" dotnet SymbolTestApp2.dll

    Microsoft (R) Windows Debugger Version 10.0.19041.685 AMD64
    Copyright (c) Microsoft Corporation. All rights reserved.

    CommandLine: dotnet SymbolTestApp2.dll
    Symbol search path is: srv*
    Executable search path is:
    ModLoad: 00007ff7`f7450000 00007ff7`f7477000   dotnet.exe
    ModLoad: 00007fff`16d90000 00007fff`16f7d000   ntdll.dll
    ModLoad: 00007fff`145e0000 00007fff`14693000   C:\WINDOWS\System32\KERNEL32.DLL
    ModLoad: 00007fff`13c30000 00007fff`13ec3000   C:\WINDOWS\System32\KERNELBASE.dll
    ModLoad: 00007fff`13a70000 00007fff`13b6c000   C:\WINDOWS\System32\ucrtbase.dll
    (92cd8.92eb4): Break instruction exception - code 80000003 (first chance)
    ntdll!LdrpDoDebuggerBreak+0x30:
    00007fff`16e62cbc cc              int     3
    0:000> .unload sos
    Unloading sos extension DLL
    0:000> .load C:\Users\mikem\.dotnet\sos\sos.dll
    0:000> .chain
    Extension DLL search Path:
        C:\Program Files\Debugging Tools for Windows (x64);...
    Extension DLL chain:
        C:\Users\mikem\.dotnet\sos\sos.dll: image 1.0.2-dev.19151.2+26ec7875d312cf57db83926db0d9340e297e2a4c, API 2.0.0, built Mon Feb 25 17:27:33 2019
            [path: C:\Users\mikem\.dotnet\sos\sos.dll]
        dbghelp: image 10.0.18317.1001, API 10.0.6,
            [path: C:\Program Files\Debugging Tools for Windows (x64)\dbghelp.dll]
        ...
        ntsdexts: image 10.0.18317.1001, API 1.0.0,
            [path: C:\Program Files\Debugging Tools for Windows (x64)\WINXP\ntsdexts.dll]

### SOS for the Microsoft Internal Windows Debugger ###

The latest released version of SOS will automatically be loaded from the internal Microsoft extension gallery. You need at least version 10.0.18317.1001 or greater of the Windows debugger (windbg or cdb). SOS will load when the "coreclr.dll" module is loaded.

    "C:\Program Files\Debugging Tools for Windows (x64)\cdb.exe" dotnet SymbolTestApp2.dll
    
    Microsoft (R) Windows Debugger Version 10.0.21251.1000 AMD64
    Copyright (c) Microsoft Corporation. All rights reserved.

    0:000> sxe ld coreclr
    0:000> g
    ModLoad: 00007ffe`e9100000 00007ffe`e9165000   C:\Program Files\dotnet\host\fxr\3.0.3\hostfxr.dll
    ModLoad: 00007ffe`e7ba0000 00007ffe`e7c32000   C:\Program Files\dotnet\shared\Microsoft.NETCore.App\3.1.20\hostpolicy.dll
    ModLoad: 00007ffe`abb60000 00007ffe`ac125000   C:\Program Files\dotnet\shared\Microsoft.NETCore.App\3.1.20\coreclr.dll
    ntdll!ZwMapViewOfSection+0x14:
    00007fff`16e2fb74 c3              ret
    0:000> .chain
    Extension DLL search Path:
        C:\Program Files\Debugging Tools for Windows (x64);...
    Extension DLL chain:
        sos: image 5.0.160202+5734230e3ee516339a4b0e4729def135027aa255, API 2.0.0, built Wed Dec  2 19:15:02 2020
            [path: C:\Users\mikem\AppData\Local\DBG\ExtRepository\EG\cache2\Packages\SOS\5.0.3.10202\win-x64\sos.dll]
        dbghelp: image 10.0.21251.1000, API 10.0.6,
            [path: C:\Program Files\Debugging Tools for Windows (x64)\dbghelp.dll]
        ext: image 10.0.21276.1001, API 1.0.0,
            [path: C:\Users\mikem\AppData\Local\DBG\ExtRepository\EG\cache2\Packages\ext\10.0.21276.1001\amd64fre\winext\ext.dll]
        ...
    0:000> !soshelp
    -------------------------------------------------------------------------------
    SOS is a debugger extension DLL designed to aid in the debugging of managed
    programs. Functions are listed by category, then roughly in order of
    importance. Shortcut names for popular functions are listed in parenthesis.
    Type "!help <functionname>" for detailed info on that function.

    Object Inspection                  Examining code and stacks
    -----------------------------      -----------------------------
    DumpObj (do)                       Threads (clrthreads)
    DumpArray (da)                     ThreadState
    DumpAsync                          IP2MD
    DumpDelegate                       U
    DumpStackObjects (dso)             DumpStack
    DumpHeap                           EEStack
    ...


