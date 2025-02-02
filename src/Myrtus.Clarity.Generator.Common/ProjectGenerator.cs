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
                await UpdateSubmodulesAsync();
                await RenameProjectAsync(projectName);

                if (modulesToAdd is { Count: > 0 })
                {
                    foreach (var module in modulesToAdd)
                    {
                        if (_availableModules.ContainsKey(module))
                        {
                            await AddModuleAsync(module, _availableModules[module]);
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Module '{module}' is not recognized and will be skipped.");
                        }
                    }
                }

                // Process files under the modules/ directory after submodules are updated
                await RenameModulesAsync(projectName);

                FinalizeProjectAsync(projectName, outputDir);
            }
            finally
            {
                // Safely delete the temporary directory
                if (Directory.Exists(_tempDir))
                {
                    ForceDeleteDirectory(_tempDir);
                }
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

        private async Task UpdateSubmodulesAsync()
        {
            _status.Status = "[bold yellow]Updating submodules...[/]";
            var result = await RunProcessAsync("git", $"-C \"{_tempDir}\" submodule update --init --recursive");
            if (!result.Success)
            {
                throw new Exception($"Git submodule update failed: {result.Error}");
            }
        }

        private async Task RenameProjectAsync(string newName)
        {
            _status.Status = "[bold yellow]Renaming project files and contents...[/]";
            var oldName = _config.Template.TemplateName;

            // Rename directories
            var allDirectories = Directory.GetDirectories(_tempDir, "*", SearchOption.AllDirectories)
                .OrderBy(d => d.Length)
                .ToList();

            foreach (var dir in allDirectories)
            {
                if (ShouldSkipPath(dir)) continue;
                string newDir = dir.Replace(oldName, newName, StringComparison.OrdinalIgnoreCase);
                if (dir != newDir && !Directory.Exists(newDir))
                {
                    Directory.Move(dir, newDir);
                }
            }

            // Rename files
            var allFiles = Directory.GetFiles(_tempDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                if (ShouldSkipPath(file)) continue;

                await RenameFileContentsAsync(file, oldName, newName);

                // Remove .DotSettings files
                if (file.EndsWith(".sln.DotSettings", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                }
            }
        }

        private async Task RenameModulesAsync(string newName)
        {
            string modulesDir = Path.Combine(_tempDir, "modules");
            if (!Directory.Exists(modulesDir))
                return;

            _status.Status = "[bold yellow]Renaming module files and contents...[/]";
            var oldName = _config.Template.TemplateName;

            // Rename module directories
            var allDirectories = Directory.GetDirectories(modulesDir, "*", SearchOption.AllDirectories)
                .OrderBy(d => d.Length)
                .ToList();

            foreach (var dir in allDirectories)
            {
                if (ShouldSkipPath(dir)) continue;
                string newDir = dir.Replace(oldName, newName, StringComparison.OrdinalIgnoreCase);
                if (dir != newDir && !Directory.Exists(newDir))
                {
                    Directory.Move(dir, newDir);
                }
            }

            // Rename module files
            var allFiles = Directory.GetFiles(modulesDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                if (ShouldSkipPath(file)) continue;

                await RenameFileContentsAsync(file, oldName, newName);

                // Remove .DotSettings files
                if (file.EndsWith(".sln.DotSettings", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                }
            }
        }

        // Keep fix_modulesRenaming approach for skipping paths
        private bool ShouldSkipPath(string path)
        {
            return path.Contains(".git") ||
                   path.Contains("tests") ||
                   path.Contains("bin") ||
                   path.Contains("obj") ||
                   path.EndsWith(".Core") ||
                   path.Contains($"{Path.DirectorySeparatorChar}.Core{Path.DirectorySeparatorChar}");
        }

        private async Task AddModuleAsync(string moduleName, string repoUrl)
        {
            _status.Status = $"[bold yellow]Adding module '{moduleName}' as a submodule...[/]";

            // Define the relative path where the module will be added
            string modulePath = Path.Combine("modules", moduleName);

            // Ensure the 'modules' directory exists
            string modulesDirectory = Path.Combine(_tempDir, "modules");
            if (!Directory.Exists(modulesDirectory))
            {
                Directory.CreateDirectory(modulesDirectory);
            }

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

            // Prevent initialization of nested submodules (like 'core/') within this module
            string cmsGitModulesPath = Path.Combine(_tempDir, ".gitmodules");
            if (File.Exists(cmsGitModulesPath))
            {
                var lines = File.ReadAllLines(cmsGitModulesPath).ToList();
                // Remove lines for any nested submodule in "core" path
                lines.RemoveAll(line => line.Contains($"path = {modulePath}/core", StringComparison.OrdinalIgnoreCase));
                await File.WriteAllLinesAsync(cmsGitModulesPath, lines);
            }

            // Optionally, remove the nested submodule directory if it exists
            string nestedSubmodulePath = Path.Combine(_tempDir, modulePath, "core");
            if (Directory.Exists(nestedSubmodulePath))
            {
                Directory.Delete(nestedSubmodulePath, true);
            }
        }

        private async Task RenameFileContentsAsync(string file, string oldName, string newName)
        {
            // Read file content
            var content = await File.ReadAllTextAsync(file);

            // Replace all occurrences of oldName except those followed by ".Core"
            content = Regex.Replace(
                content,
                $@"{Regex.Escape(oldName)}(?!\.Core)",
                newName,
                RegexOptions.IgnoreCase
            );

            // For .cs or .cshtml, also fix 'using' and 'namespace'
            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
            {
                content = Regex.Replace(
                    content,
                    $@"using\s+{Regex.Escape(oldName)}\.(?!Core)",
                    $"using {newName}.",
                    RegexOptions.IgnoreCase
                );

                content = Regex.Replace(
                    content,
                    $@"namespace\s+{Regex.Escape(oldName)}\.(?!Core)",
                    $"namespace {newName}.",
                    RegexOptions.IgnoreCase
                );
            }

            // For .csproj, update <ProjectReference Include="...">
            if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                content = Regex.Replace(
                    content,
                    $@"<ProjectReference\s+Include=""(.*?){Regex.Escape(oldName)}(?!\.Core)(.*?)""",
                    m => $"<ProjectReference Include=\"{m.Groups[1].Value}{newName}{m.Groups[2].Value}\"",
                    RegexOptions.IgnoreCase
                );
            }

            // Write updated content
            await File.WriteAllTextAsync(file, content);

            // Finally, rename the file itself if it contains oldName
            string newFilePath = file.Replace(oldName, newName, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(file, newFilePath, StringComparison.OrdinalIgnoreCase))
            {
                string newFileDirectory = Path.GetDirectoryName(newFilePath)!;
                Directory.CreateDirectory(newFileDirectory);
                if (!File.Exists(newFilePath))
                {
                    File.Move(file, newFilePath);
                }
            }
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

            AnsiConsole.Write(
                new Panel(tree)
                    .Header("Success!")
                    .BorderColor(Color.Green)
            );

            AnsiConsole.MarkupLine("\n[grey]Click the path above to open the project location[/]");
        }

        private void ForceDeleteDirectory(string directoryPath)
        {
            var directoryInfo = new DirectoryInfo(directoryPath);

            foreach (var fileInfo in directoryInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                fileInfo.Attributes = FileAttributes.Normal;
            }

            foreach (var subDirInfo in directoryInfo.GetDirectories("*", SearchOption.AllDirectories))
            {
                subDirInfo.Attributes = FileAttributes.Normal;
            }

            Directory.Delete(directoryPath, true);
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

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) output.Add(e.Data);
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) error.Add(e.Data);
            };

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
