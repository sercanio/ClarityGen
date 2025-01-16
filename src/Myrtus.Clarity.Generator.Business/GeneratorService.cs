using Spectre.Console;
using Myrtus.Clarity.Generator.Common;
using Myrtus.Clarity.Generator.DataAccess;

namespace Myrtus.Clarity.Generator.Business;

public class GeneratorService
{
    public async Task RunGeneratorAsync(string projectName, string outputDir, List<string> modulesToAdd)
    {
        await AnsiConsole.Status()
            .StartAsync("Initializing...", async ctx =>
            {
                var configService = new ConfigurationService();
                var config = await configService.LoadConfigurationAsync();
                if (config is null) return;

                var generator = new ProjectGenerator(config, ctx);
                await generator.GenerateProjectAsync(projectName, outputDir, modulesToAdd);
            });
    }
}
