using Myrtus.Clarity.Generator.Business;
using Myrtus.Clarity.Generator.DataAccess;
using Spectre.Console;

namespace Myrtus.Clarity.Generator.Presentation
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            // Draw the title in a stylish way.
            AnsiConsole.Write(new FigletText("ClarityGen").Color(Color.Cyan1));

            string projectName = string.Empty;
            string outputDirectory = string.Empty;
            List<string> modulesToAdd = new List<string>();

            // Separate positional arguments from flags.
            List<string> positionalArgs = new List<string>();

            // Process command line arguments.
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

            // If project name or output directory weren't provided, ask for them interactively.
            if (string.IsNullOrWhiteSpace(projectName))
                projectName = AnsiConsole.Ask<string>("[yellow]Enter project name:[/]");
            
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                outputDirectory = AnsiConsole.Ask<string>("[yellow]Enter output directory (or press Enter for current):[/]");
                if (string.IsNullOrWhiteSpace(outputDirectory))
                    outputDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }

            // Note: If no modules are specified via command line, we do not prompt interactively.
            // The modulesToAdd list remains empty and the project is created without additional modules.

            // Hand over the parameters to your GeneratorService.
            var generatorService = new GeneratorService();
            await generatorService.RunGeneratorAsync(projectName, outputDirectory, modulesToAdd);
        }
    }
}
