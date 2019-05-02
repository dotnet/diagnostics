# .NET Core Diagnostics Overview

With .NET Full running on Windows we have grown accustomed to a plethora of great diagnostics tools ranging from dump generation and manual analysis to more sophisticated collection engines such as DebugDiag. As .NET core is picking up (cross platform) steam  what types of diagnostics capabilities are available to us when we need to do production diagnostics? It turns out that a lot of work has been done in this area and specifically .net core 3 promises to bring a wide range of diagnostics capabilities. 

To learn more about production diagnostics in .net core 3, we'll be running through a set of diagnostics scenarios using the built in runtime/sdk tools. The walkthrougs are all run on Ubuntu 16.04 and use the latest .net core preview bits. 

Before we jump in head first, let's take a look at some basic methodoligies as it relates to production diagnostics. When an outage occurs in production, typically the first and foremost goal is mitigation. Mitigation typically involves getting the app back up and running as quickly as possible. Common mitigation techniques involve restarting the app or sometimes one or more nodes/servers. While restarting is a quick and effective mitigation technique, root cause of the failure is still expected to be understood and appropriate fix(es) made to avoid future downtime. In order to get to root cause, we need to collect as much diagnostics data as we can prior to executing the mitigation strategy. The diagnostics data collected can then be analyzed postmortem to determine root cause and possible fixes. Each of the scenarios we will explore here will outline what capabilities .net core 3 has in terms of diagnostics data collection and analysis.

Below is the list of (growing) scenarios that will be covered.


Most of the scenarios below are implemented using a simple webapi with methods that expose each particular scenario. You can easily create the webapi using:

* dotnet new webapi
* add diagsenario.cs to your Controllers folder
* dotnet build

Please note that you have to be using at least preview 5 for most of the capabilities to work. 


### [Installing the diagnostics tools](https://github.com/MarioHewardt/netcorediag/blob/master/installing_the_diagnostics_tools.md)

### [Scenario - App is leaking memory (eventual crash)](https://github.com/MarioHewardt/netcorediag/blob/master/app_is_leaking_memory_eventual_crash.md)

### [Scenario - App is running slow (due to high CPU)](https://github.com/MarioHEwardt/netcorediag/blob/master/app_running_slow_highcpu.md)

### Scenario - App is experiencing intermittent exceptions




