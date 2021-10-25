---
title: "SOS Debugging Extension"
ms.date: "10/3/2018"
helpviewer_keywords:
  - "debugging extensions"
  - "SOS debugging extensions"
  - "xplat debugging"
ms.assetid: TBD
author: "mikem"
ms.author: "mikem"
---
# SOS debugging extension for xplat

The SOS Debugging Extension lets you view information about code that is running inside the .NET Core runtime. For example, you can use the SOS Debugging Extension to display information about the managed heap, look for heap corruptions, display internal data types used by the runtime, and view information about all managed code running inside the runtime.

## Syntax

```shell
sos [command] [options]
```

Many of the commands have aliases or short cuts under lldb: 

```
clrstack [options]
```

### Command Summary 

SOS is a lldb debugger extension designed to aid in the debugging of managed programs. Functions are listed by category, then roughly in order of
importance. Shortcut names for popular functions are listed in parenthesis. Type `soshelp <functionname>` for detailed info on that function. 

    Object Inspection                  Examining code and stacks
    -----------------------------      -----------------------------
    DumpObj (dumpobj)                  Threads (clrthreads)
    DumpArray                          ThreadState
    DumpAsync (dumpasync)              IP2MD (ip2md)
    DumpStackObjects (dso)             u (clru)
    DumpHeap (dumpheap)                DumpStack (dumpstack)
    DumpVC                             EEStack (eestack)
    GCRoot (gcroot)                    CLRStack (clrstack)
    PrintException (pe)                GCInfo
                                       EHInfo
                                       bpmd (bpmd)

    Examining CLR data structures      Diagnostic Utilities
    -----------------------------      -----------------------------
    DumpDomain (dumpdomain)            VerifyHeap
    EEHeap (eeheap)                    FindAppDomain          
    Name2EE (name2ee)                  DumpLog (dumplog)
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
    HistObj  (histobj)                 SetClrPath (setclrpath)
    HistObjFind (histobjfind)          SOSFlush (sosflush)
    HistClear (histclear)              SOSStatus (sosstatus)
                                       FAQ
                                       Help (soshelp)

## Commands

