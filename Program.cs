using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Ceprkac
{
    internal static class Program
    {
        private const string MutexName = @"Local\Ceprkac_SingleInstance";
        private const string PipeName = "Ceprkac_OpenUrl";

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        private static MainForm? mainForm;
        private static Mutex? instanceMutex;

        //  Per-Monitor V2 DPI awareness (belt-and-suspenders alongside app.manifest) 
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        [STAThread]
        private static void Main(string[] args)
        {
            // Ensure PerMonitorV2 even if manifest isn't applied (e.g. debugger attach)
            try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }

            var parsed = ParseArgs(args);
            if (parsed.RegisterBrowser)
            {
                try { BrowserRegistration.RegisterAndRequestDefault(); } catch { }
                return;
            }

            InjectedModuleCleaner.StartGlobal();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                try
                {
                    LogException(e.Exception, "ThreadException");
                    MessageBox.Show(FormatCrash(e.Exception), "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    LogException(ex, "UnhandledException");
                    MessageBox.Show(FormatCrash(ex), "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            };

            static string FormatCrash(Exception? ex)
            {
                if (ex == null) return "Unhandled error";
                var msg = ex.Message;
                if (string.IsNullOrWhiteSpace(msg)) msg = ex.GetType().Name;
                return $"{ex.GetType().Name}: {msg}\r\n\r\n(Ditails saved to Ceprkac-crash.log in your temp folder)";
            }

            static void LogException(Exception? ex, string source)
            {
                try { if (ex != null) Logger.Error("CRASH", source, ex); } catch { }
                try
                {
                    var path = Path.Combine(Path.GetTempPath(), "Ceprkac-crash.log");
                    var sb = new StringBuilder();
                    sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}");
                    sb.AppendLine(ex?.ToString() ?? "null");
                    sb.AppendLine(new string('-', 80));
                    File.AppendAllText(path, sb.ToString());
                }
                catch { }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            instanceMutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                ForwardToRunningInstance(parsed.Urls);
                return;
            }

            try { BrowserRegistration.Register(); } catch { }

            var pipeThread = new Thread(PipeServerLoop) { IsBackground = true, Name = "Ceprkac-UrlPipe" };
            pipeThread.Start();

            try
            {
                mainForm = new MainForm(parsed.Urls);
                Application.Run(mainForm);
            }
            finally
            {
                try { instanceMutex.ReleaseMutex(); } catch { }
                instanceMutex.Dispose();
            }
        }

        private sealed class ParsedArgs
        {
            public bool RegisterBrowser;
            public readonly List<string> Urls = new();
        }

        private static ParsedArgs ParseArgs(string[] args)
        {
            var p = new ParsedArgs();
            foreach (var raw in args)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var a = raw.Trim().Trim('"');
                if (a.Equals("--register-browser", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("/register", StringComparison.OrdinalIgnoreCase))
                {
                    p.RegisterBrowser = true;
                    continue;
                }
                if (a.Equals("--after-webview2", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (a.StartsWith("-", StringComparison.Ordinal) || a.StartsWith("/", StringComparison.Ordinal))
                    continue;
                p.Urls.Add(a);
            }
            return p;
        }

        private static void ForwardToRunningInstance(List<string> urls)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName("Ceprkac"))
                {
                    if (proc.Id == Process.GetCurrentProcess().Id) continue;
                    try { AllowSetForegroundWindow(proc.Id); } catch { }
                    try
                    {
                        if (proc.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                            SetForegroundWindow(proc.MainWindowHandle);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            var payload = urls.Count == 0 ? "\n" : string.Join("\n", urls);
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(250);
                    var bytes = Encoding.UTF8.GetBytes(payload);
                    client.Write(bytes, 0, bytes.Length);
                    client.Flush();
                    return;
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
        }

        private static void PipeServerLoop()
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    server.WaitForConnection();
                    string text;
                    using (var reader = new StreamReader(server, Encoding.UTF8))
                        text = reader.ReadToEnd();
                    DispatchIncoming(text);
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
        }

        private static void DispatchIncoming(string text)
        {
            var form = mainForm;
            if (form == null || form.IsDisposed) return;
            var urls = new List<string>();
            foreach (var line in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var u = line.Trim();
                if (u.Length > 0) urls.Add(u);
            }
            try
            {
                form.BeginInvoke(new Action(() =>
                {
                    if (urls.Count == 0) form.RestoreAndFocus();
                    else foreach (var u in urls) form.OpenExternalUrl(u);
                }));
            }
            catch { }
        }
    }
}
