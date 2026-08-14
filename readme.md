# WIFF Metadata Extractor

A high-performance WPF desktop application that recursively scans directories for SCIEX `.wiff` files and extracts sample names, run durations (in minutes), file creation dates, and directory paths. The software can process files concurrently for maximum speed to mitigate I/O bottlenecks and automatically exports the results to a `.csv` file alongside a calculation of the total LC run time.

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
2. In the application window, input the full directory path containing your `.wiff` and `.wiff.scan` files.
3. Input your desired target year to filter the search (e.g., 2026), or use `0` to process all years.
4. Start the extraction. The software will securely scan the specified folder and all subdirectories using multiple CPU cores.
5. You can monitor the real-time extraction progress, including successfully parsed samples and skipped files (due to missing `.scan` companions), in the dark-themed UI log.
6. Upon completion, the interface will display the **Total LC Run Time** for all processed files.
7. A summary file (`RunSummary_<Year>.csv` or `RunSummary.csv`) will be generated automatically in the root of the provided folder.