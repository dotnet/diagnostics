// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.FrameworkHost.TestHost
{
    internal static class NativeHostInitializationTest
    {
        private const int Failure = unchecked((int)0x81234567);
        private const uint ProcessId = 1234;

        public static void Run(string sosPath, string scenario)
        {
            IntPtr library = LoadLibrary(sosPath);
            if (library == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot load {sosPath}");
            }

            InitializeByHostDelegate initialize = GetExport<InitializeByHostDelegate>(library, "SOSInitializeByHost");
            InitializeHostServicesDelegate initializeHostServices = GetExport<InitializeHostServicesDelegate>(library, "InitializeHostServices");
            bool noTarget = scenario == "native-no-target";
            bool updateFailure = scenario == "native-update-failure";
            bool registrationFailure = scenario == "native-registration-failure";
            bool failed = updateFailure || registrationFailure;

            TestDebuggerServices debugger = new(noTarget);
            TestHost host = new();
            TestHostServices services = new(host, updateFailure, registrationFailure);
            Check(0, initialize(IntPtr.Zero, debugger.Interface), "SOSInitializeByHost");
            int result = initializeHostServices(services.Interface);

            int uninitializations = services.Uninitializations;
            int updates = services.Updates;
            int targetsDestroyed = services.TargetsDestroyed;
            bool servicesDestroyed = services.Destroyed;
            int serviceReferences = servicesDestroyed ? 0 : services.Release();
            int hostReferences = host.Release();
            // The native singleton retains the fake debugger until this test process exits.
            int debuggerReferences = debugger.Release();
            Console.WriteLine($"result={result:X8} uninitializations={uninitializations} updates={updates} " +
                $"targetsDestroyed={targetsDestroyed} servicesDestroyed={servicesDestroyed} " +
                $"serviceReferences={serviceReferences} hostReferences={hostReferences} debuggerReferences={debuggerReferences}");

            Check(failed ? Failure : 0, result, "InitializeHostServices");
            Check(failed ? 1 : 0, uninitializations, "Uninitialize calls");
            Check(registrationFailure || noTarget ? 0 : 1, updates, "UpdateTarget calls");
            Check(noTarget ? 1 : 0, targetsDestroyed, "DestroyTarget calls");
            Check(failed ? 1 : 0, servicesDestroyed ? 1 : 0, "Host services destroyed");
            Check(failed ? 0 : 1, serviceReferences, "Host services references");
            Check(failed ? 0 : 1, hostReferences, "Host references");
            Check(1, debuggerReferences, "Debugger services references");
            if (updates != 0)
            {
                Check((int)ProcessId, (int)services.ProcessId, "Process ID");
            }
        }

        private static void Check(int expected, int actual, string name)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException($"{name}: expected {expected:X8}, actual {actual:X8}");
            }
        }

        private static T GetExport<T>(IntPtr library, string name) where T : Delegate
        {
            IntPtr address = GetProcAddress(library, name);
            if (address == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot find {name}");
            }
            return (T)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr library, string name);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int InitializeByHostDelegate(IntPtr host, IntPtr debuggerServices);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeHostServicesDelegate(IntPtr hostServices);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int UnusedDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetProcessIdDelegate(IntPtr self, out uint processId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetHostDelegate(IntPtr self, out IntPtr host);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RegisterDebuggerServicesDelegate(IntPtr self, IntPtr debuggerServices);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int UpdateTargetDelegate(IntPtr self, uint processId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void VoidDelegate(IntPtr self);

        private sealed class TestDebuggerServices : COMCallableIUnknown
        {
            private readonly bool _noTarget;

            public TestDebuggerServices(bool noTarget)
            {
                _noTarget = noTarget;
                VTableBuilder builder = AddInterface(new Guid("B4640016-6CA0-468E-BA2C-1FFF28DE7B72"), validate: false);
                // GetCurrentProcessSystemId follows 16 unused IDebuggerServices methods.
                for (int i = 0; i < 16; i++)
                {
                    builder.AddMethod(new UnusedDelegate(Unused));
                }
                builder.AddMethod(new GetProcessIdDelegate(GetProcessId));
                Interface = builder.Complete();
                AddRef();
            }

            public IntPtr Interface { get; }

            private int GetProcessId(IntPtr self, out uint processId)
            {
                processId = ProcessId;
                return _noTarget ? Failure : 0;
            }

            protected override void Destroy() { }
        }

        private sealed class TestHost : COMCallableIUnknown
        {
            public TestHost()
            {
                VTableBuilder builder = AddInterface(new Guid("E0CD8534-A88B-40D7-91BA-1B4C925761E9"), validate: false);
                Interface = builder.Complete();
                AddRef();
            }

            public IntPtr Interface { get; }

            protected override void Destroy() { }
        }

        private sealed class TestHostServices : COMCallableIUnknown
        {
            private readonly TestHost _host;
            private readonly bool _updateFailure;
            private readonly bool _registrationFailure;

            public TestHostServices(TestHost host, bool updateFailure, bool registrationFailure)
            {
                _host = host;
                _updateFailure = updateFailure;
                _registrationFailure = registrationFailure;
                VTableBuilder builder = AddInterface(new Guid("27B2CB8D-BDEE-4CBD-B6EF-75880D76D46F"), validate: false);
                builder.AddMethod(new GetHostDelegate(GetHost));
                builder.AddMethod(new RegisterDebuggerServicesDelegate(RegisterDebuggerServices));
                builder.AddMethod(new UnusedDelegate(Unused));
                builder.AddMethod(new UpdateTargetDelegate(UpdateTarget));
                builder.AddMethod(new VoidDelegate(UnusedVoid));
                builder.AddMethod(new VoidDelegate(DestroyTarget));
                builder.AddMethod(new UnusedDelegate(Unused));
                builder.AddMethod(new VoidDelegate(Uninitialize));
                Interface = builder.Complete();
                AddRef();
            }

            public IntPtr Interface { get; }
            public int Uninitializations { get; private set; }
            public int Updates { get; private set; }
            public int TargetsDestroyed { get; private set; }
            public uint ProcessId { get; private set; }
            public bool Destroyed { get; private set; }

            private int GetHost(IntPtr self, out IntPtr host)
            {
                host = _host.Interface;
                _host.AddRef();
                return 0;
            }

            private int RegisterDebuggerServices(IntPtr self, IntPtr debuggerServices) =>
                _registrationFailure ? Failure : 0;

            private int UpdateTarget(IntPtr self, uint processId)
            {
                Updates++;
                ProcessId = processId;
                return _updateFailure ? Failure : 0;
            }

            private void DestroyTarget(IntPtr self) => TargetsDestroyed++;

            private void Uninitialize(IntPtr self)
            {
                Uninitializations++;
                Release();
            }

            protected override void Destroy() => Destroyed = true;
        }

        private static int Unused(IntPtr _) => Failure;

        private static void UnusedVoid(IntPtr _) { }
    }
}
