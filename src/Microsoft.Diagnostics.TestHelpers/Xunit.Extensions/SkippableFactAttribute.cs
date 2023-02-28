using Xunit.Sdk;

namespace Xunit.Extensions
{
    [XunitTestCaseDiscoverer("Xunit.Extensions.SkippableFactDiscoverer", "Microsoft.Diagnostics.TestHelpers")]
    public class SkippableFactAttribute : FactAttribute { }
}
