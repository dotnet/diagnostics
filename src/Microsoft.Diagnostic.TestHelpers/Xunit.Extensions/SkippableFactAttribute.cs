using Xunit;
using Xunit.Sdk;

namespace Xunit.Extensions
{
    [XunitTestCaseDiscoverer("Xunit.Extensions.SkippableFactDiscoverer", "Microsoft.Diagnostic.TestHelpers")]
    public class SkippableFactAttribute : FactAttribute { }
}
