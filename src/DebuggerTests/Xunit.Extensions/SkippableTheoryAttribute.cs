using Xunit;
using Xunit.Sdk;

namespace Xunit.Extensions
{
    [XunitTestCaseDiscoverer("Xunit.Extensions.SkippableTheoryDiscoverer", "Debugger.Tests")]
    public class SkippableTheoryAttribute : TheoryAttribute { }
}
