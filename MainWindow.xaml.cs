using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Windows;
using pwiz.CLI.msdata;
using pwiz.CLI.cv;

namespace WiffReader {
    public partial class MainWindow : Window {
        public MainWindow() {
            InitializeComponent();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e) {
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog()) {
                fbd.Description = "Select the folder containing the .wiff files";
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    txtPath.Text = fbd.SelectedPath;
                }
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e) {
            string folderPath = txtPath.Text;
            int targetYear = 0;
            int.TryParse(txtYear.Text, out targetYear);
            bool isMultiThread = chkMultiThread.IsChecked ?? false;

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) {
                MessageBox.Show("Please select a valid folder path.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            btnStart.IsEnabled = false;
            chkMultiThread.IsEnabled = false;
            btnBrowse.IsEnabled = false;
            txtLog.Clear();

            var progress = new Progress<string>(msg => {
                txtLog.AppendText(msg + Environment.NewLine);
                txtLog.ScrollToEnd();
            });

            await Task.Run(() => ProcessFiles(folderPath, targetYear, isMultiThread, progress));

            btnStart.IsEnabled = true;
            chkMultiThread.IsEnabled = true;
            btnBrowse.IsEnabled = true;
        }

        private void ProcessFiles(string folderPath, int targetYear, bool isMultiThread, IProgress<string> progress) {
            try {
                string logFilePath = Path.Combine(folderPath, $"ExtractionLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                object logLock = new object();

                Action<string> LogMessage = (message) => {
                    progress.Report(message);

                    lock (logLock) {
                        File.AppendAllText(logFilePath, $"{DateTime.Now:HH:mm:ss} | {message}{Environment.NewLine}");
                    }
                };

                string[] wiffFiles = Directory.GetFiles(folderPath, "*.wiff", SearchOption.AllDirectories);

                if (wiffFiles.Length == 0) {
                    LogMessage("No .wiff files found in the selected directory.");
                    return;
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    ExtractionProgressBar.Maximum = wiffFiles.Length;
                    ExtractionProgressBar.Value = 0;
                });

                var barProgress = new Progress<int>(value => {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        ExtractionProgressBar.Value = value;
                    });
                });

                int processedCount = 0;

                string outputPath = Path.Combine(folderPath, targetYear == 0 ? "RunSummary.csv" : $"RunSummary_{targetYear}.csv");
                StringBuilder csvContent = new StringBuilder();
                csvContent.AppendLine("\"File Name\",\"Sample Name\",\"Duration (min)\",\"Polarity\",\"Year\",\"Month\",\"Day\",\"Directory\"");

                ConcurrentBag<string> csvLines = new ConcurrentBag<string>();

                int targetCores = isMultiThread ? Math.Max(1, (int)(Environment.ProcessorCount * 0.8)) : 1;
                var options = new ParallelOptions { MaxDegreeOfParallelism = targetCores };

                LogMessage($"Starting extraction for {wiffFiles.Length} file(s)...");
                LogMessage($"Using {(isMultiThread ? targetCores : 1)} concurrent thread(s).");
                LogMessage(new string('-', 60));

                double totalRunTime = 0;
                object timeLock = new object();

                Parallel.ForEach(wiffFiles, options, file => {
                    try {
                        string scanFile = file + ".scan";

                        if (!System.IO.File.Exists(scanFile)) {
                            LogMessage($"SKIPPED: Missing companion .wiff.scan for {System.IO.Path.GetFileName(file)}");
                            return;
                        }

                        DateTime creationTime = File.GetCreationTime(file);

                        if (targetYear != 0 && creationTime.Year != targetYear) {
                            return;
                        }

                        string fileName = Path.GetFileName(file);
                        string directoryPath = Path.GetDirectoryName(file);

                        string year = creationTime.Year.ToString();
                        string month = creationTime.Month.ToString("D2");
                        string day = creationTime.Day.ToString("D2");

                        try {
                            using (var msDataList = new MSDataList()) {
                                ReaderList.FullReaderList.read(file, msDataList);

                                foreach (var msd in msDataList) {
                                    try {
                                        string sampleName = msd.run.id;
                                        string baseName = Path.GetFileNameWithoutExtension(file);

                                        if (sampleName.StartsWith(baseName + "-")) {
                                            sampleName = sampleName.Substring(baseName.Length + 1);
                                        } else if (sampleName == baseName) {
                                            int firstUnderscore = sampleName.IndexOf('_');
                                            if (firstUnderscore >= 0 && firstUnderscore < sampleName.Length - 1) {
                                                sampleName = sampleName.Substring(firstUnderscore + 1);
                                            }
                                        }

                                        string durationStr = "0.00";
                                        string polarityStr = "Unknown";

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

                                                            lock (timeLock) {
                                                                totalRunTime += timeValue;
                                                            }

                                                            durationStr = timeValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

                                                            var polarityParam = lastSpectrum.cvParamChild(pwiz.CLI.cv.CVID.MS_scan_polarity);

                                                            if (polarityParam.cvid == pwiz.CLI.cv.CVID.MS_positive_scan) {
                                                                polarityStr = "Positive";
                                                            } else if (polarityParam.cvid == pwiz.CLI.cv.CVID.MS_negative_scan) {
                                                                polarityStr = "Negative";
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        string line = $"\"{fileName}\",\"{sampleName}\",\"{durationStr}\",\"{polarityStr}\",\"{year}\",\"{month}\",\"{day}\",\"{directoryPath}\"";

                                        csvLines.Add(line);

                                        LogMessage($"{fileName} -> Sample: {sampleName} | {durationStr} min");

                                    } catch (System.Exception innerEx) {
                                        LogMessage($"SKIPPED SAMPLE: Error reading a sample in {fileName} | {innerEx.Message}");
                                        continue;
                                    }
                                }
                            }
                        } catch (System.Exception ex) {
                            LogMessage($"SKIPPED FILE: Unreadable file {Path.GetFileName(file)} | {ex.Message}");
                            return;
                        }
                    } finally {
                        int currentCount = System.Threading.Interlocked.Increment(ref processedCount);
                        ((System.IProgress<int>)barProgress).Report(currentCount);
                    }
                });

                LogMessage("------------------------------------------------------------");
                LogMessage($"Total LC Run Time: {totalRunTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} min ({(totalRunTime / 60).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} h)");

                if (csvLines.Count == 0) {
                    LogMessage(new string('-', 60));
                    LogMessage($"No files matched the year {targetYear} in the specified directory.");
                    return;
                }

                foreach (string line in csvLines) {
                    csvContent.AppendLine(line);
                }

                File.WriteAllText(outputPath, csvContent.ToString());
                LogMessage(new string('-', 60));
                LogMessage($"Extraction Complete! Data saved to:\n{outputPath}");
            } catch (AggregateException ae) {
                foreach (var innerEx in ae.Flatten().InnerExceptions) {
                    progress.Report($"CRITICAL ERROR: {innerEx.Message}");
                }
                File.AppendAllText(Path.Combine(folderPath, $"ExtractionLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), $"CRITICAL ERROR: {ae.Message}{Environment.NewLine}");
            } catch (Exception ex) {
                progress.Report($"CRITICAL ERROR: {ex.Message}");
                File.AppendAllText(Path.Combine(folderPath, $"ExtractionLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), $"CRITICAL ERROR: {ex.Message}{Environment.NewLine}");
            }
        }
    }
}