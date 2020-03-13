Installing SOS on Windows
=========================

SOS will automatically be loaded from the internal Microsoft extension gallery. You need at least version 10.0.18317.1001 or greater of the Windows debugger (windbg or cdb). SOS will load when the "coreclr.dll" module is loaded.

    "C:\Program Files\Debugging Tools for Windows (x64)\cdb.exe" dotnet SymbolTestApp2.dll
    
    Microsoft (R) Windows Debugger Version 10.0.18317.1001 AMD64
    Copyright (c) Microsoft Corporation. All rights reserved.

    0:000> sxe ld coreclr
    0:000> g
    ModLoad: 00007ffe`e9100000 00007ffe`e9165000   C:\Program Files\dotnet\host\fxr\2.2.2\hostfxr.dll
    ModLoad: 00007ffe`e7ba0000 00007ffe`e7c32000   C:\Program Files\dotnet\shared\Microsoft.NETCore.App\2.1.6\hostpolicy.dll
    ModLoad: 00007ffe`abb60000 00007ffe`ac125000   C:\Program Files\dotnet\shared\Microsoft.NETCore.App\2.1.6\coreclr.dll
    ntdll!ZwMapViewOfSection+0x14:
    00007fff`16e2fb74 c3              ret
    0:000> .chain
    Extension DLL search Path:
        C:\Program Files\Debugging Tools for Windows (x64);...
    Extension DLL chain:
        sos: image 1.0.1-dev.19106.2+58b97f128be8f866a08aba9fd5c77571ae8e3f6a, API 2.0.0, built Wed Feb  6 13:06:38 2019
            [path: C:\Users\mikem\AppData\Local\DBG\ExtRepository\EG\cache2\Packages\SOS\1.0.1.0\x64\sos.dll]
        dbghelp: image 10.0.18317.1001, API 10.0.6,
            [path: C:\Program Files\Debugging Tools for Windows (x64)\dbghelp.dll]
        ...
        ntsdexts: image 10.0.18317.1001, API 1.0.0,
            [path: C:\Program Files\Debugging Tools for Windows (x64)\WINXP\ntsdexts.dll]
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

### Older versions of the Windows debugger

It is recommended that you update to the newer versions of the Windows debugger, but you can still use the latest SOS with older Windows debuggers by using the dotnet-sos CLI global tool to install. It is not as convenient. You may have to ".unload" the SOS that is loaded from the "runtime" directory.

    C:\Users\mikem>dotnet tool install -g dotnet-sos
    You can invoke the tool using the following command: dotnet-sos
    Tool 'dotnet-sos' (version '3.0.47001') was successfully installed.

Run the installer:

    C:\Users\mikem>dotnet-sos install
    Installing SOS to C:\Users\mikem\.dotnet\sos from C:\Users\mikem\.dotnet\tools\.store\dotnet-sos\3.0.47001\dotnet-sos\3.0.47001\tools\netcoreapp2.1\any\win-x64
    Creating installation directory...
    Copying files...
    Execute '.load C:\Users\mikem\.dotnet\sos\sos.dll' to load SOS in your Windows debugger.
    SOS install succeeded

SOS will need to be loaded manually with the above ".load" command:


    C:\Users\mikem>"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe" dotnet SymbolTestApp2.dll

    Microsoft (R) Windows Debugger Version 10.0.17134.12 AMD64
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
