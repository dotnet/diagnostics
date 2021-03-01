using System;
using System.Net;
using System.Net.Sockets;


namespace Microsoft.Diagnostics.NETCore.Client.DiagnosticsIpc
{
    class IpcTcpSocketTransport : IpcSocketTransport
    {
        static public IpcTcpSocketTransport Create (string address)
        {
            return new IpcTcpSocketTransport (IpcTcpSocketTransport.ParseIPAddress (address));
        }

        public IpcTcpSocketTransport(EndPoint address) :
            base(address, SocketType.Stream, ProtocolType.Tcp)
        {
        }

        static internal IPEndPoint ParseIPAddress(string address)
        {
            var parts = address.Split(':');
            if (parts.Length == 2)
            {
                return new IPEndPoint(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
            }
            else
            {
                return new IPEndPoint(IPAddress.Parse(parts[0]), 0);
            }
        }
    }
}
