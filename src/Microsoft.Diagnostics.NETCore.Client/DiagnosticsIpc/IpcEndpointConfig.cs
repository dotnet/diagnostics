// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;

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

        const string NamedPipeSchema = "namedpipe";
        const string UnixDomainSocketSchema = "uds";
        const string NamedPipeDefaultIPCRoot = @"\\.\pipe\";
        const string NamedPipeSchemaDefaultIPCRootPath = "/pipe/";

        public IpcEndpointConfig(string address, PortType type)
        {
            try
            {
                if (Uri.TryCreate(address, UriKind.Absolute, out Uri parsedAddress))
                {
                    if (string.Equals(parsedAddress.Scheme, NamedPipeSchema, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            throw new ArgumentException($"{NamedPipeSchema} only supported on Windows.");

                        address = parsedAddress.AbsolutePath;
                    }
                    else if (string.Equals(parsedAddress.Scheme, UnixDomainSocketSchema, StringComparison.OrdinalIgnoreCase))
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            throw new ArgumentException($"{UnixDomainSocketSchema} not supported on Windows, use {NamedPipeSchema}.");

                        address = parsedAddress.AbsolutePath;
                    }
                    else if (!string.IsNullOrEmpty(parsedAddress.Scheme))
                    {
                        throw new ArgumentException($"{parsedAddress.Scheme} not supported.");
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && address.StartsWith(NamedPipeDefaultIPCRoot, StringComparison.OrdinalIgnoreCase))
                Address = address.Substring(NamedPipeDefaultIPCRoot.Length);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && address.StartsWith(NamedPipeSchemaDefaultIPCRootPath, StringComparison.OrdinalIgnoreCase))
                Address = address.Substring(NamedPipeSchemaDefaultIPCRootPath.Length);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && address.StartsWith("/", StringComparison.OrdinalIgnoreCase))
                Address = address.Substring("/".Length);
            else
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
                    if (string.Equals(parts[1], "connect", StringComparison.OrdinalIgnoreCase))
                    {
                        type = PortType.Connect;
                    }
                    else if (string.Equals(parts[1], "listen", StringComparison.OrdinalIgnoreCase))
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
