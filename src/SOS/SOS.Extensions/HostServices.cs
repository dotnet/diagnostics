// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.ExtensionCommands;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Extensions
{
    /// <summary>
    /// The extension services Wrapper the native hosts are given
    /// </summary>
    public sealed unsafe class HostServices : COMCallableIUnknown, IHost
    {
        private static readonly Guid IID_IHostServices = new Guid("27B2CB8D-BDEE-4CBD-B6EF-75880D76D46F");

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

        private readonly ServiceProvider _serviceProvider;
        private readonly CommandService _commandService;
        private readonly SymbolService _symbolService;
        private readonly HostWrapper _hostWrapper;
        private ContextServiceFromDebuggerServices _contextService;
        private int _targetIdFactory;
        private ITarget _target;
        private TargetWrapper _targetWrapper;

        /// <summary>
        /// Enable the assembly resolver to get the right versions in the same directory as this assembly.
        /// </summary>
        static HostServices()
        {
            if (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework")) {
                AssemblyResolver.Enable();
            }
            DiagnosticLoggingService.Initialize();
        }

        /// <summary>
        /// The host services instance. Only valid after Initialize is called.
        /// </summary>
        public static HostServices Instance { get; private set; }

        /// <summary>
        /// The time out in minutes passed to the HTTP symbol store
        /// </summary>
        public static int DefaultTimeout { get; set; } = 4;

        /// <summary>
        /// The retry count passed to the HTTP symbol store
        /// </summary>
        public static int DefaultRetryCount { get; set; } = 0;

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
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException)
            {
                Trace.TraceError($"LoadLibrary({extensionPath}) FAILED {ex}");
            }
            if (extensionLibrary == default)
            {
                return HResult.E_FAIL;
            }
            var initialializeCallback = SOSHost.GetDelegateFunction<InitializeCallbackDelegate>(extensionLibrary, "InitializeHostServices");
            if (initialializeCallback == null)
            {
                return HResult.E_FAIL;
            }
            Debug.Assert(Instance == null);
            Instance = new HostServices();
            return initialializeCallback(Instance.IHostServices);
        }

        private HostServices()
        {
            _serviceProvider = new ServiceProvider();
            _serviceProvider.AddService<IDiagnosticLoggingService>(DiagnosticLoggingService.Instance);
            _symbolService = new SymbolService(this);
            _symbolService.DefaultTimeout = DefaultTimeout;
            _symbolService.DefaultRetryCount = DefaultRetryCount;
            _commandService = new CommandService(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ">!ext" : null);
            _commandService.AddCommands(new Assembly[] { typeof(HostServices).Assembly });
            _commandService.AddCommands(new Assembly[] { typeof(ClrMDHelper).Assembly });

            _serviceProvider.AddService<IHost>(this);
            _serviceProvider.AddService<ICommandService>(_commandService);
            _serviceProvider.AddService<ISymbolService>(_symbolService);

            _hostWrapper = new HostWrapper(this, () => _targetWrapper);
            _hostWrapper.ServiceWrapper.AddServiceWrapper(IID_IHostServices, this);

            VTableBuilder builder = AddInterface(IID_IHostServices, validate: false);
            builder.AddMethod(new GetHostDelegate(GetHost));
            builder.AddMethod(new RegisterDebuggerServicesDelegate(RegisterDebuggerServices));
            builder.AddMethod(new CreateTargetDelegate(CreateTarget));
            builder.AddMethod(new UpdateTargetDelegate(UpdateTarget));
            builder.AddMethod(new FlushTargetDelegate(FlushTarget));
            builder.AddMethod(new DestroyTargetDelegate(DestroyTarget));
            builder.AddMethod(new DispatchCommandDelegate(DispatchCommand));
            builder.AddMethod(new DisplayHelpDelegate(DisplayHelp));
            builder.AddMethod(new UninitializeDelegate(Uninitialize));
            IHostServices = builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("HostServices.Destroy");
            _hostWrapper.ServiceWrapper.RemoveServiceWrapper(IID_IHostServices);
            _hostWrapper.Release();
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        public HostType HostType => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? HostType.DbgEng : HostType.Lldb;

        IServiceProvider IHost.Services => _serviceProvider;

        IEnumerable<ITarget> IHost.EnumerateTargets() => _target != null ? new ITarget[] { _target } : Array.Empty<ITarget>();

        public void DestroyTarget(ITarget target)
        {
            if (target == null) {
                throw new ArgumentNullException(nameof(target));
            }
            Trace.TraceInformation("IHost.DestroyTarget #{0}", target.Id);
            if (target == _target)
            {
                _target = null;
                if (_targetWrapper != null)
                {
                    _targetWrapper.Release();
                    _targetWrapper = null;
                }
                _contextService.ClearCurrentTarget();
                if (target is IDisposable disposable) {
                    disposable.Dispose();
                }
            }
        }

        #endregion

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
            if (iunk == IntPtr.Zero || DebuggerServices != null) {
                return HResult.E_FAIL;
            }
            // Create the wrapper for the host debugger services
            try
            {
                DebuggerServices = new DebuggerServices(iunk, HostType);
            }
            catch (InvalidCastException ex)
            {
                Trace.TraceError(ex.Message);
                return HResult.E_NOINTERFACE;
            }
            try
            {
                var remoteMemoryService = new RemoteMemoryService(iunk);
                _serviceProvider.AddService<IRemoteMemoryService>(remoteMemoryService);
            }
            catch (InvalidCastException)
            {
            }
            HResult hr;
            try
            {
                var consoleService = new ConsoleServiceFromDebuggerServices(DebuggerServices);
                var fileLoggingConsoleService = new FileLoggingConsoleService(consoleService);
                DiagnosticLoggingService.Instance.SetConsole(consoleService, fileLoggingConsoleService);
                _serviceProvider.AddService<IConsoleService>(fileLoggingConsoleService);
                _serviceProvider.AddService<IConsoleFileLoggingService>(fileLoggingConsoleService);

                _contextService = new ContextServiceFromDebuggerServices(this, DebuggerServices);
                _serviceProvider.AddService<IContextService>(_contextService);
                _serviceProvider.AddServiceFactory<IThreadUnwindService>(() => new ThreadUnwindServiceFromDebuggerServices(DebuggerServices));

                _contextService.ServiceProvider.AddServiceFactory<ClrMDHelper>(() => {
                    ClrRuntime clrRuntime = _contextService.Services.GetService<ClrRuntime>();
                    return clrRuntime != null ? new ClrMDHelper(clrRuntime) : null;
                });

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
            if (_target != null || DebuggerServices == null) {
                return HResult.E_FAIL;
            }
            try
            {
                _target = new TargetFromDebuggerServices(DebuggerServices, this, _targetIdFactory++);
                _contextService.SetCurrentTarget(_target);
                _targetWrapper = new TargetWrapper(_contextService.Services);
                _targetWrapper.ServiceWrapper.AddServiceWrapper(SymbolServiceWrapper.IID_ISymbolService, () => new SymbolServiceWrapper(_symbolService, _target.Services.GetService<IMemoryService>()));
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
            Trace.TraceInformation("HostServices.UpdateTarget {0} #{1}", processId, _target != null ? _target.Id : "<none>");
            if (_target == null)
            {
                return CreateTarget(self);
            }
            else if (_target.ProcessId.GetValueOrDefault() != processId)
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
            if (_target != null)
            {
                _target.Flush();
            }
        }

        private void DestroyTarget(
            IntPtr self)
        {
            Trace.TraceInformation("HostServices.DestroyTarget #{0}", _target != null ? _target.Id : "<none>");
            try
            {
                if (_target != null)
                {
                    DestroyTarget(_target);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
        }

        private int DispatchCommand(
            IntPtr self,
            string commandName,
            string commandArguments)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return HResult.E_INVALIDARG;
            }
            if (!_commandService.IsCommand(commandName))
            {
                return HResult.E_NOTIMPL;
            }
            try
            {
                StringBuilder sb = new();
                sb.Append(commandName);
                if (!string.IsNullOrWhiteSpace(commandArguments))
                {
                    sb.Append(' ');
                    sb.Append(commandArguments);
                }
                if (_commandService.Execute(sb.ToString(), _contextService.Services))
                {
                    return HResult.S_OK;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
            return HResult.E_FAIL;
        }

        private int DisplayHelp(
            IntPtr self,
            string commandName)
        {
            try
            {
                if (!_commandService.DisplayHelp(commandName, _contextService.Services))
                {
                    return HResult.E_INVALIDARG;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
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
                DestroyTarget(self);

                if (DebuggerServices != null)
                {
                    // This turns off any logging to console now that debugger services will be released and the console service no longer functions.
                    DiagnosticLoggingService.Instance.SetConsole(null, null);
                    DebuggerServices.Release();
                    DebuggerServices = null;
                }

                // Send shutdown event on exit
                OnShutdownEvent.Fire();

                // Release the host services wrapper
                Release();

                // Clear HostService instance
                Instance = null;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
        }

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
            [In, MarshalAs(UnmanagedType.LPStr)] string commandArguments);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int DisplayHelpDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string commandName);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void UninitializeDelegate(
            [In] IntPtr self);

        #endregion
    }
}
