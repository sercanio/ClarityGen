using Myrtus.Clarity.Generator.Common.Models;
using Spectre.Console;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Myrtus.Clarity.Generator.Common
{
    public class ProjectGenerator
    {
        private readonly AppSettings _config;
        private readonly StatusContext _status;
        private readonly string _tempDir;

        // Dictionary of module names to Git repository URLs, loaded from config
        private readonly Dictionary<string, string> _availableModules;

        public ProjectGenerator(AppSettings config, StatusContext status)
        {
            _config = config;
            _status = status;
            _tempDir = Path.Combine(Path.GetTempPath(), $"ClarityGen-{Guid.NewGuid()}");

            // Build our dictionary from the Modules section in appsettings.json
            _availableModules = config.Modules?
                .ToDictionary(m => m.Name, m => m.GitRepoUrl, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public async Task GenerateProjectAsync(string projectName, string outputDir, List<string> modulesToAdd)
        {
            try
            {
                await CloneTemplateRepositoryAsync();
                await RenameProjectAsync(projectName);
                await UpdateSubmodulesAsync();

                // Remove the 'cms' module if it was updated automatically but not selected by the user.
                if (!modulesToAdd.Any(m => m.Equals("cms", StringComparison.OrdinalIgnoreCase)))
                {
                    // Remove the cms folder if it exists
                    string cmsModulePath = Path.Combine(_tempDir, "modules", "cms");
                    if (Directory.Exists(cmsModulePath))
                    {
                        Directory.Delete(cmsModulePath, true);
                        AnsiConsole.MarkupLine("[yellow]Note:[/] 'cms' folder removed since it was not selected.");
                    }

                    // Remove any reference to cms from the .gitmodules file
                    string gitmodulesPath = Path.Combine(_tempDir, ".gitmodules");
                    if (File.Exists(gitmodulesPath))
                    {
                        var lines = File.ReadAllLines(gitmodulesPath).ToList();
                        // Remove any line that mentions the cms module (adjust the keyword if needed)
                        lines.RemoveAll(line => line.Contains("modules/cms", StringComparison.OrdinalIgnoreCase));
                        await File.WriteAllLinesAsync(gitmodulesPath, lines);
                    }
                }

                // Add any modules specified by the user that aren’t already present
                if (modulesToAdd != null && modulesToAdd.Count > 0)
                {
                    foreach (var module in modulesToAdd)
                    {
                        // Check if the module already exists (it might have been updated as a submodule)
                        string moduleFolder = Path.Combine(_tempDir, "modules", module);
                        if (!_availableModules.ContainsKey(module))
                        {
                            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Module '{module}' is not recognized and will be skipped.");
                            continue;
                        }
                        if (!Directory.Exists(moduleFolder))
                        {
                            await AddModuleAsync(module, _availableModules[module]);
                        }
                    }
                }

                // Process modules (renaming, etc.)
                await RenameModulesAsync(projectName);
                FinalizeProjectAsync(projectName, outputDir);
            }
            finally
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
        }
        private async Task AddModuleAsync(string moduleName, string repoUrl)
        {
            _status.Status = $"[bold yellow]Adding module '{moduleName}' as a submodule...[/]";

            // Define the relative path where the module will be added
            string modulePath = Path.Combine("modules", moduleName); // Relative path

            // Ensure the 'modules' directory exists
            string modulesDirectory = Path.Combine(_tempDir, "modules");
            if (!Directory.Exists(modulesDirectory))
            {
                Directory.CreateDirectory(modulesDirectory);
            }

            // Run 'git submodule add <repoUrl> <modulePath>' within the cloned template repository
            var gitCommand = $"submodule add {repoUrl} \"{modulePath}\"";
            var result = await RunProcessAsync("git", gitCommand, _tempDir);

            if (!result.Success)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Failed to add module '{moduleName}': {result.Error}");
                return;
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]Success:[/] Module '{moduleName}' added successfully.");
            }

            // Prevent initialization of nested submodules (like 'core/') within the CMS module
            // by removing the .gitmodules entry for the nested submodule
            string cmsGitModulesPath = Path.Combine(_tempDir, ".gitmodules");
            if (File.Exists(cmsGitModulesPath))
            {
                var lines = File.ReadAllLines(cmsGitModulesPath).ToList();
                // Assuming that the nested submodule is named 'core'
                lines.RemoveAll(line => line.Contains($"path = {modulePath}/core", StringComparison.OrdinalIgnoreCase));
                await File.WriteAllLinesAsync(cmsGitModulesPath, lines);
            }

            // Optionally, remove the nested submodule's directory if it exists
            string nestedSubmodulePath = Path.Combine(_tempDir, modulePath, "core");
            if (Directory.Exists(nestedSubmodulePath))
            {
                Directory.Delete(nestedSubmodulePath, true);
            }
        }

        private async Task CloneTemplateRepositoryAsync()
        {
            _status.Status = "[bold yellow]Cloning template repository...[/]";
            var result = await RunProcessAsync("git", $"clone {_config.Template.GitRepoUrl} \"{_tempDir}\"");
            if (!result.Success)
            {
                throw new Exception($"Git clone failed: {result.Error}");
            }
        }

        private async Task RenameProjectAsync(string newName)
        {
            _status.Status = "[bold yellow]Renaming project files and contents...[/]";
            var oldName = _config.Template.TemplateName;

            var allDirectories = Directory.GetDirectories(_tempDir, "*", SearchOption.AllDirectories)
                .OrderBy(d => d.Length)
                .ToList();

            foreach (var dir in allDirectories)
            {
                if (ShouldSkipPath(dir)) continue;
                string newDir = dir.Replace(oldName, newName);
                if (dir != newDir && !Directory.Exists(newDir))
                {
                    Directory.Move(dir, newDir);
                }
            }

            var allFiles = Directory.GetFiles(_tempDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                if (ShouldSkipPath(file)) continue;
                await RenameFileContentsAsync(file, oldName, newName);
                if (file.EndsWith(".sln.DotSettings"))
                {
                    File.Delete(file);
                }
            }
        }

        private async Task RenameModulesAsync(string newName)
        {
            // Process the modules folder (but skip files/folders in any subfolder that belongs to core)
            string modulesDir = Path.Combine(_tempDir, "modules");
            if (!Directory.Exists(modulesDir))
                return;

            _status.Status = "[bold yellow]Renaming module files and contents...[/]";

            var oldName = _config.Template.TemplateName;

            var allDirectories = Directory.GetDirectories(modulesDir, "*", SearchOption.AllDirectories)
                .OrderBy(d => d.Length)
                .ToList();

            foreach (var dir in allDirectories)
            {
                if (ShouldSkipPath(dir)) continue;
                string newDir = dir.Replace(oldName, newName);
                if (dir != newDir && !Directory.Exists(newDir))
                {
                    Directory.Move(dir, newDir);
                }
            }

            var allFiles = Directory.GetFiles(modulesDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                if (ShouldSkipPath(file)) continue;
                await RenameFileContentsAsync(file, oldName, newName);
                if (file.EndsWith(".sln.DotSettings"))
                {
                    File.Delete(file);
                }
            }
        }

        private async Task UpdateSubmodulesAsync()
        {
            _status.Status = "[bold yellow]Updating submodules...[/]";
            var result = await RunProcessAsync("git", $"-C \"{_tempDir}\" submodule update --init --recursive");
            if (!result.Success)
            {
                throw new Exception($"Git submodule update failed: {result.Error}");
            }
        }

        // Modified: now uses a normalized, case-insensitive check
        private bool ShouldSkipPath(string path)
        {
            return path.Contains(".git") ||
                   path.Contains("tests") ||
                   path.Contains("bin") ||
                   path.Contains("obj") ||
                   path.EndsWith(".Core") ||
                   path.Contains($"{Path.DirectorySeparatorChar}.Core{Path.DirectorySeparatorChar}");
        }

        private async Task RenameFileContentsAsync(string file, string oldName, string newName)
        {
            var content = await File.ReadAllTextAsync(file);

            // Apply standard replacements for project files.
            content = ReplaceContentExcludingCore(content, oldName, newName);
            if (file.EndsWith(".cs") || file.EndsWith(".cshtml"))
            {
                content = UpdateUsingStatements(content, oldName, newName);
            }
            if (file.EndsWith(".csproj"))
            {
                content = UpdateProjectReferences(content, oldName, newName);
            }

            // Additional handling for docker-compose.yml
            if (Path.GetFileName(file).Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase))
            {
                // Replace all occurrences of "Myrtus" with the new app name.
                content = Regex.Replace(content, @"Myrtus", newName, RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"src/Myrtus\.Clarity\.WebAPI/Dockerfile", $"src/{newName}.Clarity.WebAPI/Dockerfile", RegexOptions.IgnoreCase);
            }

            // Additional handling for appsettings.json connection strings.
            if (Path.GetFileName(file).Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
            {
                // Replace specific connection endpoints.
                content = Regex.Replace(content, @"Myrtus-db", newName + "-db", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"Myrtus-redis", newName + "-redis", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"Myrtus-mongodb", newName + "-mongodb", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"Myrtus-seq", newName + "-seq", RegexOptions.IgnoreCase);
            }

            await File.WriteAllTextAsync(file, content);

            // Rename the file itself if its path contains the old template name.
            string newFilePath = file.Replace(oldName, newName);
            if (file != newFilePath)
            {
                string newFileDirectory = Path.GetDirectoryName(newFilePath)!;
                Directory.CreateDirectory(newFileDirectory);
                if (!File.Exists(newFilePath))
                {
                    File.Move(file, newFilePath);
                }
            }
        }


        private string ReplaceContentExcludingCore(string content, string oldName, string newName)
        {
            // Only replace occurrences of oldName that are not followed by ".Core"
            return Regex.Replace(content, $@"\b{Regex.Escape(oldName)}\b(?!\.Core)", newName);
        }

        private string UpdateUsingStatements(string content, string oldName, string newName)
        {
            // Only replace using statements for oldName.* but not oldName.Core
            return Regex.Replace(content, $@"using\s+{Regex.Escape(oldName)}\.(?!Core)", $"using {newName}.");
        }

        private string UpdateProjectReferences(string content, string oldName, string newName)
        {
            return Regex.Replace(
                content,
                $@"<ProjectReference\s+Include=""(.*?){Regex.Escape(oldName)}(?!\.Core)(.*?)""",
                m => $"<ProjectReference Include=\"{m.Groups[1].Value}{newName}{m.Groups[2].Value}\"");
        }

        private void FinalizeProjectAsync(string projectName, string outputDir)
        {
            _status.Status = "[bold green]Finalizing project...[/]";
            var finalPath = Path.Combine(outputDir, projectName);
            if (Directory.Exists(finalPath))
            {
                Directory.Delete(finalPath, true);
            }
            Directory.Move(_tempDir, finalPath);
            var tree = new Tree($"[green]Project Generated:[/] {projectName}")
                .Style(Style.Parse("cyan"));
            tree.AddNode($"[blue]Location:[/] [link={finalPath}]{finalPath}[/]");
            tree.AddNode($"[blue]Template:[/] {_config.Template.TemplateName}");
            AnsiConsole.Write(new Panel(tree)
                .Header("Success!")
                .BorderColor(Color.Green));
            AnsiConsole.MarkupLine("\n[grey]Click the path above to open the project location[/]");
        }

        private record ProcessResult(bool Success, string Output, string Error);

        private async Task<ProcessResult> RunProcessAsync(string command, string arguments, string? workingDirectory = null)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                process.StartInfo.WorkingDirectory = workingDirectory;
            }

            var output = new List<string>();
            var error = new List<string>();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) output.Add(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.Add(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return new ProcessResult(
                process.ExitCode == 0,
                string.Join(Environment.NewLine, output),
                string.Join(Environment.NewLine, error)
            );
        }
    }
}
