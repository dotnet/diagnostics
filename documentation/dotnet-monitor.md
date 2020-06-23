

# Introducing dotnet-monitor, an experimental tool

`dotnet-monitor` is an experimental tool that makes it easier to get access to diagnostics information in a dotnet process.

When running a dotnet application differences in diverse local and production environments can make collecting diagnostics artifacts (e.g., logs, traces, process dumps) challenging. `dotnet-monitor` aims to simplify the process by exposing a consistent REST API regardless of where your application is run.

This blog post details how to get started with `dotnet-monitor` and covers the following:

1. How to setup `dotnet-monitor`
2. What diagnostics artifacts can be collected; and
3. How to collect each of the artifacts

## Tour of dotnet-monitor

### Setup `dotnet-monitor`

`dotnet-monitor` will be made available via two different distribution mechanism:

1. As a .NET Core global tool; and
2. As a container image available via the Microsoft Container Registry (MCR)

The setup instructions for `dotnet-monitor` vary based on the target environment. The following section covers some common environments.

In the default configuration `dotnet-monitor` binds to two different  groups of URLs. The URLs controlled via the `--urls` parameter (defaults to http://localhost:52323) expose all the collection endpoints. The URLs controlled via the `--metricUrls` parameter (defaults to http://localhost:52325) only expose the `/metrics` endpoint. Since diagnostics artifacts such as logs, dumps, and traces can leak sensitive information about the application, it is **strongly recommended** that you do not publicly expose these endpoints.

#### Local Machine

To get started with `dotnet-monitor` locally, you will need to have [.NET Core](https://dotnet.microsoft.com/download) installed on your machine. You can then install`dotnet-monitor` as a global tool using the following command:

```bash
dotnet tool install -g dotnet-monitor --add-source https://dnceng.pkgs.visualstudio.com/public/_packaging/dotnet5-transport/nuget/v3/index.json --version 5.0.0-preview*
```

Once installed you can run `dotnet-monitor` via the following command:

```bash
dotnet monitor collect
```

#### Running in Docker

When consuming `dotnet-monitor` as a container image, it can be pulled from [MCR](https://hub.docker.com/_/microsoft-dotnet-nightly-monitor/) using the following command:

```bash
docker pull mcr.microsoft.com/dotnet/nightly/monitor:5.0.0-preview1
```
Once you have the image pulled locally, you will need to share a volume mount between your application container and `dotnet-monitor` using the following commands:

```bash
docker volume create diagnosticserver
docker run -d --rm -p 8000:80 -v diagnosticsserver:/tmp mcr.microsoft.com/dotnet/core/samples:aspnetapp
docker run -it --rm -p 52323:52323 -v diagnosticsserver:/tmp mcr.microsoft.com/dotnet/nightly/monitor:5.0.0-preview1 --urls http://*:52323
```
#### Running in a Kubernetes cluster

When running in a cluster, it is recommend to run the `dotnet-monitor` container as a sidecar alongside your application container in the same pod. The sample Kubernetes manifest below shows how to configure your deployment to include a sidecar container.

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: dotnet-hello-world
spec:
  replicas: 1
  selector:
    matchLabels:
      app: dotnet-hello-world
  template:
    metadata:
      labels:
        app: dotnet-hello-world
    spec:
      volumes:
      - name: diagnostics
        emptyDir: {}
      containers:
      - name: server
        image: mcr.microsoft.com/dotnet/core/samples:aspnetapp
        ports:
        - containerPort: 80
        volumeMounts:
          - mountPath: /tmp
            name: diagnostics
      - name: sidecar
        image: mcr.microsoft.com/dotnet/nightly/monitor
        ports:
        - containerPort: 52325
        # args: ["--urls", "http://*:52323", "--metricUrls", "http://*:52325"]
        volumeMounts:
          - name: diagnostics
            mountPath: /tmp
```

Unlike other target environments, this configuration does not make the diagnostics endpoint available on your host network. You will need to port forward traffic from your host to your target cluster.

To do this, obtain the name of the pod you wish to forward traffic to using the `kubectl` command:

```bash
$ kubectl get pod -l app=dotnet-hello-world
NAME                                 READY   STATUS    RESTARTS   AGE
dotnet-hello-world-dc6f67566-t2dzd   2/2     Running   0          37m
```

Once you have your target pod name, forward traffic using the `kubectl port-forward` command:

In PowerShell,

```powershell
PS> $job = Start-Job -ScriptBlock { kubectl port-forward pods/dotnet-hello-world-dc6f67566-t2dzd 52323:52323 }
```
In bash,

```bash
$ kubectl port-forward pods/dotnet-hello-world-dc6f67566-t2dzd 52323:52323 >/dev/null &
```

Once you have started forwarding traffic from your local network to the desired pod, you can make your desired API call. As an example, you can run the following command:

In PowerShell,

```powershell
PS> Invoke-RestMethod http://localhost:52323/processes
```
In bash,
```bash
$ curl -s http://localhost:52323/processes | jq
```

Once you have completed collecting the desired diagnostics artifacts, you can stop forwarding traffic into the container using the following command:

In PowerShell,
```powershell
PS> Stop-Job $job
PS> Remove-Job $job
```

In bash,
```bash
$ pkill kubectl
```

### Endpoints

The REST API exposed by dotnet-monitor exposes the following endpoints:

- `/processes`
- `/dump/{pid?}`
- `/gcdump/{pid?}`
- `/trace/{pid?}`
- `/logs/{pid?}`
- `/metrics`

In the sections that follow, we'll look at the functionality exposed by each of these endpoints and use these endpoints to collect diagnostics artifacts.

### Processes

The `/processes` endpoint returns a list of target processes accessible to `dotnet-monitor`. 

To get a list of available processes, run the following command:

In PowerShell,
```powershell
PS> Invoke-RestMethod http://localhost:52323/processes

pid
---
  1

```

In bash,
```bash
$ curl -s http://localhost:52323/processes | jq
[
  {
    "pid": 1
  }
]
```

As a convenience, when there is only one accessible process, `dotnet-monitor` does not require you to specify a process id for the remaining diagnostic endpoints.

> Known Issue: When running locally the `dotnet-monitor` tools lists itself as one of the target processes. 

### Dump

The `/dump` endpoint returns a process dump of the target process.

To collect a process dump, run the following command:

In PowerShell,
```powershell
PS> Start-Process http://localhost:52323/dump
```

In bash,
```bash
$ wget --content-disposition http://localhost:52323/dump
```

A dump artifact cannot be analyzed  on a machine of a different OS/Architecture than where it was captured. When collecting a dump from a Kubernetes cluster running Linux, the resulting core dump cannot be analyzed on a Windows or a macOS machine. You can however use the existing [dotnet-dump](https://docs.microsoft.com/dotnet/core/diagnostics/dotnet-dump) tool to analyze the generated dump in a Docker container using the following commands:

```bash
docker run --rm -it -v C:/dumpFiles:/dumpFiles mcr.microsoft.com/dotnet/sdk /bin/sh

# Now in the context of the docker container
dotnet tool install -g dotnet-dump
/root/.dotnet/tools/dotnet-dump analyze /dumpFiles/core_20200618_083349
```

### GC Dump

The `/gcdump` endpoint returns a GC dump of the target process.

To collect a GC dump, run the following command:

In PowerShell,
```powershell
PS> Start-Process http://localhost:52323/gcdump
```

In bash,
```bash
$ wget --content-disposition http://localhost:52323/gcdump
```

Unlike a process dump, a GC dump is a portable format which can be analyzed by Visual Studio and [perfview](https://github.com/microsoft/perfview) regardless of the platform it was collected on. To learn more about when to collect GC dumps and how to analyze them, take a look at our [earlier blog post](https://devblogs.microsoft.com/dotnet/collecting-and-analyzing-memory-dumps/).

### Traces

The `/trace` endpoint returns a trace of the target process. The default trace profile includes sampled CPU stacks, HTTP request start/stop events, and metrics for a duration of 30 seconds.

The duration of collection can be customized via the `durationSeconds` querystring parameter. The diagnostic data present in the trace can be customized via the `profile` querystring parameter to include any combination of the preset profiles:

- `CPU` (CPU profiler),
- `Http` (Request start/stop events from ASP.NET Core),
- `Logs` (Logging from the `EventSourceLogger` from `Microsoft.Extensions.Logging` library); and
- `Metrics`(Runtime and ASP.NET Core `EventCounters`).

For example, a request to  `/trace?profile=cpu,logs` will enable the collection of the CPU profiler and logs.

In addition to the `GET` endpoint, there is `POST` version of the endpoint which allows you to specify what `EventSource` providers to enable via the request body. 

To collect a trace of the target process, run the following command:

In PowerShell,

```powershell
PS> Start-Process http://localhost:52323/trace
```

In bash,
```bash
$ wget --content-disposition http://localhost:52323/trace
```

The resulting `.nettrace` file can be analyzed with both Visual Studio and PerfView.

### Logs

The `/logs` endpoint will stream logs from the target process for a duration of 30 seconds.

The duration of collection can be customized via the `durationSeconds` querystring parameter. The logs endpoint is capable of returning either newline delimited JSON ([application/x-ndjson](https://github.com/ndjson/ndjson-spec)) or the Event stream format([text/event-stream](https://developer.mozilla.org/docs/Web/API/Server-sent_events/Using_server-sent_events#Event_stream_format)) based on the specified `Accept` header in the HTTP request.

To start streaming logs from the target process, run the following command:

In PowerShell,

```powershell
PS> Start-Process http://localhost:52323/logs
```

In bash,
```bash
$ curl -H "Accept:application/x-ndjson" http://localhost:52323/logs --no-buffer

{"LogLevel":"Information","EventId":"1","Category":"Microsoft.AspNetCore.Hosting.Diagnostics","Message":"Request starting HTTP/1.1 GET http://localhost:7001/  ","Scopes":{"RequestId":"0HM0N9ARKHVJK:00000002","RequestPath":"/","SpanId":"|88c401de-4df03365b379aaa4.","TraceId":"88c401de-4df03365b379aaa4","ParentId":""},"Arguments":{"Protocol":"HTTP/1.1","Method":"GET","ContentType":null,"ContentLength":null,"Scheme":"http","Host":"localhost:7001","PathBase":"","Path":"/","QueryString":""}}
{"LogLevel":"Information","EventId":"ExecutingEndpoint","Category":"Microsoft.AspNetCore.Routing.EndpointMiddleware","Message":"Executing endpoint \u0027/Index\u0027","Scopes":{"RequestId":"0HM0N9ARKHVJK:00000002","RequestPath":"/","SpanId":"|88c401de-4df03365b379aaa4.","TraceId":"88c401de-4df03365b379aaa4","ParentId":""},"Arguments":{"EndpointName":"/Index","{OriginalFormat}":"Executing endpoint \u0027{EndpointName}\u0027"}}
```

### Metrics

The `/metrics` endpoint will return a snapshot of runtime and ASP.NET Core metrics in the [prometheus exposition format](https://prometheus.io/docs/instrumenting/exposition_formats/#text-based-format). Unlike the other diagnostics endpoints, the metrics endpoint will not be available if `dotnet-trace` detects more than one target process. In addition to being accessible via the URLs configured via the `--urls` parameters, the metrics endpoint is also accessible from the URLs configured via the `--metricUrls`. When running in Kubernetes, it may be suitable to expose the metrics URL to other services in your cluster to allow them to scrape metrics.

When deploying in-cluster, a common pattern to collect metrics is to use Prometheus or another monitoring tool to scrape the metrics endpoint exposed by your application. As an example, when running in Azure Kubernetes Services (AKS), you can [configure Azure Monitor to scrape prometheus metrics](https://docs.microsoft.com/azure/azure-monitor/insights/container-insights-prometheus-integration) exposed by `dotnet-monitor`. By following the instructions in the linked document, you can enable Azure Monitor to [enable monitoring pods](https://gist.github.com/shirhatti/0222017e8e2fdb481f735002f7bd72f7/revisions) that have been [annotated](https://gist.github.com/shirhatti/ad7a986137d7ca6b1dc094a3e0a61a0d#file-hello-world-deployment-yaml-L18-L19).

Like in the case of the other diagnostics endpoints, it is also possible to view a snapshot of current metrics by running the following command:

In PowerShell,

```powershell
PS> Invoke-RestMethod http://localhost:52323/metrics
```

In bash,
```bash
$ curl -S http://localhost:52323/metrics

# HELP systemruntime_alloc_rate_bytes Allocation Rate
# TYPE systemruntime_alloc_rate_bytes gauge
systemruntime_alloc_rate_bytes 96784 1592899673335
systemruntime_alloc_rate_bytes 96784 1592899683336
systemruntime_alloc_rate_bytes 96784 1592899693336
# HELP systemruntime_assembly_count Number of Assemblies Loaded
# TYPE systemruntime_assembly_count gauge
systemruntime_assembly_count 136 1592899673335
systemruntime_assembly_count 136 1592899683336
systemruntime_assembly_count 136 1592899693336
# HELP systemruntime_active_timer_count Number of Active Timers
# TYPE systemruntime_active_timer_count gauge
systemruntime_active_timer_count 1 1592899673335
systemruntime_active_timer_count 1 1592899683336
systemruntime_active_timer_count 1 1592899693336
# ...
# (Output truncated)
```

While metrics collection is enabled by default when `dotnet-monitor` detects exactly one target process, it can be configured to disable to collection of metrics entirely via the `--metrics` parameter. In the example below, metrics collection will not be enabled.

````bash
dotnet monitor collect --metrics false
````

## Roadmap

While we are excited about the promise dotnet-monitor holds, it is an experimental project and not a committed product. During this experimental phase we expect to engage deeply with anyone trying out dotnet-monitor to hear feedback and suggestions.

dotnet-monitor is currently committed as an experiment until .NET 5 ships. At which point we will be evaluating what we have and all that we’ve learnt to decide what we should do in the future.

## Conclusion

We are excited to introduce the first preview of `dotnet-monitor` and want your feedback. Let us know what we can do to make it easier to diagnose what's wrong with your dotnet application. We'd love for you to try it out and let us know what you think.

You can create issues or just follow along with the progress of the project on our [GitHub repository](https://github.com/dotnet/diagnostics).
