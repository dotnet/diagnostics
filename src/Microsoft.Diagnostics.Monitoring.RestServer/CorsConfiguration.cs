using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    public class CorsConfiguration
    {
        public string AllowedOrigins { get; set; }

        public string[] GetOrigins() => AllowedOrigins?.Split(';');
    }
}
