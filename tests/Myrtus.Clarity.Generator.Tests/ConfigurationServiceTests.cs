using System.Threading.Tasks;
using Myrtus.Clarity.Generator.DataAccess;
using Xunit;

namespace Myrtus.Clarity.Generator.Tests
{
    public class ConfigurationServiceTests
    {
        [Fact]
        public async Task LoadConfigurationAsync_ReturnsValidConfiguration()
        {
            // Arrange
            var configService = new ConfigurationService();

            // Act
            var config = await configService.LoadConfigurationAsync();

            // Assert
            Assert.NotNull(config);
            Assert.NotNull(config.Template);
            Assert.False(string.IsNullOrWhiteSpace(config.Template.GitRepoUrl));
        }
    }
}
