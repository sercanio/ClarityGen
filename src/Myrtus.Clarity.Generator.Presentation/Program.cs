using Myrtus.Clarity.Generator.Business;
using Spectre.Console;

namespace Myrtus.Clarity.Generator.Presentation;

public class Program
{
    static async Task Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("ClarityGen").Color(Color.Cyan1));

        string projectName = string.Empty;
        string outputDirectory = string.Empty;
        List<string> modulesToAdd = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--add-module", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                modulesToAdd.Add(args[i + 1].ToLower());
                i++; 
            }
            else
            {
                if (string.IsNullOrEmpty(projectName))
                {
                    projectName = args[i];
                }
                else if (string.IsNullOrEmpty(outputDirectory))
                {
                    outputDirectory = args[i];
                }
            }
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = AnsiConsole.Ask<string>("[yellow]Enter project name:[/]");
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = AnsiConsole.Ask<string>("[yellow]Enter output directory (or press Enter for current):[/]");

            if (string.IsNullOrWhiteSpace(outputDirectory))
                outputDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }

        var generatorService = new GeneratorService();
        await generatorService.RunGeneratorAsync(projectName, outputDirectory, modulesToAdd);
    }
}
