using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;
using Myrtus.Clarity.Generator.Common;
using Myrtus.Clarity.Generator.Common.Models;

namespace Myrtus.Clarity.Generator.Tests
{
    public class ProjectGeneratorTextTests
    {
        // Helper method to create an instance of ProjectGenerator with dummy configuration.
        // We pass null for StatusContext because our tests do not exercise code that uses it.
        private ProjectGenerator CreateTestProjectGenerator()
        {
            var appSettings = new AppSettings
            {
                Template = new TemplateSettings
                {
                    GitRepoUrl = "dummy",
                    TemplateName = "Myrtus.Clarity"
                },
                Modules = new List<ModuleSettings>()
            };

            return new ProjectGenerator(appSettings, null);
        }

        [Fact]
        public void ReplaceContentExcludingCore_ReplacesCorrectly()
        {
            // Arrange
            var generator = CreateTestProjectGenerator();
            string oldName = "Myrtus.Clarity";
            string newName = "MyProject";
            string input = "Myrtus.Clarity is great, but Myrtus.Clarity.Core should not change.";
            string expected = "MyProject is great, but Myrtus.Clarity.Core should not change.";

            // Use reflection to invoke private method ReplaceContentExcludingCore
            var method = typeof(ProjectGenerator)
                .GetMethod("ReplaceContentExcludingCore", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = method.Invoke(generator, new object[] { input, oldName, newName }) as string;

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void UpdateUsingStatements_ReplacesCorrectly()
        {
            // Arrange
            var generator = CreateTestProjectGenerator();
            string oldName = "Myrtus.Clarity";
            string newName = "MyProject";
            string input = "using Myrtus.Clarity.Generator.Business;";
            string expected = "using MyProject.Generator.Business;";

            // Use reflection to invoke private method UpdateUsingStatements
            var method = typeof(ProjectGenerator)
                .GetMethod("UpdateUsingStatements", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = method.Invoke(generator, new object[] { input, oldName, newName }) as string;

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void UpdateProjectReferences_ReplacesCorrectly()
        {
            // Arrange
            var generator = CreateTestProjectGenerator();
            string oldName = "Myrtus.Clarity";
            string newName = "MyProject";
            // Sample input XML snippet; note that we only replace if not followed by ".Core"
            string input = "<ProjectReference Include=\"..\\Myrtus.Clarity\\SomeProject.csproj\" />";
            
            // Use reflection to invoke private method UpdateProjectReferences
            var method = typeof(ProjectGenerator)
                .GetMethod("UpdateProjectReferences", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = method.Invoke(generator, new object[] { input, oldName, newName }) as string;

            // Assert: result should contain the new name and not contain the old name.
            Assert.Contains(newName, result);
            Assert.DoesNotContain(oldName, result);
        }
    }
}
