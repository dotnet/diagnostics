# .NET Core Diagnostics Vision 

[This document](https://github.com/dotnet/diagnostics/blob/main/documentation/diagnostics-planing.md)
describes a high level vision/plan for diagnostics / monitoring for the .NET Core runtime.
The overarching goal is easy to state.

* .NET Core should be the most diagnosable, easiest to manage service/app framework platform bar none

This document tries to bring more details into exactly how we can achieve this goal.

## What are the Goals of our Customers.

Broadly speaking our customer is someone building a cloud service (in Azure).  There will be great
variability in both the kinds of applications as well as the scale and complexity of the apps, but 
it is likely that every such customer has very similar needs with respect to monitoring and Diagnostics.
In particular 

  * Our customers care about the users of their service.  Thus they want to 
      * Monitor service failures proactively - Failure are VERY bad.
      * Monitor service response time - Slow service is bad.
  * Our customers care about costs 
      * Thus they want good billing information and the ability to diagnose any surprises.
      * To reason about costs, they also need throughput information (that is benefit as well as cost)
  * Our customers wish to proactively avoid problems with their service 
      * Security/Privacy.  They want their service to be hacker proof.  They want information about attacks an defenses.
      * Availability.  They want to know about any redundancy, failover and geolocation.  Failover needs testing (e.g. Chaos-Monkey) 
      * End-to-end visibility.  They will want external 'customer-like' testing to know that networking and routing to the service works properly.
      * Dependency management.  If the services use other services (including third parties), they need to monitor them as well.
      * Data management.  Regulations like [GDPR](https://eugdpr.org/the-regulation/) places constraints on data storage and management.
  * Our customers need to fix the problems they find.
      * They want the data to not only tell them problem exists, but enough additional information to easily guide them to corrective action.
      * They want the data to quickly blame the correct owner (e.g. cloud vendor, external service, or their service)  
      * They want the data presented in a way that they can understand (mirrors the model they programmed against)

### Environment Independence

So customers want information out of their Diagnostics/Monitoring system that help reach the above goals.
But it is also important to say that they want a certain uniformity, and it is this uniformity 
that is actually often the hardest part.  In particular they want this information regardless of
their choices with respect to:

  * The hardware being used (e.g. X64, X64, ARM64 ...)
  * The operating system being used (Windows, Linux ...)
  * The software environment in use (Raw VMs, containers, Azure Service Fabric, Web Applications (PAAS), Azure Functions, ...)
  * The other services used (Database, Storage, Redis Caches, Identity/Authorization (e.g. Microsoft ID, FaceBook Google)

### The Need for Collection (Verbosity) Control.

In addition to working where they need it, they also need the right 'amount' of information.  Too little 
information and problems simply don't get solved, and this has a SEVERE impact on their business.  Too much 
detail all the time simply slows things down (sometime prohibitively), and consumes resources to transmit, store, 
retrieve and analyze.  Thus a good system need to start out with a modest amount of 'always on' information
with the ability to collect more to solve basically 'any' problem.  The 'always on' component looks a lot 
like traditional logging, and the 'very detailed' end of the spectrum looks a lot like traditional debugging, 
but there are a sliding scale between these two extremes with lots of possible variation.  The important
high level point is that the ability to control the verbosity will be needed.

### Summary 

While the above description may not be complete, it is very likely to be the most important concerns of 
service providers.  Fundamentally they also have to keep their service running will to make money, so it
makes sense that the would care most about the very visible characteristics that are central to keeping things
running (making money) with a minimum of cost/effort.  Ultimately they want to know that their service is
up and performing well, and when it is not, they have sufficient information to fix things.  They want
this to be true regardless of hardware, platform, environment or what services are used.

## What is .NET Core's role? 

Many of the items on the above list are cloud (Azure) itself (e.g. billing, geolocation, failover ...) and the
.NET Core runtime does not play a central role.  The.NET Core runtime role is to:

   * Provide hooks that enable very detailed logging and control.  This includes traditional 
     debugging (where you can get extremely detailed information and very precise control) but
     also includes any other 'Profiler' hooks that allow for collecting detailed information
     about what the runtime itself is doing.
   * Insuring that all the interesting information that only the runtime has is exposed in some way.
   * Provide APIs/Standards/Guidance for adding instrumentation for code outside the runtime.
     This includes the functionality of traditional 'logging' as well as counters for light
     weight monitoring.  It also includes standards for correlation IDs mark particular pieces
     of telemetry as being part of semantically relevant groups (like a single user request).
   * Using those standards to instrument important places in the in the .NET Framework libraries.
     
### Tooling support  

While the hooks/infrastructure that the runtime provides are critical, there are other parts that
are just as critical including
   
   * An agent/tool that controls the hooks, stores it (either locally or in the cloud), 
     organizes it (with retention policies etc), as well as tooling for accessing the 
     information.

Moreover it is likely that there will be constraints on this system including

   * Control over the verbosity and targeting a particular investigation.
   * Tight constraints over impact to the running system (it is live, serving customers)
   * Restrictions on where the data can be stored and managed (e.g. for [GDPR](https://eugdpr.org/the-regulation/))
   * Accepting information from 'foreign' services to incorporate into the system.
   * Working with existing monitoring systems.

In addition there are likely to be different environments 

   * A development environment where quick setup and ease of use are critical.
   * A production environment where very low overhead, and low impact are critical.
   * A test environment where excellent drill-into detail is a high priority.

This argues for a set of tools/tooling configurations that can all operate using the
same runtime hooks.  The key point here is that the hooks and other .NET Core 
support are of no value without the tools.

## What Problems does .NET Core Runtime Monitoring need to Address

It is clear that the a diagnostics/monitoring system for services has to handle
the basics: namely get information about 'interesting' events like errors (exceptions)
as well as measure top level metrics (e.g. response time, throughput etc).  However
in addition to these, there are some requirements that are not so obvious that
deserve special mention because they either affect many parts of the design, or 
simply will require a lot of work to solve.  We list them here.

### Supporting Asynchronous Programming

Asynchronous support is a big, complex issue that deserves special mention because
it is a particular pain point with customers and is a huge issue for diagnostics.

Traditionally code was written in a 'synchronous' style where there was a single thread
of computation and instructions were executed sequentially.  In a server environment
this mode was generalized to be 'multi-threaded' where a service could have many 
independent threads of execution each one executing sequentially.  This works well 
for services that handle up to hundreds of concurrent request simultaneously.
However if the service wishes to handle thousands or 10s of thousands of outstanding 
requests the overhead of the thread (which includes a full execution stack and 
related OS machinery), and switching between threads becomes problematic.

The solution to this is scaling problem is to write asynchronous code.  In this style
of coding, when doing non-CPU activities, instead of the thread blocking for the operation to 
complete, it simply creates an 'work-item' (in .NET we use a class called System.Threading.Task) that
represents the in-flight operation.  You can then associate callback code called a 
continuation with the work-item that will run when the operation completes.  Because 
the work item 'remembers' what to do next, the thread can do other work (typically some
other continuation), while it is waiting.  Thus even with thousands of outstanding 
requests you only need a handful threads (e.g. one per processor) to service them.

Sadly Asynchronous code is significantly harder to write than synchronous code.  Basically
all the value that the call stack was providing now has to be done by the programmer using
continuations.  It is VERY easy to get this wrong, especially considering error processing.
On the .NET Platform the C# language and runtime have created a bunch of features (e.g. async
methods and Tasks), that try to make it easier to write asynchronous code.  Basically
users write something that looks much more like synchronous code, and the C# compiler
and the runtime conspire to turn this code into efficient asynchronous code.

While this support greatly eases the effort to write correct asynchronous code, it also creates 
a rather HUGE diagnostics problem.  The code that the programmer writes BY DESIGN looks
a lot like synchronous code, but under the hood, the C# compiler morphs the code in
non-trivial ways, mangling the method names to make state machines that serves as 
continuations needed for the I/O callbacks.  There is no longer a 'call stack' or in 
fact any 'blocking' by threads.  Instead methods return Tasks and you 'await' them, which
means scheduling a continuation state machine to run when the task completes.

The details are non-trivial, but the key point here is that there is 
a very significant difference between how the programmer 'thinks' about
the code (he thinks of it as being synchronous but efficient), and what is really 
happening (state machines being created, linked together and threads reused without
blocking).  Since the diagnostics/monitoring/profiling system sees what is 'really' 
happening, it has to morph the data it gets back into something that matches the 
model the programmer is expecting.

Moreover asynchronous code tends to be in the most performance-critical part of a service, 
and users will want the ability to monitor in-production systems.  Thus there is
a premium in making it so that the asynchronous-to-synchronous view transformation can 
be done with very low runtime overhead (which makes it hard).

#### Async and Request Correlation

One of the very pleasant side effects of synchronous programming was the very simple
correlation model.  A thread starts processing a request, and that thread is effectively
'committed' to doing that request until the request completes.  Thus the threadID acts
a a correlation ID for the request itself.  That is, every operation that is associated with 
that thread is also related to the request (since the thread does nothing else).
However with asynchronous programming a request is broken up into many small continuations, and the threads 
run these continuations in an effectively random order.  Thus another way of 'marking'
operations as belonging to a request must be created.

TODO Scenarios: Live, Post-mortem, Trace.

### Multi-Service Programming Activity Correlation

As we have seen, asynchronous code makes correlating a request to a operations needed
to perform the request more challenging since a thread ID does not work as a correlation
ID as well as it does in synchronous code.  However even purely synchronous code has
the same correlation problem when it requires services on other machines.  When
a request causes a database call (or any other oof-machine service) again the thread ID
can no longer serve as the correlation ID.  We need something to link the requestor and
the service provider together.

There is a trend in the industry toward [microservices](https://azure.microsoft.com/en-us/blog/microservices-an-application-revolution-powered-by-the-cloud/)
which break an application up into a set of smaller services that are very loosely coupled
(in particular they are not necessarily executed in the same process).  This makes
a thread ID even less useful as a correlation ID.  In short, we need a way of marking
operations as part that works works well in an async/microservice/multi-tier world.

#### Industry-wide Standards

It is already the case that applications use 3rd party services for things like authorization
and authentication (think logging in with your facebook/google/microsoft account).  To
do a proper job of tracing through these services, correlation IDS need to be passed around
among these 3rd parties.  It is also useful for other information to flow with the 
request (e.g. whether this particular request should have detailed logging turned on 
while it is being processed).  Supporting good logging with 3rd parties requires 
some standards of each party.
There are already w3c draft standards for the [Trace Context](https://w3c.github.io/trace-context/) ([github](https://github.com/w3c/trace-context))
and [Trace Context Headers](https://w3c.github.io/correlation-context/) ([github](https://github.com/w3c/correlation-context))
that allow for this kind of 3rd party interoperation.  We should be supporting this.

## Inventory of Existing .NET Core Monitoring / Diagnostics Infrastructure 

The .NET Core runtime already has quite a bit of infrastructure associated with 
monitoring or diagnostics.

* Formatted Text Logging (ILogger)
	* Microsoft.Extensions.Logging.ILogger - Assumes the ASP.NET Dependency Injection Framework.  Not suitable for general framework use because of this.  Definitely relevant.
	* System.Diagnostics.TraceSource - Arguably only here for compatibility.  Probably not relevant.
	* System.Diagnostics.Trace - Not expected to be used in production scenarios.  Ultra-simple development-time logging.  Probably not relevant.

* Structured loggers
    * System.Diagnostics.DiagnosticSource - Designed specifically for the in-process case (listener sees true objects).  Relatively new, meant to be used by in-proc monitors, or feed into EventSource.  Definitely relevant.
	* System.Diagnostics.Tracing.EventSource - Designed for the out of process case (all data serialized).  Built on top of ETW (Windows) or EVentPipe (Linux and Windows) Definitely relevant.

* System.Diagnostics.Activity - helper class for flowing diagnostics information around.  Defines a correlation ID.

* EventPipe - A light-weight scalable system for logging events that underlies System.Diagnostics.Tracing.EventSource.  Built original as an ETW replacement for Linux, but ultimately may supplant ETW on Windows as well.

* [Snapshot Debugger](https://docs.microsoft.com/en-us/azure/application-insights/app-insights-snapshot-debugger?toc=/azure/azure-monitor/toc.json)  Capture memory snapshots at user defined points.

* [Azure Monitor](https://docs.microsoft.com/en-us/azure/azure-monitor/overview) - An umbrella of monitoring technologies offered in Azure.
    * [Application Insights](https://docs.microsoft.com/en-us/azure/application-insights/app-insights-overview?toc=/azure/azure-monitor/toc.json) - A full featured structured logger + tools/dashboards to view data.
    * [AppInsights Profiler](https://docs.microsoft.com/en-us/azure/application-insights/app-insights-profiler) - A collector of highly detailed data for drilling into fine details of a performance problem.
    * [Application Map](https://docs.microsoft.com/en-us/azure/application-insights/app-insights-app-map?toc=/azure/azure-monitor/toc.jso) A viewer that shows the high level causal flow of a multi-service application.

    * [Azure Service Diagnostics](https://blogs.msdn.microsoft.com/appserviceteam/2018/06/06/app-service-diagnostics-profiling-an-asp-net-web-app-on-azure-app-service/) Trace collecting in Azure.

* [Geneva Monitoring](https://genevamondocs.azurewebsites.net/)- This is an internal telemetry system for applications that Microsoft itself supports (e.g. Azure Storage, Office 365, Bing, Windows services ...)
It supports Azure but also private systems like Autopilot. Quite a bit of Azure Monitor uses technology from Geneva.  This is how
Azure knows how much CPU and Memory your VM instances are taking.  Azure's MA (Monitoring Agent) is a Geneva client 
     * [Geneva Getting Started](https://genevamondocs.azurewebsites.net/getting%20started/intro.html)
     * [Geneva Analytics](https://www.analytics.msftcloudes.com/v2/#/), is the 'back end' that allows you to make interesting queries.


* Time Travel Debugging (highly detailed logs that allow program replay (and reverse execution)

* Intellitrace - TODO

## Inventory of Vendors in for Application Performance Monitoring

  * NuRelic
  * AppDynamics
  * Stackify Retrace
  * Dynatrace 
  * Riverbed SteelCentral
  * Dell Foglight
  * See https://www.imobdevtech.com/Blog/list-of-top-10-application-performance-monitoring-tools/ for more.
  * see https://stackify.com/application-performance-management-tools/ (from Stackify)
  * see https://raygun.com/blog/performance-monitoring-tools/ (from raygun)

Feedback on Application Insights as an APM tool

* We included App Insights in our list, but it is arguably not a full-fledged APM solution. It does not do code level profiling but instead provides some high-level performance details only for SQL queries and web service calls. We would call it “APM light”.
* Cons: No reporting per SQL query. No transaction tracing view. Does not correlate logs, errors, and request details well. No reporting across apps. Does not work for non web apps without major code changes. Can not track the performance of any line of code in your app via custom CLR profiling.

### What do other Runtime environments do.

TODO 

## Elements of the Plan for Diagnostics/Monitoring.

* TODO this is not done, these are rough thoughts currently.

### Philosophy/Properties of the Solution

* Integration with Azure is a feature, not a bug.  We wish the 'in Azure' experience to be
  very good, and we are not that interested in other cloud platforms.
* Bottom Up, Modular Design
    * Ultimately we want a beautify nicely integrated, better-on-Azure scenario working well.
      However as you build out that vision, you take dependencies on platforms, environments, 
      storage, correlations, tools and other infrastructure that will inevitably leave some 
      users behind (who get NO value).  Instead build the system bottom up in a modular way
      so that the components at the bottom have very few dependencies and thus should work 
      for 'anyone'.  This gives everyone SOME options.
* Make sure the basics work.  Work off our debt in terms of platforms and insure that what we have works as intended.
    * We have a pretty-good story on Windows in the sense that the runtime/OS can collect 
      the data needed to diagnose most problems in a production setting.  We are approaching
      parity for this on Linux, but we still have non-trivial amount of work to do.
    * Even on Windows we have a non-trivial number of complaints that sometimes things just
      dont work or have issues with symbols or whatever.  Typically these problem are
      catastrophic in the sense that users get NO value out of the feature unless the 
      problem is fixed.
    * There are know limitations / unfriendliness in our current windows implementations 
      (typically with respect to async support).  We should just fix this.
    * We should not detect diagnostic infrastructure issues in the field.  We need a
      non-trivial effort to beef up our testing (which is very sparse right now).
* Prefer cross-platform solutions.
    * We want to work on both Windows and Linux, and each has a notion of containers 
      which has its own set of quirks.  Thus solutions that avoid a dependency on the
      operating system are to be preferred.
    * In particular EventPipe should be preferred over ETW mechanisms (which is OS specific) 
* Let the Documentation / Demos drive the sort term deliverables.
    * We know from feedback that documentation on using our monitoring/profiling mechanisms
      are either non-existent, or poor.  We should just invest this effort, but also
      use it as a opportunity to drive feature refinement.  Basically in addition
      to other documentation, have walk-throughs of non-trivial interesting demos.
      In writing these docs, we will see the deficiencies in the current features
      or incompleteness (does snot work on all platforms) and that can be used to drive 
      feature work.
* First insure that data is available within the process, then provide a mechanism for getting it 
    out of proc (e.g. REST API of the EventPipe stream) and finally add a minimal amount of 
    'command line' access, but mostly wire into our APM solutions (Azure Monitor / Application Insights))

### Issues / Design Decisions (that effect the user experience)

* How much do we want to do work to enable 'no modification' to the apps being monitored?   
  It is additional work to provide a mechanism to serialize the data out of proc to some 
  external monitor.  How much do we care?

* What is the appropriate support in dev tools like Visual Studio for profiling/monitoring?
  Currently this kind of monitoring is done through the Azure portal.

* Is having a single machine toy monitor (e.g. dotnet monitor) worthwhile?
    * Having the ability to collect traces locally 'by hand' is useful and should be supported.
    * Ability to locally display monitoring stats is not that useful.  It is most likely
      a testing capability (when new instrumentation is added).  We should only invest minimally.

### End-to-End Scenario

1. Startup problem (config / deployment / versioning ...).  Key is just to have information
2. Intermittent problems (both functional and performance)
3. Slow degrades to performance.

*******************************************************************************
## .NET Core 3.0 Diagnostics Plan 

Focusing in on V3.0 work for .NET Core specifically.  We have a top level experience in TFS for diagnostic work
* [Close Desktop/.NET Core and windows/linux diagnosability gap](https://devdiv.visualstudio.com/DevDiv/_workitems/edit/648058)

Noah Falk has done customer research to act as input to planning 3.0 which he called the
[.NET Core Framing Document](https://na01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fmicrosoft.sharepoint.com%2F%3Aw%3A%2Ft%2FDNFPlanning%2FEYlvkars0C1EhhPlHXgTnnkB8dWyVesla7Gau10LW49A8A%3Fe%3DNRcH4O&data=02%7C01%7Cvancem%40microsoft.com%7C9ad662619d0d45250cb008d648e0c898%7C72f988bf86f141af91ab2d7cd011db47%7C1%7C0%7C636776527605907620&sdata=C7gW4FCNdb0BlwbRs1MWE0oxq0SUY6ZZh7ESWFPTlu8%3D&reserved=0).

The main summary of this doc is 5 scenarios to improve
  1. .NET Core documentation on Diagnostics is almost non-existent (especially compared to offerings from other vendors).
  2. Async programming is error-prone and difficult to diagnose.
  3. It is difficult/impossible to do a performance investigation if using Linux/Containers.
  4. It is difficult/impossible to do a crash dump investigations, and in particular memory investigations.
  5. No 'easy on ramp' to monitor performance of a deployed app.

Based on this, a V3.0 plan was written up [.NET Core V3.0 Diagnostics Plan](https://microsoft.sharepoint.com/:w:/t/DNFPlanning/ES28ZR8LCmFEn4JjXQmMM_gBrrvnWOLOhH_rswCRviBYgw?rtime=3kgIi-5T1kg)
that laid out some user stories, however that should just be consider a rough first draft.  

This was then turned into User Stories / work items that will be tracked on Github here [Github .NET Core 3.0 Diagnostics Project](https://github.com/orgs/dotnet/projects/9).

A high level summary of the work being done is as follows

 1. Add a top level landing page for Diagnostics and Performance on the https://docs.microsoft.com.
    Then flesh that out with details on how to do specific investigations, and in particular all the 
    other work items listed here will be included in that documentation.
 2. Add a new DumpASync command to the SOS debugging extension.  Work with Visual Studio Team to have it integrated into their parallel stacks view.  Make sure our existing Async diagnostics works well.
 3. Create a new 'dotnet-collect' global command that allows the collection of EventPipe data 'by hand'.  (See [dotnet-collect And dotnet-analyze](https://github.com/aspnet/AspLabs/tree/main/src/DotNetDiagnostics) for more.
    It should be able to collect traces that can be read with either PerfView or Visual Studio.
 4. Create a new 'dotnet-analyze' global command that allows you to do investigations on crash dumps.  Simple triage of crashes as well as memory investigations should be possible.
 5. Insure that normal Azure and App-Insights default dashboard metrics work with .NET Core (on all platforms).  dotnet-collect will allow the ad-hoc collection of metrics for local monitoring.

 This work mostly fixes the gaps in 'ad-hoc' investigations (where a dev is working with a particular machine).
 In addition to this we want good integration with our other players in particular

 1. Azure allows ad hoc collection of traces from the portal.  This should work with Linux/Containers
 2. App-Insights profiler is using basically the same technology that is being leveraged for the ad-hoc case with dotnet-collect.
    They have solved the problem for the Azure App-Service case (and frankly most cases in Azure).  Make sure that this
    works end-to-end (on all platforms and scenarios), and that there is good doc links from the ad-hoc case to using App-Insights.
 3. App-Insights is working on a standard for that allows them to interoperate with other vendors.  This requires some runtime
    changes to support, we will be adding this.  This is our 'real' monitoring solution and in particular our distributed
    and micro-service monitoring story.

Note that the basic plan for V3.0 to frankly make sure that things we already have 
(our existing monitoring story (e.g. App-Insights), our existing performance investigation 
capabilities (e.g. Visual Studio, PerfView), work well ON ALL PLATFORMS and we have
'fallbacks' (e.g. ad hoc ways of collecting data), when for whatever reason our main offerings
did not work.  Along with this that these solutions are WELL DOCUMENTED and discoverable.
It is not flashy work, but is the LACK of theses basics working well (and being discovered)
which drive most of the feedback we have so far.  We need to get these under control.

The most notable gap that we have cut but would rather have not, is to have this story work
well when you don't have access to a Windows machine.  Currently our performance tools are 
windows only and we have not fixed that.  Thus in the V3.0 plan above you need access to a
windows machine.  We may be able to do something simple to mitigate this in the V3.0 timeframe
(Flamegraphs are a traditional 'easy' way of avoiding building UI on Linux), but this is 
currently not in plan (if we have time we will look into it)