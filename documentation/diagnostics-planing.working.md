# Appendix/Raw Ideas

Everything after this is really not meant for consumption at this time.  It represents working notes.

## New Features

* Decouple Diagnostic information from its presentation.
  * We need tools that work in a highly integrated environment (which probably have
    a number of prerequisites (e.g. App Insights) as well as in a 'dev startup'
    scenario where trivial setup and works everywhere (no dependencies), is valuable.
    We do this by making the data available as a standard REST API, and have
    'minimal' tooling make a UI over that REST API (probably in JavaScript (just like VSCode))
    These minimal tools will be open sourced and community driven, but along side them
    we can have full featured/integrated tools (e.g. AppInsights)
  * Use JavaScript/HTML Electron for cross platform UI for these presentation tools.  (Alternatively maybe Blazor (Web Assembly))

* Standard compliance can be done with a doc driven approach.
  * If we pick a demo with a 3rd party that is committed to implementing their side
    of the correlation standards, we can write up the demo that drives the features
    needed to make that end-to-end scenario work.

* 'dotnet profile' - local machine ETW tracing.  Definitely useful.

* 'dotnet monitor' - logs monitoring information.  Ideally creates a format that other tools will just us
      thus it is really more of a format converter than anything else.  Not a lot of work, but also useful
      for people

* Currently Azure Monitor Metrics does track some performance counters

* If we care about supporting existing Application Performance Monitoring solutions, the issue of how to have
  multiple .NET Profilers connected to the same .NET Core runtime needs to be addressed.

## WORK-IN-PROGRESS

Elements of plan

* Well Instrumented Runtime
* Good Documentation on diagnostics / monitoring / profiling at [Microsoft Docs](https://docs.microsoft.com)
* Good support for Async (with Docs)
* Good support for Multi-Machine and/or MicroServices
* Works on all platforms / Architectures
* Monitoring costs very little, but you have the data you need
  * This requires sampling of REQUESTS (causality flow) Event across tiers, Azure Functions, Service Fabric. Containers

Plan

1. EventPipe (No ETW) Works on Linux, Can do counters, all inst
2. EventCounter instrumentation in the Framework.
3. Named Pipe / HTTP REST API for accessing EventPipe information for monitoring.
4. Good Causality Instrumentation (so that Async -> Sync transformation works well)

[Geneva and One DS](https://microsoft.sharepoint.com/teams/MSWHub/_layouts/15/search.aspx?k=One%20DS&q=OneDS&t=y&v=search)

[OneDS Integration Presentation](https://microsoft.sharepoint.com/teams/osg_unistore/sce/Shared%20Documents/Forms/AllItems.aspx?id=%2Fteams%2Fosg_unistore%2Fsce%2FShared%20Documents%2FTechnical%20Reference%20Documents%2F1DSIntegrationApproaches.pptx&parent=%2Fteams%2Fosg_unistore%2Fsce%2FShared%20Documents%2FTechnical%20Reference%20Documents&embed=%7B%22o%22%3A%22https%3A%2F%2Fmicrosoft.sharepoint.com%22%2C%22id%22%3A%22d9ec5f58-a52f-483e-946a-2913b83935bc%22%2C%22af%22%3Atrue%7D)

[Geneva Logging Presentation](https://microsoft.sharepoint.com/teams/mswhub/_layouts/15/search.aspx?q=OneDS&t=y&v=search&k=OneDS#Default=%7B%22k%22%3A%22OneDS%22%2C%22r%22%3A%5B%7B%22n%22%3A%22LastModifiedTime%22%2C%22t%22%3A%5B%22range(2017-11-26T22%3A42%3A43.849Z%2C%20max%2C%20to%3D%5C%22le%5C%22)%22%5D%2C%22o%22%3A%22and%22%2C%22k%22%3Afalse%2C%22m%22%3Anull%7D%5D%2C%22l%22%3A1033%7D)

[Service Logging Library (SLL)](https://microsoft.sharepoint.com/teams/mswhub/_layouts/15/search.aspx?q=SLL%20Logging&t=y&v=search&k=SLL%20Logging#Default=%7B%22k%22%3A%22SLL%20Logging%22%2C%22r%22%3A%5B%7B%22n%22%3A%22LastModifiedTime%22%2C%22t%22%3A%5B%22range(2017-11-26T22%3A44%3A19.557Z%2C%20max%2C%20to%3D%5C%22le%5C%22)%22%5D%2C%22o%22%3A%22and%22%2C%22k%22%3Afalse%2C%22m%22%3Anull%7D%5D%2C%22l%22%3A1033%7D)

[Syslog.net](https://github.com/emertechie/SyslogNet)
[RFC 5424 Syslogd transport](https://datatracker.ietf.org/doc/rfc5424/?include_text=1)

[One DS Docs](https://1dsdocs.azurewebsites.net/getting-started/csharp-getting_started.html)

[One Data Strategy 1DS](https://microsoft.sharepoint.com/teams/WAG/EngSys/Shared%20Documents/Forms/AllItems.aspx?id=%2Fteams%2FWAG%2FEngSys%2FShared%20Documents%2FTelemetry%20Collaboration%2F1DS%2F1DS%20Vision%20and%20Strategy%20(2018).docx&parent=%2Fteams%2FWAG%2FEngSys%2FShared%20Documents%2FTelemetry%20Collaboration%2F1DS&embed=%7B%22o%22%3A%22https%3A%2F%2Fmicrosoft.sharepoint.com%22%2C%22id%22%3A%222079771a-df5c-4f23-8f38-f91963fda137%22%2C%22af%22%3Atrue%7D)

[Aria](https://aria.microsoft.com/)

[Aria Event Analytics](https://www.aria.ms/?ref=vc_banner)

[Aria Telemetry SDKs](https://aria.microsoft.com/developer/downloads/downloads/telemetry-sdks)

[Azure Data Explorer](https://azure.microsoft.com/en-us/pricing/details/data-explorer/) is a public version of the Kusto which is suplanting Geneva (Asimov, Aria)

Things I think we can improve

* Making it easy to instrument
* Sampling of requests
* Harmonize System.Activity and EventSource concept of Activity.
* Guidance on how to do instrumentation.

Version 3.0 work

1. Documentation
2. I can see Existing Live Metrics on Azure Portal (Uniformly, Linux, Windows)
        Http 5XX, DataIn, DataOut, #Requests, ResponseTime.
        Ave Working set, CPU, Private Bytes
        Connections, Request in App Queue, Current Assemblies, Gen 0, 1, 2, Handle Count, Http Errors (various)
        Windows I/O Read, Write, Other, Thread Count, # AppDomain
3. Other Metrics?  (Exceptions) Some Metric that shows Async failure?
4. See Metrics Via Console / Local ?
5. Capture CPU Trace Locally.  (Linux)
6. View Trace in Visual Studio   View Traces locally (Linux)
7. Take a Heap Snapshot locally.
8. Extract a Heap Snapshot from a Crash Dump.
9. Diagnose Async starvation from Crash dump.  (VS)

Work

1. Activity ID support for Application Insights
2. Creating a 'REST-LIKE' interface for EventPipe
3. Making EventPipe Multi-session
4. Test/Validate/Fix Causality View for Async

*******************

Has some work committed or in progress.
  Experience 648209: .NET Core scenarios should support diagnostics as a perpendicular feature

In particular this one

  [.NET Core 3.0 has great Fundamentals (Acquisition, Compliance, Security, Performance, Reliability, Globalization etc.)](https://devdiv.visualstudio.com/DevDiv/_workitems/edit/633055)

[EventPipe Work](https://github.com/dotnet/coreclr/issues?q=is%3Aopen+is%3Aissue+project%3Adotnet%2Fcoreclr%2F5)

*******************

Work items in [Issues](https://github.com/dotnet/diagnostics/issues)

[Migrate DotNetDiagnostics from to AspLabs to this repo](https://github.com/dotnet/diagnostics/issues/92)

[User Story: Stream logs to a console UI in the local machine scenario](https://github.com/dotnet/diagnostics/issues/91)

[User Story: Enable diagnosing common Async problems](https://github.com/dotnet/diagnostics/issues/90)

[User Story: Heap investigation from a crash dump on a different machine Priority 2](https://github.com/dotnet/diagnostics/issues/89)

[User Story: Enable local crash dump analysis with a standalone tool](https://github.com/dotnet/diagnostics/issues/88)

[User Story: Enable ad-hoc memory leak investigation in VS](https://github.com/dotnet/diagnostics/issues/87)

[User Story: Enable ad-hoc perf trace collection in VS](https://github.com/dotnet/diagnostics/issues/86)

[User Story: Expose .NET Perf Counters in the local machine scenario](https://github.com/dotnet/diagnostics/issues/85)

[User Story: Expose .NET Core Perf Counters for App Insights Live Metrics page](https://github.com/dotnet/diagnostics/issues/84)

[Provide managed APIs for our canonical set of runtime performance counters](https://github.com/dotnet/diagnostics/issues/83)

[Add docs for .Net Core diagnostic scenarios](https://github.com/dotnet/diagnostics/issues/81)

*******************

[All .NET Core Diagnostics](https://github.com/dotnet/diagnostics/issues)

Damian Edwards, Tom McDonald, Sourabh Shirhatti

*******************

[DumpAsync work item](https://github.com/dotnet/diagnostics/issues/90)

DumpAsync

```log
!DumpAsync [-addr <Object Address>]
           [-mt <MethodTable address>]
           [-type <partial type name>]
           [-tasks]
           [-completed]
           [-fields]
           [-stacks]
           [-roots]
```

`!DumpAsync` traverses the garbage collected heap, looking for objects representing
async state machines as created when an async method's state is transferred to the
heap.  This command recognizes async state machines defined as `async void`, `async Task`,
`async Task<T>`, `async ValueTask`, and `async ValueTask<T>`.  It also optionally supports
any other tasks.

```log
"Usage: DumpAsync [-addr ObjectAddr] [-mt MethodTableAddr] [-type TypeName] [-tasks] [-completed] [-fields] [-stacks] [-roots]\n"
  "[-addr ObjectAddr]    => Only display the async object at the specified address.\n"
  "[-mt MethodTableAddr] => Only display top-level async objects with the specified method table address.\n"
  "[-type TypeName]      => Only display top-level async objects whose type name includes the specified substring.\n"
  "[-tasks]              => Include Task and Task-derived objects, in addition to any state machine objects found.\n"
  "[-completed]          => Include async objects that represent completed operations but that are still on the heap.\n"
  "[-fields]             => Show the fields of state machines.\n"
  "[-stacks]             => Gather, output, and consolidate based on continuation chains / async stacks for discovered async objects.\n"
  "[-roots]              => Perform a gcroot on each rendered async object.\n"
```

[Stephen's Async working group slides](https://microsoft-my.sharepoint.com/:p:/p/stoub/EfbGD3TCYlFMkR1jG6XlXFkBx9UJ_Wr1y458IZUJ_fJ9Zg?e=Q8CXrg)

[Notes from the 10/23/18 working group meeting](https://microsoft-my.sharepoint.com/personal/stoub_microsoft_com/_layouts/15/WopiFrame.aspx?sourcedoc={934d02da-ac53-4dc3-8a64-33921d886c04}&action=edit&wd=target%28Untitled%20Section.one%7C709eb0e8-9290-4071-98a7-23c752b25f99%2FOct%205%2C%202018%7C6341db20-85e9-4468-8446-b13fe50da646%2F%29&wdorigin=703)
