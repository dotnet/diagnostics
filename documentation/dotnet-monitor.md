

# Introducing dotnet-monitor, an experimental tool

`dotnet-monitor` is experimental tool that makes it easier to get access to diagnostics information in a dotnet process.

When running a dotnet application, either locally or in a Kubernetes cluster, environmental differences  can make collecting diagnostics artifacts (e.g., logs, traces, process dumps) challenging. `dotnet-monitor` aims to simplify the process by abstracting over all environmental differences and exposing a consistent REST API regardless of where your application is run.

`dotnet-monitor` will be made available via two different distribution mechanism:

1. As a .NET Core global tool available on NuGet.org; and
2. As a container image available via the Microsoft Container Registry (MCR)

## Tour of dotnet-monitor

### Installation

To get started with `dotnet-monitor` locally, you will need to have [.NET Core](https://dotnet.microsoft.com/download) installed on your machine. `dotnet-monitor` can then be installed as a global tool using the following command:

```bash
dotnet tool install -g dotnet-monitor --add-source https://dnceng.pkgs.visualstudio.com/public/_packaging/dotnet5-transport/nuget/v3/index.json --version 5.0.0-preview*
```

When consuming `dotnet-monitor` as a container image, it can be pulled from [MCR](https://hub.docker.com/_/microsoft-dotnet-nightly-monitor/) using the following command:

```bash
docker pull mcr.microsoft.com/dotnet/nightly/monitor:5.0.0-preview1
```

### Running `dotnet-monitor`

Once installed you can run `dotnet-monitor` via the following command,

```bash
dotnet monitor collect
```

When running in a docker container, you will need to share a volume mount between your application container and `dotnet-monitor`

```bash
docker volume create diagnosticserver
docker run -d --rm -p 8000:80 -v diagnosticsserver:/tmp mcr.microsoft.com/dotnet/core/samples:aspnetapp
docker run -it --rm -p 52323:52323 -v diagnosticsserver:/tmp mcr.microsoft.com/dotnet/nightly/monitor --urls http://*:52323
```

When running in cluster, it is recommend to run the `dotnet-monitor` container as a sidecar alongside your application container in the pod. This sample Kubernetes manifest shows how to configure your deployment to include a sidecar container.

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



In the default configuration `dotnet-monitor` binds to two different  groups of URLs. The URLs controlled via the `--urls` parameter (defaults to http://localhost:52323) expose all the collection endpoints. The URLs controlled via the ``--metricUrls` parameter (defaults to http://localhost:52325) only exposes the `/metrics` endpoint. Since diagnostics artifacts such as logs, dumps, and traces can leak sensitive information about the application, it is **strongly recommended** that you only expose the metrics URL in the container scenario.

### Endpoints

The REST API exposed by dotnet-monitor exposes the following endpoints:

- `/processes`
- `/dump/{pid?}`
- `/gcdump/{pid?}`
- `/trace/{pid?}`
- `/logs/{pid?}`
- `/metrics`

In the sections that follow we'll look at the functionality exposed by each of these endpoints and how you can use these endpoints to collect diagnostics artifacts.

### Processes

The `/processes` endpoint returns a list of target processes accessible to `dotnet-monitor`. 

> NOTE: When running locally the `dotnet-monitor` tools lists itself as one of the target processes. 

When running in a Kubernetes cluster, the URL that exposes the `/processes` endpoint isn't publicly accessible. To get access to the endpoint, you will need to port-forward traffic to your pod.

Use `kubectl get pods` to get the name of your pod and then run the following snippet:

```powershell
$job = Start-Job -ScriptBlock { kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 }
Invoke-RestMethod http://localhost:52323/processes
Stop-Job $job
Remove-Job $job
```

```bash
kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 >/dev/null &
curl -s http://localhost:52323/processes | jq
fg # Bring process back to foreground
# Send ^C to stop kubectl port-forward

```

As a convenience, `dotnet-monitor` does not require you to specify a process id for the remaining diagnostic endpoints when there's only one accessible process.

### Dump

The `/dump` endpoint returns a process dump of the target process.

Use `kubectl get pods` to get the name of your pod and then run the following snippet:

```powershell
$job = Start-Job -ScriptBlock { kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 }
Start http://localhost:52323/dump
Stop-Job $job
Remove-Job $job
```

```bash
kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 >/dev/null &
wget --content-disposition http://localhost:52323/dump
fg # Bring process back to foreground
# Send ^C to stop kubectl port-forward
```

When run against a Kubernetes cluster running Linux, the resulting core dump are linux ELF dumps. If you are running on Windows/macOS, you can use the existing [dotnet-dump](https://docs.microsoft.com/dotnet/core/diagnostics/dotnet-dump) tool to analyze the generated dump.

```bash
docker run --rm -it -v C:/dumpFiles:/dumpFiles mcr.microsoft.com/dotnet/sdk /bin/sh

# Now in the context of the docker container
dotnet tool install -g dotnet-dump
/root/.dotnet/tools/dotnet-dump analyze /dumpFiles/core_20200618_083349
```

### GC Dump

The `/gcdump` endpoint returns a GC dump of the target process.

> NOTE: Collecting a gcdump triggers a full Gen 2 garbage collection in the target process and can change the performance characteristics of your application. The duration of the GC pause experienced by the application is proportional to the size of the GC heap; applications with larger heaps will experience longer pauses.

Use `kubectl get pods` to get the name of your pod and then run the following snippet:


```powershell
$job = Start-Job -ScriptBlock { kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 }
Start http://localhost:52323/gcdump
Stop-Job $job
Remove-Job $job
```

```bash
kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 >/dev/null &
wget --content-disposition http://localhost:52323/gcdump
fg # Bring process back to foreground
# Send ^C to stop kubectl port-forward
```

Unlike in the case of process dumps, the resulting GC dump is a portable format and can be analyzed by Visual Studio and [perfview](https://github.com/microsoft/perfview) regardless of the platform it was collected on. To learn more about when to collect GC dumps and how to analyze them, take a look at our [earlier blog post](https://devblogs.microsoft.com/dotnet/collecting-and-analyzing-memory-dumps/).

### Traces

The `/trace` endpoint returns a trace of the target process. The default trace profile include sampled CPU stacks, HTTP request start/stop events, and metrics for a duration of 30 seconds.

The duration of collection can be customized via the `durationSeconds` querystring parameter The diagnostic data can be customized via the `profile` querystring parameter to include additional to select any combination of the preset profiles:

- `CPU` (CPU profiler),
- `Http` (Request start/stop events from ASP.NET Core),
- `Logs` (Logging from the EventSourceLogger from Microsoft.Extensions.Logging); and
- `Metrics`(Runtime and ASP.NET Core EventCounters).

For example, a request to  `/trace?profile=cpu,logs` will enable the collection of just the CPU profiler and logs.

In addition to the `GET` endpoint, there is `POST` version of the endpoint that allows you to specify what `EventSource` providers to enable via the request body. 

Use `kubectl get pods` to get the name of your pod and then run the following snippet:

```powershell
$job = Start-Job -ScriptBlock { kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 }
Start http://localhost:52323/trace
Stop-Job $job
Remove-Job $job
```

```bash
kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 >/dev/null &
wget --content-disposition http://localhost:52323/trace
fg # Bring process back to foreground
# Send ^C to stop kubectl port-forward
```

The resulting `.nettrace` file can be analyzed with both Visual Studio and Perfview.

### Logs

The `/logs` endpoint will stream logs from the target process for a duration of 30 seconds.

The duration of collection can be customized via the `durationSeconds` querystring parameter. The logs endpoint is capable of returning either newline delimited json([application/x-ndjson](https://github.com/ndjson/ndjson-spec)) or the Event stream format([text/event-stream](https://developer.mozilla.org/docs/Web/API/Server-sent_events/Using_server-sent_events#Event_stream_format)) based on the specified `Accept` header in the HTTP request.

Use `kubectl get pods` to get the name of your pod and then run the following snippet:

```powershell
$job = Start-Job -ScriptBlock { kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 }
Start http://localhost:52323/logs
Stop-Job $job
Remove-Job $job
```

```bash
kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 >/dev/null &
curl -H "Accept:application/x-ndjson" http://localhost:52323/logs --no-buffer
fg # Bring process back to foreground
# Send ^C to stop kubectl port-forward
```

### Metrics

The `/metrics` endpoint will return a snapshot of runtime and ASP.NET Core metrics in the [prometheus exposition format](https://prometheus.io/docs/instrumenting/exposition_formats/#text-based-format). Unlike the other diagnostics endpoints, the metrics endpoint will not be available if `dotnet-trace` detects more than one target process. In addition to being accessible via the URLs configured via the `--urls` parameters, the metrics endpoint is also accessible from the URLs configured via the `--metricUrls`.

While metrics collection is enabled by default when `dotnet-monitor` detects exactly one target process, it can configured to disable to collection of metrics entirely via the `--metrics` parameter. In the example below, metrics collection will not be enabled.

````bash
dotnet monitor collect --metrics false
````

When deploying in-cluster, a common patter to collect metrics is to use Prometheus or another monitoring tool to scrape the metrics endpoint exposed by your application. As an example, when running in Azure Kubernetes Services(AKS), you can [configure Azure Monitor to scrape prometheus metrics](https://docs.microsoft.com/azure/azure-monitor/insights/container-insights-prometheus-integration) exposed by `dotnet-monitor`. By following the instructions in the linked document, you can enable Azure Monitor to [enable monitoring pods](https://gist.github.com/shirhatti/0222017e8e2fdb481f735002f7bd72f7/revisions) that have been [annotated](https://gist.github.com/shirhatti/ad7a986137d7ca6b1dc094a3e0a61a0d#file-hello-world-deployment-yaml-L18-L19).

Like in the case of the other diagnostics endpoints, it is also possible to view a snapshot of current metrics, by port-forwarding traffic to the `dotnet-monitor` container. Use `kubectl get pods` to get the name of your pod and then run the following snippet:

```powershell
$job = Start-Job -ScriptBlock { kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 }
Start http://localhost:52323/metrics
Stop-Job $job
Remove-Job $job
```

```bash
kubectl port-forward pods/dotnet-hello-world-558948b9dd-pjmjm 52323:52323 >/dev/null &
curl -S http://localhost:52323/metrics
fg # Bring process back to foreground
# Send ^C to stop kubectl port-forward
```

## Roadmap

While we are excited about the promise dotnet-monitor holds, it’s an experimental project and not a committed product. During this experimental phase we expect to engage deeply with anyone trying out dotnet-monitor to hear feedback and suggestions.

dotnet-monitor is currently committed as an experiment until .NET 5 ships. At which point we will be evaluating what we have and all that we’ve learnt to decide what we should do in the future.

## Conclusion

We are excited to introduce the first preview of `dotnet-monitor` and want your feedback. Let us know what we can do to make it easier to diagnose what's wrong with your dotnet application. We'd love for you to try it out and let us know what you think.

You can create issues or just follow along with the progress of the project on our [GitHub repository](https://github.com/dotnet/diagnostics).
