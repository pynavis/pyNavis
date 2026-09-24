using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Execution;
using Xunit;

namespace PyNavis.Tests
{
    public class HostGateTests
    {
        private static PushButtonModel Button(int? min = null, int? max = null) =>
            new PushButtonModel { Title = "T", MinHostYear = min, MaxHostYear = max };

        [Fact]
        public void No_Bounds_Allows() => Assert.Null(HostGate.BlockMessage(Button(), 2026));

        // RuntimeHost falls back to year 0 when the API version will not parse. Gating on
        // that blocked every bundle with a min_host_version behind "this is 0": a host we
        // cannot identify must run the tool, not refuse it.
        [Fact]
        public void UnknownHostYear_NeverBlocks()
        {
            Assert.Null(HostGate.BlockMessage(Button(min: 2025), 0));
            Assert.Null(HostGate.BlockMessage(Button(max: 2024), 0));
        }

        [Fact]
        public void Below_Min_Blocks_With_Message()
        {
            var msg = HostGate.BlockMessage(Button(min: 2025), 2024);
            Assert.NotNull(msg);
            Assert.Contains("2025", msg);
        }

        [Fact]
        public void Above_Max_Blocks() => Assert.NotNull(HostGate.BlockMessage(Button(max: 2024), 2026));

        [Fact]
        public void Inside_Range_Allows() => Assert.Null(HostGate.BlockMessage(Button(2024, 2026), 2025));

        [Theory]
        [InlineData("23.0.0.0", 2026)]  // Navisworks internal major = year - 2003
        [InlineData("20.1.5.0", 2023)]
        public void Year_From_Api_Version(string version, int year) =>
            Assert.Equal(year, HostGate.YearFromApiVersion(version));

        [Fact]
        public void Garbage_Version_Is_Null() => Assert.Null(HostGate.YearFromApiVersion("unknown"));
    }
}
