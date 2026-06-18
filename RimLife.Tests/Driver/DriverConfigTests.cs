using RimLife.Driver;
using Xunit;

namespace RimLife.Tests.Driver
{
    /// <summary>
    /// DriverConfig 单元测试。验证配置默认值和阈值查询。
    /// </summary>
    public class DriverConfigTests
    {
        [Fact]
        public void CreateDefault_HasSensibleDefaults()
        {
            var config = DriverConfig.CreateDefault();

            Assert.Equal(5, config.DirectorCountThreshold);
            Assert.Equal(15f, config.DirectorImportanceThreshold);
            Assert.Equal(5, config.ScreenwriterCountThreshold);
            Assert.Equal(15f, config.ScreenwriterImportanceThreshold);
            Assert.Equal(5, config.FreelancerCountThreshold);
            Assert.Equal(15f, config.FreelancerImportanceThreshold);
            Assert.Equal(200, config.RecentHistoryCapacity);
            Assert.Equal(10, config.MaxAgentRounds);
        }

        [Fact]
        public void GetEffectiveImportanceThreshold_ReturnsCorrectRoleValues()
        {
            var config = new DriverConfig
            {
                DirectorImportanceThreshold = 10f,
                FreelancerImportanceThreshold = 20f,
                ScreenwriterImportanceThreshold = 30f
            };

            Assert.Equal(10f, config.GetEffectiveImportanceThreshold(Workspace.WorkspaceRole.Director));
            Assert.Equal(20f, config.GetEffectiveImportanceThreshold(Workspace.WorkspaceRole.Freelancer));
            Assert.Equal(30f, config.GetEffectiveImportanceThreshold(Workspace.WorkspaceRole.Screenwriter));
        }

        [Fact]
        public void GetEffectiveCountThreshold_ReturnsCorrectRoleValues()
        {
            var config = new DriverConfig
            {
                DirectorCountThreshold = 3,
                FreelancerCountThreshold = 7,
                ScreenwriterCountThreshold = 10
            };

            Assert.Equal(3, config.GetEffectiveCountThreshold(Workspace.WorkspaceRole.Director));
            Assert.Equal(7, config.GetEffectiveCountThreshold(Workspace.WorkspaceRole.Freelancer));
            Assert.Equal(10, config.GetEffectiveCountThreshold(Workspace.WorkspaceRole.Screenwriter));
        }
    }
}
