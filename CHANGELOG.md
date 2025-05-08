# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [v1.0.1] - 2025-05-08]

### Changed
- **Version Bump:**
  - Updated the version to `1.0.1` to signify a minor update with bug fixes and improvements.
- **Readme Update:**
  - Updated the README file to reflect the latest changes and improvements in the project.
- **LICENCE File:**
  - Added a LICENSE file to clarify the project's licensing terms and conditions.

## [v1.0.0] - 2025-05-08

### Changed
- **Version Bump:**
  - Updated the version to `1.0.0` to signify a stable release after extensive testing and feedback.
- **Nuget Package Name Change:**
  - Changed the NuGet package name from `Myrtus.Clarity.Generator` to `ClarityGen` for better branding and recognition.

### Removed
- **MongoDB References:**
  - Removed `MongoDB.Bson` and `MongoDB.Driver` packages from `AppTemplate.Domain.csproj`, `AppTemplate.Infrastructure.csproj`, and `AppTemplate.Web.csproj`.
  - Eliminated MongoDB-related `using` directives in `Program.cs`.

### Changed
- **Project File Reorganization:**
  - Adjusted `AppTemplate.Application.csproj` to list project references before package references for improved clarity and maintainability.
## [v0.2.11] - 2025-05-07

### Removed
- **CMS Module Removal:**
  - Removed the CMS module from the default configuration.
- **MongoDB Database Support:**
  - Removed MongoDB database support from the default configuration.
  
## [v0.2.7] - 2025-02-09

### Added
- **Interactive Module Selection:**
  - Added support for interactive module selection if no modules are specified via command line.
  - Preselects "cms" module if it exists in the configuration.

- **WebUI Module Integration:**
  - Added support for integrating a WebUI module.
  - Updates `docker-compose.yml` to include a WebUI service and forces image names to lowercase.

### Changed
- **Command-Line Argument Handling:**
  - Improved command-line argument handling to separate positional arguments from flags.
  - Enhanced error handling for non-interactive mode.

### Fixed
- **Bug Fixes:**
  - Fixed issues related to renaming project files and contents.
  - Corrected handling of submodule updates and module additions.

## [v0.2.6] - 2025-02-09

### Added
- **Frontend Code Generation:**
  - Introduced a new feature for generating frontend boilerplate code along with backend setup, simplifying the process of scaffolding both frontend and backend parts of the application.
  - Supported frameworks and libraries like React, TailwindCSS, and Ant Design for seamless integration into generated projects.

- **Dynamic Module Loading:**
  - Added support for dynamically loading and unloading modules based on user configuration in the `modules` directory.
  - Optimized the system to automatically detect new modules and integrate them into the project structure without requiring additional configuration steps.

### Changed
- **Code Generator Enhancements:**
  - Improved the code generator to handle frontend creation, including routing setup, component scaffolding, and initial state management.
  - Refined the backend code generator to ensure consistency in naming conventions and project structure.
  
- **Project Initialization Flow:**
  - Simplified project initialization by adding a prompt for module selection during setup, ensuring the correct modules are added based on user input.
  - Enhanced user prompts for selecting additional features or configurations during project setup.

### Fixed
- **Bug Fixes:**
  - Fixed issues where the frontend code generation would miss necessary dependencies in some edge cases.
  - Corrected a bug in module integration where specific routes were not properly linked when dynamically loading additional modules.

## [v0.2.0] - 2024-04-27

### Added
- **Module Addition via Command-Line Arguments:**
  - Introduced the ability to add modules dynamically using flags like `--add-module cms` during project generation.
  - Automated cloning of the CMS module repository as a Git submodule under the `modules/` directory.
  - Configurable module support allowing easy extension for additional modules in the future.

- **CMS Module Integration:**
  - Seamlessly integrated the CMS module from [Myrtus.Clarity.Module.CMS](https://github.com/sercanio/Myrtus.Clarity.Module.CMS.git) without duplicating core libraries.
  - Optimized project structure to ensure core libraries are reused from the main project.

### Changed
- **Submodule Management:**
  - Enhanced logic to prevent duplication of core libraries when adding new modules.
  - Improved user feedback with clear warnings for unrecognized modules.

- **User Experience:**
  - Maintained interactive prompts for project name and output directory while supporting advanced module additions.
  - Implemented robust error handling to provide informative messages during module integration processes.

### Fixed
- **Bug Fixes:**
  - Resolved issues related to nested submodule initialization to avoid redundant cloning of core libraries.

## [v0.1.0] - 2024-04-20

### Added
- **Initial Release: ClarityGen Pre-release**
  - Introduced a streamlined way to generate project templates from a Git repository.
  - Automated key processes for efficient project setup:
    - Cloning a template repository from a configurable Git URL.
    - Renaming project files and contents dynamically based on the new project name.
    - Updating and initializing Git submodules.
    - Finalizing and organizing the generated project in a customizable output directory.
