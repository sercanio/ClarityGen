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
        /// - If only CMS is selected, clones the backend CMS module into modules/cms.
        /// - If WebUI is selected, clones the generic Web UI repository.
        /// - If both are selected, it also clones the CMS UI module into WebUI/src/modules.
        /// Additionally, if the WebUI module is selected, the generated docker-compose.yml file
        /// is updated to include a WebUI service and all image names are forced to lowercase.
        /// </summary>
        public async Task GenerateProjectAsync(string projectName, string outputDir, List<string> modulesToAdd)
        {
            // Determine module selections.
            bool cmsSelected = modulesToAdd.Any(m => m.Equals("cms", StringComparison.OrdinalIgnoreCase));
            bool webUISelected = modulesToAdd.Any(m =>
                                    m.Equals("webui", StringComparison.OrdinalIgnoreCase) ||
                                    m.Equals("ui", StringComparison.OrdinalIgnoreCase));
            _includeWebUI = webUISelected;

            try
            {
                await CloneTemplateRepositoryAsync();
                await RenameProjectAsync(projectName);
                await UpdateSubmodulesAsync();

                // Remove default CMS folder from template if present.
                string defaultCmsFolder = Path.Combine(_tempDir, "modules", "cms");
                if (Directory.Exists(defaultCmsFolder))
                {
                    Directory.Delete(defaultCmsFolder, true);
                    AnsiConsole.MarkupLine("[yellow]Note:[/] Default CMS folder removed from template repository.");
                }
                // (Optionally update .gitmodules if necessary.)

                // Process backend CMS module.
                if (cmsSelected)
                {
                    modulesToAdd.RemoveAll(m => m.Equals("cms", StringComparison.OrdinalIgnoreCase));
                    await AddCmsModuleAsync(projectName);
                }

                // Process Web UI module.
                if (webUISelected)
                {
                    modulesToAdd.RemoveAll(m => m.Equals("webui", StringComparison.OrdinalIgnoreCase)
                                               || m.Equals("ui", StringComparison.OrdinalIgnoreCase));
                    await AddWebUIAsync(projectName);

                    // When both CMS and WebUI are selected, also add the CMS UI module.
                    if (cmsSelected)
                    {
                        await AddCmsUIModuleAsync(projectName);
                    }
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
                    // Lowercase the project name for Docker naming conventions.
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
                    Directory.Delete(_tempDir, true);
                }
            }
        }

        /// <summary>
        /// Clones the template repository into the temporary directory.
        /// </summary>
        private async Task CloneTemplateRepositoryAsync()
        {
            _status.Status = "[bold yellow]Cloning template repository...[/]";
            var result = await RunProcessAsync("git", $"clone {_config.Template.GitRepoUrl} \"{_tempDir}\"");
            if (!result.Success)
            {
                throw new Exception($"Git clone failed: {result.Error}");
            }
        }

        /// <summary>
        /// Renames project files and contents from the template name to the new project name.
        /// </summary>
        private async Task RenameProjectAsync(string newName)
        {
            _status.Status = "[bold yellow]Renaming project files and contents...[/]";
            string oldName = _config.Template.TemplateName;

            // Rename directories first.
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

            // Then rename file contents and files.
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
        /// Clones the backend CMS module repository into the modules/cms folder.
        /// </summary>
        private async Task AddCmsModuleAsync(string newName)
        {
            _status.Status = "[bold yellow]Cloning backend CMS module repository...[/]";
            string repoUrl = "https://github.com/sercanio/Myrtus.Clarity.Module.CMS.git";
            string cmsModuleDir = Path.Combine(_tempDir, "modules", "cms");
            var result = await RunProcessAsync("git", $"clone {repoUrl} \"{cmsModuleDir}\"");
            if (!result.Success)
            {
                throw new Exception($"Git clone for backend CMS module failed: {result.Error}");
            }
            await RenameModuleAsync(newName, cmsModuleDir);
            AnsiConsole.MarkupLine("[green]Backend CMS module added and renamed successfully.[/]");
        }

        /// <summary>
        /// Clones the CMS UI module repository into the WebUI/src/modules/cms folder.
        /// </summary>
        private async Task AddCmsUIModuleAsync(string newName)
        {
            _status.Status = "[bold yellow]Cloning CMS UI module repository...[/]";
            string repoUrl = "https://github.com/sercanio/Myrtus.Clarity.WebUI.Module.CMS.git";
            string cmsUIModuleDir = Path.Combine(_tempDir, "WebUI", "src", "modules", "cms");
            Directory.CreateDirectory(Path.Combine(_tempDir, "WebUI", "src", "modules")); // Ensure parent folder exists.
            var result = await RunProcessAsync("git", $"clone {repoUrl} \"{cmsUIModuleDir}\"");
            if (!result.Success)
            {
                throw new Exception($"Git clone for CMS UI module failed: {result.Error}");
            }
            await RenameWebUIAsync(newName, cmsUIModuleDir);
            AnsiConsole.MarkupLine("[green]CMS UI module added and renamed successfully.[/]");
        }

        /// <summary>
        /// Clones the generic Web UI repository into the WebUI folder.
        /// </summary>
        private async Task AddWebUIAsync(string newName)
        {
            _status.Status = "[bold yellow]Cloning Web UI repository...[/]";
            string webUIDir = Path.Combine(_tempDir, "WebUI");
            var result = await RunProcessAsync("git", $"clone https://github.com/sercanio/Myrtus.Clarity.WebUI \"{webUIDir}\"");
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
        /// Renames a backend module by recursively replacing occurrences of the old name with the new name.
        /// </summary>
        private async Task RenameModuleAsync(string newName, string moduleDir)
        {
            string oldName = _config.Template.TemplateName;
            var allFiles = Directory.GetFiles(moduleDir, "*.*", SearchOption.AllDirectories);
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
            var allDirs = Directory.GetDirectories(moduleDir, "*", SearchOption.AllDirectories)
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
        /// The new service block is inserted immediately before the top-level "volumes:" key.
        /// Additionally, this method runs a regex to force all "image:" lines to use lowercase names.
        /// </summary>
        private async Task UpdateDockerComposeForWebUIAsync(string newName)
        {
            // Assume the docker-compose.yml file is at the root of _tempDir.
            string composeFile = Path.Combine(_tempDir, "docker-compose.yml");
            if (!File.Exists(composeFile))
            {
                // If the file does not exist, nothing to update.
                return;
            }

            string content = await File.ReadAllTextAsync(composeFile);

            // Check whether the webui service is already defined.
            if (!content.Contains($"{newName}-webui", StringComparison.OrdinalIgnoreCase))
            {
                // Define the webui service block.
                var webUIBlock = $@"
  {newName.ToLowerInvariant()}-webui:
    image: {newName.ToLowerInvariant()}-webui
    build:
      context: ./WebUI
      dockerfile: Dockerfile
    container_name: {newName}.WebUI
    ports:
      - ""3000:80""
    labels:
      - ""traefik.enable=true""
      - 'traefik.http.routers.webui.rule=Host(`localhost`) && PathPrefix(`/`)'
      - ""traefik.http.routers.webui.entrypoints=web""
      - ""traefik.http.services.webui.loadbalancer.server.port=80""
      
";

                // Use a regex in multiline mode to find the top-level "volumes:" key (at the beginning of a line)
                // and insert our webUIBlock just above it.
                var pattern = @"(?m)^(volumes:)";
                content = Regex.Replace(content, pattern, webUIBlock + "$1");

                // Now force all "image:" lines to be lower case.
                content = Regex.Replace(content, @"^( *image:\s*)([^\s]+)", m =>
                {
                    return m.Groups[1].Value + m.Groups[2].Value.ToLowerInvariant();
                }, RegexOptions.Multiline);

                await File.WriteAllTextAsync(composeFile, content);
                AnsiConsole.MarkupLine("[green]Web UI container added to docker-compose.yml and image names forced to lowercase.[/]");
            }
        }

        /// <summary>
        /// Determines whether to skip a file or directory path based on common exclusions.
        /// </summary>
        private bool ShouldSkipPath(string path)
        {
            return path.Contains(".git") ||
                   path.Contains("tests") ||
                   path.Contains("bin") ||
                   path.Contains("obj") ||
                   path.EndsWith(".Core") ||
                   path.Contains($"{Path.DirectorySeparatorChar}.Core{Path.DirectorySeparatorChar}");
        }

        /// <summary>
        /// Reads a file, replaces occurrences of oldName with newName (excluding ".Core" when needed),
        /// updates using statements and project references, and then writes back the content.
        /// Also renames the file if its name contains the oldName.
        /// </summary>
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

            // Additional processing for docker-compose.yml and appsettings.json files.
            if (Path.GetFileName(file).Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase))
            {
                content = Regex.Replace(content, @"Myrtus", newName, RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"src/Myrtus\.Clarity\.WebAPI/Dockerfile", $"src/{newName}.Clarity.WebAPI/Dockerfile", RegexOptions.IgnoreCase);
                // Also force image names to lowercase in this file.
                content = Regex.Replace(content, @"^( *image:\s*)([^\s]+)", m =>
                {
                    return m.Groups[1].Value + m.Groups[2].Value.ToLowerInvariant();
                }, RegexOptions.Multiline);
            }
            if (Path.GetFileName(file).Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
            {
                content = Regex.Replace(content, @"Myrtus-db", newName + "-db", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"Myrtus-redis", newName + "-redis", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"Myrtus-mongodb", newName + "-mongodb", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"Myrtus-seq", newName + "-seq", RegexOptions.IgnoreCase);
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
        /// Replaces occurrences of oldName with newName in the given content, except when followed by ".Core".
        /// </summary>
        private string ReplaceContentExcludingCore(string content, string oldName, string newName)
        {
            // This negative lookahead ensures that after oldName, if there's any series of dot tokens ending in .Core,
            // the replacement is skipped.
            return Regex.Replace(content, $@"\b{Regex.Escape(oldName)}\b(?!((\.[A-Za-z0-9]+)*\.Core\b))", newName);
        }

        /// <summary>
        /// Updates using statements in the content.
        /// </summary>
        private string UpdateUsingStatements(string content, string oldName, string newName)
        {
            return Regex.Replace(content, $@"using\s+{Regex.Escape(oldName)}\.(?!((\w+\.)*Core\b))", $"using {newName}.");
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
        /// Moves the generated project from the temporary directory to the output directory,
        /// and prints the final project location.
        /// </summary>
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
