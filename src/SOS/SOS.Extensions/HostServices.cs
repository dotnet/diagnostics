// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.ExtensionCommands;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Extensions
{
    /// <summary>
    /// The extension services Wrapper the native hosts are given
    /// </summary>
    public sealed unsafe class HostServices : COMCallableIUnknown, SOSLibrary.ISOSModule, ISettingsService
    {
        private static readonly Guid IID_IHostServices = new("27B2CB8D-BDEE-4CBD-B6EF-75880D76D46F");

        /// <summary>
        /// This is the prototype of the native callback function.
        /// </summary>
        /// <param name="hostServices">The instance of the host services for the native code to use</param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeCallbackDelegate(
            IntPtr hostServices);

        internal IntPtr IHostServices { get; }

        internal DebuggerServices DebuggerServices { get; private set; }

        private readonly Host _host;
        private readonly CommandService _commandService;
        private readonly SymbolService _symbolService;
        private readonly HostWrapper _hostWrapper;
        private ServiceContainer _servicesWithManagedOnlyFilter;
        private TargetFromDebuggerServices _targetFromDebuggerServices;
        private ContextServiceFromDebuggerServices _contextServiceFromDebuggerServices;

        /// <summary>
        /// Enable the assembly resolver to get the right versions in the same directory as this assembly.
        /// </summary>
        static HostServices()
        {
            if (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework"))
            {
                AssemblyResolver.Enable();
            }
            DiagnosticLoggingService.Initialize();
        }

        /// <summary>
        /// The host services instance. Only valid after Initialize is called.
        /// </summary>
        public static HostServices Instance { get; private set; }

        /// <summary>
        /// Returns the host interface instance
        /// </summary>
        public IHost Host => _host;

        /// <summary>
        /// The time out in minutes passed to the HTTP symbol store
        /// </summary>
        public static int DefaultTimeout { get; set; } = 4;

        /// <summary>
        /// The retry count passed to the HTTP symbol store
        /// </summary>
        public static int DefaultRetryCount { get; set; }

        /// <summary>
        /// Executes an SOS command (managed or native) and captures the output
        /// </summary>
        /// <param name="commandLine">command line to execute</param>
        /// <returns>list of output lines</returns>
        public IReadOnlyList<string> ExecuteCommand(string commandLine) => _commandService.ExecuteAndCapture(commandLine, _contextServiceFromDebuggerServices.Services);

        /// <summary>
        /// Executes a host debugger command and captures the output
        /// </summary>
        /// <param name="commandLine">command line to execute</param>
        /// <returns>list of output lines</returns>
        public IReadOnlyList<string> ExecuteHostCommand(string commandLine) => DebuggerServices.ExecuteHostCommand(commandLine, DEBUG_OUTPUT.NORMAL | DEBUG_OUTPUT.ERROR);

        /// <summary>
        /// This is the main managed entry point that the native hosting code calls. It needs to be a single function
        /// and is restricted to just a string parameter because host APIs (i.e. desktop clr) have this narrow interface.
        /// </summary>
        /// <param name="extensionPath">Path and filename of native extensions to callback</param>
        /// <returns>hresult</returns>
        public static int Initialize(
            [MarshalAs(UnmanagedType.LPStr)] string extensionPath)
        {
            IntPtr extensionLibrary = default;
            try
            {
                extensionLibrary = DataTarget.PlatformFunctions.LoadLibrary(extensionPath);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                Trace.TraceError($"LoadLibrary({extensionPath}) FAILED {ex}");
            }
            if (extensionLibrary == default)
            {
                return HResult.E_FAIL;
            }
            InitializeCallbackDelegate initialializeCallback = SOSHost.GetDelegateFunction<InitializeCallbackDelegate>(extensionLibrary, "InitializeHostServices");
            if (initialializeCallback == null)
            {
                return HResult.E_FAIL;
            }
            Debug.Assert(Instance == null);
            Instance = new HostServices(extensionPath, extensionLibrary);
            return initialializeCallback(Instance.IHostServices);
        }

        private HostServices(string extensionPath, IntPtr extensionsLibrary)
        {
            SOSPath = Path.GetDirectoryName(extensionPath);
            SOSHandle = extensionsLibrary;

            _host = new Host(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? HostType.DbgEng : HostType.Lldb);
            _commandService = new CommandService(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ">!sos" : null);
            _host.ServiceManager.NotifyExtensionLoad.Register(_commandService.AddCommands);

            _host.OnTargetCreate.Register((target) => target.OnDestroyEvent.Register(() => {
                if (_targetFromDebuggerServices == target)
                {
                    _targetFromDebuggerServices = null;
                }
            }));

            _symbolService = new SymbolService(_host) {
                DefaultTimeout = DefaultTimeout,
                DefaultRetryCount = DefaultRetryCount
            };

            _hostWrapper = new HostWrapper(_host);
            _hostWrapper.ServiceWrapper.AddServiceWrapper(IID_IHostServices, this);

            VTableBuilder builder = AddInterface(IID_IHostServices, validate: false);
            builder.AddMethod(new GetHostDelegate(GetHost));
            builder.AddMethod(new RegisterDebuggerServicesDelegate(RegisterDebuggerServices));
            builder.AddMethod(new CreateTargetDelegate(CreateTarget));
            builder.AddMethod(new UpdateTargetDelegate(UpdateTarget));
            builder.AddMethod(new FlushTargetDelegate(FlushTarget));
            builder.AddMethod(new DestroyTargetDelegate(DestroyTarget));
            builder.AddMethod(new DispatchCommandDelegate(DispatchCommand));
            builder.AddMethod(new UninitializeDelegate(Uninitialize));
            IHostServices = builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("HostServices.Destroy");
            _hostWrapper.ServiceWrapper.RemoveServiceWrapper(IID_IHostServices);
            _hostWrapper.ReleaseWithCheck();
        }

        #region IHostServices

        private int GetHost(
            IntPtr self,
            out IntPtr host)
        {
            host = _hostWrapper.IHost;
            _hostWrapper.AddRef();
            return HResult.S_OK;
        }

        private int RegisterDebuggerServices(
            IntPtr self,
            IntPtr iunk)
        {
            Trace.TraceInformation("HostServices.RegisterDebuggerServices");
            if (iunk == IntPtr.Zero || DebuggerServices != null)
            {
                return HResult.E_FAIL;
            }
            // Create the wrapper for the host debugger services
            try
            {
                DebuggerServices = new DebuggerServices(iunk, _host.HostType);
            }
            catch (InvalidCastException ex)
            {
                Trace.TraceError(ex.Message);
                return HResult.E_NOINTERFACE;
            }
            HResult hr;
            try
            {
                ConsoleServiceFromDebuggerServices consoleService = new(DebuggerServices);
                FileLoggingConsoleService fileLoggingConsoleService = new(consoleService);
                DiagnosticLoggingService.Instance.SetConsole(consoleService, fileLoggingConsoleService);

                // Register all the services and commands in the SOS.Extensions (this) assembly
                _host.ServiceManager.RegisterAssembly(typeof(HostServices).Assembly);

                // Register all the services and commands in the SOS.Hosting assembly
                _host.ServiceManager.RegisterAssembly(typeof(SOSHost).Assembly);

                // Register all the services and commands in the Microsoft.Diagnostics.ExtensionCommands assembly
                _host.ServiceManager.RegisterAssembly(typeof(ClrMDHelper).Assembly);

                // Display any extension assembly loads on console
                _host.ServiceManager.NotifyExtensionLoad.Register((Assembly assembly) => fileLoggingConsoleService.WriteLine($"Loading extension {assembly.Location}"));
                _host.ServiceManager.NotifyExtensionLoadFailure.Register((Exception ex) => fileLoggingConsoleService.WriteLine(ex.Message));

                // Load any extra extensions in the search path
                _host.ServiceManager.LoadExtensions();

                // Loading extensions or adding service factories not allowed after this point.
                ServiceContainer serviceContainer = _host.CreateServiceContainer();

                // Add all the global services to the global service container
                serviceContainer.AddService<SOSLibrary.ISOSModule>(this);
                serviceContainer.AddService<ISettingsService>(this);
                serviceContainer.AddService<SOSHost.INativeDebugger>(DebuggerServices);
                serviceContainer.AddService<ICommandService>(_commandService);
                serviceContainer.AddService<ISymbolService>(_symbolService);
                serviceContainer.AddService<IConsoleService>(fileLoggingConsoleService);
                serviceContainer.AddService<IConsoleFileLoggingService>(fileLoggingConsoleService);
                serviceContainer.AddService<IDiagnosticLoggingService>(DiagnosticLoggingService.Instance);

                _contextServiceFromDebuggerServices = new ContextServiceFromDebuggerServices(_host, DebuggerServices);
                serviceContainer.AddService<IContextService>(_contextServiceFromDebuggerServices);

                ThreadUnwindServiceFromDebuggerServices threadUnwindService = new(DebuggerServices);
                serviceContainer.AddService<IThreadUnwindService>(threadUnwindService);

                // Used to invoke only managed commands
                _servicesWithManagedOnlyFilter = new(_contextServiceFromDebuggerServices.Services);
                _servicesWithManagedOnlyFilter.AddService(new SOSCommandBase.ManagedOnlyCommandFilter());

                // Add each extension command to the native debugger
                foreach ((string name, string help, IEnumerable<string> aliases) in _commandService.Commands)
                {
                    hr = DebuggerServices.AddCommand(name, help, aliases);
                    if (hr != HResult.S_OK)
                    {
                        Trace.TraceWarning($"Cannot add extension command {hr:X8} {name} - {help}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                return HResult.E_FAIL;
            }
            try
            {
                RemoteMemoryService remoteMemoryService = new(iunk);
                // This service needs another reference since it is implemented as part of IDebuggerServices and gets
                // disposed in Uninitialize() below by the DisposeServices call.
                remoteMemoryService.AddRef();
                _host.ServiceContainer.AddService<IRemoteMemoryService>(remoteMemoryService);
            }
            catch (InvalidCastException)
            {
            }
            hr = DebuggerServices.GetSymbolPath(out string symbolPath);
            if (hr == HResult.S_OK)
            {
                if (!_symbolService.ParseSymbolPathFixDefault(symbolPath))
                {
                    Trace.TraceError("ParseSymbolPath FAILED: {0}", symbolPath);
                }
            }
            else
            {
                Trace.TraceError("DebuggerServices.GetSymbolPath FAILED: {0:X8}", hr);
            }
            return HResult.S_OK;
        }

        private int CreateTarget(
            IntPtr self)
        {
            Trace.TraceInformation("HostServices.CreateTarget");
            if (_targetFromDebuggerServices != null || DebuggerServices == null)
            {
                return HResult.E_FAIL;
            }
            try
            {
                _targetFromDebuggerServices = new TargetFromDebuggerServices(DebuggerServices, _host);
                _contextServiceFromDebuggerServices.SetCurrentTarget(_targetFromDebuggerServices);
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                return HResult.E_FAIL;
            }
            return HResult.S_OK;
        }

        private int UpdateTarget(
            IntPtr self,
            uint processId)
        {
            Trace.TraceInformation("HostServices.UpdateTarget {0} #{1}", processId, _targetFromDebuggerServices != null ? _targetFromDebuggerServices.Id : "<none>");
            if (_targetFromDebuggerServices == null)
            {
                return CreateTarget(self);
            }
            else if (_targetFromDebuggerServices.ProcessId.GetValueOrDefault() != processId)
            {
                DestroyTarget(self);
                return CreateTarget(self);
            }
            return HResult.S_OK;
        }

        private void FlushTarget(
            IntPtr self)
        {
            Trace.TraceInformation("HostServices.FlushTarget");
            _targetFromDebuggerServices?.Flush();
        }

        private void DestroyTarget(
            IntPtr self)
        {
            Trace.TraceInformation("HostServices.DestroyTarget #{0}", _targetFromDebuggerServices != null ? _targetFromDebuggerServices.Id : "<none>");
            try
            {
                _targetFromDebuggerServices?.Destroy();
                _targetFromDebuggerServices = null;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
        }

        private int DispatchCommand(
            IntPtr self,
            string commandName,
            string commandArguments,
            bool displayCommandNotFound)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return HResult.E_INVALIDARG;
            }
            try
            {
                _commandService.Execute(commandName, commandArguments, string.Equals(commandName, "help", StringComparison.OrdinalIgnoreCase) ?  _contextServiceFromDebuggerServices.Services : _servicesWithManagedOnlyFilter);
            }
            catch (Exception ex)
            {
                if (!displayCommandNotFound && ex is CommandNotFoundException)
                {
                    // Returning E_NOTIMPL means no managed command or it filtered away so execute the C++ version if one
                    return HResult.E_NOTIMPL;
                }
                Trace.TraceError(ex.ToString());
                IConsoleService consoleService = _host.Services.GetService<IConsoleService>();
                // TODO: when we can figure out how to deal with error messages in the scripts that are displayed on STDERROR under lldb
                //consoleService.WriteLineError(ex.Message);
                consoleService.WriteLine(ex.Message);
                return HResult.E_FAIL;
            }
            return HResult.S_OK;
        }

        private void Uninitialize(
            IntPtr self)
        {
            Trace.TraceInformation("HostServices.Uninitialize");
            try
            {
                _host.DestoryTargets();
                _targetFromDebuggerServices = null;

                // Send shutdown event on exit
                _host.OnShutdownEvent.Fire();

                // Dispose of the global services which RemoteMemoryService but not host services (this)
                _host.ServiceContainer.DisposeServices();

                // This turns off any logging to console now that debugger services will be released and the console service will no longer work.
                DiagnosticLoggingService.Instance.SetConsole(consoleService: null, fileLoggingService: null);

                // Release the debugger services instance
                DebuggerServices?.ReleaseWithCheck();
                DebuggerServices = null;

                // Clear HostService instance
                Instance = null;

                // Release the host services wrapper
                this.ReleaseWithCheck();
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
        }

        #endregion

        #region ISettingsService

        public bool DacSignatureVerificationEnabled
        {
            get
            {
                HResult hr = DebuggerServices.GetDacSignatureVerificationSettings(out bool value);
                if (hr.IsOK)
                {
                    return value;
                }
                // Return true (verify DAC signature) if any errors. Secure by default.
                return true;
            }
            set
            {
                throw new NotSupportedException("Changing the DacSignatureVerificationEnabled setting is not supported.");
            }
        }

        #endregion

        #region SOSLibrary.ISOSModule

        public string SOSPath { get; }

        public IntPtr SOSHandle { get; }

        #endregion

        #region IHostServices delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetHostDelegate(
            [In] IntPtr self,
            [Out] out IntPtr host);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RegisterDebuggerServicesDelegate(
            [In] IntPtr self,
            [In] IntPtr iunk);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CreateTargetDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int UpdateTargetDelegate(
            [In] IntPtr self,
            [In] uint processId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void FlushTargetDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DestroyTargetDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int DispatchCommandDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string commandName,
            [In, MarshalAs(UnmanagedType.LPStr)] string commandArguments,
            bool displayCommandNotFound);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void UninitializeDelegate(
            [In] IntPtr self);

        #endregion
    }
}
