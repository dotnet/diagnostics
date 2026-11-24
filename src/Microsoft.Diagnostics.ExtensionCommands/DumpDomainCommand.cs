// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using static Microsoft.Diagnostics.ExtensionCommands.DacCommandHelpers;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpdomain", Aliases = new[] { "DumpDomain" }, Help = "Displays assemblies in all AppDomains or in the specified AppDomain.")]
    public sealed class DumpDomainCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IModuleService modules, ITarget target) : SosDacCommandBase(services, runtime)
    {
        private const string Separator = "--------------------------------------";
        private int PointerSize => memory.PointerSize;

        [Argument<string>(Name = "address", Help = "Optional AppDomain address in hexadecimal or a debugger expression.")]
        public string Address { get; set; }

        [Option<bool>(Name = "--dml", Aliases = new[] { "/d" }, Help = "Enable debugger markup links.")]
        public bool DmlEnabled { get; set; }

        public override unsafe void Invoke()
        {
            ulong address = 0;
            if (!string.IsNullOrEmpty(Address) && !TryParseAddressOrExpression(Address, out address))
            {
                throw new DiagnosticsException($"Invalid AppDomain address or expression: {Address}");
            }
            address = ToClrDataAddress(address, PointerSize);

            ISOSDacInterface dac = Dac;
            DacpAppDomainStoreData store = default;
            if (dac.GetAppDomainStoreData(&store) != 0)
            {
                throw new DiagnosticsException("Unable to get AppDomain information");
            }

            DacpAppDomainData domain = default;
            if (address != 0)
            {
                if (dac.GetAppDomainData(address, &domain) != 0)
                {
                    throw new DiagnosticsException("Fail to fill AppDomain");
                }

                WriteLine(Separator);
                WriteDomainHeader(address, address == store.sharedDomain ? "Shared Domain:      " :
                    address == store.systemDomain ? "System Domain:      " : FormatDomainLabel(domain.dwId));
                WriteDomain(dac, domain);
                return;
            }

            WriteLine(Separator);
            if (store.systemDomain != 0)
            {
                WriteDomainHeader(store.systemDomain, "System Domain:      ");
                if (dac.GetAppDomainData(store.systemDomain, &domain) != 0)
                {
                    throw new DiagnosticsException("Unable to get system domain info.");
                }
                WriteDomain(dac, domain);
            }
            if (store.sharedDomain != 0)
            {
                WriteLine(Separator);
                WriteDomainHeader(store.sharedDomain, "Shared Domain:      ");
                if (dac.GetAppDomainData(store.sharedDomain, &domain) != 0)
                {
                    throw new DiagnosticsException("Unable to get shared domain info");
                }
                WriteDomain(dac, domain);
            }

            ClrDataAddress[] addresses = new ClrDataAddress[store.DomainCount];
            if (dac.GetAppDomainList((uint)store.DomainCount, addresses, null) != 0)
            {
                throw new DiagnosticsException("Unable to get array of AppDomains");
            }
            foreach (ulong appDomain in addresses)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                int hr = dac.GetAppDomainData(appDomain, &domain);
                if (hr != 0)
                {
                    throw new DiagnosticsException($"Failed to get appdomain {FormatDacAddress(appDomain, PointerSize, upperCase: true)}, error {hr:x}");
                }

                WriteLine(Separator);
                WriteDomainHeader(appDomain, FormatDomainLabel(domain.dwId));
                WriteDomain(dac, domain);
            }
        }

        private static string FormatDomainLabel(uint id) => $"Domain {unchecked((int)id)}:{(id < 10 ? " " : "")}          ";

        private void WriteDomainHeader(ulong address, string label)
        {
            Write(label);
            WriteDacAddressLink(Console, address, PointerSize, "DumpDomain", DmlEnabled);
            WriteLine();
        }

        private unsafe void WriteDomain(ISOSDacInterface dac, DacpAppDomainData domain)
        {
            WriteLine("LowFrequencyHeap:   {0}", FormatDacAddress(domain.pLowFrequencyHeap, PointerSize, upperCase: true));
            WriteLine("HighFrequencyHeap:  {0}", FormatDacAddress(domain.pHighFrequencyHeap, PointerSize, upperCase: true));
            WriteLine("StubHeap:           {0}", FormatDacAddress(domain.pStubHeap, PointerSize, upperCase: true));
            WriteLine("Stage:              {0}", domain.appDomainStage is >= DacpAppDomainDataStage.STAGE_CREATING and <= DacpAppDomainDataStage.STAGE_CLOSED
                ? domain.appDomainStage.ToString()[6..]
                : "UNKNOWN");
            if (domain.AppSecDesc != 0)
            {
                WriteLine("SecurityDescriptor: {0}", FormatDacAddress(domain.AppSecDesc, PointerSize, upperCase: true));
            }

            string friendlyName = GetAppDomainName(dac, domain.AppDomainPtr);
            WriteLine("Name:               {0}", friendlyName == "" ? "None" : friendlyName ?? "Error getting AppDomain friendly name");

            if (domain.AssemblyCount == 0)
            {
                return;
            }
            ClrDataAddress[] addresses = new ClrDataAddress[domain.AssemblyCount];
            if (dac.GetAssemblyList(domain.AppDomainPtr, domain.AssemblyCount, addresses, null) != 0)
            {
                WriteLine("Unable to get array of Assemblies");
                return;
            }

            for (int i = 0; i < addresses.Length; i++)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                if (i != 0)
                {
                    WriteLine();
                }
                ulong address = addresses[i];
                Write("Assembly:           ");
                WriteDacAddressLink(Console, address, PointerSize, "DumpAssembly", DmlEnabled);
                DacpAssemblyData assembly = default;
                if (dac.GetAssemblyData(domain.AppDomainPtr, address, &assembly) != 0)
                {
                    continue;
                }
                if (assembly.isDynamic != 0)
                {
                    Write(" (Dynamic)");
                }
                WriteLine(" [{0}]", GetAssemblyName(dac, address) ?? "");
                WriteAssemblyModules(Console, dac, assembly, modules, target.OperatingSystem, PointerSize, DmlEnabled, forDumpDomain: true);
            }
            WriteLine();
        }
    }
}
