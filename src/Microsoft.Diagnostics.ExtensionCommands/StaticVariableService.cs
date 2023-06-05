// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [ServiceExport(Scope = ServiceScope.Runtime)]
    public class StaticVariableService
    {
        private Dictionary<ulong, ClrStaticField> _fields;
        private IEnumerator<(ulong Address, ClrStaticField Static)> _enumerator;

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        /// <summary>
        /// Returns the static field at the given address.
        /// </summary>
        /// <param name="address">The address of the static field.  Note that this is not a pointer to
        /// an object, but rather a pointer to where the CLR runtime tracks the static variable's
        /// location.  In all versions of the runtime, address will live in the middle of a pinned
        /// object[].</param>
        /// <param name="field">The field corresponding to the given address.  Non-null if return
        /// is true.</param>
        /// <returns>True if the address corresponded to a static variable, false otherwise.</returns>
        public bool TryGetStaticByAddress(ulong address, out ClrStaticField field)
        {
            if (_fields is null)
            {
                _fields = new();
                _enumerator = EnumerateStatics().GetEnumerator();
            }

            if (_fields.TryGetValue(address, out field))
            {
                return true;
            }

            // pay for play lookup
            if (_enumerator is not null)
            {
                do
                {
                    _fields[_enumerator.Current.Address] = _enumerator.Current.Static;
                    if (_enumerator.Current.Address == address)
                    {
                        field = _enumerator.Current.Static;
                        return true;
                    }
                } while (_enumerator.MoveNext());

                _enumerator = null;
            }

            return false;
        }

        public IEnumerable<(ulong Address, ClrStaticField Static)> EnumerateStatics()
        {
            ClrAppDomain shared = Runtime.SharedDomain;

            foreach (ClrModule module in Runtime.EnumerateModules())
            {
                foreach ((ulong mt, _) in module.EnumerateTypeDefToMethodTableMap())
                {
                    ClrType type = Runtime.GetTypeByMethodTable(mt);
                    if (type is null)
                    {
                        continue;
                    }

                    foreach (ClrStaticField stat in type.StaticFields)
                    {
                        foreach (ClrAppDomain domain in Runtime.AppDomains)
                        {
                            ulong address = stat.GetAddress(domain);
                            if (address != 0)
                            {
                                yield return (address, stat);
                            }
                        }

                        if (shared is not null)
                        {
                            ulong address = stat.GetAddress(shared);
                            if (address != 0)
                            {
                                yield return (address, stat);
                            }
                        }
                    }
                }
            }
        }
    }
}
