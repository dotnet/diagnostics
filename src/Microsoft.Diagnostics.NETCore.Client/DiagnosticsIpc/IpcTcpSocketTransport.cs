using System;
using System.Net;
using System.Net.Sockets;


namespace Microsoft.Diagnostics.NETCore.Client.DiagnosticsIpc
{
    class IpcTcpSocketTransport : IpcSocketTransport
    {
        static public IpcTcpSocketTransport Create (string address)
        {
            return new IpcTcpSocketTransport (IpcTcpSocketTransport.ResolveIPAddress (address));
        }

        public IpcTcpSocketTransport(EndPoint address) :
            base(address, SocketType.Stream, ProtocolType.Tcp)
        {
        }

        static internal bool TryParseIPAddress(string address)
        {
            return TryParseIPAddress (address, out _, out _);
        }

        static internal bool TryParseIPAddress(string address, out string hostAddress, out int hostPort)
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

                if (!IPAddress.TryParse(hostAddress, out _))
                {
                    if (!Uri.TryCreate(Uri.UriSchemeNetTcp + "://" + hostAddress + ":" + hostPort, UriKind.RelativeOrAbsolute, out _))
                    {
                        hostAddress = "";
                        hostPort = -1;
                    }
                }
            }
            catch (Exception)
            {
                ;
            }

            return !string.IsNullOrEmpty(hostAddress) && hostPort != -1;
        }

        static internal IPEndPoint ResolveIPAddress(string address)
        {
            string hostAddress;
            int hostPort;
            IPAddress ipAddress = null;

            if (!TryParseIPAddress (address, out hostAddress, out hostPort))
                throw new ArgumentException (string.Format("Could not parse {0} into host and port arguments", address));

            try
            {
                if (!IPAddress.TryParse(hostAddress, out ipAddress))
                {
                    var host = Dns.GetHostEntry (hostAddress);
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
    }
}
