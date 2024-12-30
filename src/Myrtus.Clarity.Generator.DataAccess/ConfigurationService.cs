using System.Text.Json;
using Spectre.Console;
using Myrtus.Clarity.Generator.Common.Models;

namespace Myrtus.Clarity.Generator.DataAccess;

public class ConfigurationService
{
    public async Task<AppSettings?> LoadConfigurationAsync()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Configuration file not found");
            return null;
        }

        try
        {
            var configContent = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<AppSettings>(configContent);

            if (string.IsNullOrEmpty(config?.Template?.GitRepoUrl))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Invalid configuration: Git repo URL is missing");
                return null;
            }

            return config;
        }
        catch (Exception ex)
        {
            var safeMessage = Markup.Escape(ex.Message);
            AnsiConsole.MarkupLine($"[red]Configuration Error:[/] {safeMessage}");
            return null;
        }
    }
}
