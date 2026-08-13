# WIFF Metadata Extractor

A C# console application that recursively scans directories for SCIEX `.wiff` files and extracts the sample names, run durations (in minutes), file creation dates, and directory paths. The software allows filtering by a specific year, processes files concurrently for maximum speed, and automatically exports the results to a `.csv` file.

Built with ProteoWizard Version: 3.0.24121-ce45d8c (automated build)

## Setup and Compilation Instructions

Due to the complex native C++ dependencies required by ProteoWizard's `.NET` bindings, this project must be compiled directly into a deployment folder containing the vendor libraries. 

1. Create a deployment folder on your machine (e.g., `C:\PwizDeploy`).
2. Download and install **ProteoWizard** (x64).
3. Copy **all** files and `.manifest` documents from the ProteoWizard installation directory and paste them into `C:\PwizDeploy`.
4. Open the project in Visual Studio. Ensure the target framework is set to `.NET Framework 4.8` and the platform is `x64`.
5. Edit your `.csproj` file to output the build directly to the deployment folder:

```xml
<OutputPath>C:\PwizDeploy\</OutputPath>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
```

6. Build the project. The executable will be generated directly among the native dependencies, automatically resolving any Side-by-Side configuration errors.

## How to Use

1. Execute the compiled `.exe` file.
2. The console will prompt: `Please enter the full path to the folder containing the .wiff files (or press Enter to exit):`
3. Paste the full directory path containing your `.wiff` and `.wiff.scan` files and press **Enter**.
4. The console will then prompt: `Please enter the target year (e.g., 2026), or 0 for all years:`
5. Type your desired year to filter the search, or `0` to process everything, and press **Enter**.
6. The software will scan the specified folder and all subdirectories, reading the metadata from the matching files using multiple CPU cores.
7. The extracted table will be printed to the console, and a summary file (`RunSummary_<Year>.csv` or `RunSummary.csv`) will be generated in the root of the provided folder.
8. The application will remain open, allowing you to seamlessly paste a new path and start another analysis.