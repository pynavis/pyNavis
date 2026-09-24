using PyNavis.Runtime.Execution;
using Xunit;

namespace PyNavis.Tests
{
    public class HostEnvVarTests
    {
        [Fact]
        public void EnvVars_Set_Get_Remove_CaseInsensitive()
        {
            var host = PyNavisHost.Instance;
            host.SetEnvVar("MyFlag", 42);
            Assert.Equal(42, host.GetEnvVar("myflag"));
            host.SetEnvVar("MyFlag", null);          // null removes
            Assert.Null(host.GetEnvVar("MyFlag"));
            Assert.Null(host.GetEnvVar("never-set"));
        }
    }
}
