using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Microsoft.Diagnostics.Tools.Logs
{
    internal class LogViewerService : BackgroundService
    {
        private static readonly string _MicrosoftExtensionsLoggingProviderName = "Microsoft-Extensions-Logging";

        //private readonly ILoggerFactory _loggerFactory;
        private readonly LogViewerServiceOptions _logViewerOptions;
        private readonly IApplicationLifetime _lifetime;

        private IDisposable _optionsReloadToken;
        private LoggerFilterOptions _loggerOptions;
        private ulong _sessionId;
        private Stream _eventStream;
        private List<Provider> _providerList;
        private SessionConfigurationV2 _configuration;

        public LogViewerService(IOptions<LogViewerServiceOptions> logViewerOptions, IApplicationLifetime applicationLifetime, IOptionsMonitor<LoggerFilterOptions> loggerOptions)
        {
            _loggerOptions = loggerOptions.CurrentValue;
            _optionsReloadToken = loggerOptions.OnChange(ReloadConfiguration);
            _logViewerOptions = logViewerOptions.Value;
            _lifetime = applicationLifetime;
            //_loggerFactory = LoggerFactory.Create(logging =>
            //{
            //    logging.AddConsole();
            //    logging.AddFilter(_ => true);
            //});
        }

        private void ReloadConfiguration(LoggerFilterOptions options)
        {
            _loggerOptions = options;
            //_loggerFactory.CreateLogger<LogViewerService>().LogInformation("Configuration was reloaded");
            Console.WriteLine("CONFIGURATION RELOADED");
            EventPipeClient.StopTracing(_logViewerOptions.ProcessId, _sessionId);
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.Register(() =>
            {
                _optionsReloadToken?.Dispose();
                EventPipeClient.StopTracing(_logViewerOptions.ProcessId, _sessionId);
            });

            while (!cancellationToken.IsCancellationRequested)
            {
                BuildConfiguration();
                _eventStream = EventPipeClient.CollectTracing2(_logViewerOptions.ProcessId, _configuration, out _sessionId);
                await Task.Run(() => ProcessEvents());
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                _lifetime.StopApplication();
            }

        }

        private void BuildConfiguration()
        {
            var filterDataStringBuilder = new StringBuilder();
            filterDataStringBuilder.Append("FilterSpecs=\"");
            foreach (var filter in _loggerOptions.Rules)
            {
                if ((string.IsNullOrEmpty(filter.ProviderName) || filter.ProviderName.Equals(typeof(ConsoleLoggerProvider).FullName)) && filter.LogLevel.HasValue)
                {
                    var categoryName = string.IsNullOrEmpty(filter.CategoryName) ? "Default" : filter.CategoryName;
                    filterDataStringBuilder.Append($"{categoryName}:{filter.LogLevel};");
                }
            }
            filterDataStringBuilder.Append("\"");
            var filterData = filterDataStringBuilder.ToString();
            _providerList = new List<Provider>()
            {
                new Provider(name: _MicrosoftExtensionsLoggingProviderName,
                             keywords: 4, //LoggingEventSource.Keywords.FormattedMessage
                             eventLevel: EventLevel.LogAlways,
                             filterData: filterData)
            };
            _configuration = new SessionConfigurationV2(
                    circularBufferSizeMB: 100,
                    format: EventPipeSerializationFormat.NetTrace,
                    requestRundown: false,
                    providers: _providerList);
        }

        private void ProcessEvents()
        {
            using var source = new EventPipeEventSource(_eventStream);
            source.Dynamic.AddCallbackForProviderEvent(_MicrosoftExtensionsLoggingProviderName, "FormattedMessage", (traceEvent) =>
            {
                // Level, FactoryID, LoggerName, EventID, EventName, FormattedMessage
                var categoryName = (string)traceEvent.PayloadValue(2);
                //var logger = _loggerFactory.CreateLogger(categoryName);
                var logLevel = (LogLevel)traceEvent.PayloadValue(0);
                var message = (string)traceEvent.PayloadValue(4);
                //logger.Log(logLevel, message);
                Console.WriteLine(logLevel.ToString() + Environment.NewLine + message);
            });
            source.Process();
        }

        public override void Dispose()
        {
            _optionsReloadToken?.Dispose();
            _eventStream?.Dispose();
        }
    }
}