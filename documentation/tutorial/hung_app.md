The newest documentation is now being maintained at [debug-deadlock](https://learn.microsoft.com/dotnet/core/diagnostics/debug-deadlock). This documentation is no longer being updated.

# App stops responding

**IMPORTANT: This tutorial uses API/methods available in dotnet core preview 5. These API/methods are _subject to change._** 

http://localhost:5000/api/diagscenario/deadlock

In this scenario, the endpoint will experience unresponsiveness and substantial thread accumulation. We'll show how you can use both the existing tools to analyze the problem as well as extending the existing tools to create a more automated debug session. 

### Memory counters
As of preview 5, the lock contention and thread counters are not yet available. They are on the backlog to be included in future milestones. The general symptom of this issue is that one or more requests will take a long time to process and ultimately timeout.

### Core dump generation
In order to investigate applications that seem to be making no progress a memory dump is often beneficial allowing us to inspect the state of all the threads as well as any possible locks that may have contention issues. 



Run the webapi (dotnet run)  and navigate to the following URL:



http://localhost:5000/api/diagscenario/deadlock



Let the request run for about 10-15 seconds. The first thing we need in order to generate a core dump is to find the process identifier of our webapi process:

> ```bash
> $ dotnet-trace list-ps
> ...
> 80926 webapi     /home/marioh/webapi/bin/Debug/netcoreapp3.0/webapi
> sudo dotnet triggerdump.dll 80926 500
> ```



Once we have the process identifier, we can use the dotnet-dump collect tool (specifying the process identifier) to generate the core dump:



> ```bash
> $ sudo ./dotnet-dump collect -p 80926
> ```



At this point, we have the core dump and the next step is analyzing it to find the root cause of our unresponsive application. 



### Analyzing the core dump
To start our investigation, let's open the core dump using dotnet-dump analyze:



> ```bash
> $ ./dotnet-dump analyze  ~/.dotnet/tools/core_20190513_143916
> ```



Since we are looking for the root cause of the unresponsiveness, it is often useful to first get an overall feel for the thread state in the process. We can use the threads command as shown below:



> threads
> *0 0x1DBFF (121855)
>  1 0x1DC01 (121857)
>  2 0x1DC02 (121858)
>  3 0x1DC03 (121859)
>  4 0x1DC04 (121860)
>  5 0x1DC05 (121861)
>  6 0x1DC06 (121862)
>  7 0x1DC07 (121863)
>  8 0x1DC08 (121864)
>  9 0x1DC09 (121865)
>  10 0x1DC0A (121866)
>  11 0x1DC0D (121869)
>  12 0x1DC0E (121870)
>  13 0x1DC10 (121872)
>  14 0x1DC11 (121873)
>  15 0x1DC12 (121874)
>  16 0x1DC13 (121875)
>  17 0x1DC14 (121876)
>  18 0x1DC15 (121877)
>  19 0x1DC1C (121884)
>  20 0x1DC1D (121885)
>  21 0x1DC1E (121886)
>  22 0x1DC21 (121889)
>  23 0x1DC22 (121890)
>  24 0x1DC23 (121891)
>  25 0x1DC24 (121892)
>  26 0x1DC25 (121893)
>
> ...
>
> ...
>
>  317 0x1DD48 (122184)
>  318 0x1DD49 (122185)
>  319 0x1DD4A (122186)
>  320 0x1DD4B (122187)
>  321 0x1DD4C (122188)

 

The output shows all the threads currently running in the process with their associated debugger thread ID and operating system thread id. Based on the output above it looks like we have a little over 300 threads. 

The next step is to get a better understanding of what the threads are currently doing by getting the callstack. There is a command called clrstack that can be used to output both the callstack of the currently selected thread as well as a shortcut to show the callstacks for all the threads. To perform the latter, run the following command:

```bash
> clrstack -all
      
  ...
  ...
  ...
        Child SP               IP Call Site
00007F2AE37B5680 00007f305abc6360 [GCFrame: 00007f2ae37b5680]
00007F2AE37B5770 00007f305abc6360 [GCFrame: 00007f2ae37b5770]
00007F2AE37B57D0 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2ae37b57d0] System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
00007F2AE37B5920 00007F2FE392B31F testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_1() [/home/marioh/webapi/Controllers/diagscenario.cs @ 36]
00007F2AE37B5950 00007F2FE392B46D System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 201]
00007F2AE37B5CA0 00007f30593044af [GCFrame: 00007f2ae37b5ca0]
00007F2AE37B5D70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2ae37b5d70]
OS Thread Id: 0x1dc82
        Child SP               IP Call Site
00007F2AE2FB4680 00007f305abc6360 [GCFrame: 00007f2ae2fb4680]
00007F2AE2FB4770 00007f305abc6360 [GCFrame: 00007f2ae2fb4770]
00007F2AE2FB47D0 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2ae2fb47d0] System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
00007F2AE2FB4920 00007F2FE392B31F testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_1() [/home/marioh/webapi/Controllers/diagscenario.cs @ 36]
00007F2AE2FB4950 00007F2FE392B46D System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 201]
00007F2AE2FB4CA0 00007f30593044af [GCFrame: 00007f2ae2fb4ca0]
00007F2AE2FB4D70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2ae2fb4d70]
OS Thread Id: 0x1dc83
        Child SP               IP Call Site
00007F2AE27B3680 00007f305abc6360 [GCFrame: 00007f2ae27b3680]
00007F2AE27B3770 00007f305abc6360 [GCFrame: 00007f2ae27b3770]
00007F2AE27B37D0 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2ae27b37d0] System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
00007F2AE27B3920 00007F2FE392B31F testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_1() [/home/marioh/webapi/Controllers/diagscenario.cs @ 36]
00007F2AE27B3950 00007F2FE392B46D System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 201]
00007F2AE27B3CA0 00007f30593044af [GCFrame: 00007f2ae27b3ca0]
00007F2AE27B3D70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2ae27b3d70]
OS Thread Id: 0x1dc84
        Child SP               IP Call Site
00007F2AE1FB2680 00007f305abc6360 [GCFrame: 00007f2ae1fb2680]
00007F2AE1FB2770 00007f305abc6360 [GCFrame: 00007f2ae1fb2770]
00007F2AE1FB27D0 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2ae1fb27d0] System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
00007F2AE1FB2920 00007F2FE392B31F testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_1() [/home/marioh/webapi/Controllers/diagscenario.cs @ 36]
00007F2AE1FB2950 00007F2FE392B46D System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 201]
00007F2AE1FB2CA0 00007f30593044af [GCFrame: 00007f2ae1fb2ca0]
00007F2AE1FB2D70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2ae1fb2d70]
OS Thread Id: 0x1dc85
        Child SP               IP Call Site
00007F2AE17B1680 00007f305abc6360 [GCFrame: 00007f2ae17b1680]
00007F2AE17B1770 00007f305abc6360 [GCFrame: 00007f2ae17b1770]
00007F2AE17B17D0 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2ae17b17d0] System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
00007F2AE17B1920 00007F2FE392B31F testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_1() [/home/marioh/webapi/Controllers/diagscenario.cs @ 36]
00007F2AE17B1950 00007F2FE392B46D System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 201]
00007F2AE17B1CA0 00007f30593044af [GCFrame: 00007f2ae17b1ca0]
00007F2AE17B1D70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2ae17b1d70]
OS Thread Id: 0x1dc86
        Child SP               IP Call Site
00007F2AE0FB0680 00007f305abc6360 [GCFrame: 00007f2ae0fb0680]
00007F2AE0FB0770 00007f305abc6360 [GCFrame: 00007f2ae0fb0770]
00007F2AE0FB07D0 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2ae0fb07d0] System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
00007F2AE0FB0920 00007F2FE392B31F testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_1() [/home/marioh/webapi/Controllers/diagscenario.cs @ 36]
00007F2AE0FB0950 00007F2FE392B46D System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 201]
00007F2AE0FB0CA0 00007f30593044af [GCFrame: 00007f2ae0fb0ca0]
00007F2AE0FB0D70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2ae0fb0d70]
OS Thread Id: 0x1dc87
        Child SP               IP Call Site
00007F2AE07AF680 00007f305abc6360 [GCFrame: 00007f2ae07af680]
00007F2AE07AF770 00007f305abc6360 [GCFrame: 00007f2ae07af770]
00007F2AE07AF7D0 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2ae07af7d0] System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
00007F2AE07AF920 00007F2FE392B31F testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_1() [/home/marioh/webapi/Controllers/diagscenario.cs @ 36]
00007F2AE07AF950 00007F2FE392B46D System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 201]
00007F2AE07AFCA0 00007f30593044af [GCFrame: 00007f2ae07afca0]
00007F2AE07AFD70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2ae07afd70]
OS Thread Id: 0x1dc88
        Child SP               IP Call Site
00007F2ADFFAE680 00007f305abc6360 [GCFrame: 00007f2adffae680]
00007F2ADFFAE770 00007f305abc6360 [GCFrame: 00007f2adffae770]
00007F2ADFFAE7D0 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2adffae7d0] System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
00007F2ADFFAE920 00007F2FE392B31F testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_1() [/home/marioh/webapi/Controllers/diagscenario.cs @ 36]
00007F2ADFFAE950 00007F2FE392B46D System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 201]
00007F2ADFFAECA0 00007f30593044af [GCFrame: 00007f2adffaeca0]
00007F2ADFFAED70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2adffaed70]
...
...
...
```

(output abbreviated)

Eye balling the callstacks for all 300+ threads shows a pattern where a majority of the threads share a common callstack:



```bash
OS Thread Id: 0x1dc88
        Child SP               IP Call Site
00007F2ADFFAE680 00007f305abc6360 [GCFrame: 00007f2adffae680]
00007F2ADFFAE770 00007f305abc6360 [GCFrame: 00007f2adffae770]
00007F2ADFFAE7D0 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2adffae7d0] System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
00007F2ADFFAE920 00007F2FE392B31F testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_1() [/home/marioh/webapi/Controllers/diagscenario.cs @ 36]
00007F2ADFFAE950 00007F2FE392B46D System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 201]
00007F2ADFFAECA0 00007f30593044af [GCFrame: 00007f2adffaeca0]
00007F2ADFFAED70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2adffaed70]
```

The callstack seems to show that the request arrived in our deadlock method which in turn makes a call to Monitor.ReliableEnter. This is a good indication that the majority of threads are trying to enter a monitor lock and are waiting on the availability of the lock (i.e., it is currently locked by a different thread). The next step then is to find out which thread is actually holding the monitor lock. Since monitors (by and large) store lock information in the sync block table, we can use the syncblk command to get more information:



```bash
> syncblk
Index         SyncBlock MonitorHeld Recursion Owning Thread Info          SyncBlock Owner
   41 000000000143D038          603         1 00007F2B542D28C0 1dc1d  20   00007f2e90080fb8 System.Object
   42 000000000143D080            3         1 00007F2B400016A0 1dc1e  21   00007f2e90080fd0 System.Object
-----------------------------
Total           264
Free            0

```

The two interesting columns are the MonitorHeld column and the Owning Thread Info column. The former shows whether a monitor lock is acquired by a thread (and also number of waiters) and the latter shows which thread currently owns the monitor lock. Please note that the Owning Thread Info shows three different columns. The column of interest is the operating system thread id (second column). 

At this point, we know that two different threads (0x1dc1d and 0x1dc1e) each hold a monitor lock. The next step is to take a look at what those threads are doing and if there is any possibility that they are stuck indefinitely holding the lock. Let's use the setthread and clrstack commands to switch to each of the threads and display the callstacks:

```bash
> setthread 0x1dc1d
> clrstack
OS Thread Id: 0x1dc1d (20)
        Child SP               IP Call Site
00007F2B862B9610 00007f305abc6360 [GCFrame: 00007f2b862b9610]
00007F2B862B9700 00007f305abc6360 [GCFrame: 00007f2b862b9700]
00007F2B862B9760 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2b862b9760] System.Threading.Monitor.Enter(System.Object)
00007F2B862B98B0 00007F2FE392A9E5 testwebapi.Controllers.DiagScenarioController.DeadlockFunc() [/home/marioh/webapi/Controllers/diagscenario.cs @ 57]
00007F2B862B98F0 00007F2FE392A8FC testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_0() [/home/marioh/webapi/Controllers/diagscenario.cs @ 27]
00007F2B862B9910 00007F2FE02B7BA2 System.Threading.ThreadHelper.ThreadStart_Context(System.Object) [/__w/3/s/src/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs @ 51]
00007F2B862B9930 00007F2FE02C1021 System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 172]
00007F2B862B9980 00007F2FE02B7CBE System.Threading.ThreadHelper.ThreadStart() [/__w/3/s/src/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs @ 101]
00007F2B862B9CA0 00007f30593044af [GCFrame: 00007f2b862b9ca0]
00007F2B862B9D70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2b862b9d70]
> setthread 0x1dc1e
> clrstack
OS Thread Id: 0x1dc1e (21)
        Child SP               IP Call Site
00007F2B85AB8640 00007f305abc6360 [GCFrame: 00007f2b85ab8640]
00007F2B85AB8730 00007f305abc6360 [GCFrame: 00007f2b85ab8730]
00007F2B85AB8790 00007f305abc6360 [HelperMethodFrame_1OBJ: 00007f2b85ab8790] System.Threading.Monitor.Enter(System.Object)
00007F2B85AB88E0 00007F2FE392AAAE testwebapi.Controllers.DiagScenarioController.<DeadlockFunc>b__4_0() [/home/marioh/webapi/Controllers/diagscenario.cs @ 53]
00007F2B85AB8910 00007F2FE02B7BA2 System.Threading.ThreadHelper.ThreadStart_Context(System.Object) [/__w/3/s/src/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs @ 51]
00007F2B85AB8930 00007F2FE02C1021 System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object) [/__w/3/s/src/System.Private.CoreLib/shared/System/Threading/ExecutionContext.cs @ 172]
00007F2B85AB8980 00007F2FE02B7CBE System.Threading.ThreadHelper.ThreadStart() [/__w/3/s/src/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs @ 101]
00007F2B85AB8CA0 00007f30593044af [GCFrame: 00007f2b85ab8ca0]
00007F2B85AB8D70 00007f30593044af [DebuggerU2MCatchHandlerFrame: 00007f2b85ab8d70]

```

Starting with the first thread, our deadlock method (which already owns a monitor lock) seems to be trying to acquire yet another lock but is waiting for the lock to become available. Similarly, the second thread (which also owns another monitor lock), is also trying to acquire a lock and waiting for the lock to become available. 

To summarize our findings, we have two threads, each of which already own a lock and are stuck waiting for another lock to become available (deadlock). The remaining 300+ threads that are all waiting are most likely also waiting on one of the locks that caused the deadlock. 



### Simplifying the analysis process

One of the pain points in our analysis process was around having to eye ball hundreds of callstacks to see if we can spot a common pattern. It would be much easier if we had a command that could output just the unique callstacks and all the thread ID's associated with those callstacks. In this section, we'll implement that command as a custom command for dotnet-dump analyze. 

Before we get started, you need to do the following:

1. Clone the dotnet/diagnostics repo (https://github.com/dotnet/diagnostics)
2. Make sure all the pre-requisites are in place to build the repo (https://github.com/dotnet/diagnostics/blob/main/README.md)
3. Add the uniquestacks.cs file to ~/diagnostics/src/Tools/dotnet-dump/Commands folder.
4. Build the repo using ~/diagnostics/build.sh 

All the interesting command code is located in the InvokeAsync method (uniquestacks.cs) and uses CLRMD to enumerate all the threads like so:

```c#
foreach (ClrThread thread in AnalyzeContext.Runtime.Threads)
{
    if (!thread.IsAlive) continue;

    StringBuilder completeStack = new StringBuilder();
    foreach (ClrStackFrame frame in thread.StackTrace)
    {
        switch (frame.Kind)
        {
            case ClrStackFrameType.ManagedMethod:
                completeStack.Append(frame.DisplayString);      
                break;

            case ClrStackFrameType.Runtime:
                completeStack.Append("["+ frame.DisplayString +"]");

                if (frame.Method != null)
                {
                    completeStack.Append("Special");
                    completeStack.Append(frame.Method);
                }

                break;
        }

        completeStack.Append("\n");

        string cStack = completeStack.ToString();

        if(threads.ContainsKey(cStack)==true)
        {
            threads[cStack].Add(thread.OSThreadId);
        }
        else
        {
            List<uint> l = new List<uint>();
            l.Add(thread.OSThreadId);
            threads.Add(cStack, l);
        }

        }
}
```



The code simply iterates over all the threads, gets the callstacks and finally adding them to a dictionary. Once the loop has finished the dictionary will contain all the unique callstacks and associated thread ids. 

Lastly, the code prints out the dictionary allowing us to see the duplicate callstacks with ease. 

Build the dotnet-dump tool (dotnet build) and run it from the following location on the same dump that we generated earlier:

```bash
~/diagnostics/artifacts/bin/dotnet-dump/Debug/netcoreapp3.1/publish/dotnet dotnet-dump.dll analyze ~/.dotnet/tools/core_20190513_143916
```

You can now use the 'uniquestacks' command and see the following output (partial):

```bash
...
...
...

[GCFrame]
[GCFrame]
[HelperMethodFrame_1OBJ]
testwebapi.Controllers.DiagScenarioController.<deadlock>b__3_1()
System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object)
[GCFrame]

121917,121918,121919,121920,121921,121922,121923,121924,121925,121926,121927,121928,121929,121930,121931,121932,121933,121934,121935,121936,121937,121938,121939,121940,121941,121942,121943,121944,121945,121946,121947,121948,121949,121950,121951,121952,121953,121954,121955,121956,121957,121958,121959,121960,121961,121962,121963,121964,121965,121966,121967,121968,121969,121970,121971,121972,121973,121974,121975,121976,121977,121978,121979,121980,121981,121982,121983,121984,121985,121986,121987,121988,121989,121990,121991,121992,121993,121994,121995,121996,121997,121998,121999,122000,122001,122002,122003,122004,122005,122006,122007,122008,122009,122010,122011,122012,122013,122014,122015,122016,122017,122018,122019,122020,122021,122022,122023,122024,122025,122026,122027,122028,122029,122030,122031,122032,122033,122034,122035,122036,122037,122038,122039,122040,122041,122042,122043,122044,122045,122046,122047,122048,122049,122050,122051,122052,122053,122054,122055,122056,122057,122058,122059,122060,122061,122062,122063,122064,122065,122066,122067,122068,122069,122070,122071,122072,122073,122074,122075,122076,122077,122078,122079,122080,122081,122082,122083,122084,122085,122086,122087,122088,122089,122090,122091,122092,122093,122094,122095,122096,122097,122098,122099,122100,122101,122102,122103,122104,122105,122106,122107,122108,122109,122110,122111,122112,122113,122114,122115,122116,122117,122118,122119,122120,122121,122122,122123,122124,122125,122126,122127,122128,122129,122130,122131,122132,122133,122134,122135,122136,122137,122138,122139,122140,122141,122142,122143,122144,122145,122146,122147,122148,122149,122150,122151,122152,122153,122154,122155,122156,122157,122158,122159,122160,122161,122162,122163,122164,122165,122166,122167,122168,122169,122170,122171,122172,122173,122174,122175,122176,122177,122178,122179,122180,122181,122182,122183,122184,122185,122186,122187,122188
```

The output shows a large number of threads (with associated thread ID's) that is commonly a good indicator that a threading issue is taking place. 
