using Xunit;
using Xunit.Sdk;

namespace Xunit.Extensions
{
    [XunitTestCaseDiscoverer("Xunit.Extensions.SkippableFactDiscoverer", "Debugger.Tests")]
    public class SkippableFactAttribute : FactAttribute { }
}
