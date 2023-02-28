// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics
{
    public class DbgShimAPI
    {
        private static bool _initialized;

        private static CreateProcessForLaunchDelegate _createProcessForLaunch;
        private static CloseResumeHandleDelegate _closeResumeHandle;
        private static ResumeProcessDelegate _resumeProcess;

        private static RegisterForRuntimeStartupDelegate _registerForRuntimeStartup;
        private static RegisterForRuntimeStartupExDelegate _registerForRuntimeStartupEx;
        private static RegisterForRuntimeStartup3Delegate _registerForRuntimeStartup3;
        private static UnregisterForRuntimeStartupDelegate _unregisterForRuntimeStartup;

        private static EnumerateCLRsDelegate _enumerateCLRs;
        private static CloseCLREnumerationDelegate _closeCLREnumeration;
        private static CreateVersionStringFromModuleDelegate _createVersionStringFromModule;

        private static CreateDebuggingInterfaceFromVersionDelegate _createDebuggingInterfaceFromVersion;
        private static CreateDebuggingInterfaceFromVersionExDelegate _createDebuggingInterfaceFromVersionEx;
        private static CreateDebuggingInterfaceFromVersion2Delegate _createDebuggingInterfaceFromVersion2;
        private static CreateDebuggingInterfaceFromVersion3Delegate _createDebuggingInterfaceFromVersion3;

        private static CLRCreateInstanceDelegate _clrCreateInstance;

        private static IntPtr _dbgshimModuleHandle = IntPtr.Zero;

        public const int CorDebugVersion_2_0 = 3;
        public const int CorDebugVersion_4_0 = 4;

        public static void Initialize(string dbgshimPath)
        {
            // check if already initialized
            if (_initialized)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(dbgshimPath) || !File.Exists(dbgshimPath))
            {
                throw new ArgumentException($"Dbgshim path not set or the dbgshim at '{dbgshimPath}' does not exists");
            }
            _dbgshimModuleHandle = DataTarget.PlatformFunctions.LoadLibrary(dbgshimPath);
            _createProcessForLaunch = GetDelegateFunction<CreateProcessForLaunchDelegate>("CreateProcessForLaunch");
            _resumeProcess = GetDelegateFunction<ResumeProcessDelegate>("ResumeProcess");
            _closeResumeHandle = GetDelegateFunction<CloseResumeHandleDelegate>("CloseResumeHandle");
            _registerForRuntimeStartup = GetDelegateFunction<RegisterForRuntimeStartupDelegate>("RegisterForRuntimeStartup");
            _registerForRuntimeStartupEx = GetDelegateFunction<RegisterForRuntimeStartupExDelegate>("RegisterForRuntimeStartupEx");
            _registerForRuntimeStartup3 = GetDelegateFunction<RegisterForRuntimeStartup3Delegate>("RegisterForRuntimeStartup3", optional: true);
            _unregisterForRuntimeStartup = GetDelegateFunction<UnregisterForRuntimeStartupDelegate>("UnregisterForRuntimeStartup");
            _enumerateCLRs = GetDelegateFunction<EnumerateCLRsDelegate>("EnumerateCLRs");
            _closeCLREnumeration = GetDelegateFunction<CloseCLREnumerationDelegate>("CloseCLREnumeration");
            _createVersionStringFromModule = GetDelegateFunction<CreateVersionStringFromModuleDelegate>("CreateVersionStringFromModule");
            _createDebuggingInterfaceFromVersion = GetDelegateFunction<CreateDebuggingInterfaceFromVersionDelegate>("CreateDebuggingInterfaceFromVersion");
            _createDebuggingInterfaceFromVersionEx = GetDelegateFunction<CreateDebuggingInterfaceFromVersionExDelegate>("CreateDebuggingInterfaceFromVersionEx");
            _createDebuggingInterfaceFromVersion2 = GetDelegateFunction<CreateDebuggingInterfaceFromVersion2Delegate>("CreateDebuggingInterfaceFromVersion2");
            _createDebuggingInterfaceFromVersion3 = GetDelegateFunction<CreateDebuggingInterfaceFromVersion3Delegate>("CreateDebuggingInterfaceFromVersion3", optional: true);
            _clrCreateInstance = GetDelegateFunction<CLRCreateInstanceDelegate>("CLRCreateInstance");
            _initialized = true;
        }

        public static bool IsRegisterForRuntimeStartup3Supported => _registerForRuntimeStartup3 != default;

        public static bool IsCreateDebuggingInterfaceFromVersion3Supported => _createDebuggingInterfaceFromVersion3 != default;

        public static HResult CreateProcessForLaunch(string commandLine, bool suspendProcess, string currentDirectory, out int processId, out IntPtr resumeHandle)
        {
            return _createProcessForLaunch(commandLine, suspendProcess, lpEnvironment: IntPtr.Zero, currentDirectory, out processId, out resumeHandle);
        }

        public static HResult ResumeProcess(IntPtr handle) => _resumeProcess(handle);

        public static HResult CloseResumeHandle(IntPtr handle) => _closeResumeHandle(handle);

        public delegate void RuntimeStartupCallbackDelegate(ICorDebug cordbg, object parameter, HResult hresult);

        public static HResult RegisterForRuntimeStartup(int pid, object parameter, out (IntPtr, GCHandle) unregister, RuntimeStartupCallbackDelegate callback)
        {
            IntPtr nativeCallback = RuntimeStartupCallback(parameter, callback, out GCHandle gchNativeCallback, out IntPtr nativeParameter);
            HResult hr = _registerForRuntimeStartup((uint)pid, nativeCallback, nativeParameter, out IntPtr unregisterToken);
            unregister = (unregisterToken, gchNativeCallback);
            return hr;
        }

        public static HResult RegisterForRuntimeStartupEx(int pid, string applicationGroupId, object parameter, out (IntPtr, GCHandle) unregister, RuntimeStartupCallbackDelegate callback)
        {
            IntPtr nativeCallback = RuntimeStartupCallback(parameter, callback, out GCHandle nativeCallbackHandle, out IntPtr nativeParameter);
            HResult hr = _registerForRuntimeStartupEx((uint)pid, applicationGroupId, nativeCallback, nativeParameter, out IntPtr unregisterToken);
            unregister = (unregisterToken, nativeCallbackHandle);
            return hr;
        }

        public static HResult RegisterForRuntimeStartup3(int pid, string applicationGroupId, object parameter, IntPtr libraryProvider, out (IntPtr, GCHandle) unregister, RuntimeStartupCallbackDelegate callback)
        {
            if (_registerForRuntimeStartup3 == default)
            {
                throw new NotSupportedException("RegisterForRuntimeStartup3 not supported");
            }
            IntPtr nativeCallback = RuntimeStartupCallback(parameter, callback, out GCHandle nativeCallbackHandle, out IntPtr nativeParameter);
            HResult hr = _registerForRuntimeStartup3((uint)pid, applicationGroupId, libraryProvider, nativeCallback, nativeParameter, out IntPtr unregisterToken);
            unregister = (unregisterToken, nativeCallbackHandle);
            return hr;
        }

        private delegate void NativeRuntimeStartupCallbackDelegate(IntPtr cordbg, IntPtr parameter, int hresult);

        private static IntPtr RuntimeStartupCallback(object parameter, RuntimeStartupCallbackDelegate callback, out GCHandle nativeCallbackHandle, out IntPtr nativeParameter)
        {
            NativeRuntimeStartupCallbackDelegate native = (IntPtr cordbg, IntPtr param, int hresult) => {
                GCHandle gch = GCHandle.FromIntPtr(param);
                callback(ICorDebug.Create(cordbg), gch.Target, hresult);
                gch.Free();
            };
            // Need to keep native callback delegate alive until UnregisterForRuntimeStartup
            nativeCallbackHandle = GCHandle.Alloc(native);

            // Need to keep parameter alive until the callback is invoked
            GCHandle gchParameter = GCHandle.Alloc(parameter);
            nativeParameter = GCHandle.ToIntPtr(gchParameter);

            // Return the function pointer for the native callback
            return Marshal.GetFunctionPointerForDelegate(native);
        }

        public static HResult UnregisterForRuntimeStartup((IntPtr unregisterToken, GCHandle nativeCallbackHandle) unregister)
        {
            HResult hr = _unregisterForRuntimeStartup(unregister.unregisterToken);
            unregister.nativeCallbackHandle.Free();
            return hr;
        }

        private const int HRESULT_ERROR_PARTIAL_COPY = unchecked((int)0x8007012b);
        private const int HRESULT_ERROR_BAD_LENGTH = unchecked((int)0x80070018);

        public static unsafe HResult EnumerateCLRs(int processId, Action<IntPtr[], string[]> callback)
        {
            HResult hr = HResult.S_OK;

            int numRetries = 0;
            while (numRetries < 25)
            {
                hr = _enumerateCLRs(processId, out IntPtr* handleArray, out char** stringArray, out int arrayLength);
                if (hr == HResult.S_OK)
                {
                    IntPtr[] handles = new IntPtr[arrayLength];
                    string[] moduleNames = new string[arrayLength];
                    try
                    {
                        for (int i = 0; i < arrayLength; i++)
                        {
                            handles[i] = handleArray[i];
                            moduleNames[i] = new string(stringArray[i]);
                        }
                        callback(handles, moduleNames);
                    }
                    finally
                    {
                        hr = _closeCLREnumeration(handleArray, stringArray, arrayLength);
                    }
                    break;
                }
                // EnumerateCLRs uses the OS API CreateToolhelp32Snapshot which can return ERROR_BAD_LENGTH or
                // ERROR_PARTIAL_COPY. If we get either of those, we try wait 1/10th of a second try again (that
                // is the recommendation of the OS API owners).
                if ((hr != HRESULT_ERROR_PARTIAL_COPY) && (hr != HRESULT_ERROR_BAD_LENGTH))
                {
                    break;
                }
                Thread.Sleep(100);
                numRetries++;
            }
            return hr;
        }

        private const int HRESULT_ERROR_INSUFFICIENT_BUFFER = unchecked((int)0x8007007a);

        public static unsafe HResult CreateVersionStringFromModule(int processId, string modulePath, out string versionString)
        {
            versionString = null;
            HResult hr = _createVersionStringFromModule(processId, modulePath, null, 0, out int versionStringSize);
            if (hr == HRESULT_ERROR_INSUFFICIENT_BUFFER)
            {
                char[] versionBuffer = new char[versionStringSize];
                fixed (char* versionBufferPtr = versionBuffer)
                {
                    hr = _createVersionStringFromModule(processId, modulePath, versionBufferPtr, versionStringSize, out versionStringSize);
                    if (hr == 0)
                    {
                        versionString = new string(versionBuffer);
                    }
                }
            }
            return hr;
        }

        public static HResult CreateDebuggingInterfaceFromVersion(string versionString, out ICorDebug cordbg)
        {
            HResult hr = _createDebuggingInterfaceFromVersion(versionString, out IntPtr punk);
            cordbg = ICorDebug.Create(punk);
            return hr;
        }

        public static HResult CreateDebuggingInterfaceFromVersionEx(int debuggerVersion, string versionString, out ICorDebug cordbg)
        {
            HResult hr = _createDebuggingInterfaceFromVersionEx(debuggerVersion, versionString, out IntPtr punk);
            cordbg = ICorDebug.Create(punk);
            return hr;
        }

        public static HResult CreateDebuggingInterfaceFromVersion2(int debuggerVersion, string versionString, string applicationGroupId, out ICorDebug cordbg)
        {
            HResult hr = _createDebuggingInterfaceFromVersion2(debuggerVersion, versionString, applicationGroupId, out IntPtr punk);
            cordbg = ICorDebug.Create(punk);
            return hr;
        }

        public static HResult CreateDebuggingInterfaceFromVersion3(int debuggerVersion, string versionString, string applicationGroupId, IntPtr libraryProvider, out ICorDebug cordbg)
        {
            if (_createDebuggingInterfaceFromVersion3 == default)
            {
                throw new NotSupportedException("CreateDebuggingInterfaceFromVersion3 not supported");
            }
            HResult hr = _createDebuggingInterfaceFromVersion3(debuggerVersion, versionString, applicationGroupId, libraryProvider, out IntPtr punk);
            cordbg = ICorDebug.Create(punk);
            return hr;
        }

        public static HResult CLRCreateInstance(out ICLRDebugging clrDebugging)
        {
            HResult hr = _clrCreateInstance(ICLRDebugging.CLSID_ICLRDebugging, ICLRDebugging.IID_ICLRDebugging, out IntPtr punk);
            clrDebugging = ICLRDebugging.Create(punk);
            return hr;
        }

        private static T GetDelegateFunction<T>(string functionName, bool optional = false)
            where T : Delegate
        {
            IntPtr functionAddress = DataTarget.PlatformFunctions.GetLibraryExport(_dbgshimModuleHandle, functionName);
            if (functionAddress == IntPtr.Zero)
            {
                if (optional)
                {
                    return default;
                }
                throw new ArgumentException($"Failed to get address of dbgshim!{functionName}");
            }
            return (T)Marshal.GetDelegateForFunctionPointer(functionAddress, typeof(T));
        }

        #region DbgShim pinvoke delegates

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        private delegate int CreateProcessForLaunchDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string lpCommandLine,
            [MarshalAs(UnmanagedType.Bool)] bool bSuspendProcess,
            IntPtr lpEnvironment,
            [MarshalAs(UnmanagedType.LPWStr)] string lpCurrentDirectory,
            out int processId,
            out IntPtr suspendHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        private delegate int ResumeProcessDelegate(
            IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        private delegate int CloseResumeHandleDelegate(
            IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int RegisterForRuntimeStartupDelegate(
            uint processId,
            IntPtr callback,
            IntPtr parameter,
            out IntPtr unregisterToken);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int RegisterForRuntimeStartupExDelegate(
            uint processId,
            [MarshalAs(UnmanagedType.LPWStr)] string applicationGroupId,
            IntPtr callback,
            IntPtr parameter,
            out IntPtr unregisterToken);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int RegisterForRuntimeStartup3Delegate(
            uint processId,
            [MarshalAs(UnmanagedType.LPWStr)] string applicationGroupId,
            IntPtr libraryProvider,
            IntPtr callback,
            IntPtr parameter,
            out IntPtr unregisterToken);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int UnregisterForRuntimeStartupDelegate(
            IntPtr unregisterToken);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int EnumerateCLRsDelegate(
            int processId,
            out IntPtr* handleArray,
            out char** stringArray,
            out int arrayLength);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CloseCLREnumerationDelegate(
            IntPtr* handleArray,
            char** stringArray,
            int arrayLength);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CreateVersionStringFromModuleDelegate(
            int processId,
            [MarshalAs(UnmanagedType.LPWStr)] string moduleName,
            char* versionString,
            int versionStringLength,
            out int actualVersionStringLength);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CreateDebuggingInterfaceFromVersionDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string versionString,
            out IntPtr cordbg);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CreateDebuggingInterfaceFromVersionExDelegate(
            int debuggerVersion,
            [MarshalAs(UnmanagedType.LPWStr)] string versionString,
            out IntPtr cordbg);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CreateDebuggingInterfaceFromVersion2Delegate(
            int debuggerVersion,
            [MarshalAs(UnmanagedType.LPWStr)] string versionString,
            [MarshalAs(UnmanagedType.LPWStr)] string applicationGroupId,
            out IntPtr cordbg);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CreateDebuggingInterfaceFromVersion3Delegate(
            int debuggerVersion,
            [MarshalAs(UnmanagedType.LPWStr)] string versionString,
            [MarshalAs(UnmanagedType.LPWStr)] string applicationGroupId,
            IntPtr libraryProvider,
            out IntPtr cordbg);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CLRCreateInstanceDelegate(
            in Guid clrsid,
            in Guid riid,
            out IntPtr pInterface);

        #endregion
    }
}
