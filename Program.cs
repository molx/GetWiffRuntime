using System;
using System.IO;
using System.Text;
using pwiz.CLI.msdata;
using pwiz.CLI.cv;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace WiffReader {
    class Program {
        static void Main(string[] args) {
            while (true) {
                try {
                    Console.WriteLine("Please enter the full path to the folder containing the .wiff files (or press Enter to exit):");
                    string folderPath = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(folderPath)) {
                        break;
                    }

                    if (!Directory.Exists(folderPath)) {
                        Console.WriteLine("Invalid folder path.\n");
                        continue;
                    }

                    Console.WriteLine("Please enter the target year (e.g., 2026), or 0 for all years:");
                    string yearInput = Console.ReadLine();
                    int targetYear = 0;
                    int.TryParse(yearInput, out targetYear);

                    string[] wiffFiles = Directory.GetFiles(folderPath, "*.wiff", SearchOption.AllDirectories);

                    if (wiffFiles.Length == 0) {
                        Console.WriteLine("No .wiff files found in the directory.\n");
                        continue;
                    }

                    string outputPath = Path.Combine(folderPath, targetYear == 0 ? "RunSummary.csv" : $"RunSummary_{targetYear}.csv");
                    StringBuilder csvContent = new StringBuilder();
                    csvContent.AppendLine("\"File Name\",\"Sample Name\",\"Duration (min)\",\"Year\",\"Month\",\"Day\",\"Directory\"");

                    Console.WriteLine("\nFile Name | Sample Name | Duration (min) | Year | Month | Day | Directory");
                    Console.WriteLine("-------------------------------------------------------------------------------------------------");
                    ConcurrentBag<string> csvLines = new ConcurrentBag<string>();

                    int targetCores = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));
                    var options = new ParallelOptions { MaxDegreeOfParallelism = targetCores };

                    Parallel.ForEach(wiffFiles, options, file => {
                        DateTime creationTime = File.GetCreationTime(file);

                        if (targetYear != 0 && creationTime.Year != targetYear) {
                            return;
                        }

                        string fileName = Path.GetFileName(file);
                        string directoryPath = Path.GetDirectoryName(file);

                        string year = creationTime.Year.ToString();
                        string month = creationTime.Month.ToString("D2");
                        string day = creationTime.Day.ToString("D2");

                        using (var msDataList = new MSDataList()) {
                            ReaderList.FullReaderList.read(file, msDataList);

                            foreach (var msd in msDataList) {
                                string sampleName = msd.run.id;
                                string expectedPrefix = Path.GetFileNameWithoutExtension(file) + "-";

                                if (sampleName.StartsWith(expectedPrefix)) {
                                    sampleName = sampleName.Substring(expectedPrefix.Length);
                                }

                                string durationStr = "0.00";

                                if (msd.run.spectrumList != null && msd.run.spectrumList.size() > 0) {
                                    int lastIndex = msd.run.spectrumList.size() - 1;

                                    using (var lastSpectrum = msd.run.spectrumList.spectrum(lastIndex, false)) {
                                        if (lastSpectrum.scanList != null && lastSpectrum.scanList.scans.Count > 0) {
                                            var scanStartTime = lastSpectrum.scanList.scans[0].cvParam(CVID.MS_scan_start_time);

                                            if (!scanStartTime.empty()) {
                                                string rawValue = scanStartTime.value.ToString().Replace(",", ".");

                                                if (double.TryParse(rawValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double timeValue)) {
                                                    if (scanStartTime.units == CVID.UO_second) {
                                                        timeValue = timeValue / 60.0;
                                                    }

                                                    durationStr = timeValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                                                }
                                            }
                                        }
                                    }
                                }

                                string line = $"\"{fileName}\",\"{sampleName}\",\"{durationStr}\",\"{year}\",\"{month}\",\"{day}\",\"{directoryPath}\"";
                                csvLines.Add(line);
                                Console.WriteLine($"{fileName} | {sampleName} | {durationStr} | {year} | {month} | {day} | {directoryPath}");
                            }
                        }
                    });

                    if (csvLines.Count == 0) {
                        Console.WriteLine($"\nNo files matched the year {targetYear} in the specified directory.\n");
                        continue;
                    }

                    foreach (string line in csvLines) {
                        csvContent.AppendLine(line);
                    }

                    File.WriteAllText(outputPath, csvContent.ToString());
                    Console.WriteLine($"\nData successfully saved to: {outputPath}\n");
                } catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                }
            }
        }
    }
}