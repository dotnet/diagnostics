// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.Tools.Analyze
{
    internal static class ClrMdExtensions
    {
        public static bool IsDerivedFrom(this ClrType self, string typeName)
        {
            while (self != null)
            {
                if (string.Equals(self.Name, typeName))
                {
                    return true;
                }
                self = self.BaseType;
            }

            return false;
        }

        public static string GetDisplayValue(this ClrInstanceField self, ClrValueClass clrObject)
        {
            if (self.IsObjectReference)
            {
                var obj = clrObject.GetObjectField(self.Name);
                if (obj.IsNull)
                {
                    return "null";
                }
                return $"0x{obj.Address:X} [{obj.Type.Name}:0x{obj.Type.MethodTable:X}]";
            }
            else if(self.HasSimpleValue)
            {
                return $"{self.GetValue(clrObject.Address)} [{self.Type.Name}]";
            }
            else if(self.IsValueClass)
            {
                var vt = clrObject.GetValueClassField(self.Name);
                return $"0x{vt.Address:X} [struct {vt.Type.Name}:0x{vt.Type.MethodTable:X}]";
            }
            else
            {
                return "<unknown value>";
            }
        }

        public static string GetDisplayValue(this ClrInstanceField self, ClrObject clrObject)
        {
            if (self.IsObjectReference)
            {
                var obj = clrObject.GetObjectField(self.Name);
                if (obj.IsNull)
                {
                    return "null";
                }
                return $"0x{obj.Address:X} [{obj.Type.Name}:0x{obj.Type.MethodTable:X}]";
            }
            else if(self.HasSimpleValue)
            {
                return self.GetValue(clrObject.Address).ToString();
            }
            else if(self.IsValueClass)
            {
                var vt = clrObject.GetValueClassField(self.Name);
                return $"0x{vt.Address:X} [struct {vt.Type.Name}:0x{vt.Type.MethodTable:X}]";
            }
            else
            {
                return "<unknown value>";
            }
        }
    }
}
