using System;
using System.IO;
using System.Threading.Tasks;
using Myrtus.Clarity.Generator.Business;
using Xunit;
using System.Collections.Generic;

namespace Myrtus.Clarity.Generator.Tests
{
    public class IntegrationTests
    {
        [Fact(Skip = "Integration test - requires git and network access")]
        public async Task RunGeneratorAsync_CreatesFinalProjectDirectory()
        {
            // Arrange
            string projectName = "IntegrationTestProject";
            string tempOutputDir = Path.Combine(Path.GetTempPath(), "IntegrationTestOutput");
            Directory.CreateDirectory(tempOutputDir);
            var modulesToAdd = new List<string>(); // For this test, we pass no additional modules.

            var generatorService = new GeneratorService();

            // Act
            await generatorService.RunGeneratorAsync(projectName, tempOutputDir, modulesToAdd);

            // Assert: the final project directory should exist.
            string finalPath = Path.Combine(tempOutputDir, projectName);
            Assert.True(Directory.Exists(finalPath), "Final project directory was not created.");

            // Cleanup (be sure to remove test artifacts)
            Directory.Delete(finalPath, true);
            Directory.Delete(tempOutputDir, true);
        }
    }
}
