using Myrtus.Clarity.Generator.Business;
using Spectre.Console;

namespace Myrtus.Clarity.Generator.Presentation;

public class Program
{
    static async Task Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("ClarityGen").Color(Color.Cyan1));

        if (args.Length == 0)
        {
            var projectName = AnsiConsole.Ask<string>("[yellow]Enter project name:[/]");
            var outputDirectory = AnsiConsole.Ask<string>("[yellow]Enter output directory (or press Enter for current):[/]");

            if (string.IsNullOrWhiteSpace(outputDirectory))
                outputDirectory = AppDomain.CurrentDomain.BaseDirectory;

            args = new[] { projectName, outputDirectory };
        }

        string newProjectName = args[0];
        string outputDir = args.Length > 1 ? args[1] : AppDomain.CurrentDomain.BaseDirectory;

        var generatorService = new GeneratorService();
        await generatorService.RunGeneratorAsync(newProjectName, outputDir);
    }
}
