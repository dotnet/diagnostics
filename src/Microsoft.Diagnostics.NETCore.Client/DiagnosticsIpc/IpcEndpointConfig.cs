// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal class IpcEndpointConfig
    {
        public enum PortType
        {
            Connect,
            Listen
        }

        PortType _type;

        public string Address { get; }

        public bool IsConnectConfig {
            get
            {
                if (!string.IsNullOrEmpty(Address) && _type == PortType.Connect)
                    return true;
                else
                    return false;
            }
        }

        public bool IsListenConfig {
            get
            {
                if (!string.IsNullOrEmpty(Address) && _type == PortType.Listen)
                    return true;
                else
                    return false;
            }
        }

        public IpcEndpointConfig(string address, PortType type)
        {
            Address = address;
            _type = type;
        }

        public static IpcEndpointConfig Parse(string config)
        {
            string address = "";
            PortType type = PortType.Connect;

            if (!string.IsNullOrEmpty(config))
            {
                var parts = config.Split(',');
                if (parts.Length > 2)
                    throw new ArgumentException($"Unknow IPC endpoint config format, {config}.");

                if (string.IsNullOrEmpty(parts[0]))
                    throw new ArgumentException($"Missing IPC endpoint config address, {config}.");

                type = PortType.Listen;
                address = parts[0];

                if (parts.Length == 2)
                {
                    if (string.Compare(parts[1], "connect", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        type = PortType.Connect;
                    }
                    else if (string.Compare(parts[1], "listen", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        type = PortType.Listen;
                    }
                    else
                    {
                        throw new ArgumentException($"Unknow IPC endpoint config keyword, {parts[1]} in {config}.");
                    }
                }
            }

            return new IpcEndpointConfig(address, type);
        }
    }
}
