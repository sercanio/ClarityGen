# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

