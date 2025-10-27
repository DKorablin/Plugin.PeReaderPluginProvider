# PE Reader Plugin Provider

A specialized .NET Standard 2.0 plugin provider that enables loading assemblies by reading their PE (Portable Executable) structure before full assembly loading. This provider is designed to work with the SAL (Software Abstraction Layer) plugin system.

## Features

- Pre-load PE file analysis to identify valid plugins without fully loading assemblies
- Smart plugin discovery and loading from specified directories
- Automatic monitoring of plugin directories for changes
- Safe assembly resolution with recursive dependency handling
- Support for hot-loading plugins after startup
- Built-in protection against assembly loading cycles
- Detailed trace logging for debugging and diagnostics

## How It Works

The provider uses a two-step approach for plugin loading:

1. **PE File Analysis**: 
   - Scans assemblies using PE file reader to identify potential plugins
   - Verifies interface implementations without loading the full assembly
   - Checks for proper implementation of the [IPlugin](https://www.nuget.org/packages/SAL.Flatbed) interface

2. **Plugin Loading**:
   - Loads verified plugin assemblies into the application domain
   - Handles assembly resolution for plugin dependencies
   - Monitors plugin directories for changes and supports hot-loading

## Usage

1. Download one of the compatible host applications that support SAL plugins:
    - [Flatbed Dialog](https://dkorablin.github.io/Flatbed-Dialog/)
    - [Flatbed Dialog (Lite)](https://dkorablin.github.io/Flatbed-Dialog-Lite/)
    - [Flatbed MDI](https://dkorablin.github.io/Flatbed-MDI/)
    - [Flatbed MDI (Avalon)](https://dkorablin.github.io/Flatbed-MDI-Avalon/)
    - [Flatbed Worker Service](https://dkorablin.github.io/Flatbed-WorkerService/)
2. Extract to a folder of your choice.
3. Put the plugin to the `Plugins` subfolder of the host application.
4. Modify application.settings file and add `SAL_Path` variable with the path to the plugins folder or start the host application with the `/SAL_Path` command line argument to specify the plugin directory if needed.

## Requirements

- .NET Standard 2.0 compatible platform
- SAL (Software Abstraction Layer) framework
- Proper implementation of [IPlugin](https://www.nuget.org/packages/SAL.Flatbed) interface in plugins

## Features

- Safe assembly loading with PE structure verification
- Directory monitoring for plugin changes
- Comprehensive tracing and error reporting
- Protection against recursive assembly loading
- Support for both startup and runtime plugin loading