// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Tools.DiagnosticsServerRouter
{
    internal static class USBMuxInterop
    {
        public const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        public const string MobileDeviceLibrary = "/System/Library/PrivateFrameworks/MobileDevice.framework/MobileDevice";
        public const string LibC = "libc";

        public const int EINTR = 4;

        public enum AMDeviceNotificationMessage : uint
        {
            None = 0,
            Connected = 1,
            Disconnected = 2,
            Unsubscribed = 3
        }

        public struct AMDeviceNotificationCallbackInfo
        {
            public AMDeviceNotificationCallbackInfo(IntPtr device, AMDeviceNotificationMessage message)
            {
                am_device = device;
                this.message = message;
            }

            public IntPtr am_device;
            public AMDeviceNotificationMessage message;
        }

        public delegate void DeviceNotificationDelegate(ref AMDeviceNotificationCallbackInfo info);

        #region MobileDeviceLibrary
        [DllImport(MobileDeviceLibrary)]
        public static extern uint AMDeviceNotificationSubscribe(DeviceNotificationDelegate callback, uint unused0, uint unused1, uint unused2, out IntPtr context);

        [DllImport(MobileDeviceLibrary)]
        public static extern uint AMDeviceNotificationUnsubscribe(IntPtr context);

        [DllImport(MobileDeviceLibrary)]
        public static extern uint AMDeviceConnect(IntPtr device);

        [DllImport(MobileDeviceLibrary)]
        public static extern uint AMDeviceDisconnect(IntPtr device);

        [DllImport(MobileDeviceLibrary)]
        public static extern uint AMDeviceGetConnectionID(IntPtr device);

        [DllImport(MobileDeviceLibrary)]
        public static extern int AMDeviceGetInterfaceType(IntPtr device);

        [DllImport(MobileDeviceLibrary)]
        public static extern uint USBMuxConnectByPort(uint connection, ushort port, out int socketHandle);
        #endregion
        #region CoreFoundationLibrary
        [DllImport(CoreFoundationLibrary)]
        public static extern void CFRunLoopRun();

        [DllImport(CoreFoundationLibrary)]
        public static extern void CFRunLoopStop(IntPtr runLoop);

        [DllImport(CoreFoundationLibrary)]
        public static extern IntPtr CFRunLoopGetCurrent();
        #endregion
        #region LibC
        [DllImport(LibC, SetLastError = true)]
        public static extern unsafe int send(int handle, byte* buffer, IntPtr length, int flags);

        [DllImport(LibC, SetLastError = true)]
        public static extern unsafe int recv(int handle, byte* buffer, IntPtr length, int flags);

        [DllImport(LibC, SetLastError = true)]
        public static extern int close(int handle);
        #endregion
    }

#pragma warning disable CA1844 // Provide memory-based overrides of async methods when subclassing 'Stream'
    internal sealed class USBMuxStream : Stream
#pragma warning restore CA1844 // Provide memory-based overrides of async methods when subclassing 'Stream'
    {
        private int _handle = -1;

        public USBMuxStream(int handle)
        {
            _handle = handle;
        }

        public bool IsOpen => _handle != -1;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotImplementedException();

        public override long Position
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;

            if (offset + count > buffer.Length)
            {
                throw new InvalidOperationException("Potential write beyond end of buffer");
            }

            if (offset < 0)
            {
                throw new InvalidOperationException("Write before beginning of buffer");
            }

            if (count < 0)
            {
                throw new InvalidOperationException("Negative read count");
            }

            while (true)
            {
                if (!IsOpen)
                {
                    throw new EndOfStreamException();
                }

                unsafe
                {
                    fixed (byte* fixedBuffer = buffer)
                    {
                        bytesRead = USBMuxInterop.recv(_handle, fixedBuffer + offset, new IntPtr(count), 0);
                    }
                }

                if (bytesRead == -1 && Marshal.GetLastWin32Error() == USBMuxInterop.EINTR)
                {
                    continue;
                }

                return bytesRead;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                int result = 0;
                using (cancellationToken.Register(() => Close()))
                {
                    try
                    {
                        result = Read(buffer, offset, count);
                    }
                    catch (Exception)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        result = 0;
                    }
                }
                return result;
            });
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            bool continueWrite = true;
            int bytesToWrite = count;
            int currentBytesWritten = 0;
            int totalBytesWritten = 0;

            while (continueWrite && bytesToWrite - totalBytesWritten > 0)
            {
                if (!IsOpen)
                {
                    throw new EndOfStreamException();
                }

                unsafe
                {
                    fixed (byte* fixedBuffer = buffer)
                    {
                        currentBytesWritten = USBMuxInterop.send(_handle, fixedBuffer + totalBytesWritten, new IntPtr(bytesToWrite - totalBytesWritten), 0);
                    }
                }

                if (currentBytesWritten == -1 && Marshal.GetLastWin32Error() == USBMuxInterop.EINTR)
                {
                    continue;
                }

                continueWrite = currentBytesWritten != -1;

                if (!continueWrite)
                {
                    break;
                }

                totalBytesWritten += currentBytesWritten;
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (cancellationToken.Register(() => Close()))
                {
                    Write(buffer, offset, count);
                }
            }, cancellationToken);
        }

        public override void Close()
        {
            if (IsOpen)
            {
                USBMuxInterop.close(_handle);
                _handle = -1;
            }
        }

        protected override void Dispose(bool disposing)
        {
            Close();
            base.Dispose(disposing);
        }
    }

    internal sealed class USBMuxTcpClientRouterFactory : TcpClientRouterFactory
    {
        private readonly int _port;
        private IntPtr _device = IntPtr.Zero;
        private uint _deviceConnectionID;
        private IntPtr _loopingThread = IntPtr.Zero;

        public static TcpClientRouterFactory CreateUSBMuxInstance(string tcpClient, int runtimeTimeoutMs, ILogger logger)
        {
            return new USBMuxTcpClientRouterFactory(tcpClient, runtimeTimeoutMs, logger);
        }

        public USBMuxTcpClientRouterFactory(string tcpClient, int runtimeTimeoutMs, ILogger logger)
            : base(tcpClient, runtimeTimeoutMs, logger)
        {
            _port = new IpcTcpSocketEndPoint(tcpClient).EndPoint.Port;
        }

        public override async Task<Stream> ConnectTcpStreamAsync(CancellationToken token)
        {
            return await ConnectTcpStreamAsyncInternal(token, _auto_shutdown).ConfigureAwait(false);
        }

        public override async Task<Stream> ConnectTcpStreamAsync(CancellationToken token, bool retry)
        {
            return await ConnectTcpStreamAsyncInternal(token, retry).ConfigureAwait(false);
        }

        public override void Start()
        {
            // Start device subscription thread.
            StartNotificationSubscribeThread();
        }

        public override void Stop()
        {
            // Stop device subscription thread.
            StopNotificationSubscribeThread();
        }

        private async Task<Stream> ConnectTcpStreamAsyncInternal(CancellationToken token, bool retry)
        {
            int handle = -1;

            _logger?.LogDebug($"Connecting new tcp endpoint over usbmux \"{_tcpClientAddress}\".");

            using var connectTimeoutTokenSource = new CancellationTokenSource();
            using var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutTokenSource.Token);

            connectTimeoutTokenSource.CancelAfter(TcpClientTimeoutMs);

            do
            {
                try
                {
                    handle = ConnectTcpClientOverUSBMux();
                    retry = false;
                }
                catch (Exception)
                {
                    if (connectTimeoutTokenSource.IsCancellationRequested)
                    {
                        _logger?.LogDebug("No USB stream connected, timing out.");

                        if (_auto_shutdown)
                        {
                            throw new RuntimeTimeoutException(TcpClientTimeoutMs);
                        }

                        throw new TimeoutException();
                    }

                    // If we are not doing retries when runtime is unavailable, fail right away, this will
                    // break any accepted IPC connections, making sure client is notified and could reconnect.
                    // If not, retry until succeed or time out.
                    if (!retry)
                    {
                        _logger?.LogTrace($"Failed connecting {_port} over usbmux.");
                        throw;
                    }

                    _logger?.LogTrace($"Failed connecting {_port} over usbmux, wait {TcpClientRetryTimeoutMs} ms before retrying.");

                    // If we get an error (without hitting timeout above), most likely due to unavailable device/listener.
                    // Delay execution to prevent to rapid retry attempts.
                    await Task.Delay(TcpClientRetryTimeoutMs, token).ConfigureAwait(false);
                }
            }
            while (retry);

            return new USBMuxStream(handle);
        }

        private int ConnectTcpClientOverUSBMux()
        {
            uint result = 0;
            int handle = -1;
            ushort networkPort = (ushort)IPAddress.HostToNetworkOrder(unchecked((short)_port));

            lock (this)
            {
                if (_deviceConnectionID == 0)
                {
                    throw new Exception($"Failed to connect device over USB, no device currently connected.");
                }

                result = USBMuxInterop.USBMuxConnectByPort(_deviceConnectionID, networkPort, out handle);
            }

            if (result != 0)
            {
                throw new Exception($"Failed to connect device over USB using connection {_deviceConnectionID} and port {_port}.");
            }

            return handle;
        }

        private bool ConnectDevice(IntPtr newDevice)
        {
            if (_device != IntPtr.Zero)
            {
                return false;
            }

            _device = newDevice;
            if (USBMuxInterop.AMDeviceConnect(_device) == 0)
            {
                _deviceConnectionID = USBMuxInterop.AMDeviceGetConnectionID(_device);
                _logger?.LogInformation($"Successfully connected new device, id={_deviceConnectionID}.");
                return true;
            }
            else
            {
                _logger?.LogError($"Failed connecting new device.");
                return false;
            }
        }

        private bool DisconnectDevice()
        {
            if (_device != IntPtr.Zero)
            {
                if (_deviceConnectionID != 0)
                {
                    USBMuxInterop.AMDeviceDisconnect(_device);
                    _logger?.LogInformation($"Successfully disconnected device, id={_deviceConnectionID}.");
                    _deviceConnectionID = 0;
                }

                _device = IntPtr.Zero;
            }

            return true;
        }

        private void AMDeviceNotificationCallback(ref USBMuxInterop.AMDeviceNotificationCallbackInfo info)
        {
            _logger?.LogTrace($"AMDeviceNotificationInternal callback, device={info.am_device}, action={info.message}");

            try
            {
                lock (this)
                {
                    int interfaceType = USBMuxInterop.AMDeviceGetInterfaceType(info.am_device);
                    switch (info.message)
                    {
                        case USBMuxInterop.AMDeviceNotificationMessage.Connected:
                            if (interfaceType == 1 && _device == IntPtr.Zero)
                            {
                                ConnectDevice(info.am_device);
                            }
                            else if (interfaceType == 1 && _device != IntPtr.Zero)
                            {
                                _logger?.LogInformation($"Discovered new device, but one is already connected, ignoring new device.");
                            }
                            else if (interfaceType == 0)
                            {
                                _logger?.LogInformation($"Discovered new device not connected over USB, ignoring new device.");
                            }
                            break;
                        case USBMuxInterop.AMDeviceNotificationMessage.Disconnected:
                        case USBMuxInterop.AMDeviceNotificationMessage.Unsubscribed:
                            if (_device == info.am_device)
                            {
                                DisconnectDevice();
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed AMDeviceNotificationCallback: {ex.Message}. Failed handling device={info.am_device} using action={info.message}");
            }
        }

        private void AMDeviceNotificationSubscribeLoop()
        {
            IntPtr context = IntPtr.Zero;

            try
            {
                lock (this)
                {
                    if (_loopingThread != IntPtr.Zero)
                    {
                        _logger?.LogError($"AMDeviceNotificationSubscribeLoop already running.");
                        throw new Exception("AMDeviceNotificationSubscribeLoop already running.");
                    }

                    _loopingThread = USBMuxInterop.CFRunLoopGetCurrent();
                }

                _logger?.LogTrace($"Calling AMDeviceNotificationSubscribe.");

                if (USBMuxInterop.AMDeviceNotificationSubscribe(AMDeviceNotificationCallback, 0, 0, 0, out context) != 0)
                {
                    _logger?.LogError($"Failed AMDeviceNotificationSubscribe call.");
                    throw new Exception("Failed AMDeviceNotificationSubscribe call.");
                }

                _logger?.LogTrace($"Start dispatching notifications.");
                USBMuxInterop.CFRunLoopRun();
                _logger?.LogTrace($"Stop dispatching notifications.");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed running subscribe loop: {ex.Message}. Disabling detection of devices connected over USB.");
            }
            finally
            {
                lock (this)
                {
                    if (_loopingThread != IntPtr.Zero)
                    {
                        _loopingThread = IntPtr.Zero;
                    }

                    DisconnectDevice();
                }

                if (context != IntPtr.Zero)
                {
                    _logger?.LogTrace($"Calling AMDeviceNotificationUnsubscribe.");
                    USBMuxInterop.AMDeviceNotificationUnsubscribe(context);
                }
            }
        }

        private void StartNotificationSubscribeThread()
        {
            new Thread(new ThreadStart(() => AMDeviceNotificationSubscribeLoop())).Start();
        }

        private void StopNotificationSubscribeThread()
        {
            lock (this)
            {
                if (_loopingThread != IntPtr.Zero)
                {
                    USBMuxInterop.CFRunLoopStop(_loopingThread);
                }
            }
        }
    }
}
