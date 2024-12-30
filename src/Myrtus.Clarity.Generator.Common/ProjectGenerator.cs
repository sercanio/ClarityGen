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

        // Mapping of module names to their Git repository URLs
        private readonly Dictionary<string, string> _availableModules = new Dictionary<string, string>
        {
            { "cms", "https://github.com/sercanio/Myrtus.Clarity.Module.CMS.git" }
            // Add more modules here as needed
        };

        public ProjectGenerator(AppSettings config, StatusContext status)
        {
            _config = config;
            _status = status;
            _tempDir = Path.Combine(Path.GetTempPath(), $"ClarityGen-{Guid.NewGuid()}");
        }

        public async Task GenerateProjectAsync(string projectName, string outputDir, List<string> modulesToAdd)
        {
            try
            {
                await CloneTemplateRepositoryAsync();
                await RenameProjectAsync(projectName);
                await UpdateSubmodulesAsync();

                if (modulesToAdd != null && modulesToAdd.Count > 0)
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
            var oldCoreName = oldName + ".Core";

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

        private async Task UpdateSubmodulesAsync()
        {
            _status.Status = "[bold yellow]Updating submodules...[/]";

            var result = await RunProcessAsync("git", $"-C \"{_tempDir}\" submodule update --init --recursive");
            if (!result.Success)
            {
                throw new Exception($"Git submodule update failed: {result.Error}");
            }
        }

        private bool ShouldSkipPath(string path)
        {
            return path.Contains(".git") ||
                   path.Contains("Migrations") ||
                   path.Contains("tests") ||
                   path.Contains("bin") ||
                   path.Contains("obj") ||
                   path.EndsWith(".Core") ||
                   path.Contains($"{Path.DirectorySeparatorChar}.Core{Path.DirectorySeparatorChar}");
        }

        private async Task RenameFileContentsAsync(string file, string oldName, string newName)
        {
            var content = await File.ReadAllTextAsync(file);
            content = ReplaceContentExcludingCore(content, oldName, newName);

            if (file.EndsWith(".cs") || file.EndsWith(".cshtml"))
            {
                content = UpdateUsingStatements(content, oldName, newName);
            }

            if (file.EndsWith(".csproj"))
            {
                content = UpdateProjectReferences(content, oldName, newName);
            }

            await File.WriteAllTextAsync(file, content);

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
            return Regex.Replace(content, $@"\b{Regex.Escape(oldName)}\b(?!\.Core)", newName);
        }

        private string UpdateUsingStatements(string content, string oldName, string newName)
        {
            return Regex.Replace(content, $@"using\s+{Regex.Escape(oldName)}\.(?!Core)", $"using {newName}.");
        }

        private string UpdateProjectReferences(string content, string oldName, string newName)
        {
            return Regex.Replace(content,
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
                string.Join(Environment.NewLine, error));
        }
    }
}
