using Myrtus.Clarity.Generator.Business;
using Myrtus.Clarity.Generator.DataAccess;
using Spectre.Console;

namespace Myrtus.Clarity.Generator.Presentation
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            // Draw the title in a stylish way
            AnsiConsole.Write(new FigletText("ClarityGen").Color(Color.Cyan1));

            string projectName = string.Empty;
            string outputDirectory = string.Empty;
            List<string> modulesToAdd = new List<string>();

            // We'll separate positional arguments from flags.
            List<string> positionalArgs = new List<string>();

            // Process command line arguments:
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--add-module", StringComparison.OrdinalIgnoreCase))
                {
                    // Move past the flag and collect subsequent arguments until another flag is detected.
                    i++;
                    while (i < args.Length && !args[i].StartsWith("--"))
                    {
                        modulesToAdd.Add(args[i].ToLower());
                        i++;
                    }
                    i--; // Adjust for the outer loop increment.
                }
                else
                {
                    positionalArgs.Add(args[i]);
                }
            }

            // Positional arguments: first is project name, second is output directory.
            if (positionalArgs.Count > 0)
                projectName = positionalArgs[0];
            if (positionalArgs.Count > 1)
                outputDirectory = positionalArgs[1];

            // Handle interactive input based on the environment
            bool isInteractiveMode = !args.Contains("--non-interactive") && !Console.IsInputRedirected;

            if (string.IsNullOrWhiteSpace(projectName))
            {
                if (isInteractiveMode)
                {
                    projectName = AnsiConsole.Ask<string>("[yellow]Enter project name:[/]");
                }
                else
                {
                    throw new InvalidOperationException("Project name must be provided in non-interactive mode.");
                }
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                if (isInteractiveMode)
                {
                    outputDirectory = AnsiConsole.Ask<string>("[yellow]Enter output directory (or press Enter for current):[/]");
                    if (string.IsNullOrWhiteSpace(outputDirectory))
                        outputDirectory = AppDomain.CurrentDomain.BaseDirectory;
                }
                else
                {
                    throw new InvalidOperationException("Output directory must be provided in non-interactive mode.");
                }
            }

            // If no modules were specified via command line, prompt interactively if allowed.
            if (modulesToAdd.Count == 0 && isInteractiveMode)
            {
                var configService = new ConfigurationService();
                var config = await configService.LoadConfigurationAsync();

                if (config != null && config.Modules != null && config.Modules.Any())
                {
                    var prompt = new MultiSelectionPrompt<string>()
                        .Title("Select modules to add to the project:")
                        .NotRequired() // User may choose no module.
                        .PageSize(10)
                        .MoreChoicesText("[grey](Use space to toggle and enter to accept)[/]")
                        .InstructionsText("[blue](Press [green]<space>[/] to toggle a module, [green]<enter>[/] to accept)[/]");
                    
                    // Add each module individually.
                    foreach (var module in config.Modules.Select(m => m.Name))
                    {
                        prompt.AddChoice(module);
                    }

                    // Preselect "cms" if it exists.
                    if (config.Modules.Any(m => m.Name.Equals("cms", StringComparison.OrdinalIgnoreCase)))
                    {
                        prompt.Select("cms");
                    }

                    modulesToAdd = AnsiConsole.Prompt(prompt);
                }
            }

            // Now hand over the parameters to your GeneratorService.
            var generatorService = new GeneratorService();
            await generatorService.RunGeneratorAsync(projectName, outputDirectory, modulesToAdd);
        }
    }
}
