#Dotnet Diagnostic Tools CLI Design

Capturing some comments from https://github.com/dotnet/diagnostics/issues/85 as a starting point
we can edit from. This needs cleanup.

@davidfowl wrote:

@shirhatti We're thinking it should be part of dotnet-collect since all of the flags and infrastructure would likely be the same and it should have a "top" like interface. 

![image](https://user-images.githubusercontent.com/95136/49297973-09575400-f471-11e8-99ec-823e616eafa2.png)

We'll need to decide what things we show (aggregations and counters)




@shirhatti wrote:

```
NAME

    dotnet-collect - Collect diagnostic information from a .NET process

SYNOPSIS

    dotnet collect [-v, --version] [-h, --help]
                   [-p, --process-id]
                   [-o, --output]
                   <command> [<args>]

OPTIONS

    -v, --version
        Prints the version of the dotnet collect utility.

    -h, --help
        Prints the synopsis and a list of the most commonly used commands. 

    -p, --process-id <PROCESS_ID>
        The process id of the process you want to collect diagnostic information
        from.
    
    -o, --output <OUTPUT_DIRECTORY>
        The output directory where this diagnostic data should be written to.


================================================================================

NAME

    dotnet-collect-dump - Collect a process dump 

SYNOPSIS

    dotnet collect dump [-h, --help]
                        
DESCRIPTION

    On Windows, dotnet-collect-dump collects a Windows minidump.
    On Linux, dotnet-collect-dump collects a core dump using createdump.

OPTIONS

    -h, --help
        Prints the synopsis and a list of the most commonly used commands. 

================================================================================

NAME

    dotnet-collect-trace - Collect a trace

SYNOPSIS

    dotnet collect dump [-h, --help]
                        [--provider]
                        [--buffer]
                        

OPTIONS

    -h, --help
        Prints the synopsis and a list of the most commonly used commands.

    --provider <PROVIDER_SPEC>
        An EventPipe provider to enable.
        A string in the form '<provider name>:<keywords>:<level>'. 
    
    --buffer <BUFFER_SIZE_IN_MB>
        The size of the in-memory circular buffer in megabytes.

================================================================================

NAME

    dotnet-monitor

SYNOPSIS

    dotnet monitor [-v, --version] [-h, --help]
                   [--provider]
                   [--buffer]
                        

OPTIONS

    -v, --version
        Prints the version of the dotnet collect utility.

    -h, --help
        Prints the synopsis and a list of the most commonly used commands.

    --provider <PROVIDER_SPEC>
        An EventPipe provider to enable.
        A string in the form '<provider name>:<keywords>:<level>'. 
    
    --buffer <BUFFER_SIZE_IN_MB>
        The size of the in-memory circular buffer in megabytes.

================================================================================
```

# Background Info

## Command line tools with similar roles

### perf

Perf is a tool that collects performance traces on Linux in kernel or user-mode. It follows a perf <verb\> <arguments\> convention for its CLI.

     perf

	 usage: perf [--version] [--help] COMMAND [ARGS]
	
	 The most commonly used perf commands are:
	  annotate        Read perf.data (created by perf record) and display annotated code
	  archive         Create archive with object files with build-ids found in perf.data file
	  bench           General framework for benchmark suites
	  buildid-cache   Manage <tt>build-id</tt> cache.
	  buildid-list    List the buildids in a perf.data file
	  diff            Read two perf.data files and display the differential profile
	  inject          Filter to augment the events stream with additional information
	  kmem            Tool to trace/measure kernel memory(slab) properties
	  kvm             Tool to trace/measure kvm guest os
	  list            List all symbolic event types
	  lock            Analyze lock events
	  probe           Define new dynamic tracepoints
	  record          Run a command and record its profile into perf.data
	  report          Read perf.data (created by perf record) and display the profile
	  sched           Tool to trace/measure scheduler properties (latencies)
	  script          Read perf.data (created by perf record) and display trace output
	  stat            Run a command and gather performance counter statistics
	  test            Runs sanity tests.
	  timechart       Tool to visualize total system behavior during a workload
	  top             System profiling tool.

     See 'perf help COMMAND' for more information on a specific command.



Perf stat [options] <command\_line\> [more\_options] runs the command-line, collects performance statistics, and then displays the counters:


	perf stat -B dd if=/dev/zero of=/dev/null count=1000000
	
	1000000+0 records in
	1000000+0 records out
	512000000 bytes (512 MB) copied, 0.956217 s, 535 MB/s
	
	 Performance counter stats for 'dd if=/dev/zero of=/dev/null count=1000000':
	
	            5,099 cache-misses             #      0.005 M/sec (scaled from 66.58%)
	          235,384 cache-references         #      0.246 M/sec (scaled from 66.56%)
	        9,281,660 branch-misses            #      3.858 %     (scaled from 33.50%)
	      240,609,766 branches                 #    251.559 M/sec (scaled from 33.66%)
	    1,403,561,257 instructions             #      0.679 IPC   (scaled from 50.23%)
	    2,066,201,729 cycles                   #   2160.227 M/sec (scaled from 66.67%)
	              217 page-faults              #      0.000 M/sec
	                3 CPU-migrations           #      0.000 M/sec
	               83 context-switches         #      0.000 M/sec
	       956.474238 task-clock-msecs         #      0.999 CPUs
	
	       0.957617512  seconds time elapsed


Perf record <comamnd\_line\> collects a trace for the given command\_line

	perf record ./noploop 1
	
	[ perf record: Woken up 1 times to write data ]
	[ perf record: Captured and wrote 0.002 MB perf.data (~89 samples) ]


Perf report [options] reads data from the trace file and renders it to the command-line

	perf report
	
	# Events: 1K cycles
	#
	# Overhead          Command                   Shared Object  Symbol
	# ........  ...............  ..............................  .....................................
	#
	    28.15%      firefox-bin  libxul.so                       [.] 0xd10b45
	     4.45%          swapper  [kernel.kallsyms]               [k] mwait_idle_with_hints
	     4.26%          swapper  [kernel.kallsyms]               [k] read_hpet
	     2.13%      firefox-bin  firefox-bin                     [.] 0x1e3d
	     1.40%  unity-panel-ser  libglib-2.0.so.0.2800.6         [.] 0x886f1
	     [...]


perf top monitors a machine and shows an updating console UI with the most expensive functions

	perf top
	-------------------------------------------------------------------------------------------------------------------------------------------------------
	  PerfTop:     260 irqs/sec  kernel:61.5%  exact:  0.0% [1000Hz
	cycles],  (all, 2 CPUs)
	-------------------------------------------------------------------------------------------------------------------------------------------------------
	
	            samples  pcnt function                       DSO
	            _______ _____ ______________________________ ___________________________________________________________
	
	              80.00 23.7% read_hpet                      [kernel.kallsyms]
	              14.00  4.2% system_call                    [kernel.kallsyms]
	              14.00  4.2% __ticket_spin_lock             [kernel.kallsyms]
	              14.00  4.2% __ticket_spin_unlock           [kernel.kallsyms]
	               8.00  2.4% hpet_legacy_next_event         [kernel.kallsyms]
	               7.00  2.1% i8042_interrupt                [kernel.kallsyms]
	               7.00  2.1% strcmp                         [kernel.kallsyms]
	               6.00  1.8% _raw_spin_unlock_irqrestore    [kernel.kallsyms]
	               6.00  1.8% pthread_mutex_lock             /lib/i386-linux-gnu/libpthread-2.13.so
	               6.00  1.8% fget_light                     [kernel.kallsyms]
	               6.00  1.8% __pthread_mutex_unlock_usercnt /lib/i386-linux-gnu/libpthread-2.13.so
	               5.00  1.5% native_sched_clock             [kernel.kallsyms]
	               5.00  1.5% drm_addbufs_sg                 /lib/modules/2.6.38-8-generic/kernel/drivers/gpu/drm/drm.ko



### Pprof

Pprof is both a runtime library used by golang to collect trace data as well as a CLI tool to visualize that data after it has been collected. The CLI tool is the focus here. Snippets below from https://github.com/google/pprof/blob/master/doc/README.md

Pprof follows the convention Pprof <format\> [options] source. 

Unlike many of the other tools there is no need for a verb because it only does one action, reporting on trace data. Source can be an on-disk file or a URL that is streaming the trace data. Format is flexible enough to include text on the console, file based graphics formats, and optionally starting a web browser to visualize content. 

Interactive terminal use:

    pprof [options] source

Web Interface

    pprof -http=[host]:[port] [options] source

Common options:

- -flat [default], -cum: Sort entries based on their flat or cumulative weight respectively, on text reports.
- -functions [default], -filefunctions, -files, -lines, -addresses: Generate the report using the specified granularity.
- -noinlines: Attribute inlined functions to their first out-of-line caller. For example, a command like pprof -list foo -noinlines profile.pb.gz can be used to produce the annotated source listing attributing the metrics in the inlined functions to the out-of-line calling line.
- -nodecount= int: Maximum number of entries in the report. pprof will only print this many entries and will use heuristics to select which entries to trim.
- -focus= regex: Only include samples that include a report entry matching regex.
- -ignore= regex: Do not include samples that include a report entry matching regex.
- -show_from= regex: Do not show entries above the first one that matches regex.
- -show= regex: Only show entries that match regex.
- -hide= regex: Do not show entries that match regex.


###Jcmd

Java previously had numerous single-role tools such as jhat, jps, jstack, jinfo, etc that did a variety of diagnostic tasks (respectively they show heap analysis, process status, stacks, and runtime/machine info). Starting in Java8 jcmd, a new multi-role tool, offers a super-set of functionality from all those tools. Snippets below are from https://docs.oracle.com/javase/8/docs/technotes/guides/troubleshoot/tooldescr006.html

Jcmd uses a Jcmd <process\_id/main\_class\> <verb> [options] convention. The set of verbs that are supported varies dynamically depending on the version of the java runtime running in the indicated process. This makes jcmd more of a proxy for a CLI in the runtime than a CLI tool in its own right.

	> jcmd
	5485 sun.tools.jcmd.JCmd
	2125 MyProgram
	 
	> jcmd MyProgram help (or "jcmd 2125 help")
	2125:
	The following commands are available:
	JFR.stop
	JFR.start
	JFR.dump
	JFR.check
	VM.native_memory
	VM.check_commercial_features
	VM.unlock_commercial_features
	ManagementAgent.stop
	ManagementAgent.start_local
	ManagementAgent.start
	Thread.print
	GC.class_stats
	GC.class_histogram
	GC.heap_dump
	GC.run_finalization
	GC.run
	VM.uptime
	VM.flags
	VM.system_properties
	VM.command_line
	VM.version
	help


### WPR

The Windows Performance Recorder is a CLI or GUI tool to capture etw traces on windows. Rather than tracing directly in the WPR process, the trace operates similar to a background service and WPR is a front-end that sends commands to modify its operation. Snippets below from  https://docs.microsoft.com/en-us/previous-versions/windows/it-pro/windows-8.1-and-8/hh448229(v=win.10)

WPR uses the WPR -<verb\> [options] convention.


	wpr {-profiles [<path> [ …]] |
         -start<arguments> |
         -stop<arguments> |
         -cancel |
         -status<arguments> |
         -log<argument> |
         -purgecache |
         -help<arguments> |
         -profiledetails |
         -disablepagingexecutive}


### Perfview

Perfview is CLI or GUI tool that allows collecting, analyzing and viewing ETW traces. 

PerfView uses the PerfView <verb/> [options] CLI convention:

	PerfView [DataFile]
		run CommandAndArgs ...
		collect [DataFile]
		start [DataFile]
		stop
		mark [Message]
		abort
		merge [DataFile]
		unzip [DataFile]
		listSessions
		ListCpuCounters
		EnableKernelStacks
		DisableKernelStacks
		HeapSnapshot Process [DataFile]
		ForceGC Process
		HeapSnapshotFromProcessDump ProcessDumpFile [DataFile]
		GuiRun
		GuiCollect
		GuiHeapSnapshot
		UserCommand CommandAndArgs ...
        ...


PerfView has some commands that manipulate an ongoing trace without keeping the PerfView process running (example: start/stop/mark/abort), other commands that capture traces synchronously (example: collect/run), and then further commands that manipulate or view trace data that is already on disk.

### ProcDump

ProcDump is a tool for capturing one or more process dumps (core file on Linux). Historically it was Windows only but it has recently been made cross-platform. Snippets below are from https://docs.microsoft.com/en-us/sysinternals/downloads/procdump

ProcDump uses CLI convention: ProcDump [options]

	usage: procdump [-a] [[-c|-cl CPU usage] [-u] [-s seconds]] [-n exceeds] [-e [1 [-b]] [-f <filter,...>] [-g] [-h]
     [-l] [-m|-ml commit usage] [-ma | -mp] [-o] [-p|-pl counter threshold] [-r] [-t] [-d <callback DLL>] [-64] <[-w]
     <process name or service name or PID> [dump file] | -i <dump file> | -u | -x <dump file> <image file> [arguments]
     >] [-? [ -e]
	Parameter
	Description
	-a
	Avoid outage. Requires -r. If the trigger will cause the target to suspend for a prolonged time due to an exceeded concurrent dump limit, the trigger will be skipped.
	-b
	Treat debug breakpoints as exceptions (otherwise ignore them).
	-c
	CPU threshold at which to create a dump of the process.
	-cl
	CPU threshold below which to create a dump of the process.
	-d
	Invoke the minidump callback routine named MiniDumpCallbackRoutine of the specified DLL.
	-e
	Write a dump when the process encounters an unhandled exception. Include the 1 to create dump on first chance exceptions.
	-f
	Filter the first chance exceptions. Wildcards (*) are supported. To just display the names without dumping, use a blank ("") filter.
	-g
	Run as a native debugger in a managed process (no interop).
	-h
	Write dump if process has a hung window (does not respond to window messages for at least 5 seconds).
	-i
	Install ProcDump as the AeDebug postmortem debugger. Only -ma, -mp, -d and -r are supported as additional options.
	-l
	Display the debug logging of the process.
	-m
	Memory commit threshold in MB at which to create a dump.
	-ma
	Write a dump file with all process memory. The default dump format only includes thread and handle information.
	-ml
	Trigger when memory commit drops below specified MB value.
	-mp
	Write a dump file with thread and handle information, and all read/write process memory. To minimize dump size, memory areas larger than 512MB are searched for, and if found, the largest area is excluded. A memory area is the collection of same sized memory allocation areas. The removal of this (cache) memory reduces Exchange and SQL Server dumps by over 90%.
	-n
	Number of dumps to write before exiting.
	-o
	Overwrite an existing dump file.
	-p
	Trigger on the specified performance counter when the threshold is exceeded. Note: to specify a process counter when there are multiple instances of the process running, use the process ID with the following syntax: "\Process(<name>_<pid>)\counter"
	-pl
	Trigger when performance counter falls below the specified value.
	-r
	Dump using a clone. Concurrent limit is optional (default 1, max 5).
	CAUTION: a high concurrency value may impact system performance.
	- Windows 7   : Uses Reflection. OS doesn't support -e.
	- Windows 8.0 : Uses Reflection. OS doesn't support -e.
	- Windows 8.1+: Uses PSS. All trigger types are supported.
	-s
	Consecutive seconds before dump is written (default is 10).
	-t
	Write a dump when the process terminates.
	-u
	Treat CPU usage relative to a single core (used with -c).
	As the only option, Uninstalls ProcDump as the postmortem debugger.
	-w
	Wait for the specified process to launch if it's not running.
	-x
	Launch the specified image with optional arguments. If it is a Store Application or Package, ProcDump will start on the next activation (only).
	-64
	By default ProcDump will capture a 32-bit dump of a 32-bit process when running on 64-bit Windows. This option overrides to create a 64-bit dump. Only use for WOW64 subsystem debugging.
	-?
	Use -? -e to see example command lines.

###Perfmon

Perfmon is a Windows GUI tool that shows interactive performance counters and some reports of system performance. It has a minimal CLI that simply launches different GUI views. 

perfmon </res|report|rel|sys>
