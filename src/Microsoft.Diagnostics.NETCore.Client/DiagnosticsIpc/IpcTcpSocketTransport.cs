// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net;
using System.Net.Sockets;

namespace Microsoft.Diagnostics.NETCore.Client
{
    class IpcTcpSocketTransport : IpcSocketTransport
    {
        public static IpcTcpSocketTransport Create(string hostAddress, int hostPort)
        {
            return new IpcTcpSocketTransport(hostAddress, hostPort);
        }

        public IpcTcpSocketTransport(string hostAddress, int hostPort)
            : base(ResolveIPAddress(hostAddress, hostPort), SocketType.Stream, ProtocolType.Tcp)
        {
            if (EnableDualMode(this, hostAddress))
                this.DualMode = true;
        }

        internal static bool EnableDualMode(Socket socket, string hostAddress)
        {
            return UseAnyIPAddressDualMode(hostAddress) && socket.AddressFamily == AddressFamily.InterNetworkV6;
        }

        internal static bool UseAnyIPAddressDualMode(string hostAddress)
        {
            return string.CompareOrdinal(hostAddress, "*") == 0;
        }

        internal static bool TryParseIPAddress(string address)
        {
            return TryParseIPAddress(address, out _, out _);
        }

        internal static bool TryParseIPAddress(string address, out string hostAddress, out int hostPort)
        {
            hostAddress = "";
            hostPort = -1;

            try
            {
                string[] addressSegments = address.Split(':');
                if (addressSegments.Length > 2)
                {
                    hostAddress = string.Join(":", addressSegments, 0, addressSegments.Length - 1);
                    hostPort = int.Parse(addressSegments[addressSegments.Length - 1]);
                }
                else if (addressSegments.Length == 2)
                {
                    hostAddress = addressSegments[0];
                    hostPort = int.Parse(addressSegments[1]);
                }

                if (!UseAnyIPAddressDualMode(hostAddress))
                {
                    if (!IPAddress.TryParse(hostAddress, out _))
                    {
                        if (!Uri.TryCreate(Uri.UriSchemeNetTcp + "://" + hostAddress + ":" + hostPort, UriKind.RelativeOrAbsolute, out _))
                        {
                            hostAddress = "";
                            hostPort = -1;
                        }
                    }
                }
            }
            catch (Exception)
            {
                ;
            }

            return !string.IsNullOrEmpty(hostAddress) && hostPort != -1;
        }

        static IPEndPoint ResolveIPAddress(string hostAddress, int hostPort)
        {
            IPAddress ipAddress = null;

            try
            {
                if (UseAnyIPAddressDualMode(hostAddress))
                {
                    if (Socket.OSSupportsIPv6)
                    {
                        ipAddress = IPAddress.IPv6Any;
                    }
                    else
                    {
                        ipAddress = IPAddress.Any;
                    }
                }
                else if (!IPAddress.TryParse(hostAddress, out ipAddress))
                {
                    var host = Dns.GetHostEntry(hostAddress);
                    if (host.AddressList.Length > 0)
                        ipAddress = host.AddressList[0];
                }
            }
            catch
            {
                ;
            }

            if (ipAddress == null)
                throw new ArgumentException(string.Format("Could not resolve {0} into an IP address", hostAddress));

            return new IPEndPoint(ipAddress, hostPort);
        }

        static IPEndPoint ResolveIPAddress(string address)
        {
            string hostAddress;
            int hostPort;

            if (!TryParseIPAddress(address, out hostAddress, out hostPort))
                throw new ArgumentException(string.Format("Could not parse {0} into host, port", address));

            return ResolveIPAddress(hostAddress, hostPort);
        }
    }
}
