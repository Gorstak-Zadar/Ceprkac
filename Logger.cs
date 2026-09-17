using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Ceprkac
{
    /// <summary>
    /// Central, thread-safe file logger. Writes everything to
    /// %AppData%\Ceprkac\ceprkac.log so that any future problem can be diagnosed
    /// from a single place. Logging must NEVER throw or slow the UI, so every
    /// write is best-effort and swallowed on failure.
    ///
    /// The log is rotated once it grows past a size cap: the current file is moved
    /// to ceprkac.log.1 (overwriting any previous rotation) and a fresh file starts.
    /// This keeps disk usage bounded while still preserving recent history.
    /// </summary>
    internal static class Logger
    {
        private static readonly object _gate = new object();
        private static string? _logPath;
        private static string? _rollPath;
        private const long MaxBytes = 5 * 1024 * 1024; // 5 MB before rotation

        /// <summary>
        /// Point the logger at the app data folder. Safe to call more than once.
        /// Called early in MainForm construction, before anything interesting runs.
        /// </summary>
        public static void Init(string appDataFolder)
        {
            try
            {
                Directory.CreateDirectory(appDataFolder);
                lock (_gate)
                {
                    _logPath = Path.Combine(appDataFolder, "ceprkac.log");
                    _rollPath = Path.Combine(appDataFolder, "ceprkac.log.1");
                }
                WriteLine("LOG", $"===== Ceprkac log opened {DateTime.Now:yyyy-MM-dd HH:mm:ss} (PID {Process.GetCurrentProcess().Id}) =====");
            }
            catch { /* never let logging break startup */ }
        }

        /// <summary>General purpose entry: Logger.Log("AUTOFILL", "message").</summary>
        public static void Log(string category, string message)
            => WriteLine(category, message);

        /// <summary>Log an exception with an optional context note.</summary>
        public static void Error(string category, string context, Exception ex)
            => WriteLine(category, $"ERROR {context}: {ex.GetType().Name}: {ex.Message}");

        private static void WriteLine(string category, string message)
        {
            string? path;
            string? roll;
            lock (_gate)
            {
                path = _logPath;
                roll = _rollPath;
            }
            if (string.IsNullOrEmpty(path)) return; // Init not called yet

            var line = new StringBuilder()
                .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ")
                .Append('[').Append("T").Append(Thread.CurrentThread.ManagedThreadId).Append("] ")
                .Append('[').Append(category).Append("] ")
                .Append(message)
                .Append(Environment.NewLine)
                .ToString();

            try
            {
                lock (_gate)
                {
                    RotateIfNeeded(path!, roll);
                    File.AppendAllText(path!, line, Encoding.UTF8);
                }
            }
            catch { /* disk full, locked, etc. - ignore */ }

            // Mirror to the debugger output for live sessions.
            try { Debug.Write(line); } catch { }
        }

        private static void RotateIfNeeded(string path, string? roll)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < MaxBytes) return;
                if (!string.IsNullOrEmpty(roll))
                {
                    try { if (File.Exists(roll)) File.Delete(roll); } catch { }
                    try { File.Move(path, roll); } catch { }
                }
            }
            catch { }
        }
    }
}
