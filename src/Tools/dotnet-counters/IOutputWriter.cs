using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public interface IOutputWriter
    {
        void Update(string providerName, ICounterPayload payload);
    }
}