|Command|Description|
|-------------|-----------------|
|**bpmd** [**-nofuturemodule**] [\<*module name*> \<*method name*>] [**-md** <`MethodDesc`>] **-list** **-clear** \<*pending breakpoint number*> **-clearall**|Creates a breakpoint at the specified method in the specified module.<br /><br /> If the specified module and method have not been loaded, this command waits for a notification that the module was loaded and just-in-time (JIT) compiled before creating a breakpoint.<br /><br /> You can manage the list of pending breakpoints by using the **-list**, **-clear**, and **-clearall** options:<br /><br /> The **-list** option generates a list of all the pending breakpoints. If a pending breakpoint has a non-zero module ID, that breakpoint is specific to a function in that particular loaded module. If the pending breakpoint has a zero module ID, that breakpoint applies to modules that have not yet been loaded.<br /><br /> Use the **-clear** or **-clearall** option to remove pending breakpoints from the list.|
|**CLRStack** [**-a**] [**-l**] [**-p**] [**-n**] [**-f**] [**-r**] [**-all**]|Provides a stack trace of managed code only.<br /><br /> The **-p** option shows arguments to the managed function.<br /><br /> The **-l** option shows information on local variables in a frame. The SOS Debugging Extension cannot retrieve local names, so the output for local names is in the format \<*local address*> **=** \<*value*>.<br /><br /> The **-a** option is a shortcut for **-l** and **-p** combined.<br /><br /> The **-n** option disables the display of source file names and line numbers. If the debugger has the option SYMOPT_LOAD_LINES specified, SOS will look up the symbols for every managed frame and if successful will display the corresponding source file name and line number. The **-n** (No line numbers) parameter can be specified to disable this behavior.<br /><br />The **-f** option (full mode) displays the native frames intermixing them with the managed frames and the assembly name and function offset for the managed frames.<br /><br />The **-r** option dumps the registers for each stack frame.<br /><br />The **-all** option dumps all the managed threads' stacks.|
|**DumpArray** [**-start** \<*startIndex*>] [**-length** \<*length*>] [**-details**] [**-nofields**] \<*array object address*><br /><br /> -or-<br /><br /> **DA** [**-start** \<*startIndex*>] [**-length** \<*length*>] [**-detail**] [**-nofields**] *array object address*>|Examines elements of an array object.<br /><br /> The **-start** option specifies the starting index at which to display elements.<br /><br /> The **-length** option specifies how many elements to show.<br /><br /> The **-details** option displays details of the element using the **DumpObj** and **DumpVC** formats.<br /><br /> The **-nofields** option prevents arrays from displaying. This option is available only when the **-detail** option is specified.|
|**DumpAsync** (**dumpasync**) [**-mt** \<*MethodTable address*>] [**-type** \<*partial type name*>] [**-waiting**] [**-roots**]|DumpAsync traverses the garbage collected heap, looking for objects representing async state machines as created when an async method's state is transferred to the heap.  This command recognizes async state machines defined as `async void`, `async Task`, `async Task<T>`, `async ValueTask`, and `async ValueTask<T>`.<br /><br />The output includes a block of details for each async state machine object found. These details include:<br />- a line for the type of the async state machine object, including its MethodTable address, its object address, its size, and its type name.<br />- a line for the state machine type name as contained in the object.<br />- a listing of each field on the state machine.<br />- a line for a continuation from this state machine object, if one or more has been registered.<br />- discovered GC roots for this async state machine object.<br />|
|**DumpAssembly** \<*assembly address*>|Displays information about an assembly.<br /><br /> The **DumpAssembly** command lists multiple modules, if they exist.<br /><br /> You can get an assembly address by using the **DumpDomain** command.|
|**DumpClass** \<*EEClass address*>|Displays information about the `EEClass` structure associated with a type.<br /><br /> The **DumpClass** command displays static field values but does not display nonstatic field values.<br /><br /> Use the **DumpMT**, **DumpObj**, **Name2EE**, or **Token2EE** command to get an `EEClass` structure address.|
|**DumpDomain** [\<*domain address*>]|Enumerates each <xref:System.Reflection.Assembly> object that is loaded within the specified <xref:System.AppDomain> object address.  When called with no parameters, the **DumpDomain** command lists all <xref:System.AppDomain> objects in a process.|
|**DumpHeap** [**-stat**] [**-strings**] [**-short**] [**-min** \<*size*>] [**-max** \<*size*>] [**-thinlock**] [**-startAtLowerBound**] [**-mt** \<*MethodTable address*>] [**-type** \<*partial type name*>][*start* [*end*]]|Displays information about the garbage-collected heap and collection statistics about objects.<br /><br /> The **DumpHeap** command displays a warning if it detects excessive fragmentation in the garbage collector heap.<br /><br /> The **-stat** option restricts the output to the statistical type summary.<br /><br /> The **-strings** option restricts the output to a statistical string value summary.<br /><br /> The **-short** option limits output to just the address of each object. This lets you easily pipe output from the command to another debugger command for automation.<br /><br /> The **-min** option ignores objects that are less than the `size` parameter, specified in bytes.<br /><br /> The **-max** option ignores objects that are larger than the `size` parameter, specified in bytes.<br /><br /> The **-thinlock** option reports ThinLocks.  For more information, see the **SyncBlk** command.<br /><br /> The `-startAtLowerBound` option forces the heap walk to begin at the lower bound of a supplied address range. During the planning phase, the heap is often not walkable because objects are being moved. This option forces **DumpHeap** to begin its walk at the specified lower bound. You must supply the address of a valid object as the lower bound for this option to work. You can display memory at the address of a bad object to manually find the next method table. If the garbage collection is currently in a call to `memcopy`, you may also be able to find the address of the next object by adding the size to the start address, which is supplied as a parameter.<br /><br /> The **-mt** option lists only those objects that correspond to the specified `MethodTable` structure.<br /><br /> The **-type** option lists only those objects whose type name is a substring match of the specified string.<br /><br /> The `start` parameter begins listing from the specified address.<br /><br /> The `end` parameter stops listing at the specified address.|
|**DumpIL** \<*Managed DynamicMethod object*> &#124;       \<*DynamicMethodDesc pointer*> &#124;        \<*MethodDesc pointer*>|Displays the Microsoft intermediate language (MSIL) that is associated with a managed method.<br /><br /> Note that dynamic MSIL is emitted differently than MSIL that is loaded from an assembly. Dynamic MSIL refers to objects in a managed object array rather than to metadata tokens.|
|**DumpLog** [**-addr** \<*addressOfStressLog*>] [<*Filename*>]|Writes the contents of an in-memory stress log to the specified file. If you do not specify a name, this command creates a file called StressLog.txt in the current directory.<br /><br /> The in-memory stress log helps you diagnose stress failures without using locks or I/O. To enable the stress log, set the following registry keys under HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\\.NETFramework:<br /><br /> (DWORD) StressLog = 1<br /><br /> (DWORD) LogFacility = 0xffffffff<br /><br /> (DWORD) StressLogSize = 65536<br /><br /> The optional `-addr` option lets you specify a stress log other than the default log.|
|**DumpMD** \<*MethodDesc address*>|Displays information about a `MethodDesc` structure at the specified address.<br /><br /> You can use the **IP2MD** command to get the `MethodDesc` structure address from a managed function.|
|**DumpMT** [**-MD**] \<*MethodTable address*>|Displays information about a method table at the specified address. Specifying the **-MD** option displays a list of all methods defined with the object.<br /><br /> Each managed object contains a method table pointer.|
|**DumpModule** [**-mt**] \<*Module address*>|Displays information about a module at the specified address. The **-mt** option displays the types defined in a module and the types referenced by the module<br /><br /> You can use the **DumpDomain** or **DumpAssembly** command to retrieve a module's address.|
|**DumpObj** [**-nofields**] \<*object address*><br /><br /> -or-<br /><br /> **DO** \<*object address*>|Displays information about an object at the specified address. The **DumpObj** command displays the fields, the `EEClass` structure information, the method table, and the size of the object.<br /><br /> You can use the **DumpStackObjects** command to retrieve an object's address.<br /><br /> Note that you can run the **DumpObj** command on fields of type `CLASS` because they are also objects.<br /><br /> The `-`**nofields** option prevents fields of the object being displayed, it is useful for objects like String.|
|**DumpRuntimeTypes**|Displays the runtime type objects in the garbage collector heap and lists their associated type names and method tables.|
|**DumpStack** [**-EE**] [**-n**] [`top` *stack* [`bottom` *stac*`k`]]|Displays a stack trace.<br /><br /> The **-EE** option causes the **DumpStack** command to display only managed functions. Use the `top` and `bottom` parameters to limit the stack frames displayed on x86 platforms.<br /><br /> The **-n** option disables the display of source file names and line numbers. If the debugger has the option SYMOPT_LOAD_LINES specified, SOS will look up the symbols for every managed frame and if successful will display the corresponding source file name and line number. The **-n** (No line numbers) parameter can be specified to disable this behavior.<br /><br /> On x86 and x64 platforms, the **DumpStack** command creates a verbose stack trace.<br /><br /> On IA-64-based platforms, the **DumpStack** command mimics the debugger's **K** command. The `top` and `bottom` parameters are ignored on IA-64-based platforms.|
|**DumpSig** \<*sigaddr*> \<*moduleaddr*>|Displays information about a `Sig` structure at the specified address.|
|**DumpSigElem** \<*sigaddr*> \<*moduleaddr*>|Displays a single element of a signature object. In most cases, you should use **DumpSig** to look at individual signature objects. However, if a signature has been corrupted in some way, you can use **DumpSigElem** to read the valid portions of it.|
|**DumpStackObjects** [**-verify**] [`top` *stack* [`bottom` *stack*]]<br /><br /> -or-<br /><br /> **DSO** [**-verify**] [`top` *stack* [`bottom` *stack*]]|Displays all managed objects found within the bounds of the current stack.<br /><br /> The **-verify** option validates each non-static `CLASS` field of an object field.<br /><br /> Use the **DumpStackObject** command with stack tracing commands such as the **K** command and the **clrstack** command to determine the values of local variables and parameters.|
|**DumpVC** \<*MethodTable address*> \<*Address*>|Displays information about the fields of a value class at the specified address.<br /><br /> The **MethodTable** parameter allows the **DumpVC** command to correctly interpret fields. Value classes do not have a method table as their first field.|
|**EEHeap** [**-gc**] [**-loader**]|Displays information about process memory consumed by internal runtime data structures.<br /><br /> The **-gc** and **-loader** options limit the output of this command to garbage collector or loader data structures.<br /><br /> The information for the garbage collector lists the ranges of each segment in the managed heap.  If the pointer falls within a segment range given by **-gc**, the pointer is an object pointer.|
|**EEStack** [**-short**] [**-EE**]|Runs the **DumpStack** command on all threads in the process.<br /><br /> The **-EE** option is passed directly to the **DumpStack** command. The **-short** parameter limits the output to the following kinds of threads:<br /><br />Threads that have taken a lock.<br /><br />Threads that have been stalled in order to allow a garbage collection.<br /><br /> Threads that are currently in managed code.|
|**EHInfo** [\<*MethodDesc address*>] [\<*Code address*>]|Displays the exception handling blocks in a specified method.  This command displays the code addresses and offsets for the clause block (the `try` block) and the handler block (the `catch` block).|
|**FAQ**|Displays frequently asked questions.|
|**FindAppDomain** \<*Object address*>|Determines the application domain of an object at the specified address.|
|**GCInfo** \<*MethodDesc address*>\<*Code address*>|Displays data that indicates when registers or stack locations contain managed objects. If a garbage collection occurs, the collector must know the locations of references to objects so it can update them with new object pointer values.|
|**GCRoot** [**-nostacks**] [**-all**] \<*Object address*>|Displays information about references (or roots) to an object at the specified address.<br /><br /> The **GCRoot** command examines the entire managed heap and the handle table for handles within other objects and handles on the stack. Each stack is then searched for pointers to objects, and the finalizer queue is also searched.<br /><br /> This command does not determine whether a stack root is valid or is discarded. Use the **clrstack** and **U** commands to disassemble the frame that the local or argument value belongs to in order to determine if the stack root is still in use.<br /><br /> The **-nostacks** option restricts the search to garbage collector handles and reachable objects.<br /><br /> The **-all** option forces all roots to be displayed instead of just the unique roots.|
|**GCWhere**  *\<object address>*|Displays the location and size in the garbage collection heap of the argument passed in. When the argument lies in the managed heap but is not a valid object address, the size is displayed as 0 (zero).|
|**Help** (**soshelp**) [\<*command*>] [`faq`]|Displays all available commands when no parameter is specified, or displays detailed help information about the specified command.<br /><br /> The `faq` parameter displays answers to frequently asked questions.|
|**HistClear**|Releases any resources used by the family of `Hist` commands.<br /><br /> Generally, you do not have to explicitly call `HistClear`, because each `HistInit` cleans up the previous resources.|
|**HistInit**|Initializes the SOS structures from the stress log saved in the debuggee.|
|**HistObj** *<obj_address>*|Examines all stress log relocation records and displays the chain of garbage collection relocations that may have led to the address passed in as an argument.|
|**HistObjFind**  *<obj_address>*|Displays all the log entries that reference an object at the specified address.|
|**HistRoot** *\<root>*|Displays information related to both promotions and relocations of the specified root.<br /><br /> The root value can be used to track the movement of an object through the garbage collections.|
|**IP2MD** (**ip2md**) \<*Code address*>|Displays the `MethodDesc` structure at the specified address in code that has been JIT-compiled.|
|**Name2EE** (**name2ee**) \<*module name*> \<*type or method name*><br /><br /> -or-<br /><br /> **Name2EE** \<*module name*>**!**\<*type or method name*>|Displays the `MethodTable` structure and `EEClass` structure for the specified type or method in the specified module.<br /><br /> The specified module must be loaded in the process.<br /><br /> To get the proper type name you can browse the module by using the an IL reflection tool like Ildasm or ILSpy. You can also pass `*` as the module name parameter to search all loaded managed modules. The *module name* parameter can also be the debugger's name for a module, such as `mscorlib` or `image00400000`.<br /><br /> This command supports the Windows debugger syntax of <`module`>`!`<`type`>. The type must be fully qualified.|
|**PrintException** [**-nested**] [**-lines**] [\<*Exception object address*>]<br /><br /> -or-<br /><br /> **PE** [**-nested**] [\<*Exception object address*>]|Displays and formats fields of any object derived from the <xref:System.Exception> class at the specified address. If you do not specify an address, the **PrintException** command displays the last exception thrown on the current thread.<br /><br /> The **-nested** option displays details about nested exception objects.<br /><br /> The **-lines** option displays source information, if available.<br /><br /> You can use this command to format and view the `_stackTrace` field, which is a binary array.|
|**SyncBlk** [**-all** &#124; \<*syncblk number*>]|Displays the specified `SyncBlock` structure or all `SyncBlock` structures.  If you do not pass any arguments, the **SyncBlk** command displays the `SyncBlock` structure corresponding to objects that are owned by a thread.<br /><br /> A `SyncBlock` structure is a container for extra information that does not need to be created for every object. It can hold COM interop data, hash codes, and locking information for thread-safe operations.|
|**SOSFlush**|Flushes an internal SOS cache.|
|**SOSStatus** [**-reset**]|Displays internal SOS status or reset the internal cached state.|
|**SetHostRuntime** [\<runtime-directory\>]|This command sets the path to the .NET Core runtime to use to host the managed code that runs as part of SOS in the debugger (lldb). The runtime needs to be at least version 2.1.0 or greater. If there are spaces in directory, it needs to be single-quoted (').<br/><br/>Normally, SOS attempts to find an installed .NET Core runtime to run its managed code automatically but this command is available if it fails. The default is to use the same runtime (libcoreclr) being debugged. Use this command if the default runtime being debugged isn't working enough to run the SOS code or if the version is less than 2.1.0.<br/><br/>If you received the following error message when running a SOS command, use this command to set the path to 2.1.0 or greater .NET Core runtime. <br/><br/>`(lldb) clrstack`<br/>`Error: Fail to initialize CoreCLR 80004005 ClrStack failed`<br/><br/>`(lldb) sethostruntime /usr/share/dotnet/shared/Microsoft.NETCore.App/2.1.6`<br/><br/>You can use the "dotnet --info" in a command shell to find the path of an installed .NET Core runtime.|
|**SetSymbolServer** [**-ms**] [**-disable**] [**-log**] [**-loadsymbols**] [**-cache** \<cache-path>] [**-directory** \<search-directory>] [**-sympath** \<windows-symbol-path>] [\<symbol-server-URL>]|Enables the symbol server downloading support.<br/><br/>The **-ms** option enables downloading from the public Microsoft symbol server.<br/><br/>The **-disable** option turns on the symbol download support.<br/><br/>The **-cache** \<cache-path> option specifies a symbol cache directory. The default is $HOME/.dotnet/symbolcache if not specified.<br/><br/>The **-directory** option add a path to search for symbols. Can be more than one.<br/><br/>The **-sympath** option adds server, cache and directory paths in the Windows symbol path format.<br/><br/>The **-log** option enables symbol download logging.<br/><br/>The **-loadsymbols** option attempts to download the native .NET Core symbols for the runtime.|
|**Token2EE** \<*module name*> \<*token*>|Turns the specified metadata token in the specified module into a `MethodTable` structure or `MethodDesc` structure.<br /><br /> You can pass `*` for the module name parameter to find what that token maps to in every loaded managed module. You can also pass the debugger's name for a module, such as `mscorlib` or `image00400000`.|
|**Threads** (**clrthreads**) [**-live**] [**-special**]|Displays all managed threads in the process.<br /><br /> The **Threads** command displays the debugger shorthand ID, the CLR thread ID, and the operating system thread ID.  Additionally, the **Threads** command displays a Domain column that indicates the application domain in which a thread is executing, an APT column that displays the COM apartment mode, and an Exception column that displays the last exception thrown in the thread.<br /><br /> The **-live** option displays threads associated with a live thread.<br /><br /> The **-special** option displays all special threads created by the CLR. Special threads include garbage collection threads (in concurrent and server garbage collection), debugger helper threads, finalizer threads, <xref:System.AppDomain> unload threads, and thread pool timer threads.|
|**ThreadState \<** *State value field* **>**|Displays the state of the thread. The `value` parameter is the value of the `State` field in the **Threads** report output.|
|**U** [**-gcinfo**] [**-ehinfo**] [**-n**] \<*MethodDesc address*> &#124; \<*Code address*>|Displays an annotated disassembly of a managed method specified either by a `MethodDesc` structure pointer for the method or by a code address within the method body. The **U** command displays the entire method from start to finish, with annotations that convert metadata tokens to names.<br /><br /> The **-gcinfo** option causes the **U** command to display the `GCInfo` structure for the method.<br /><br /> The **-ehinfo** option displays exception information for the method. You can also obtain this information with the **EHInfo** command.<br /><br /> The **-n** option disables the display of source file names and line numbers. If the debugger has the option SYMOPT_LOAD_LINES specified, SOS looks up the symbols for every managed frame and, if successful, displays the corresponding source file name and line number. You can specify the **-n** option to disable this behavior.|
|**VerifyHeap**|Checks the garbage collector heap for signs of corruption and displays any errors found.<br /><br /> Heap corruptions can be caused by platform invoke calls that are constructed incorrectly.|

### Aliases ###

By default you can reach all the SOS commands by using: _sos [command\_name]_
However the common commands have been aliased so that you don't need the SOS prefix:

    bpmd            -- Creates a breakpoint at the specified managed method in the specified module.
    clrstack        -- Provides a stack trace of managed code only.
    clrthreads      -- List the managed threads running.
    clru            -- Displays an annotated disassembly of a managed method.
    dso             -- Displays all managed objects found within the bounds of the current stack.
    dumpasync       -- Displays info about async state machines on the garbage-collected heap.
    dumpclass       -- Displays information about a EE class structure at the specified address.
    dumpdomain      -- Displays information all the AppDomains and all assemblies within the domains.
    dumpheap        -- Displays info about the garbage-collected heap and collection statistics about objects.
    dumpil          -- Displays the Microsoft intermediate language (MSIL) that is associated with a managed method.
    dumplog         -- Writes the contents of an in-memory stress log to the specified file.
    dumpmd          -- Displays information about a MethodDesc structure at the specified address.
    dumpmodule      -- Displays information about a EE module structure at the specified address.
    dumpmt          -- Displays information about a method table at the specified address.
    dumpobj         -- Displays info about an object at the specified address.
    dumpstack       -- Displays a native and managed stack trace.
    eeheap          -- Displays info about process memory consumed by internal runtime data structures.
    eestack         -- Runs dumpstack on all threads in the process.
    gcroot          -- Displays info about references (or roots) to an object at the specified address.
    histclear       -- Releases any resources used by the family of Hist commands.
    histinit        -- Initializes the SOS structures from the stress log saved in the debuggee.
    histobj         -- Examines all stress log relocation records and displays the chain of garbage collection relocations that may have led to the address passed in as an argument.
    histobjfind     -- Displays all the log entries that reference an object at the specified address.
    histroot        -- Displays information related to both promotions and relocations of the specified root.
    ip2md           -- Displays the MethodDesc structure at the specified address in code that has been JIT-compiled.
    loadsymbols     -- Load the .NET Core native module symbols.
    name2ee         -- Displays the MethodTable structure and EEClass structure for the specified type or method in the specified module.
    pe              -- Displays and formats fields of any object derived from the Exception class at the specified address.
    setclrpath      -- Set the path to load coreclr dac/dbi files. setclrpath <path>
    sethostruntime  -- Sets or displays the .NET Core runtime directory to use to run managed code in SOS.
    setsymbolserver -- Enables the symbol server support.
    setsostid       -- Set the current os tid/thread index instead of using the one lldb provides. setsostid <tid> <index>
    sos             -- Various coreclr debugging commands. See 'soshelp' for more details. sos <command-name> <args>
    soshelp         -- Displays all available commands when no parameter is specified, or displays detailed help information about the specified command. soshelp <command>
    syncblk         -- Displays the SyncBlock holder info.

## Remarks

TBD

## Examples

The following command displays the contents of an array at the address `00ad28d0`.  The display starts from the second element and continues for five elements.

```
sos DumpArray -start 2 -length 5 -detail 00ad28d0
```

 The following command displays the contents of an assembly at the address `1ca248`.

```
sos DumpAssembly 1ca248
```

 The following command displays information about the garbage collector heap.

```
dumpheap
```

 The following command writes the contents of the in-memory stress log to a (default) file called StressLog.txt in the current directory.

```
dumplog
```

 The following command displays the `MethodDesc` structure at the address `902f40`.

```
dumpmd 902f40
```

 The following command displays information about a module at the address `1caa50`.

```
dumpmodule 1caa50
```

 The following command displays information about an object at the address `a79d40`.

```
dumpobj a79d40
```

 The following command displays the fields of a value class at the address `00a79d9c` using the method table at the address `0090320c`.

```
sos DumpVC 0090320c 00a79d9c
```

 The following command displays the process memory used by the garbage collector.

```
eeheap -gc
```

 The following command determines the application domain of an object at the address `00a79d98`.

```
sos FindAppDomain 00a79d98
```

 The following command displays all garbage collector handles in the current process.

```
sos GCInfo 5b68dbb8
```

 The following command displays the `MethodTable` and `EEClass` structures for the `Main` method in the class `MainClass` in the module `unittest.exe`.

```
name2ee unittest.exe MainClass.Main
```

 The following command displays information about the metadata token at the address `02000003` in the module `unittest.exe`.

```
sos Token2EE unittest.exe 02000003
```

 The following displays the managed threads and the threadstate for one:

```
clrthreads
```
```
ThreadCount:      2
UnstartedThread:  0
BackgroundThread: 1
PendingThread:    0
DeadThread:       0
Hosted Runtime:   no
                                                                                                        Lock
       ID OSID ThreadOBJ           State GC Mode     GC Alloc Context                  Domain           Count Apt Exception
   1    1 12fd 0000000000673A90    20020 Cooperative 00007FFF5801D9A0:00007FFF5801E808 0000000000654CD0 0     Ukn
   7    2 1306 0000000000697E90    21220 Preemptive  0000000000000000:0000000000000000 0000000000654CD0 0     Ukn (Finalizer)
(lldb) sos ThreadState 21220
    Legal to Join
    Background
    CLR Owns
    Fully initialized
```
