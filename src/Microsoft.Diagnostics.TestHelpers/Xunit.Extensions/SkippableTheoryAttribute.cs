using Xunit.Sdk;

namespace Xunit.Extensions
{
    [XunitTestCaseDiscoverer("Xunit.Extensions.SkippableTheoryDiscoverer", "Microsoft.Diagnostics.TestHelpers")]
    public class SkippableTheoryAttribute : TheoryAttribute { }
}
