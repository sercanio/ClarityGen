using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Spectre.Console;
using Myrtus.Clarity.Generator.Common.Models;

namespace Myrtus.Clarity.Generator.Common
{
    public class ProjectGenerator
    {
        private readonly AppSettings _config;
        private readonly StatusContext _status;
        private readonly string _tempDir;
        private readonly Dictionary<string, string> _availableModules;
        // Flag to indicate if the WebUI module is to be added.
        private bool _includeWebUI;

        public ProjectGenerator(AppSettings config, StatusContext status)
        {
            _config = config;
            _status = status;
            _tempDir = Path.Combine(Path.GetTempPath(), $"ClarityGen-{Guid.NewGuid()}");

            _availableModules = config.Modules?
                .ToDictionary(m => m.Name, m => m.GitRepoUrl, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Generates the project.
        /// - Clones the template repository.
        /// - Renames projects and modules.
        /// - Adds modules (WebUI, etc.).
        /// - Updates docker-compose.yml (if needed).
        /// - **Removes existing Git history and initializes a new repository.**
        /// </summary>
        public async Task GenerateProjectAsync(string projectName, string outputDir, List<string> modulesToAdd)
        {
            // Determine module selections.
            bool webUISelected = modulesToAdd.Any(m =>
                                    m.Equals("webui", StringComparison.OrdinalIgnoreCase) ||
                                    m.Equals("ui", StringComparison.OrdinalIgnoreCase));
            _includeWebUI = webUISelected;

            try
            {
                await CloneTemplateRepositoryAsync();
                await RenameProjectAsync(projectName);
                await UpdateSubmodulesAsync();

                // Remove default CMS folder if present.
                string defaultCmsFolder = Path.Combine(_tempDir, "modules", "cms");
                if (Directory.Exists(defaultCmsFolder))
                {
                    Directory.Delete(defaultCmsFolder, true);
                    AnsiConsole.MarkupLine("[yellow]Note:[/] Default CMS folder removed from template repository.");
                }

                // Process Web UI module.
                if (webUISelected)
                {
                    modulesToAdd.RemoveAll(m => m.Equals("webui", StringComparison.OrdinalIgnoreCase)
                                               || m.Equals("ui", StringComparison.OrdinalIgnoreCase));
                    await AddWebUIAsync(projectName);
                }

                // Process any remaining modules.
                if (modulesToAdd != null && modulesToAdd.Count > 0)
                {
                    foreach (var module in modulesToAdd)
                    {
                        if (!_availableModules.ContainsKey(module))
                        {
                            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Module '{module}' is not recognized and will be skipped.");
                            continue;
                        }
                        string moduleFolder = Path.Combine(_tempDir, "modules", module);
                        if (!Directory.Exists(moduleFolder))
                        {
                            await AddModuleAsync(module, _availableModules[module]);
                        }
                    }
                }

                // If the WebUI module is included, update the docker-compose file accordingly.
                if (_includeWebUI)
                {
                    await UpdateDockerComposeForWebUIAsync(projectName.ToLowerInvariant());
                }

                // Rename modules and finalize the project.
                await RenameModulesAsync(projectName);
                FinalizeProjectAsync(projectName, outputDir);
            }
            finally
            {
                if (Directory.Exists(_tempDir))
                {
                    try
                    {
                        RemoveReadOnlyAttributesRecursively(_tempDir); // Ensure all files are writable
                        Directory.Delete(_tempDir, true);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Warning:[/] Could not fully clean up temp directory: {Markup.Escape(ex.Message)}");
                    }
                }
            }
        }

        /// <summary>
        /// Clones the template repository into the temporary directory.
        /// </summary>
        private async Task CloneTemplateRepositoryAsync()
        {
            _status.Status = "[bold yellow]Cloning template repository...[/]";

            // Set global long path support before cloning
            await RunProcessAsync("git", "config --global core.longpaths true");

            var result = await RunProcessAsync("git", $"clone {_config.Template.GitRepoUrl} \"{_tempDir}\"");
            if (!result.Success)
            {
                throw new Exception($"Git clone failed: {result.Error}");
            }

            // After clone, set local long path support in the cloned repo
            await RunProcessAsync("git", "config --local core.longpaths true", _tempDir);
        }

        /// <summary>
        /// Renames project files and contents from the template name to the new project name.
        /// </summary>
        private async Task RenameProjectAsync(string newName)
        {
            _status.Status = "[bold yellow]Renaming project files and contents...[/]";
            string oldName = _config.Template.TemplateName;

            // Rename directories.
            var allDirectories = Directory.GetDirectories(_tempDir, "*", SearchOption.AllDirectories)
                .OrderBy(d => d.Length)
                .ToList();
            foreach (var dir in allDirectories)
            {
                if (ShouldSkipPath(dir))
                    continue;
                string newDir = dir.Replace(oldName, newName);
                if (dir != newDir && !Directory.Exists(newDir))
                {
                    Directory.Move(dir, newDir);
                }
            }

            // Rename file contents.
            var allFiles = Directory.GetFiles(_tempDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                if (ShouldSkipPath(file))
                    continue;
                await RenameFileContentsAsync(file, oldName, newName);
                if (file.EndsWith(".sln.DotSettings"))
                {
                    File.Delete(file);
                }
            }
        }

        /// <summary>
        /// Renames module files and contents within the modules folder.
        /// </summary>
        private async Task RenameModulesAsync(string newName)
        {
            string modulesDir = Path.Combine(_tempDir, "modules");
            if (!Directory.Exists(modulesDir))
                return;

            _status.Status = "[bold yellow]Renaming module files and contents...[/]";
            string oldName = _config.Template.TemplateName;

            var allDirectories = Directory.GetDirectories(modulesDir, "*", SearchOption.AllDirectories)
                .OrderBy(d => d.Length)
                .ToList();
            foreach (var dir in allDirectories)
            {
                if (ShouldSkipPath(dir))
                    continue;
                string newDir = dir.Replace(oldName, newName);
                if (dir != newDir && !Directory.Exists(newDir))
                {
                    Directory.Move(dir, newDir);
                }
            }

            var allFiles = Directory.GetFiles(modulesDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                if (ShouldSkipPath(file))
                    continue;
                await RenameFileContentsAsync(file, oldName, newName);
                if (file.EndsWith(".sln.DotSettings"))
                {
                    File.Delete(file);
                }
            }
        }

        /// <summary>
        /// Updates submodules recursively.
        /// </summary>
        private async Task UpdateSubmodulesAsync()
        {
            _status.Status = "[bold yellow]Updating submodules...[/]";
            var result = await RunProcessAsync("git", $"-C \"{_tempDir}\" submodule update --init --recursive");
            if (!result.Success)
            {
                throw new Exception($"Git submodule update failed: {result.Error}");
            }
        }

        /// <summary>
        /// Clones the generic Web UI repository into the WebUI folder.
        /// </summary>
        private async Task AddWebUIAsync(string newName)
        {
            _status.Status = "[bold yellow]Cloning Web UI repository...[/]";
            string webUIDir = Path.Combine(_tempDir, "WebUI");
            var result = await RunProcessAsync("git", $"clone https://github.com/sercanio/AppTemplate-WebUI \"{webUIDir}\"");
            if (!result.Success)
            {
                throw new Exception($"Git clone for Web UI failed: {result.Error}");
            }
            await RenameWebUIAsync(newName, webUIDir);
            AnsiConsole.MarkupLine("[green]Web UI added and renamed successfully.[/]");
        }

        /// <summary>
        /// Clones a module repository as a git submodule.
        /// </summary>
        private async Task AddModuleAsync(string moduleName, string repoUrl)
        {
            _status.Status = $"[bold yellow]Adding module '{moduleName}' as a submodule...[/]";
            string modulePath = Path.Combine("modules", moduleName);
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

            string gitModulesPath = Path.Combine(_tempDir, ".gitmodules");
            if (File.Exists(gitModulesPath))
            {
                var lines = File.ReadAllLines(gitModulesPath).ToList();
                lines.RemoveAll(line => line.Contains($"path = {modulePath}/core", StringComparison.OrdinalIgnoreCase));
                await File.WriteAllLinesAsync(gitModulesPath, lines);
            }

            string nestedSubmodulePath = Path.Combine(_tempDir, modulePath, "core");
            if (Directory.Exists(nestedSubmodulePath))
            {
                Directory.Delete(nestedSubmodulePath, true);
            }
        }

        /// <summary>
        /// Renames a Web UI module by recursively replacing occurrences of the old name with the new name.
        /// </summary>
        private async Task RenameWebUIAsync(string newName, string targetDir)
        {
            string oldName = _config.Template.TemplateName;
            var allFiles = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                if (ShouldSkipPath(file))
                    continue;
                await RenameFileContentsAsync(file, oldName, newName);
                string newFilePath = file.Replace(oldName, newName);
                if (file != newFilePath && !File.Exists(newFilePath))
                {
                    string newFileDirectory = Path.GetDirectoryName(newFilePath)!;
                    Directory.CreateDirectory(newFileDirectory);
                    File.Move(file, newFilePath);
                }
            }

            var allDirs = Directory.GetDirectories(targetDir, "*", SearchOption.AllDirectories)
                                   .OrderByDescending(d => d.Length)
                                   .ToList();
            foreach (var dir in allDirs)
            {
                if (ShouldSkipPath(dir))
                    continue;
                string newDir = dir.Replace(oldName, newName);
                if (dir != newDir && !Directory.Exists(newDir))
                {
                    Directory.Move(dir, newDir);
                }
            }
        }

        /// <summary>
        /// Updates the docker-compose.yml file to add a WebUI service block if not already present.
        /// </summary>
        private async Task UpdateDockerComposeForWebUIAsync(string newName)
        {
            // Assume the docker-compose.yml file is at the root of _tempDir.
            string composeFile = Path.Combine(_tempDir, "docker-compose.yml");
            if (!File.Exists(composeFile))
            {
                return;
            }

            string content = await File.ReadAllTextAsync(composeFile);

            if (!content.Contains($"{newName}-webui", StringComparison.OrdinalIgnoreCase))
            {
                var webUIBlock = $@"
  {newName}-webui:
    image: {newName}-webui
    build:
      context: ./WebUI
      dockerfile: Dockerfile
    container_name: {newName}.WebUI
    ports:
      - ""3000:80""
      
";

                var pattern = @"(?m)^(volumes:)";
                content = Regex.Replace(content, pattern, webUIBlock + "$1");

                content = Regex.Replace(content, @"^( *image:\s*)([^\s]+)", m =>
                {
                    return m.Groups[1].Value + m.Groups[2].Value.ToLowerInvariant();
                }, RegexOptions.Multiline);

                await File.WriteAllTextAsync(composeFile, content);
                AnsiConsole.MarkupLine("[green]Web UI container added to docker-compose.yml and image names forced to lowercase.[/]");
            }
        }

        /// <summary>
        /// Removes any .git directories and files from the specified directory.
        /// Ensures that files are not read-only before deletion.
        /// </summary>
        private void RemoveGitFolders(string directory)
        {
            // Remove .git at the root (can be a file or directory)
            var rootGitPath = Path.Combine(directory, ".git");
            if (Directory.Exists(rootGitPath))
            {
                RemoveReadOnlyAttributesRecursively(rootGitPath);
                Directory.Delete(rootGitPath, true);
            }
            else if (File.Exists(rootGitPath))
            {
                File.SetAttributes(rootGitPath, FileAttributes.Normal);
                File.Delete(rootGitPath);
            }

            // Remove any .git directories in subdirectories.
            foreach (var gitDir in Directory.GetDirectories(directory, ".git", SearchOption.AllDirectories))
            {
                RemoveReadOnlyAttributesRecursively(gitDir);
                Directory.Delete(gitDir, true);
            }

            // Remove any .git files in subdirectories.
            foreach (var gitFile in Directory.GetFiles(directory, ".git", SearchOption.AllDirectories))
            {
                File.SetAttributes(gitFile, FileAttributes.Normal);
                File.Delete(gitFile);
            }
        }

        /// <summary>
        /// Recursively sets the attributes of all files in the directory to Normal.
        /// </summary>
        private void RemoveReadOnlyAttributesRecursively(string directory)
        {
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            foreach (var dir in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
            {
                var dirInfo = new DirectoryInfo(dir);
                dirInfo.Attributes = FileAttributes.Normal;
            }
        }

        /// <summary>
        /// Finalizes the project by moving the temporary directory to the final output location,
        /// removing Git history, and initializing a new Git repository.
        /// </summary>
        private void FinalizeProjectAsync(string projectName, string outputDir)
        {
            _status.Status = "[bold green]Finalizing project...[/]";
            var finalPath = Path.Combine(outputDir, projectName);
            if (Directory.Exists(finalPath))
            {
                Directory.Delete(finalPath, true);
            }

            // Remove existing Git history.
            RemoveGitFolders(_tempDir);
            var gitModulesPath = Path.Combine(_tempDir, ".gitmodules");
            if (File.Exists(gitModulesPath))
            {
                File.Delete(gitModulesPath);
            }

            // Move the cleaned project to its final location.
            Directory.Move(_tempDir, finalPath);

            // Initialize a new Git repository.
            var initResult = RunProcessAsync("git", "init", finalPath).Result;
            if (!initResult.Success)
            {
                AnsiConsole.MarkupLine($"[red]Error initializing git repo: {initResult.Error}[/]");
            }
            var addResult = RunProcessAsync("git", "add .", finalPath).Result;
            if (!addResult.Success)
            {
                AnsiConsole.MarkupLine($"[red]Error adding files to git: {addResult.Error}[/]");
            }
            var commitResult = RunProcessAsync("git", "commit -m \"Initial commit\"", finalPath).Result;
            if (!commitResult.Success)
            {
                AnsiConsole.MarkupLine($"[red]Error committing to git: {commitResult.Error}[/]");
            }

            var tree = new Tree($"[green]Project Generated:[/] {projectName}")
                .Style(Style.Parse("cyan"));
            tree.AddNode($"[blue]Location:[/] [link={finalPath}]{finalPath}[/]");
            tree.AddNode($"[blue]Template:[/] {_config.Template.TemplateName}");
            AnsiConsole.Write(new Panel(tree)
                .Header("Success!")
                .BorderColor(Color.Green));
            AnsiConsole.MarkupLine("\n[grey]Click the path above to open the project location[/]");
        }

        /// <summary>
        /// Determines whether to skip a file or directory based on common exclusions.
        /// </summary>
        private bool ShouldSkipPath(string path)
        {
            return path.Contains(".git") ||
                   path.Contains("bin") ||
                   path.Contains("obj") ||
                   path.EndsWith(".Core") ||
                   path.Contains($"{Path.DirectorySeparatorChar}.Core{Path.DirectorySeparatorChar}");
        }

        /// <summary>
        /// Reads a file, replaces occurrences of oldName with newName (excluding ".Core" when needed),
        /// and renames the file if its name contains the oldName.
        /// </summary>
        private async Task RenameFileContentsAsync(string file, string oldName, string newName)
        {
            var content = await File.ReadAllTextAsync(file);
            content = ReplaceContentExcludingCore(content, oldName, newName);
            content = content.Replace(oldName, newName);

            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".cshtml.cs", StringComparison.OrdinalIgnoreCase))
            {
                content = UpdateUsingStatements(content, oldName, newName);
            }
            if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                content = UpdateProjectReferences(content, oldName, newName);
            }
            if (Path.GetFileName(file).Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase))
            {
                content = Regex.Replace(content, @"AppTemplate", newName, RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"src/AppTemplate.Presentation/Dockerfile", $"src/{newName}.Clarity.Presentation/Dockerfile", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"^( *image:\s*)([^\s]+)", m =>
                {
                    return m.Groups[1].Value + m.Groups[2].Value.ToLowerInvariant();
                }, RegexOptions.Multiline);
            }
            if (Path.GetFileName(file).Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
            {
                content = Regex.Replace(content, @"AppTemplate-db", newName + "-db", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"AppTemplate-redis", newName + "-redis", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"AppTemplate-mongodb", newName + "-mongodb", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"AppTemplate-seq", newName + "-seq", RegexOptions.IgnoreCase);
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

        /// <summary>
        /// Replaces occurrences of oldName with newName, except when followed by ".Core".
        /// </summary>
        private string ReplaceContentExcludingCore(string content, string oldName, string newName)
        {
            return Regex.Replace(content, $@"\b{Regex.Escape(oldName)}\b(?!((\.[A-Za-z0-9]+)*\.Core\b))", newName);
        }

        /// <summary>
        /// Updates using statements in the content.
        /// </summary>
        private string UpdateUsingStatements(string content, string oldName, string newName)
        {
            return Regex.Replace(content,
                $@"(?m)^(?<prefix>@?using\s+){Regex.Escape(oldName)}\.(?!((\w+\.)*Core\b))",
                m => $"{m.Groups["prefix"].Value}{newName}.");
        }

        /// <summary>
        /// Updates project reference paths in the content.
        /// </summary>
        private string UpdateProjectReferences(string content, string oldName, string newName)
        {
            return Regex.Replace(
                content,
                $@"(<ProjectReference\s+Include=\""[^\""]*?){Regex.Escape(oldName)}(?!((\.[A-Za-z0-9]+)*\.Core\b))([^\""]*\"")",
                m => $"{m.Groups[1].Value}{newName}{m.Groups[4].Value}"
            );
        }

        /// <summary>
        /// Helper method to run an external process asynchronously.
        /// </summary>
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

        private record ProcessResult(bool Success, string Output, string Error);
    }
}