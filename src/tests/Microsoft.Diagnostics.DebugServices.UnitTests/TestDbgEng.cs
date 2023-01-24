using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.Diagnostics.TestHelpers;
using SOS.Extensions;
using SOS.Hosting;
using SOS.Hosting.DbgEng.Interop;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class TestDbgEng : TestHost
    {
        private static DbgEngController _controller;

        public TestDbgEng(TestConfiguration config)
            : base (config)
        {
        }

        protected override ITarget GetTarget()
        {
            // Create/initialize dbgeng controller
            _controller ??= new DbgEngController(DbgEngPath, DumpFile, SOSPath);

            var contextService = Host.Services.GetService<IContextService>();
            return contextService.GetCurrentTarget();
        }

        private static IHost Host => HostServices.Instance;

        private string DbgEngPath => TestConfiguration.MakeCanonicalPath(Config.AllSettings["DbgEngPath"]);

        private string SOSPath =>TestConfiguration.MakeCanonicalPath(Config.AllSettings["SOSPath"]);

        public override string ToString() => "DbgEng: " + DumpFile;

        class DbgEngController : IDebugOutputCallbacks
        {
            [UnmanagedFunctionPointer(CallingConvention.Winapi)]
            private delegate int DebugCreateDelegate(
                ref Guid interfaceId,
                [MarshalAs(UnmanagedType.IUnknown)] out object iinterface);

            private static readonly Guid _iidClient = new Guid("e3acb9d7-7ec2-4f0c-a0da-e81e0cbbe628");
            private readonly CharToLineConverter _converter;

            internal readonly IDebugClient Client;
            internal readonly IDebugControl Control;
            internal readonly IDebugSymbols2 Symbols;

            internal DbgEngController(string dbgengPath, string dumpPath, string sosPath)
            {
                Trace.TraceInformation($"DbgEngController: {dbgengPath} {dumpPath} {sosPath}");
                _converter = new CharToLineConverter((text) => {
                    Trace.TraceInformation(text);
                });
                IntPtr dbgengLibrary = DataTarget.PlatformFunctions.LoadLibrary(dbgengPath);
                var debugCreate = SOSHost.GetDelegateFunction<DebugCreateDelegate>(dbgengLibrary, "DebugCreate");
                if (debugCreate == null) {
                    throw new DiagnosticsException($"DebugCreate export not found");
                }
                Guid iid = _iidClient;
                HResult hr = debugCreate(ref iid, out object client);
                if (hr != HResult.S_OK) {
                    throw new DiagnosticsException($"DebugCreate FAILED {hr:X8}");
                }
                Client = (IDebugClient)client;
                Control = (IDebugControl)client;
                Symbols = (IDebugSymbols2)client;

                hr = Client.SetOutputCallbacks(this);
                if (hr != HResult.S_OK) {
                    throw new DiagnosticsException($"SetOutputCallbacks FAILED {hr:X8}");
                }

                // Automatically enable/adjust symbol server support. Override the default cache path so
                // the cache isn't created in the debugger binaries .nuget package cache directory.
                string cachePath = Path.Combine(Environment.GetEnvironmentVariable("PROGRAMDATA"), "dbg", "sym");
                string sympath = $"{Path.GetDirectoryName(dumpPath)};cache*{cachePath};SRV*https://msdl.microsoft.com/download/symbols";
                hr = Symbols.SetSymbolPath(sympath);
                if (hr != HResult.S_OK) {
                    Trace.TraceError($"SetSymbolPath({sympath}) FAILED {hr:X8}");
                }

                // Load dump file
                hr = Client.OpenDumpFile(dumpPath);
                if (hr != HResult.S_OK) {
                    throw new DiagnosticsException($"OpenDumpFile({dumpPath} FAILED {hr:X8}");
                }
                ProcessEvents();

                // Load the sos extensions
                hr = Control.Execute(DEBUG_OUTCTL.ALL_CLIENTS, $".load {sosPath}", DEBUG_EXECUTE.DEFAULT);
                if (hr != HResult.S_OK) {
                    throw new DiagnosticsException($"Loading {sosPath} FAILED {hr:X8}");
                }

                // Set the HTTP symbol store timeout and retry count before the symbol path is added to the symbol service
                HostServices.DefaultTimeout = 6;
                HostServices.DefaultRetryCount = 5;

                // Initialize the extension host
                hr = HostServices.Initialize(sosPath);
                if (hr != HResult.S_OK) {
                    throw new DiagnosticsException($"HostServices.Initialize({sosPath}) FAILED {hr:X8}");
                }

                ISymbolService symbolService = Host.Services.GetService<ISymbolService>();
                Trace.TraceInformation($"SymbolService: {symbolService}");
            }

            /// <summary>
            /// Wait for dbgeng events
            /// </summary>
            internal void ProcessEvents()
            {
                while (true) {
                    // Wait until the target stops
                    HResult hr = Control.WaitForEvent(DEBUG_WAIT.DEFAULT, uint.MaxValue);
                    if (hr == HResult.S_OK) {
                        Trace.TraceInformation("ProcessEvents.WaitForEvent returned status {0}", ExecutionStatus);
                        if (!IsTargetRunning()) {
                            Trace.TraceInformation("ProcessEvents target stopped");
                            break;
                        }
                    }
                    else {
                        Trace.TraceError("ProcessEvents.WaitForEvent FAILED {0:X8}", hr);
                        break;
                    }
                }
            }

            /// <summary>
            /// Returns true if the target is running code
            /// </summary>
            private bool IsTargetRunning()
            {
                switch (ExecutionStatus) {
                    case DEBUG_STATUS.GO:
                    case DEBUG_STATUS.GO_HANDLED:
                    case DEBUG_STATUS.GO_NOT_HANDLED:
                    case DEBUG_STATUS.STEP_OVER:
                    case DEBUG_STATUS.STEP_INTO:
                    case DEBUG_STATUS.STEP_BRANCH:
                        return true;
                }
                return false;
            }

            private DEBUG_STATUS ExecutionStatus
            {
                get {
                    HResult hr = Control.GetExecutionStatus(out DEBUG_STATUS status);
                    if (hr != HResult.S_OK) {
                        throw new DiagnosticsException($"GetExecutionStatus FAILED {hr:X8}");
                    }
                    return status;
                }
                set {
                    HResult hr = Control.SetExecutionStatus(value);
                    if (hr != HResult.S_OK) {
                        throw new DiagnosticsException($"SetExecutionStatus FAILED {hr:X8}");
                    }
                }
            }

            #region IDebugOutputCallbacks

            int IDebugOutputCallbacks.Output(DEBUG_OUTPUT mask, string text)
            {
                try
                {
                    _converter.Input(text);
                }
                catch (Exception)
                {
                }
                return 0;
            }

            #endregion
        }
    }
}
