using System;
using System.Diagnostics.Tracing;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;



namespace Microsoft.Diagnostics.Grape
{
	public class EventPipeTraceGenerator
	{
		TestRunner _runner;
		string _pathToExe;
		string _traceName;
		List<EventPipeProvider> _providers;

		public EventPipeTraceGenerator(string pathToExe, string traceName, List<EventPipeProvider> providers)
		{
			_pathToExe = pathToExe;
			_traceName = traceName;
			_providers = providers;
		}

	    public void Collect(int duration)
	    {
	    	var pid = LaunchProcess(_pathToExe);
	    	TraceProcessForDuration(pid, duration, _traceName);
	    }

		private int LaunchProcess(string pathToExe)
	    {
	        _runner = new TestRunner(pathToExe);
	        // Sleep for some time until diagnostics server pipe gets created
	        _runner.Start(2000); 
	    	return _runner.Pid;
	    }

	    public void TraceProcessForDuration(int processId, int duration, string traceName)
		{
		    var client = new DiagnosticsClient(processId);
		    using (var traceSession = client.StartEventPipeSession(_providers))
		    {
		        Task copyTask = Task.Run(async () =>
		        {
		            using (FileStream fs = new FileStream(traceName, FileMode.Create, FileAccess.Write))
		            {
		                await traceSession.EventStream.CopyToAsync(fs);
		            }
		        });
		        copyTask.Wait(duration * 1000);
		        traceSession.Stop();
		    }
		}
	}
}
