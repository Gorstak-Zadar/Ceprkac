using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Ceprkac
{
    /// <summary>
    /// Backbone: every mapped module is identified (keep-tree / bundled /
    /// Microsoft-family signature) and foreign ones are unloaded immediately.
    ///
    /// In-process loads hit <see cref="LdrRegisterDllNotification"/> and are
    /// queued (never FreeLibrary under the loader lock). A 50 ms sweep covers
    /// children and manual maps that skip LdrLoadDll.
    ///
    /// Keep: this exe dir (bundled names only if unsigned), Edge WebView2,
    /// WebView2 user-data, Windows, .NET, GPU vendors.
    /// Children use the same identity check - not "Temp only".
    /// Empty paths are skipped so a lookup miss cannot unmap a GPU ICD.
    /// </summary>
    internal sealed class InjectedModuleCleaner
    {
        public static InjectedModuleCleaner? Instance { get; private set; }

        private const string WebView2ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        private const int PollMs = 50;
        private const uint LdrLoaded = 1;
        private const uint ThreadSetContext = 0x0010;

        private readonly HashSet<IntPtr> _ours = new();
        private readonly HashSet<IntPtr> _attempted = new();
        private readonly Dictionary<int, ChildState> _children = new();
        private readonly List<string> _prefixes = new();
        private readonly ConcurrentQueue<(IntPtr Base, string Path)> _ldrQueue = new();
        private readonly ManualResetEventSlim _stop = new(false);
        private readonly AutoResetEvent _pulse = new(false);
        private readonly object _startLock = new();
        private Thread? _thread;
        private IntPtr _ldrCookie;
        private LdrDllNotification? _ldrCb;
        private Dictionary<string, string>? _dosDevices;
        private string _selfDir = "";
        private string _selfImage = "";

        private sealed class ChildState
        {
            public HashSet<IntPtr> Ours { get; } = new();
        }

        public static InjectedModuleCleaner StartGlobal()
        {
            var c = Instance;
            if (c != null) { c.Start(); return c; }
            c = new InjectedModuleCleaner();
            Instance = c;
            c.Start();
            return c;
        }

        public void Start()
        {
            lock (_startLock)
            {
                if (_thread != null) return;
                BuildPrefixes();
                _thread = new Thread(Run) { IsBackground = true, Name = "Ceprkac-ModuleCleaner" };
                _thread.Start();
                RegisterLdr();
            }
        }

        public void Stop()
        {
            _stop.Set();
            _pulse.Set();
            UnregisterLdr();
            _thread?.Join(500);
        }

        private void BuildPrefixes()
        {
            void add(string? p)
            {
                if (string.IsNullOrWhiteSpace(p)) return;
                var n = NormalizePath(p);
                if (n.Length == 0) return;
                if (File.Exists(n))
                {
                    try { n = Path.GetDirectoryName(n) ?? n; } catch { }
                    n = NormalizePath(n);
                }
                if (n.Length == 0) return;
                if (!_prefixes.Contains(n)) _prefixes.Add(n);
            }

            try
            {
                _selfImage = NormalizePath(Process.GetCurrentProcess().MainModule?.FileName);
                _selfDir = DirOf(_selfImage);
            }
            catch
            {
                try
                {
                    _selfDir = NormalizePath(AppDomain.CurrentDomain.BaseDirectory);
                }
                catch { }
            }

            try { add(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()); } catch { }

            add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Microsoft\EdgeWebView");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Microsoft\EdgeWebView");
            add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\EdgeWebView");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Microsoft\Edge");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Microsoft\Edge");
            add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\Edge");
            add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\EdgeCore");
            add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\Microsoft\EdgeUpdate");
            add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\EdgeUpdate");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Microsoft\EdgeUpdate");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Microsoft\EdgeUpdate");
            add(Environment.GetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER"));
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Common Files\Microsoft Shared");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Common Files\Microsoft Shared");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\dotnet");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\dotnet");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\NVIDIA Corporation");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\NVIDIA Corporation");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\AMD");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\AMD");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\ATI Technologies");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\ATI Technologies");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Intel");
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Intel");
            add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ceprkac", "WebView2UserData"));
            DiscoverEdgeRuntimeFolders(add);
        }

        /// <summary>
        /// Loadable-module extensions. Modules are enumerated via EnumProcessModulesEx
        /// regardless of extension, so the filename-keyed helpers below must not be
        /// .dll-only: a bundled Microsoft.*.winmd / System.*.winmd (WinRT/WinUI) or a
        /// sideload plant with a non-.dll module extension must still be classified.
        /// </summary>
        private static readonly HashSet<string> ModuleExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".dll", ".winmd", ".ocx", ".cpl", ".ax", ".node",
                ".drv", ".acm", ".tsp", ".mui", ".efi",
            };

        internal static bool IsModuleExtension(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            string ext;
            try { ext = Path.GetExtension(fileName) ?? ""; }
            catch { return false; }
            return ext.Length > 0 && ModuleExtensions.Contains(ext);
        }

        internal static bool IsBundledFileName(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            var n = fileName!.ToLowerInvariant();
            if (n == "ceprkac.exe" || n == "webview2loader.dll") return true;
            // Microsoft.* / System.* framework modules - any loadable-module extension
            // (WinUI/WinRT ship Microsoft.*.winmd and System.*.winmd next to the app).
            if ((n.StartsWith("microsoft.", StringComparison.Ordinal) ||
                 n.StartsWith("system.", StringComparison.Ordinal)) &&
                IsModuleExtension(n))
                return true;
            return false;
        }

        /// <summary>True = keep mapped. False = unload. Empty path = skip (do not unload).</summary>
        internal bool IsAllowedModule(string? processImage, string? modulePath)
        {
            if (string.IsNullOrWhiteSpace(modulePath)) return true;
            var mod = NormalizePath(modulePath);
            if (mod.Length == 0) return true;

            var image = NormalizePath(processImage);
            if (image.Length > 0 && string.Equals(mod, image, StringComparison.Ordinal))
                return true;

            if (IsGpuIcdName(mod)) return true;
            if (BelongsPath(mod)) return true;

            var procDir = DirOf(image);
            var modDir = DirOf(mod);
            bool underApp = procDir.Length > 0 && (modDir.Equals(procDir, StringComparison.Ordinal)
                || modDir.StartsWith(procDir + "\\", StringComparison.Ordinal));

            string file;
            try { file = Path.GetFileName(mod); }
            catch { file = ""; }

            if (underApp)
            {
                if (IsSideloadFileName(file) && !IsMicrosoftFamilySigned(mod))
                    return false;
                if (IsBundledFileName(file)) return true;
                if (IsMicrosoftFamilySigned(mod)) return true;
                // Unsigned random DLL next to Ceprkac.exe is a plant.
                if (procDir.Equals(_selfDir, StringComparison.Ordinal))
                    return false;
                // Child (msedgewebview2) own directory: Edge ships many DLLs.
                return true;
            }

            if (IsUserWritableDrop(mod)) return false;
            if (IsMicrosoftFamilySigned(mod) && IsProgramFiles(mod)) return true;
            return false;
        }

        /// <summary>
        /// Search-order hijack target base names (dbghelp, version, winmm, ...). These are
        /// classic .dll names, but matching is done on the base name + any loadable-module
        /// extension so a plant that maps as a non-.dll module variant is still caught.
        /// </summary>
        private static readonly HashSet<string> SideloadBaseNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "dbghelp", "version", "winmm", "dwrite", "cryptsp", "userenv",
                "profapi", "wtsapi32", "dhcpcsvc", "iphlpapi", "msasn1", "netapi32",
                "samcli", "sspicli", "crypt32", "textshaping", "winhttp", "urlmon",
                "propsys", "dwmapi",
            };

        private static bool IsSideloadFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            if (!IsModuleExtension(fileName)) return false;
            string baseName;
            try { baseName = Path.GetFileNameWithoutExtension(fileName) ?? ""; }
            catch { return false; }
            return baseName.Length > 0 && SideloadBaseNames.Contains(baseName);
        }

        private static bool IsUserWritableDrop(string n)
        {
            if (n.IndexOf("\\temp\\", StringComparison.Ordinal) >= 0) return true;
            if (n.IndexOf("\\downloads\\", StringComparison.Ordinal) >= 0) return true;
            if (n.IndexOf("\\desktop\\", StringComparison.Ordinal) >= 0) return true;
            if (n.IndexOf("\\appdata\\", StringComparison.Ordinal) >= 0)
            {
                if (n.IndexOf("\\microsoft\\edge", StringComparison.Ordinal) >= 0) return false;
                if (n.IndexOf("\\webview2userdata\\", StringComparison.Ordinal) >= 0) return false;
                if (n.IndexOf("\\ebwebview\\", StringComparison.Ordinal) >= 0) return false;
                return true;
            }
            return false;
        }

        private static bool IsProgramFiles(string n) =>
            n.IndexOf("\\program files\\", StringComparison.Ordinal) >= 0
            || n.IndexOf("\\program files (x86)\\", StringComparison.Ordinal) >= 0;

        private static bool IsMicrosoftFamilySigned(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            if (!WinTrust.VerifyFile(path)) return false;
            try
            {
#pragma warning disable SYSLIB0026
                using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0026
                var s = (cert.Subject ?? "") + (cert.Issuer ?? "");
                return ContainsPublisher(s, "Microsoft")
                    || ContainsPublisher(s, "NVIDIA")
                    || ContainsPublisher(s, "Advanced Micro Devices")
                    || ContainsPublisher(s, "Intel")
                    || ContainsPublisher(s, "Google")
                    || ContainsPublisher(s, "Chromium");
            }
            catch { return false; }
        }

        private static bool ContainsPublisher(string subject, string token) =>
            subject.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void DiscoverEdgeRuntimeFolders(Action<string?> add)
        {
            string[] keys =
            {
                @"SOFTWARE\Microsoft\EdgeUpdate\ClientState\" + WebView2ClientGuid,
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\ClientState\" + WebView2ClientGuid,
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + WebView2ClientGuid,
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" + WebView2ClientGuid,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft EdgeWebView",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft EdgeWebView",
            };
            string[] names = { "location", "Location", "InstallLocation", "UninstallString" };
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var key in keys)
                {
                    try
                    {
                        using var k = hive.OpenSubKey(key);
                        if (k == null) continue;
                        foreach (var name in names)
                        {
                            var raw = k.GetValue(name) as string;
                            if (string.IsNullOrWhiteSpace(raw)) continue;
                            var v = raw!;
                            if (name.Equals("UninstallString", StringComparison.OrdinalIgnoreCase))
                            {
                                if (v.StartsWith("\"", StringComparison.Ordinal))
                                {
                                    int end = v.IndexOf('"', 1);
                                    if (end > 1) v = v.Substring(1, end - 1);
                                }
                                else
                                {
                                    int sp = v.IndexOf(" /", StringComparison.Ordinal);
                                    if (sp > 0) v = v.Substring(0, sp);
                                }
                            }
                            add(v);
                        }
                    }
                    catch { }
                }
            }
        }

        private bool BelongsPath(string path)
        {
            var n = NormalizePath(path);
            if (n.Length == 0) return false;
            if (IsTempPath(n)) return false;
            foreach (var pre in _prefixes)
            {
                if (n == pre || n.StartsWith(pre + "\\", StringComparison.Ordinal))
                    return true;
            }
            return IsGpuIcdName(n);
        }

        private static bool IsTempPath(string n) =>
            n.IndexOf("\\temp\\", StringComparison.Ordinal) >= 0
            || n.IndexOf("\\tmp\\", StringComparison.Ordinal) >= 0
            || n.EndsWith("\\temp", StringComparison.Ordinal)
            || n.EndsWith("\\tmp", StringComparison.Ordinal);

        private static readonly string[] GpuIcdPrefixes =
        {
            "nvldumd", "nvwgf2um", "nvd3dum", "nvoglv", "nvapi", "nvopencl", "nvcuda",
            "atidxx", "atio6axx", "amdxc", "amdvlk", "atiadlxx", "atioglxx", "amdocl",
            "igc64", "igc32", "igd10", "igd12", "igdail", "igd9s", "ig4icd", "ig9icd",
            "ig11icd", "ig12icd", "igvk", "intelocl", "igdrcl",
        };

        private static bool IsGpuIcdName(string n)
        {
            string file;
            try { file = Path.GetFileNameWithoutExtension(n); }
            catch { return false; }
            if (string.IsNullOrEmpty(file)) return false;
            file = file.ToLowerInvariant();
            foreach (var p in GpuIcdPrefixes)
                if (file.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        private void Run()
        {
            WaitHandle[] waits = { _stop.WaitHandle, _pulse };
            while (!_stop.IsSet)
            {
                try
                {
                    DrainLdr();
                    SweepSelf();
                    SweepChildren();
                }
                catch { }
                WaitHandle.WaitAny(waits, PollMs);
            }
        }

        private void DrainLdr()
        {
            while (_ldrQueue.TryDequeue(out var item))
            {
                try
                {
                    if (item.Base == IntPtr.Zero) continue;
                    if (IsAllowedModule(_selfImage, item.Path))
                    {
                        _ours.Add(item.Base);
                        continue;
                    }
                    TryUnloadLocal(item.Base);
                }
                catch { }
            }
        }

        private void SweepSelf()
        {
            var proc = GetCurrentProcess();
            var self = GetModuleHandleW(null);
            var mods = EnumModules(proc);
            foreach (var (h, path) in mods)
            {
                if (h == IntPtr.Zero || h == self) continue;
                if (IsAllowedModule(_selfImage, path))
                {
                    _ours.Add(h);
                    continue;
                }
                if (NormalizePath(path).Length == 0) continue;
                TryUnloadLocal(h);
            }
        }

        private void SweepChildren()
        {
            var live = DescendantPids();
            foreach (var dead in new List<int>(_children.Keys))
                if (!live.Contains(dead)) _children.Remove(dead);

            const uint access = 0x0400 | 0x0010 | 0x0002 | 0x0008 | 0x0020 | 0x1000;
            foreach (var pid in live)
            {
                var h = OpenProcess(access, false, (uint)pid);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (!_children.TryGetValue(pid, out var st))
                    {
                        st = new ChildState();
                        _children[pid] = st;
                    }
                    string childImage = QueryImagePath(h);
                    var mods = EnumModules(h);
                    foreach (var (mh, path) in mods)
                    {
                        if (mh == IntPtr.Zero) continue;
                        if (IsAllowedModule(childImage, path))
                        {
                            st.Ours.Add(mh);
                            continue;
                        }
                        if (NormalizePath(path).Length == 0) continue;
                        TryUnloadRemote(h, pid, mh);
                    }
                }
                catch { }
                finally { CloseHandle(h); }
            }
        }

        private string QueryImagePath(IntPtr proc)
        {
            var buf = new char[32768];
            int n = GetModuleFileNameEx(proc, IntPtr.Zero, buf, buf.Length);
            return n > 0 ? new string(buf, 0, n) : "";
        }

        private HashSet<int> DescendantPids()
        {
            int me = Process.GetCurrentProcess().Id;
            var rows = new List<(int pid, int ppid)>();
            var snap = CreateToolhelp32Snapshot(0x00000002, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return new HashSet<int>();
            try
            {
                var pe = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
                if (!Process32FirstW(snap, ref pe)) return new HashSet<int>();
                do { rows.Add(((int)pe.th32ProcessID, (int)pe.th32ParentProcessID)); }
                while (Process32NextW(snap, ref pe));
            }
            finally { CloseHandle(snap); }
            var tree = new HashSet<int> { me };
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var (pid, ppid) in rows)
                    if (tree.Contains(ppid) && tree.Add(pid)) changed = true;
            }
            tree.Remove(me);
            return tree;
        }

        private void TryUnloadLocal(IntPtr hmod)
        {
            if (!_attempted.Add(hmod)) return;
            for (int i = 0; i < 32; i++)
                if (!FreeLibrary(hmod)) break;
        }

        private void TryUnloadRemote(IntPtr proc, int pid, IntPtr hmod)
        {
            if (!_attempted.Add(hmod)) return;
            var k32 = GetModuleHandleW("kernel32.dll");
            var free = GetProcAddress(k32, "FreeLibrary");
            if (free == IntPtr.Zero) return;

            // QueueUserAPC(FreeLibrary, thread, hmod) - same primitive Sentinel uses.
            // CreateRemoteThread is the inject API we are defending against.
            if (QueueFreeLibraryApc(pid, free, hmod))
                return;

            var t = CreateRemoteThread(proc, IntPtr.Zero, UIntPtr.Zero, free, hmod, 0, out _);
            if (t == IntPtr.Zero) return;
            WaitForSingleObject(t, 100);
            CloseHandle(t);
        }

        private static bool QueueFreeLibraryApc(int pid, IntPtr freeLibrary, IntPtr hmod)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                foreach (ProcessThread thread in p.Threads)
                {
                    IntPtr ht = OpenThread(ThreadSetContext, false, (uint)thread.Id);
                    if (ht == IntPtr.Zero) continue;
                    try
                    {
                        if (QueueUserAPC(freeLibrary, ht, hmod) != 0)
                            return true;
                    }
                    finally { CloseHandle(ht); }
                }
            }
            catch { }
            return false;
        }

        private List<(IntPtr, string)> EnumModules(IntPtr proc)
        {
            var result = new List<(IntPtr, string)>();
            var arr = new IntPtr[1024];
            if (!EnumProcessModulesEx(proc, arr, arr.Length * IntPtr.Size, out int needed, 3))
                return result;
            int count = Math.Min(needed / IntPtr.Size, arr.Length);
            var buf = new char[32768];
            for (int i = 0; i < count; i++)
            {
                if (arr[i] == IntPtr.Zero) continue;
                int n = GetModuleFileNameEx(proc, arr[i], buf, buf.Length);
                result.Add((arr[i], n > 0 ? new string(buf, 0, n) : ""));
            }
            return result;
        }

        private string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try
            {
                var p = path!.Trim().Replace('/', '\\');
                if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
                if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = p.Substring(4);
                p = ToDosPath(p);
                if (p.Length >= 2 && p[1] == ':')
                {
                    var sb = new StringBuilder(32768);
                    int n = GetLongPathName(p, sb, sb.Capacity);
                    if (n > 0 && n < sb.Capacity) p = sb.ToString();
                }
                p = Path.GetFullPath(p).TrimEnd('\\');
                return p.ToLowerInvariant();
            }
            catch { return ""; }
        }

        private static string DirOf(string normalizedPath)
        {
            try
            {
                var d = Path.GetDirectoryName(normalizedPath);
                return string.IsNullOrEmpty(d) ? "" : d.TrimEnd('\\');
            }
            catch { return ""; }
        }

        private string ToDosPath(string path)
        {
            if (!path.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
                return path;
            var map = _dosDevices ?? (_dosDevices = BuildDosDeviceMap());
            foreach (var kv in map)
            {
                if (path.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value + path.Substring(kv.Key.Length);
            }
            return path;
        }

        private static Dictionary<string, string> BuildDosDeviceMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var d in Environment.GetLogicalDrives())
                {
                    var letter = d.TrimEnd('\\');
                    var sb = new StringBuilder(260);
                    if (QueryDosDevice(letter, sb, sb.Capacity) != 0)
                    {
                        var device = sb.ToString();
                        if (!string.IsNullOrEmpty(device))
                            map[device] = letter;
                    }
                }
            }
            catch { }
            return map;
        }

        private void RegisterLdr()
        {
            _ldrCb = OnLdr;
            LdrRegisterDllNotification(0, _ldrCb, IntPtr.Zero, out _ldrCookie);
        }

        /// <summary>
        /// Loader-lock safe: copy path + base onto the queue, return.
        /// Do not FreeLibrary, LoadLibrary, or allocate other modules here.
        /// </summary>
        private void OnLdr(uint reason, IntPtr data, IntPtr ctx)
        {
            if (reason != LdrLoaded || data == IntPtr.Zero) return;
            try
            {
                var n = Marshal.PtrToStructure<LdrDllNotificationData>(data);
                if (n.DllBase == IntPtr.Zero || n.FullDllName == IntPtr.Zero) return;
                var us = Marshal.PtrToStructure<UnicodeStr>(n.FullDllName);
                if (us.Buffer == IntPtr.Zero || us.Length < 2) return;
                int chars = us.Length / 2;
                string path = Marshal.PtrToStringUni(us.Buffer, chars) ?? "";
                _ldrQueue.Enqueue((n.DllBase, path));
                _pulse.Set();
            }
            catch { }
        }

        private void UnregisterLdr()
        {
            if (_ldrCookie != IntPtr.Zero)
                LdrUnregisterDllNotification(_ldrCookie);
            _ldrCookie = IntPtr.Zero;
        }

        private delegate void LdrDllNotification(uint reason, IntPtr data, IntPtr ctx);

        [StructLayout(LayoutKind.Sequential)]
        private struct LdrDllNotificationData
        {
            public uint Flags;
            public IntPtr FullDllName;
            public IntPtr BaseDllName;
            public IntPtr DllBase;
            public uint SizeOfImage;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeStr
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint a, bool i, uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? n);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr h, string n);
        [DllImport("kernel32.dll")] private static extern IntPtr CreateRemoteThread(IntPtr p, IntPtr a, UIntPtr s, IntPtr start, IntPtr arg, uint f, out uint tid);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint f, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr s, ref PROCESSENTRY32W pe);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr s, ref PROCESSENTRY32W pe);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetLongPathName(string lpszShortPath, StringBuilder lpszLongPath, int cchBuffer);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint access, bool inherit, uint tid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint QueueUserAPC(IntPtr pfn, IntPtr thread, IntPtr data);
        [DllImport("psapi.dll", SetLastError = true)] private static extern bool EnumProcessModulesEx(IntPtr p, IntPtr[] m, int cb, out int n, uint f);
        [DllImport("psapi.dll", CharSet = CharSet.Unicode)] private static extern int GetModuleFileNameEx(IntPtr p, IntPtr m, [Out] char[] b, int s);
        [DllImport("ntdll.dll")] private static extern uint LdrRegisterDllNotification(uint f, LdrDllNotification cb, IntPtr ctx, out IntPtr cookie);
        [DllImport("ntdll.dll")] private static extern uint LdrUnregisterDllNotification(IntPtr cookie);
    }

    /// <summary>Authenticode check - subject-only is not enough (Kiro used that).</summary>
    internal static class WinTrust
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        public static bool VerifyFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            IntPtr pFile = IntPtr.Zero;
            try
            {
                var fileInfo = new WinTrustFileInfo
                {
                    cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                    pcwszFilePath = path,
                };
                pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
                Marshal.StructureToPtr(fileInfo, pFile, false);

                var data = new WinTrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                    dwUIChoice = 2,
                    fdwRevocationChecks = 0,
                    dwUnionChoice = 1,
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    pFile = pFile,
                    dwStateAction = 1,
                    hWVTStateData = IntPtr.Zero,
                    dwProvFlags = 0x20, // WTD_CACHE_ONLY_URL_RETRIEVAL
                    pwszURLReference = null,
                    pSignatureSettings = IntPtr.Zero,
                };
                var action = GenericVerifyV2;
                uint r = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                data.dwStateAction = 2;
                WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                return r == 0;
            }
            catch { return false; }
            finally
            {
                if (pFile != IntPtr.Zero) Marshal.FreeHGlobal(pFile);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public string? pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);
    }
}
