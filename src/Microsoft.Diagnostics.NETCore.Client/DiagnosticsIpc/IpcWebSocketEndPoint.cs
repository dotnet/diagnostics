// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net;
using System.Net.Sockets;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal sealed class IpcWebSocketEndPoint
    {
        public Uri EndPoint { get; }

        public static bool IsWebSocketEndPoint(string endPoint)
        {
            bool result = true;

            try
            {
                ParseWebSocketEndPoint(endPoint, out _);
            }
            catch (Exception)
            {
                result = false;
            }

            return result;
        }

        public IpcWebSocketEndPoint(string endPoint)
        {
            ParseWebSocketEndPoint(endPoint, out Uri uri);
            EndPoint = uri;
        }

        private static void ParseWebSocketEndPoint(string endPoint, out Uri uri)
        {
            string uriToParse;
            // Host can contain wildcard (*) that is a reserved charachter in URI's.
            // Replace with dummy localhost representation just for parsing purpose.
            if (endPoint.IndexOf("//*", StringComparison.Ordinal) != -1)
            {
                // FIXME: This is a workaround for the fact that Uri.Host is not set for wildcard host.
                throw new ArgumentException("Wildcard host is not supported for WebSocket endpoints");
            }
            else
            {
                uriToParse = endPoint;
            }

            string[] supportedSchemes = new string[] { "ws", "wss", "http", "https" };

            if (!string.IsNullOrEmpty(uriToParse) && Uri.TryCreate(uriToParse, UriKind.Absolute, out uri))
            {
                bool supported = false;
                foreach (string scheme in supportedSchemes)
                {
                    if (string.Compare(uri.Scheme, scheme, StringComparison.InvariantCultureIgnoreCase) == 0)
                    {
                        supported = true;
                        break;
                    }
                }
                if (!supported)
                {
                    throw new ArgumentException(string.Format("Unsupported Uri schema, \"{0}\"", uri.Scheme));
                }
                return;
            }
            else
            {
                throw new ArgumentException(string.Format("Could not parse {0} into host, port", endPoint));
            }
        }
    }
}
