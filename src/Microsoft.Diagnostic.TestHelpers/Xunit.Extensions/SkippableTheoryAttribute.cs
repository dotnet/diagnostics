using Xunit.Sdk;

namespace Xunit.Extensions
{
    [XunitTestCaseDiscoverer("Xunit.Extensions.SkippableTheoryDiscoverer", "Microsoft.Diagnostic.TestHelpers")]
    public class SkippableTheoryAttribute : TheoryAttribute { }
}
