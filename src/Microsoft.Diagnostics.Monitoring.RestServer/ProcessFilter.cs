using System;
using System.ComponentModel;
using System.Globalization;

namespace Microsoft.Diagnostics.Monitoring
{
    [TypeConverter(typeof(ProcessFilterTypeConverter))]
    public struct ProcessFilter
    {
        public ProcessFilter(int processId)
        {
            ProcessId = processId;
            RuntimeInstanceCookie = null;
        }

        public ProcessFilter(Guid runtimeInstanceCookie)
        {
            ProcessId = null;
            RuntimeInstanceCookie = runtimeInstanceCookie;
        }

        public int? ProcessId { get; }

        public Guid? RuntimeInstanceCookie { get; }
    }

    internal class ProcessFilterTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            if (null == sourceType)
            {
                throw new ArgumentNullException(nameof(sourceType));
            }
            return sourceType == typeof(string) || sourceType == typeof(ProcessFilter);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string valueString)
            {
                if (string.IsNullOrEmpty(valueString))
                {
                    return null;
                }
                else if (Guid.TryParse(valueString, out Guid cookie))
                {
                    return new ProcessFilter(cookie);
                }
                else if (int.TryParse(valueString, out int processId))
                {
                    return new ProcessFilter(processId);
                }
            }
            else if (value is ProcessFilter identifier)
            {
                return identifier;
            }
            throw new FormatException();
        }
    }
}
