# **ClarityGen Project Generator**

````ascii
   ____   _                  _   _              ____
  / ___| | |   __ _   _ __  (_) | |_   _   _   / ___|   ___   _ __
 | |     | |  / _` | | '__| | | | __| | | | | | |  _   / _ \ | '_ \
 | |___  | | | (_| | | |    | | | |_  | |_| | | |_| | |  __/ | | | |
  \____| |_|  \__,_| |_|    |_|  \__|  \__, |  \____|  \___| |_| |_|
                                       |___/
````
**Clarity Generator** is a powerful tool designed to streamline the creation of project templates. By automating the process of cloning repositories, renaming files and contents, updating submodules, and finalizing setups, it helps developers save time and effort.

![Version](https://img.shields.io/badge/version-0.2.2-blue)
![DotNet](https://img.shields.io/badge/dotnet-v9.0-purple)
![MIT License](https://img.shields.io/badge/license-MIT-green)
![Build Status](https://img.shields.io/github/actions/workflow/status/sercanio/Myrtus.Clarity/build.yml?branch=main)


---
## Table of Contents
1. [Features](#features)
2. [Configuration](#configuration)
3. [Usage](#usage)
4. [Code Overview](#code-overview)
5. [Contributing](#contributing)
6. [License](#license)

---

## **Features**
- Clone a template repository from a specified Git URL.
- Automatically rename project files and contents based on the provided project name.
- Update Git submodules seamlessly.
- Move the finalized project to a specified output directory for immediate use.

---

## **Configuration**

The generator’s settings are stored in the `appsettings.json` file. Below is a sample configuration:

```json
{
  "Template": {
    "GitRepoUrl": "https://github.com/sercanio/Myrtus.Clarity.git",
    "TemplateName": "Myrtus.Clarity"
  },
  "Modules": [
    {
      "Name": "cms",
      "GitRepoUrl": "https://github.com/sercanio/Myrtus.Clarity.Module.CMS.git"
    }
  ],
  "Output": {
    "DefaultPath": "."
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

---

## **Usage**

### **Command-Line Execution**
Run the application by providing the project name and output directory as command-line arguments.

### Syntax:
````bash
ClarityGen.exe <ProjectName> <OutputDirectory> [--add-module <ModuleName>]...
````

#### Example:
```bash
ClarityGen.exe MyNewProject C:\Users\serca\source\repos\sercanio\tmp --add-module cms
```

### **Interactive Mode**
If no arguments are provided, the program will prompt for the project name and output directory:

```bash
ClarityGen.exe
```

---

## **Code Overview**

### **Key Components**
1. **`Program.cs`**  
   The entry point of the application.  
   - Displays a welcome message.  
   - Handles command-line arguments and prompts for input if required.  
   - Initiates the project generation process.  

2. **`ConfigurationService.cs`**  
   Manages configuration loading from `appsettings.json`.  
   - Validates configuration values.  
   - Supplies necessary settings to other components.  

3. **`GeneratorService.cs`**  
   Orchestrates the overall project generation workflow.  
   - Initializes the configuration and project generator.  
   - Manages execution flow and error handling.  

4. **`ProjectGenerator.cs`**  
   Handles core operations:  
   - Cloning the template repository.  
   - Renaming files and their contents.  
   - Updating submodules.  
   - Finalizing and organizing the output.  

---

## **Contributing**
Contributions are welcome!  
- **Pull Requests**: Submit improvements or new features via a pull request.  
- **Issues**: Report bugs or suggest enhancements by opening an issue.  

---

## **License**
This project is licensed under the **MIT License**. See the [LICENSE](LICENSE.txt) file for more details.

