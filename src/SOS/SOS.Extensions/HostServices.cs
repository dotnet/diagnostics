// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.ExtensionCommands;
using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

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
        private delegate HResult InitializeCallbackDelegate(
            IntPtr hostServices);

        internal IntPtr IHostServices { get; }

        internal DebuggerServices DebuggerServices { get; private set; }

        private readonly ServiceProvider _serviceProvider;
        private readonly CommandProcessor _commandProcessor;
        private readonly SymbolService _symbolService;
        private readonly HostWrapper _hostWrapper;
        private ContextServiceFromDebuggerServices _contextService;
        private int _targetIdFactory;
        private ITarget _target;
        private TargetWrapper _targetWrapper;
        private IMemoryService _memoryService;

        /// <summary>
        /// Enable the assembly resolver to get the right versions in the same directory as this assembly.
        /// </summary>
        static HostServices()
        {
            if (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework"))
            {
                AssemblyResolver.Enable();
            }
            LoggingCommand.Initialize();
        }

        /// <summary>
        /// The host services instance. Only valid after Initialize is called.
        /// </summary>
        public static HostServices Instance { get; private set; }

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
            _symbolService = new SymbolService(this);
            _commandProcessor = new CommandProcessor(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ">!ext" : null);
            _commandProcessor.AddCommands(new Assembly[] { typeof(HostServices).Assembly });
            _commandProcessor.AddCommands(new Assembly[] { typeof(ClrMDHelper).Assembly });

            _serviceProvider.AddService<IHost>(this);
            _serviceProvider.AddService<ICommandService>(_commandProcessor);
            _serviceProvider.AddService<ISymbolService>(_symbolService);

            _hostWrapper = new HostWrapper(this, () => _targetWrapper);
            _hostWrapper.AddServiceWrapper(IID_IHostServices, this);
            _hostWrapper.AddServiceWrapper(SymbolServiceWrapper.IID_ISymbolService, () => new SymbolServiceWrapper(this, () => _memoryService));

            VTableBuilder builder = AddInterface(IID_IHostServices, validate: false);
            builder.AddMethod(new GetHostDelegate(GetHost));
            builder.AddMethod(new RegisterDebuggerServicesDelegate(RegisterDebuggerServices));
            builder.AddMethod(new CreateTargetDelegate(CreateTarget));
            builder.AddMethod(new UpdateTargetDelegate(UpdateTarget));
            builder.AddMethod(new FlushTargetDelegate(FlushTarget));
            builder.AddMethod(new DestroyTargetDelegate(DestroyTarget));
            builder.AddMethod(new DispatchCommandDelegate(DispatchCommand));
            builder.AddMethod(new DispatchCommandDelegate(DisplayHelp));
            builder.AddMethod(new UninitializeDelegate(Uninitialize));
            IHostServices = builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("HostServices.Destroy");
            _hostWrapper.RemoveServiceWrapper(IID_IHostServices);
            _hostWrapper.Release();
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        public HostType HostType => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? HostType.DbgEng : HostType.Lldb;

        IServiceProvider IHost.Services => _serviceProvider;

        IEnumerable<ITarget> IHost.EnumerateTargets() => _target != null ? new ITarget[] { _target } : Array.Empty<ITarget>();

        public void DestroyTarget(ITarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            Trace.TraceInformation("IHost.DestroyTarget #{0}", target.Id);
            if (target == _target)
            {
                _target = null;
                _memoryService = null;
                if (_targetWrapper != null)
                {
                    _targetWrapper.Release();
                    _targetWrapper = null;
                }
                _contextService.ClearCurrentTarget();
                if (target is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        #endregion

        #region IHostServices

        private HResult GetHost(
            IntPtr self,
            out IntPtr host)
        {
            host = _hostWrapper.IHost;
            _hostWrapper.AddRef();
            return HResult.S_OK;
        }

        private HResult RegisterDebuggerServices(
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
                _serviceProvider.AddService<IConsoleService>(consoleService);

                _contextService = new ContextServiceFromDebuggerServices(this, DebuggerServices);
                _serviceProvider.AddService<IContextService>(_contextService);
                _serviceProvider.AddServiceFactory<IThreadUnwindService>(() => new ThreadUnwindServiceFromDebuggerServices(DebuggerServices));

                _contextService.ServiceProvider.AddServiceFactory<ClrMDHelper>(() =>
                {
                    ClrRuntime clrRuntime = _contextService.Services.GetService<ClrRuntime>();
                    return clrRuntime != null ? new ClrMDHelper(clrRuntime) : null;
                });

                // Add each extension command to the native debugger
                foreach ((string name, string help, IEnumerable<string> aliases) in _commandProcessor.Commands)
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

        private HResult CreateTarget(
            IntPtr self)
        {
            Trace.TraceInformation("HostServices.CreateTarget");
            if (_target != null || DebuggerServices == null)
            {
                return HResult.E_FAIL;
            }
            try
            {
                _target = new TargetFromDebuggerServices(DebuggerServices, this, _targetIdFactory++);
                _contextService.SetCurrentTarget(_target);
                _targetWrapper = new TargetWrapper(_contextService.Services);
                _memoryService = _contextService.Services.GetService<IMemoryService>();
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                return HResult.E_FAIL;
            }
            return HResult.S_OK;
        }

        private HResult UpdateTarget(
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

        private HResult DispatchCommand(
            IntPtr self,
            string commandLine)
        {
            if (commandLine == null)
            {
                return HResult.E_INVALIDARG;
            }
            try
            {
                return _commandProcessor.Execute(commandLine, _contextService.Services);
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
            return HResult.E_FAIL;
        }

        private HResult DisplayHelp(
            IntPtr self,
            string command)
        {
            try
            {
                if (!_commandProcessor.DisplayHelp(command, _contextService.Services))
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
        private delegate HResult GetHostDelegate(
            [In] IntPtr self,
            [Out] out IntPtr host);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult RegisterDebuggerServicesDelegate(
            [In] IntPtr self,
            [In] IntPtr iunk);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult CreateTargetDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult UpdateTargetDelegate(
            [In] IntPtr self,
            [In] uint processId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void FlushTargetDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DestroyTargetDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult DispatchCommandDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string commandLine);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult DisplayHelpDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string command);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void UninitializeDelegate(
            [In] IntPtr self);

        #endregion
    }
}
