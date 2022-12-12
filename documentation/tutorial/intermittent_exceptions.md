# App is experiencing intermittent exceptions

In this scenario, an ASP.NET application throws intermittent and sporadic exceptions making it challenging to use on demand dump generation tools to capture a dump precisely at the point of the exception being thrown. .NET has the capability to automatically generate dumps when an application exits as a result of an unhandled exception. However, in the case of ASP.NET the ASP.NET runtime catches all exceptions thrown to avoid the application exiting and as such we can't rely on the automatic core dump generation since an exception thrown never becomes unhandled. Fortunately, the Sysinternals ProcDump (v1.4+) for Linux allows you to generate dumps when the application throws any 1st chance exception.
ProcDump for Linux download/installation instructions can be found here - [ProcDump for Linux](https://github.com/Sysinternals/ProcDump-for-Linux)


For example, if we wanted to generate a core dump when an application throws a 1st chance exception, we can use the following:

> ```bash
> sudo procdump -e MyApp
> ```

If we wanted to specify which specific exception to generate a core dump on we can use the -f (filter) switch:

> ```bash
> sudo procdump -e -f System.InvalidOperationException MyApp
> ```

We can comma separate the list of exceptions in the exception filter.


### Performance considerations

ProcDump for Linux implements exception monitoring by using the profiler API. It attaches the profiler to the target process and waits for the exception notifications to arrive and if the filter is satisfied uses the .NET diagnostics pipe to instruct the runtime to generate a dump. Having a profiler attached to a process represents some amount of overhead but unless the application throws a large number of exceptions the overhead should be minimal.