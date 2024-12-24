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
            var config = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(configPath));

            if (string.IsNullOrEmpty(config?.Template?.GitRepoUrl))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Invalid configuration: Git repo URL is missing");
                return null;
            }

            return config;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Configuration Error:[/] {ex.Message}");
            return null;
        }
    }
}
