using RimLife.Driver;
using Xunit;

namespace RimLife.Tests.Driver
{
    /// <summary>
    /// DriverConfig 单元测试。验证配置默认值、权重映射和阈值→Severity 映射。
    /// </summary>
    public class DriverConfigTests
    {
        [Fact]
        public void CreateDefault_HasSensibleDefaults()
        {
            var config = DriverConfig.CreateDefault();

            Assert.Equal(5, config.DirectorCountThreshold);
            Assert.Equal(15, config.DirectorImportanceThreshold);
            Assert.Equal(5, config.ScreenwriterCountThreshold);
            Assert.Equal(15, config.ScreenwriterImportanceThreshold);
            Assert.Equal(5, config.FreelancerCountThreshold);
            Assert.Equal(15, config.FreelancerImportanceThreshold);
            Assert.Equal(200, config.RecentHistoryCapacity);
            Assert.Equal(10, config.MaxAgentRounds);
        }

        [Fact]
        public void GetSeverityWeight_ReturnsCorrectWeights()
        {
            var config = DriverConfig.CreateDefault();

            Assert.Equal(1, config.GetSeverityWeight("Minor"));
            Assert.Equal(3, config.GetSeverityWeight("Major"));
            Assert.Equal(5, config.GetSeverityWeight("Extreme"));
        }

        [Fact]
        public void GetSeverityWeight_Unknown_ReturnsZero()
        {
            var config = DriverConfig.CreateDefault();
            Assert.Equal(0, config.GetSeverityWeight("Unknown"));
        }

        [Fact]
        public void GetSeverityWeight_Null_ReturnsZero()
        {
            var config = DriverConfig.CreateDefault();
            Assert.Equal(0, config.GetSeverityWeight(null));
        }

        [Fact]
        public void GetSeverityWeight_Empty_ReturnsZero()
        {
            var config = DriverConfig.CreateDefault();
            Assert.Equal(0, config.GetSeverityWeight(""));
        }

        [Fact]
        public void CustomWeightMap_Works()
        {
            var config = new DriverConfig();
            config.SeverityWeights["Minor"] = 2;
            config.SeverityWeights["Major"] = 6;
            config.SeverityWeights["Extreme"] = 10;
            config.SeverityWeights["Custom"] = 8;

            Assert.Equal(2, config.GetSeverityWeight("Minor"));
            Assert.Equal(6, config.GetSeverityWeight("Major"));
            Assert.Equal(10, config.GetSeverityWeight("Extreme"));
            Assert.Equal(8, config.GetSeverityWeight("Custom"));
        }

    }
}
