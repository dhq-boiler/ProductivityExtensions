# boilersExtensions
This is an "it's me" Visual Studio extension with an assortment of features I wanted while working on it.
English(en-US) and Japanese are supported.

<img src="boilersExtensions/Preview.png" width="300">

## Features

### 🔄 Region Navigator
Navigate between `#region` and `#endregion` directives in C# files easily. Use the context menu or keyboard shortcut (Ctrl+F2) to quickly jump between region blocks.

### 🔍 Sync to Solution Explorer
Select "View in Solution Explorer" from the document tab context menu to locate the current file in Solution Explorer and make it the active selection.

### 🌐 GitHub Line Navigation
When working with repositories that have a GitHub remote, right-click in the editor to "Open the corresponding line in the GitHub hosting repository". This opens the current file at the current line in your browser.

### 🏷️ Type Hierarchy
Quickly change types in your code using type hierarchy. Right-click on a type and select "Change type from type hierarchy" to see and select from all compatible types in your solution.

### 🔄 Project & Solution Renaming
Rename projects and solutions with automatic updates to relevant references, namespaces, and file paths:
- Right-click on a project to "Rename this project"
- Right-click on the solution to "Rename this solution"

### 🆔 GUID Utilities
- Select a GUID string in your code and right-click to "Update selected Guid string" to replace it with a new GUID
- "Batch update Guid" to replace multiple GUIDs in the active document at once

### 🌱 Seed Generator for EFCore
Generate test data for Entity Framework Core entities:
- Automatically analyzes entity classes, properties, and relationships
- Creates seed data with proper relationship handling
- Supports fixed values for properties
- Handles parent-child relationships with customizable record counts

<!-- ![Seed Generator Dialog](path/to/seed-generator-screenshot.png) -->

#### Features of the Seed Generator:
- Entity relationship detection and visualization
- Parent-child relationship handling
- Foreign key reference management
- Support for enum values
- Random data generation based on property names and types

### ⚙️ Customization
Access the extension settings via Tools > boilersExtensions > Preferences... to:
- Change the UI language (English or Japanese)
- Enable/disable individual features
- Customize behavior of various components

## Requirements
- Visual Studio 2022
- .NET Framework 4.8

## Installation
Install from the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=dhq-boiler.BE001) or download the VSIX from the releases page.

## Development
The extension is built with:
- Visual Studio SDK
- Roslyn API for code analysis
- WPF for UI components
- ReactiveProperty for MVVM implementation

## License
See [LICENSE](LICENSE) for details.

## Support and Contribution
Issues and pull requests are welcome in the repository.

---

Made with ❤️ by dhq_boiler