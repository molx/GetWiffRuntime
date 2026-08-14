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
                string[] wiffFiles = Directory.GetFiles(folderPath, "*.wiff", SearchOption.AllDirectories);

                if (wiffFiles.Length == 0) {
                    progress.Report("No .wiff files found in the selected directory.");
                    return;
                }

                string outputPath = Path.Combine(folderPath, targetYear == 0 ? "RunSummary.csv" : $"RunSummary_{targetYear}.csv");
                StringBuilder csvContent = new StringBuilder();
                csvContent.AppendLine("\"File Name\",\"Sample Name\",\"Duration (min)\",\"Year\",\"Month\",\"Day\",\"Directory\"");

                ConcurrentBag<string> csvLines = new ConcurrentBag<string>();

                int targetCores = isMultiThread ? Math.Max(1, (int)(Environment.ProcessorCount * 0.8)) : 1;
                var options = new ParallelOptions { MaxDegreeOfParallelism = targetCores };

                progress.Report($"Starting extraction for {wiffFiles.Length} file(s)...");
                progress.Report($"Using {(isMultiThread ? targetCores : 1)} concurrent thread(s).");
                progress.Report(new string('-', 60));

                double totalRunTime = 0;
                object timeLock = new object();

                Parallel.ForEach(wiffFiles, options, file => {
                    string scanFile = file + ".scan";

                    if (!System.IO.File.Exists(scanFile)) {
                        progress.Report($"SKIPPED: Missing companion .wiff.scan for {System.IO.Path.GetFileName(file)}");
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

                    using (var msDataList = new MSDataList()) {
                        ReaderList.FullReaderList.read(file, msDataList);

                        foreach (var msd in msDataList) {
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
                                            }
                                        }
                                    }
                                }
                            }

                            string line = $"\"{fileName}\",\"{sampleName}\",\"{durationStr}\",\"{year}\",\"{month}\",\"{day}\",\"{directoryPath}\"";
                            csvLines.Add(line);
                            progress.Report($"{fileName} -> Sample: {sampleName} | {durationStr} min");
                        }
                    }
                });

                progress.Report("------------------------------------------------------------");
                progress.Report($"Total LC Run Time: {totalRunTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} min ({(totalRunTime/60).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} h)");

                if (csvLines.Count == 0) {
                    progress.Report(new string('-', 60));
                    progress.Report($"No files matched the year {targetYear} in the specified directory.");
                    return;
                }

                foreach (string line in csvLines) {
                    csvContent.AppendLine(line);
                }

                File.WriteAllText(outputPath, csvContent.ToString());
                progress.Report(new string('-', 60));
                progress.Report($"Extraction Complete! Data saved to:\n{outputPath}");
            } catch (AggregateException ae) {
                foreach (var innerEx in ae.Flatten().InnerExceptions) {
                    progress.Report($"CRITICAL ERROR: {innerEx.Message}");
                }
            } catch (Exception ex) {
                progress.Report($"CRITICAL ERROR: {ex.Message}");
            }
        }
    }
}