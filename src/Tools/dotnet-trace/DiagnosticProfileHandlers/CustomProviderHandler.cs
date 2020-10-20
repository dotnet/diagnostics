using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;


namespace Microsoft.Diagnostics.Tools.Trace.DiagnosticProfileHandlers
{
    /// <summary>
    /// This class contains logic for handling events from non-preset providers such as user-defined EventSources.
    /// </summary>
    internal class CustomProviderHandler : IDiagnosticProfileHandler
    {
        private Guid RundownProviderGuid = new Guid("A669021C-C450-4609-A035-5AF59AF4DF18");
        public void AddHandler(EventPipeEventSource source)
        {
            source.Dynamic.All += Dynamic_All;
        }

        private void Dynamic_All(TraceEvent obj)
        {
            // Skip over rundown and EventPipe provider since we don't want to print them.
            if (obj.ProviderGuid == RundownProviderGuid)
                return;

            if (obj.ProviderName == "Microsoft-DotNETCore-EventPipe")
                return;

            Console.WriteLine($"[{obj.ProviderName}|{obj.EventName}|{obj.TimeStamp.ToString("s", CultureInfo.InvariantCulture)}] {GetObjectPayload(obj)}");
        }
        
        private string GetObjectPayload(TraceEvent obj)
        {
            string payload = "";
            foreach (string payloadName in obj.PayloadNames)
            {
                payload += $"{payloadName}={obj.PayloadStringByName(payloadName)};";
            }
            return payload;
        }
    }
}
