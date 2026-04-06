using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniStats
{
    public sealed class FpsService : IDisposable
    {
        private readonly object syncRoot = new object();
        private readonly string presentMonPath = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");
        private readonly string sessionName = $"MiniStats_{Environment.ProcessId}";

        private Process? presentMonProcess;
        private CancellationTokenSource? readCancellationTokenSource;
        private Task? outputReaderTask;
        private Task? errorReaderTask;
        private string currentTargetProcessName = string.Empty;
        private float? latestFps;
        private DateTime latestFpsUtc = DateTime.MinValue;
        private int msBetweenPresentsColumnIndex = -1;

        public float? ReadFps()
        {
            string targetProcessName = GetForegroundTargetProcessName();
            EnsurePresentMonForTarget(targetProcessName);

            lock (syncRoot)
            {
                if (!latestFps.HasValue)
                {
                    return null;
                }

                if ((DateTime.UtcNow - latestFpsUtc).TotalSeconds > 2.5)
                {
                    return null;
                }

                return latestFps.Value;
            }
        }

        private void EnsurePresentMonForTarget(string targetProcessName)
        {
            if (string.Equals(currentTargetProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase))
            {
                if (presentMonProcess != null && !presentMonProcess.HasExited)
                {
                    return;
                }
            }

            StopPresentMon();

            if (string.IsNullOrWhiteSpace(targetProcessName))
            {
                currentTargetProcessName = string.Empty;
                return;
            }

            if (!File.Exists(presentMonPath))
            {
                currentTargetProcessName = string.Empty;
                return;
            }

            currentTargetProcessName = targetProcessName;
            msBetweenPresentsColumnIndex = -1;

            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = presentMonPath,
                Arguments = $"--process_name \"{targetProcessName}\" --output_stdout --no_console_stats --stop_existing_session --session_name \"{sessionName}\" --terminate_on_proc_exit",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            };

            presentMonProcess = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };

            readCancellationTokenSource = new CancellationTokenSource();

            presentMonProcess.Start();

            outputReaderTask = Task.Run(() => ReadOutputLoop(presentMonProcess, readCancellationTokenSource.Token));
            errorReaderTask = Task.Run(() => DrainErrorLoop(presentMonProcess, readCancellationTokenSource.Token));
        }

        private void ReadOutputLoop(Process process, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    string? line = process.StandardOutput.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    ProcessOutputLine(line);
                }
            }
            catch
            {
            }
        }

        private void DrainErrorLoop(Process process, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    string? line = process.StandardError.ReadLine();
                    if (line == null)
                    {
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        private void ProcessOutputLine(string line)
        {
            List<string> columns = ParseCsvLine(line);
            if (columns.Count == 0)
            {
                return;
            }

            if (msBetweenPresentsColumnIndex < 0)
            {
                msBetweenPresentsColumnIndex = FindColumnIndex(columns, "msBetweenPresents");
                return;
            }

            if (msBetweenPresentsColumnIndex >= columns.Count)
            {
                return;
            }

            string rawValue = columns[msBetweenPresentsColumnIndex];
            if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double msBetweenPresents))
            {
                return;
            }

            if (msBetweenPresents <= 0)
            {
                return;
            }

            float fps = (float)(1000.0 / msBetweenPresents);

            lock (syncRoot)
            {
                latestFps = fps;
                latestFpsUtc = DateTime.UtcNow;
            }
        }

        private static int FindColumnIndex(List<string> columns, string columnName)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (string.Equals(columns[i], columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<string> ParseCsvLine(string line)
        {
            List<string> values = new List<string>();
            StringBuilder currentValue = new StringBuilder();
            bool insideQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char currentChar = line[i];

                if (currentChar == '"')
                {
                    if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentValue.Append('"');
                        i++;
                        continue;
                    }

                    insideQuotes = !insideQuotes;
                    continue;
                }

                if (currentChar == ',' && !insideQuotes)
                {
                    values.Add(currentValue.ToString());
                    currentValue.Clear();
                    continue;
                }

                currentValue.Append(currentChar);
            }

            values.Add(currentValue.ToString());
            return values;
        }

        private static string GetForegroundTargetProcessName()
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return string.Empty;
            }

            GetWindowThreadProcessId(foregroundWindow, out uint processId);
            if (processId == 0)
            {
                return string.Empty;
            }

            try
            {
                using Process process = Process.GetProcessById((int)processId);

                string processName = process.ProcessName;
                if (string.IsNullOrWhiteSpace(processName))
                {
                    return string.Empty;
                }

                if (string.Equals(processName, "MiniStats", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(processName, "PresentMon", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(processName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                return $"{processName}.exe";
            }
            catch
            {
                return string.Empty;
            }
        }

        private void StopPresentMon()
        {
            CancellationTokenSource? localCancellationTokenSource = readCancellationTokenSource;
            Process? localProcess = presentMonProcess;

            readCancellationTokenSource = null;
            outputReaderTask = null;
            errorReaderTask = null;
            presentMonProcess = null;
            msBetweenPresentsColumnIndex = -1;

            lock (syncRoot)
            {
                latestFps = null;
                latestFpsUtc = DateTime.MinValue;
            }

            if (localCancellationTokenSource != null)
            {
                try
                {
                    localCancellationTokenSource.Cancel();
                }
                catch
                {
                }

                localCancellationTokenSource.Dispose();
            }

            if (localProcess == null)
            {
                return;
            }

            try
            {
                if (!localProcess.HasExited)
                {
                    localProcess.Kill(true);
                    localProcess.WaitForExit(1000);
                }
            }
            catch
            {
            }
            finally
            {
                localProcess.Dispose();
            }
        }

        public void Dispose()
        {
            StopPresentMon();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}