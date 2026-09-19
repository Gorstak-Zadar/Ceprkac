using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Ceprkac
{
    public partial class MainForm : Form, IMessageFilter
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        private const uint GA_ROOT = 2;
        private const int SW_RESTORE = 9;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;
        private const int WM_DPICHANGED = 0x02E0;

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_CHAR = 0x0102;
        private const int WM_DEADCHAR = 0x0103;
        private const int WM_UNICHAR = 0x0109;

        private readonly ChromeTabStrip tabStrip;
        private readonly Panel navPanel;
        private readonly TableLayoutPanel navLayout;
        private readonly Panel addressWrap;
        private readonly TextBox addressBox;
        private readonly ChromeButton goBtn;
        private readonly ChromeButton backBtn;
        private readonly ChromeButton fwdBtn;
        private readonly ChromeButton refreshBtn;
        private readonly ChromeButton bookmarkBtn;
        private readonly ChromeButton downloadsBtn;
        private readonly ChromeButton menuBtn;
        private readonly ContextMenuStrip menuStrip;
        private readonly ContextMenuStrip downloadsMenu;
        private readonly ToolTip chromeTip;
        private readonly ToolStrip bookmarksBar;
        private float _chromeDpiScale = 1f;
        private int _chromeDpi = 96;
        private Font? _navFont;
        private Font? _navFontLg;
        private Font? _addressFont;
        private Font? _bookmarkFont;
        private Font? _statusFont;
        private readonly Panel webViewPanel;
        private readonly ToolStripStatusLabel statusLabel;
        private readonly StatusStrip statusStrip;

        private readonly string appDataFolder;
        private readonly string bookmarksFile;
        private readonly string historyFile;
        private readonly string passwordsFile;
        private readonly string cardsFile;
        private readonly string addressesFile;
        private readonly string walletFile;
        private readonly string settingsFile;
        private readonly string downloadsFile;
        private readonly string configFile;
        private readonly List<BookmarkNode> bookmarks = new();
        private readonly List<string> history = new();
        private readonly List<SavedCredential> savedPasswords = new();
        private readonly List<SavedCard> savedCards = new();
        private readonly List<SavedAddress> savedAddresses = new();
        private readonly List<WalletProfile> savedProfiles = new();
        private readonly List<string> closedTabs = new();
        private readonly List<DownloadItem> downloads = new();
        private readonly AutoCompleteStringCollection addressSuggest = new();
        // True only while the user is actively editing the omnibox (typed/pasted).
        // Focus alone must NOT block live URL updates - that left the bar stuck on the
        // previous page when FocusOmnibox raced SourceChanged.
        private bool addressUserEditing;
        private string addressCommittedUrl = "";
        private string homePageUrl = "https://www.google.com";
        private string searchUrlTemplate = "https://www.google.com/search?q={0}";
        private CoreWebView2Environment? sharedEnvironment;
        private InjectedModuleCleaner? moduleCleaner;
        private DateTime lastProcessRecover = DateTime.MinValue;
        private readonly List<string> pendingExternalUrls = new();
        private DateTime lastCredentialOfferUi = DateTime.MinValue;
        // Permanently dismissed - user clicked "Type password manually..."
        private readonly HashSet<string> dismissedCredentialHosts = new(StringComparer.OrdinalIgnoreCase);
        // Temporarily suppressed - user closed the picker without picking (maybe by accident).
        // Re-allows after 20 seconds so it can re-appear on next autofill trigger.
        private readonly Dictionary<string, DateTime> recentlyClosedCredentialHosts = new(StringComparer.OrdinalIgnoreCase);
        private ContextMenuStrip? credentialPickerMenu;
        // Timestamp of the last new-tab open - LostFocus is suppressed for 800ms
        // to prevent the GotFocus/LostFocus race from killing addressUserEditing.
        private DateTime _newTabOpenedAt = DateTime.MinValue;

        // Focus-loop breaker. Some article pages (e.g. links out of index.hr to The
        // Guardian etc.) run scripts / iframes that repeatedly reclaim WebView focus.
        // Clicking the omnibox then triggers a GotFocus/LostFocus storm: the page steals
        // focus back, LostFocus re-syncs the bar, the bar re-selects, focus bounces again
        // - the whole window blinks ~100x/sec. We detect rapid LostFocus events and stop
        // reacting to them, and we suppress URL-driven address-bar writes while the
        // omnibox has (or just had) focus so the page's continuous route updates can't
        // repaint the bar underneath the user.
        private DateTime _lastAddressLostFocus = DateTime.MinValue;
        private int _addressLostFocusStreak;
        private bool _addressFocused;
        private DateTime _lastAddressFocusChange = DateTime.MinValue;

        private BrowserTab? ActiveTab => tabStrip.SelectedIndex >= 0 && tabStrip.SelectedIndex < tabStrip.Tabs.Count
            ? tabStrip.Tabs[tabStrip.SelectedIndex] : null;

        public MainForm(IEnumerable<string>? startupUrls = null)
        {
            EnsureModuleCleaner();
            if (startupUrls != null)
            {
                foreach (var u in startupUrls)
                    if (!string.IsNullOrWhiteSpace(u)) pendingExternalUrls.Add(u.Trim());
            }
            Text = "Ceprkac";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(1280, 860);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(600, 400);
            BackColor = Theme.TitleBar;

            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Ceprkac.ico");
                if (File.Exists(iconPath))
                {
                    using var src = new Icon(iconPath);
                    Icon = (Icon)src.Clone();
                }
            }
            catch { }

            appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ceprkac");
            Logger.Init(appDataFolder);
            bookmarksFile = Path.Combine(appDataFolder, "bookmarks.txt");
            historyFile = Path.Combine(appDataFolder, "history.txt");
            passwordsFile = Path.Combine(appDataFolder, "passwords.dat");
            cardsFile = Path.Combine(appDataFolder, "cards.dat");
            addressesFile = Path.Combine(appDataFolder, "addresses.dat");
            walletFile = Path.Combine(appDataFolder, "wallet.dat");
            settingsFile = Path.Combine(appDataFolder, "settings.txt");
            downloadsFile = Path.Combine(appDataFolder, "downloads.json");
            configFile = Path.Combine(appDataFolder, "config.json");

            // Tab strip
            tabStrip = new ChromeTabStrip { Dock = DockStyle.Top };
            tabStrip.TabClicked += (_, i) => SwitchToTab(i);
            tabStrip.TabCloseClicked += (_, i) => CloseTab(i);
            tabStrip.NewTabClicked += (_, _) => AddNewTab(homePageUrl);

            // Nav bar - GBrowser-style HBox: buttons keep their size, address stretches.
            // ToolStrip hosted the omnibox and clipped bookmark/downloads/menu at 4K 175%.
            var darkRenderer = new DarkToolStripRenderer();
            chromeTip = new ToolTip();

            navPanel = new Panel
            {
                Dock = DockStyle.Top,
                BackColor = Theme.Toolbar,
                Padding = new Padding(8, 4, 8, 4),
                Height = 44,
            };
            navLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 1,
                BackColor = Theme.Toolbar,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            navLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            navLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            navLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            navLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            navLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            navLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            navLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            navLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            navLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));

            backBtn = new ChromeButton(ChromeIconKind.Back);
            fwdBtn = new ChromeButton(ChromeIconKind.Forward);
            refreshBtn = new ChromeButton(ChromeIconKind.Reload);
            goBtn = new ChromeButton(ChromeIconKind.Go);
            bookmarkBtn = new ChromeButton(ChromeIconKind.Star);
            downloadsBtn = new ChromeButton(ChromeIconKind.Download);
            menuBtn = new ChromeButton(ChromeIconKind.Menu);
            chromeTip.SetToolTip(backBtn, "Back");
            chromeTip.SetToolTip(fwdBtn, "Forward");
            chromeTip.SetToolTip(refreshBtn, "Reload");
            chromeTip.SetToolTip(goBtn, "Go");
            chromeTip.SetToolTip(bookmarkBtn, "Bookmark (Ctrl+D)");
            chromeTip.SetToolTip(downloadsBtn, "Downloads");
            chromeTip.SetToolTip(menuBtn, "Menu");

            addressBox = new TextBox
            {
                BackColor = Theme.AddressBox,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel),
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                AutoCompleteMode = AutoCompleteMode.None,
                AutoCompleteSource = AutoCompleteSource.CustomSource,
                AutoCompleteCustomSource = addressSuggest,
            };
            // Omnibox: never flip AutoCompleteMode during KeyPress - that recreates the
            // EDIT HWND and eats the first character (especially when SelectAll is on and
            // Text.Length is already > 0 from the current URL). Mark editing on input, and
            // enable Suggest only after the character has landed (TextChanged + BeginInvoke).
            addressBox.KeyPress += (_, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                addressUserEditing = true;
            };
            addressBox.TextChanged += (_, _) =>
            {
                if (!addressBox.Focused) return;
                if (!string.Equals(addressBox.Text, addressCommittedUrl, StringComparison.Ordinal))
                    addressUserEditing = true;
                if (!addressUserEditing) return;
                if (addressBox.Text.Length < 1) return;
                if (addressBox.AutoCompleteMode == AutoCompleteMode.Suggest) return;
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (addressBox.IsDisposed || !addressBox.Focused || !addressUserEditing) return;
                        if (addressBox.Text.Length < 1) return;
                        if (addressBox.AutoCompleteMode != AutoCompleteMode.Suggest)
                            addressBox.AutoCompleteMode = AutoCompleteMode.Suggest;
                    }
                    catch { }
                }));
            };
            addressBox.LostFocus += (_, _) =>
            {
                _addressFocused = false;
                _lastAddressFocusChange = DateTime.Now;
                if (ActiveTab?.FocusOmnibox == true) return;
                // Grace period: suppress for 800ms after a new tab opens so the
                // GotFocus/LostFocus race can't kill addressUserEditing.
                if ((DateTime.Now - _newTabOpenedAt).TotalMilliseconds < 800) return;

                // Focus-loop breaker: if LostFocus keeps firing in quick succession the
                // page is yanking focus back off the omnibox. Re-syncing the bar here just
                // feeds the loop (rewrite text -> reselect -> focus bounces -> LostFocus).
                // Count rapid events and, once we cross the threshold, stop reacting so the
                // blink storm dies out. The streak resets after a quiet gap.
                var now = DateTime.Now;
                if ((now - _lastAddressLostFocus).TotalMilliseconds < 250)
                    _addressLostFocusStreak++;
                else
                    _addressLostFocusStreak = 0;
                _lastAddressLostFocus = now;
                if (_addressLostFocusStreak >= 3)
                    return;

                addressUserEditing = false;
                try
                {
                    if (ActiveTab != null)
                        SyncAddressBarFromTab(ActiveTab, force: true);
                }
                catch { }
            };
            addressBox.GotFocus += (_, _) =>
            {
                _addressFocused = true;
                _lastAddressFocusChange = DateTime.Now;
                _addressLostFocusStreak = 0;
            };
            addressBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    addressUserEditing = false;
                    addressBox.AutoCompleteMode = AutoCompleteMode.None;
                    SetAddressText(addressCommittedUrl, force: true);
                    try { ActiveTab?.WebView.Focus(); } catch { }
                    return;
                }
                if (e.KeyCode != Keys.Enter) return;
                e.Handled = true;
                e.SuppressKeyPress = true;
                addressUserEditing = false;
                addressBox.AutoCompleteMode = AutoCompleteMode.None;
                var navText = addressBox.Text;
                try { File.AppendAllText(Path.Combine(appDataFolder, "addrbar.log"), $"[{DateTime.Now:HH:mm:ss.fff}] ENTER pressed, addressBox.Text=\"{navText}\"\n"); } catch { }
                NavigateCurrentTab(navText);
                var t = ActiveTab;
                if (t != null) t.FocusOmnibox = false;
            };
            addressWrap = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Toolbar,
                Padding = new Padding(6, 4, 6, 4),
                Margin = Padding.Empty,
            };
            addressWrap.Controls.Add(addressBox);

            downloadsMenu = new ContextMenuStrip
            {
                BackColor = Theme.ActiveTab,
                ForeColor = Color.White,
                Renderer = darkRenderer,
                ShowImageMargin = false,
            };
            menuStrip = new ContextMenuStrip
            {
                BackColor = Theme.ActiveTab,
                ForeColor = Color.White,
                Renderer = darkRenderer,
                ShowImageMargin = false,
            };
            menuStrip.Items.Add(new ToolStripMenuItem("New Tab", null, (_, _) => AddNewTab(homePageUrl)) { ShortcutKeyDisplayString = "Ctrl+T", ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Duplicate Tab", null, (_, _) => DuplicateActiveTab()) { ShortcutKeyDisplayString = "Ctrl+Shift+K", ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Reopen Closed Tab", null, (_, _) => RestoreClosedTab()) { ShortcutKeyDisplayString = "Ctrl+Shift+T", ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripSeparator());
            menuStrip.Items.Add(new ToolStripMenuItem("Find in Page...", null, (_, _) => ActiveTab?.WebView.CoreWebView2?.ExecuteScriptAsync("document.execCommand('find')")) { ShortcutKeyDisplayString = "Ctrl+F", ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Zoom In", null, (_, _) => ZoomBy(0.1)) { ShortcutKeyDisplayString = "Ctrl+Plus", ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Zoom Out", null, (_, _) => ZoomBy(-0.1)) { ShortcutKeyDisplayString = "Ctrl+Minus", ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Reset Zoom", null, (_, _) => ZoomReset()) { ShortcutKeyDisplayString = "Ctrl+0", ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripSeparator());
            menuStrip.Items.Add(new ToolStripMenuItem("Add Bookmark", null, (_, _) => AddCurrentPageBookmark()) { ShortcutKeys = Keys.Control | Keys.D, ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Import Bookmarks...", null, (_, _) => ImportBookmarksHtml()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Export Bookmarks...", null, (_, _) => ExportBookmarksHtml()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Clear Bookmarks", null, (_, _) => ClearBookmarks()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripSeparator());
            menuStrip.Items.Add(new ToolStripMenuItem("Clear History", null, (_, _) => ClearHistory()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripSeparator());
            menuStrip.Items.Add(new ToolStripMenuItem("Manage Passwords...", null, (_, _) => ManagePasswords()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Import Passwords (CSV)...", null, (_, _) => ImportPasswordsCsv()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Clear Saved Passwords", null, (_, _) => ClearPasswords()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripSeparator());
            menuStrip.Items.Add(new ToolStripMenuItem("Wallet (Addresses & Payment)...", null, (_, _) => ManageWallet()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Export Passwords && Wallet...", null, (_, _) => ExportAutofillData()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Import Passwords && Wallet...", null, (_, _) => ImportAutofillData()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripSeparator());
            menuStrip.Items.Add(new ToolStripMenuItem("DevTools", null, (_, _) => ActiveTab?.WebView.CoreWebView2?.OpenDevToolsWindow()) { ShortcutKeys = Keys.Control | Keys.I, ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Change Search Engine...", null, (_, _) => { ShowSearchEnginePicker(); }) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripMenuItem("Set as Default Browser...", null, (_, _) => SetAsDefaultBrowser()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });
            menuStrip.Items.Add(new ToolStripSeparator());
            menuStrip.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Close()) { ForeColor = Color.White, BackColor = Theme.ActiveTab });

            backBtn.Click += (_, _) => { var c = ActiveTab?.WebView.CoreWebView2; if (c?.CanGoBack == true) c.GoBack(); };
            fwdBtn.Click += (_, _) => { var c = ActiveTab?.WebView.CoreWebView2; if (c?.CanGoForward == true) c.GoForward(); };
            refreshBtn.Click += (_, _) => ActiveTab?.WebView.CoreWebView2?.Reload();
            goBtn.Click += (_, _) => { addressUserEditing = false; NavigateCurrentTab(addressBox.Text); };
            bookmarkBtn.Click += (_, _) => AddCurrentPageBookmark();
            downloadsBtn.Click += (_, _) =>
            {
                RebuildDownloadsMenu();
                downloadsMenu.Show(downloadsBtn, new Point(0, downloadsBtn.Height));
            };
            menuBtn.Click += (_, _) => menuStrip.Show(menuBtn, new Point(0, menuBtn.Height));

            void HostNav(Control c, int col)
            {
                c.Dock = DockStyle.Fill;
                navLayout.Controls.Add(c, col, 0);
            }
            HostNav(backBtn, 0);
            HostNav(fwdBtn, 1);
            HostNav(refreshBtn, 2);
            HostNav(addressWrap, 3);
            HostNav(goBtn, 4);
            HostNav(bookmarkBtn, 5);
            HostNav(downloadsBtn, 6);
            HostNav(menuBtn, 7);
            navPanel.Controls.Add(navLayout);

            Shown += (_, _) => ApplyChromeDpi();
            HandleCreated += (_, _) => ApplyChromeDpi();
            DpiChanged += MainForm_DpiChanged;

            // Bookmarks bar (ToolStrip for nested folder support)
            bookmarksBar = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                BackColor = Theme.BookmarkBar,
                ForeColor = Color.White,
                Renderer = darkRenderer,
                Padding = new Padding(4, 2, 4, 2),
                AutoSize = false,
                Height = 30,
                Font = new Font("Segoe UI", 8f),
                CanOverflow = true,
                LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            };


            // WebView panel
            webViewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.ActiveTab };

            // Status bar
            statusLabel = new ToolStripStatusLabel("Ready") { ForeColor = Theme.ForeDim };
            statusStrip = new StatusStrip { BackColor = Theme.StatusBar, Renderer = darkRenderer, SizingGrip = false, AutoSize = false, Height = 22 };
            statusStrip.Items.Add(statusLabel);

            // Layout (reverse dock order)
            Controls.Add(webViewPanel);
            Controls.Add(bookmarksBar);
            Controls.Add(navPanel);
            Controls.Add(tabStrip);
            Controls.Add(statusStrip);

            KeyPreview = true;
            KeyDown += MainForm_KeyDown;
            Application.AddMessageFilter(this);
            Load += (_, _) => InitializeAsync();
            FormClosing += (_, _) =>
            {
                Application.RemoveMessageFilter(this);
                SaveWindowState();
                moduleCleaner?.Stop();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int v = 1; DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int)); } catch { }
        }

        // WinForms + WebView2 will otherwise scale chrome on every DPI message (compound or collapse to 96).
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED && IsHandleCreated)
            {
                int proposed = (int)(m.WParam.ToInt64() & 0xFFFF);
                int monitorDpi = ReadMonitorDpi();
                // WebView2 posts WM_DPICHANGED 96 while the window sits on a 175% monitor.
                // Applying that shrinks tabs/toolbar to unusable 96-DPI sizes and eats the buttons.
                if (proposed > 0 && monitorDpi >= 96 && Math.Abs(proposed - monitorDpi) > 12)
                {
                    ApplyChromeDpi();
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.T) { RestoreClosedTab(); e.Handled = true; }
            else if (e.Control && e.Shift && e.KeyCode == Keys.K) { DuplicateActiveTab(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.T) { AddNewTab(homePageUrl); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.W) { if (tabStrip.SelectedIndex >= 0) CloseTab(tabStrip.SelectedIndex); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.L) { FocusAddressBar(selectAll: true); e.Handled = true; }
            else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)) { ZoomBy(0.1); e.Handled = true; }
            else if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)) { ZoomBy(-0.1); e.Handled = true; }
            else if (e.Control && (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0)) { ZoomReset(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape && ActiveTab?.FocusOmnibox == true)
            {
                ActiveTab.FocusOmnibox = false;
                try { ActiveTab.WebView.Focus(); } catch { }
                e.Handled = true;
            }
            else if (e.Control && e.Shift && e.KeyCode == Keys.Tab)
            {
                if (tabStrip.Tabs.Count > 1) SwitchToTab((tabStrip.SelectedIndex - 1 + tabStrip.Tabs.Count) % tabStrip.Tabs.Count);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.Tab)
            {
                if (tabStrip.Tabs.Count > 1) SwitchToTab((tabStrip.SelectedIndex + 1) % tabStrip.Tabs.Count);
                e.Handled = true;
            }
        }

        private async void InitializeAsync()
        {
            try
            {
                Directory.CreateDirectory(appDataFolder);
                LoadSettings();
                if (!File.Exists(settingsFile))
                    ShowSearchEnginePicker();
                LoadBookmarks();
                LoadHistory();
                LoadPasswords();
                LoadCards();
                LoadAddresses();
                LoadWallet();
                LoadDownloads();
                LoadWindowState();
                RefreshBookmarksBar();
                RefreshAddressSuggest();
                // Log suggest list contents for debugging
                try {
                    var suspicious = addressSuggest.Cast<string>().Where(s => s.Contains("blank", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (suspicious.Any())
                        File.AppendAllText(Path.Combine(appDataFolder, "addrbar.log"), $"[STARTUP] Suspicious suggest entries: {string.Join(", ", suspicious)}\n");
                } catch { }

                // Load or download ad blocklist
                await LoadOrUpdateBlocklistAsync();

                var userDataFolder = Path.Combine(appDataFolder, "WebView2UserData");
                Directory.CreateDirectory(userDataFolder);

                if (!await EnsureWebView2RuntimeAsync())
                    return;

                try
                {
                    var envOpts = new CoreWebView2EnvironmentOptions(
                        "--no-first-run --disable-background-networking --disable-features=msSmartScreenProtection");
                    sharedEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOpts);
                }
                catch (Exception createEx)
                {
                    statusLabel.Text = "WebView2 runtime missing or broken - repairing...";
                    Refresh();
                    if (await InstallWebView2RuntimeAsync() && !AlreadyRestartedForWebView2)
                    {
                        RestartApp("--after-webview2");
                        return;
                    }
                    throw new Exception(createEx.Message, createEx);
                }
                if (pendingExternalUrls.Count > 0)
                {
                    var urls = pendingExternalUrls.ToArray();
                    pendingExternalUrls.Clear();
                    foreach (var u in urls) AddNewTab(u, focusOmnibox: false);
                }
                else
                    AddNewTab(homePageUrl);
                // WebView2 can post a fake 96-DPI message during init - re-assert chrome after the first tab.
                BeginInvoke(new Action(ApplyChromeDpi));
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Failed to initialize WebView2.";
                MessageBox.Show(this, $"WebView2 initialization failed:\r\n{ex.Message}\r\n\r\n{ex.StackTrace}",
                    "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void EnableTls12()
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |=
                    System.Net.SecurityProtocolType.Tls12 | (System.Net.SecurityProtocolType)3072;
            }
            catch { }
        }

        private const string WebView2ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

        private static bool IsWebView2InRegistry()
        {
            string[] keys =
            {
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + WebView2ClientGuid,
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" + WebView2ClientGuid,
            };
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var key in keys)
                {
                    try
                    {
                        using var k = hive.OpenSubKey(key);
                        var pv = k?.GetValue("pv") as string;
                        if (!string.IsNullOrEmpty(pv) && pv != "0.0.0.0") return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        private static bool IsWebView2RuntimeInstalled()
        {
            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (!string.IsNullOrEmpty(version)) return true;
            }
            catch { }
            return IsWebView2InRegistry();
        }

        private static bool AlreadyRestartedForWebView2 =>
            Environment.GetCommandLineArgs().Any(a =>
                string.Equals(a, "--after-webview2", StringComparison.OrdinalIgnoreCase));

        private async Task<bool> EnsureWebView2RuntimeAsync()
        {
            bool apiOk = false;
            try { apiOk = !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); } catch { }

            if (apiOk) return true;

            // Installed for this machine but this process cannot see it yet (bitness / loader).
            if (IsWebView2InRegistry())
            {
                if (!AlreadyRestartedForWebView2)
                {
                    RestartApp("--after-webview2");
                    return false;
                }
                return true;
            }

            statusLabel.Text = "WebView2 not found - downloading runtime from Microsoft...";
            Refresh();
            if (!await InstallWebView2RuntimeAsync())
            {
                var retry = MessageBox.Show(this,
                    "The Edge WebView2 runtime is required and could not be installed automatically.\r\n\r\nRetry download?",
                    "WebView2 Required", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);
                if (retry == DialogResult.Retry)
                    return await EnsureWebView2RuntimeAsync();
                statusLabel.Text = "WebView2 runtime is required.";
                return false;
            }

            if (!AlreadyRestartedForWebView2)
            {
                RestartApp("--after-webview2");
                return false;
            }
            return IsWebView2RuntimeInstalled();
        }

        private async Task<bool> InstallWebView2RuntimeAsync()
        {
            EnableTls12();
            var bootstrapperPath = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");
            try
            {
                statusLabel.Text = "Downloading WebView2 runtime...";
                Refresh();
                byte[]? bytes = null;
                try
                {
                    using (var http = new HttpClient())
                    {
                        http.Timeout = TimeSpan.FromMinutes(5);
                        bytes = await http.GetByteArrayAsync(
                            "https://go.microsoft.com/fwlink/p/?LinkId=2124703");
                    }
                }
                catch
                {
                    using (var wc = new System.Net.WebClient())
                        bytes = await wc.DownloadDataTaskAsync(
                            "https://go.microsoft.com/fwlink/p/?LinkId=2124703");
                }
                if (bytes == null || bytes.Length < 10000) return false;
                File.WriteAllBytes(bootstrapperPath, bytes);

                statusLabel.Text = "Installing WebView2 runtime...";
                Refresh();
                await RunWebView2Setup(bootstrapperPath, "/silent /install", false);
                if (!IsWebView2InRegistry() && !IsWebView2RuntimeInstalled())
                    await RunWebView2Setup(bootstrapperPath, "/install", true);

                for (int i = 0; i < 20 && !IsWebView2InRegistry() && !IsWebView2RuntimeInstalled(); i++)
                {
                    statusLabel.Text = "Waiting for WebView2 runtime...";
                    await Task.Delay(500);
                }
                return IsWebView2InRegistry() || IsWebView2RuntimeInstalled();
            }
            catch
            {
                return false;
            }
            finally
            {
                try { File.Delete(bootstrapperPath); } catch { }
            }
        }

        private static async Task<bool> RunWebView2Setup(string path, string args, bool elevate)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = args,
                    UseShellExecute = elevate,
                    CreateNoWindow = !elevate,
                };
                if (elevate) psi.Verb = "runas";
                var proc = Process.Start(psi);
                if (proc == null) return false;
                await Task.Run(() => proc.WaitForExit());
                return IsWebView2RuntimeInstalled();
            }
            catch
            {
                return false;
            }
        }

        private void RestartApp(string extraArg = "")
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = extraArg ?? "",
                    UseShellExecute = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                });
            }
            catch { }
            BeginInvoke(new Action(Close));
        }

        private void EnsureModuleCleaner()
        {
            if (moduleCleaner != null) return;
            moduleCleaner = InjectedModuleCleaner.Instance ?? InjectedModuleCleaner.StartGlobal();
        }

        private void MainForm_DpiChanged(object? sender, DpiChangedEventArgs e)
        {
            int proposed = e.DeviceDpiNew;
            int monitorDpi = ReadMonitorDpi();
            if (proposed > 0 && monitorDpi >= 96 && Math.Abs(proposed - monitorDpi) > 12)
            {
                ApplyChromeDpi();
                return;
            }
            Bounds = new Rectangle(
                e.SuggestedRectangle.Left, e.SuggestedRectangle.Top,
                e.SuggestedRectangle.Width, e.SuggestedRectangle.Height);
            ApplyChromeDpi();
        }

        /// <summary>Monitor effective DPI. WebView2's window DPI is often 96 even on a 175% display.</summary>
        private int ReadMonitorDpi()
        {
            try
            {
                if (IsHandleCreated)
                {
                    var mon = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
                    if (mon != IntPtr.Zero && GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint x, out _) == 0 && x >= 96)
                        return (int)Math.Min(x, 384);
                }
            }
            catch { }
            try
            {
                if (IsHandleCreated)
                {
                    uint w = GetDpiForWindow(Handle);
                    if (w >= 96 && w <= 384) return (int)w;
                }
            }
            catch { }
            try { if (DeviceDpi >= 96) return DeviceDpi; } catch { }
            return 96;
        }

        private int Dip(int v) => Math.Max(1, (int)Math.Round(v * _chromeDpiScale));

        private static Font UiPx(float px96, float scale)
            => new Font("Segoe UI", Math.Max(8f, px96 * scale), FontStyle.Regular, GraphicsUnit.Pixel);

        private void ApplyChromeDpi()
        {
            if (IsDisposed || !IsHandleCreated) return;
            int dpi = ReadMonitorDpi();
            if (dpi < 96) dpi = 96;
            _chromeDpi = dpi;
            _chromeDpiScale = dpi / 96f;

            MinimumSize = new Size(Dip(600), Dip(400));
            tabStrip.ApplyDpiScale(_chromeDpiScale);

            int btn = Dip(36);
            int navH = Dip(44);
            navPanel.Padding = new Padding(Dip(8), Dip(4), Dip(8), Dip(4));
            navPanel.MinimumSize = new Size(0, navH);
            navPanel.Height = navH;

            navLayout.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, btn);
            navLayout.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, btn);
            navLayout.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, btn);
            navLayout.ColumnStyles[3] = new ColumnStyle(SizeType.Percent, 100f);
            navLayout.ColumnStyles[4] = new ColumnStyle(SizeType.Absolute, btn);
            navLayout.ColumnStyles[5] = new ColumnStyle(SizeType.Absolute, btn);
            navLayout.ColumnStyles[6] = new ColumnStyle(SizeType.Absolute, btn);
            navLayout.ColumnStyles[7] = new ColumnStyle(SizeType.Absolute, btn);

            var oldNav = _navFont;
            var oldNavLg = _navFontLg;
            var oldAddr = _addressFont;
            var oldBm = _bookmarkFont;
            var oldSt = _statusFont;
            _navFont = UiPx(16f, _chromeDpiScale);
            _navFontLg = UiPx(18f, _chromeDpiScale);
            _addressFont = UiPx(14f, _chromeDpiScale);
            _bookmarkFont = UiPx(12f, _chromeDpiScale);
            _statusFont = UiPx(11f, _chromeDpiScale);

            foreach (var b in new[] { backBtn, fwdBtn, goBtn, bookmarkBtn, downloadsBtn })
            {
                b.Font = _navFont;
                b.MinimumSize = new Size(btn, Dip(28));
            }
            refreshBtn.Font = _navFontLg;
            refreshBtn.MinimumSize = new Size(btn, Dip(28));
            menuBtn.Font = _navFontLg;
            menuBtn.MinimumSize = new Size(btn, Dip(28));
            addressBox.Font = _addressFont;
            addressWrap.Padding = new Padding(Dip(6), Dip(4), Dip(6), Dip(4));

            bookmarksBar.ImageScalingSize = new Size(Dip(16), Dip(16));
            bookmarksBar.Padding = new Padding(Dip(4), Dip(2), Dip(4), Dip(2));
            bookmarksBar.Font = _bookmarkFont;
            bookmarksBar.Height = Dip(30);
            bookmarksBar.MinimumSize = new Size(0, Dip(28));
            foreach (ToolStripItem item in bookmarksBar.Items)
                item.Font = _bookmarkFont;
            menuStrip.Font = _bookmarkFont;
            downloadsMenu.Font = _bookmarkFont;

            statusStrip.AutoSize = false;
            statusStrip.Height = Dip(22);
            statusLabel.Font = _statusFont;

            navLayout.PerformLayout();
            Invalidate(true);
            DisposeFont(oldNav);
            DisposeFont(oldNavLg);
            DisposeFont(oldAddr);
            DisposeFont(oldBm);
            DisposeFont(oldSt);
        }

        private static void DisposeFont(Font? f)
        {
            if (f == null) return;
            try { f.Dispose(); } catch { }
        }

        private void SetAddressText(string? url, bool force = false)
        {
            url = url ?? "";
            addressCommittedUrl = url;
            // Never overwrite text the user is actively editing.
            if (addressUserEditing) return;
            // Don't let page-driven updates (SourceChanged/HistoryChanged from scripts,
            // ads, or redirect chains) rewrite the bar while the user has the omnibox
            // focused or just clicked into it. That rewrite reselects the text and, on
            // focus-stealing pages, feeds the blink loop. User-initiated navigation
            // passes force:true and is allowed through.
            if (!force && (_addressFocused
                || (DateTime.Now - _lastAddressFocusChange).TotalMilliseconds < 400))
                return;
            if (addressBox.Text == url) return;
            // Lightweight persistent log - caller name only, no stack trace.
            try
            {
                var caller = new System.Diagnostics.StackFrame(1, false).GetMethod()?.Name ?? "?";
                File.AppendAllText(
                    Path.Combine(appDataFolder, "addrbar.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] \"{url}\" <- {caller}\n");
            }
            catch { }
            addressBox.AutoCompleteMode = AutoCompleteMode.None;
            addressBox.Text = url;
            addressBox.SelectionStart = addressBox.Text.Length;
            addressBox.SelectionLength = 0;
        }

        private void SyncAddressBarFromTab(BrowserTab tab, bool force = false)
        {
            string url = "";
            try { url = tab.WebView.CoreWebView2?.Source ?? tab.Url ?? ""; }
            catch { url = tab.Url ?? ""; }
            // Don't show about:blank in the address bar - treat it as empty
            if (url == "about:blank") url = "";
            if (!string.IsNullOrEmpty(url)) tab.Url = url;
            if (ActiveTab != tab) return;
            SetAddressText(url, force: force);
        }

        // The address bar is a normal WinForms TextBox and handles its own input.
        // When a new tab opens we set addressUserEditing=true and clear the box.
        // The WebView2 control grabs OS focus during initialisation regardless of
        // WinForms Focus() calls. We intercept WM_CHAR here so any printable keystroke
        // typed immediately after opening a tab goes to the address bar, not the page.
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_CHAR && addressUserEditing && !addressBox.IsDisposed && !addressBox.Focused)
            {
                char ch = (char)m.WParam.ToInt32();
                if (ch >= 0x20) // printable
                {
                    addressBox.Focus();
                    // Let the char land in the now-focused address box normally.
                }
            }
            return false;
        }

        // Focus the address bar and select its contents so the first keystroke
        // replaces the pre-filled URL (standard browser omnibox behavior). Typing
        // is handled natively by the TextBox - no manual character routing.
        private void FocusAddressBar(bool selectAll = true)
        {
            if (addressBox.IsDisposed) return;
            // Do NOT reset addressUserEditing here - callers that want to clear
            // it do so themselves. Resetting it here lets SyncAddressBarFromTab
            // overwrite the box while focus events are still settling.
            addressBox.Focus();
            if (selectAll) addressBox.SelectAll();
            else
            {
                addressBox.SelectionLength = 0;
                addressBox.SelectionStart = addressBox.Text.Length;
            }
        }

        private void DuplicateActiveTab()
        {
            var tab = ActiveTab;
            if (tab == null) return;
            var url = tab.WebView.CoreWebView2?.Source ?? tab.Url;
            if (string.IsNullOrWhiteSpace(url)) url = homePageUrl;
            AddNewTab(url, focusOmnibox: false);
        }

        public void RestoreAndFocus()
        {
            if (IsDisposed) return;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
            try
            {
                ShowWindow(Handle, SW_RESTORE);
                SetForegroundWindow(Handle);
            }
            catch { }
        }

        public void OpenExternalUrl(string url)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OpenExternalUrl(url)));
                return;
            }
            RestoreAndFocus();
            if (string.IsNullOrWhiteSpace(url)) return;
            if (sharedEnvironment == null)
            {
                pendingExternalUrls.Add(url);
                return;
            }
            AddNewTab(url, focusOmnibox: false);
        }

        private void SetAsDefaultBrowser()
        {
            try
            {
                BrowserRegistration.RegisterAndRequestDefault();
                statusLabel.Text = BrowserRegistration.IsDefault()
                    ? "Ceprkac is the default browser."
                    : "Pick Ceprkac under http / https in Windows Settings.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not register as default browser:\r\n" + ex.Message,
                    "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void AddNewTab(string url, int? insertAfter = null, bool focusOmnibox = true)
        {
            if (sharedEnvironment == null) return;
            // When opening a tab for the user to search (focusOmnibox=true), navigate
            // to about:blank instead of the home page. The WebView has nothing to render
            // so it doesn't steal focus. home page loads if user clicks the page without
            // typing, or if user presses Enter with empty bar in NavigateCurrentTab.
            string navUrl = (focusOmnibox && url == homePageUrl) ? "about:blank" : url;
            var webView = new WebView2
            {
                Dock = DockStyle.Fill,
                Visible = true,
                TabStop = false,
                DefaultBackgroundColor = Theme.ActiveTab,
            };
            var tab = new BrowserTab { Url = navUrl, WebView = webView, FocusOmnibox = focusOmnibox };

            int insertIndex = insertAfter.HasValue ? insertAfter.Value + 1
                : tabStrip.SelectedIndex >= 0 ? tabStrip.SelectedIndex + 1
                : tabStrip.Tabs.Count;

            tabStrip.Tabs.Insert(insertIndex, tab);
            webViewPanel.Controls.Add(webView);
            _ = webView.Handle;
            webView.GotFocus += (_, _) =>
            {
                // Clear the focus guard when WebView gets focus.
                tab.FocusOmnibox = false;
            };
            webView.MouseDown += (_, _) =>
            {
                // User explicitly clicked the page on a new empty tab - load home page.
                if (addressBox.Text.Length == 0 && !string.IsNullOrEmpty(homePageUrl)
                    && (tab.WebView.CoreWebView2?.Source ?? "") == "about:blank")
                    NavigateTab(tab, homePageUrl);
            };

            // Set editing flag BEFORE SwitchToTab so SyncAddressBarFromTab
            // skips overwriting the address bar with the home URL.
            // tab.Url stays as-is (homePageUrl) so NavigateTab fires correctly.
            if (focusOmnibox) addressUserEditing = true;
            if (focusOmnibox) _newTabOpenedAt = DateTime.Now;

            SwitchToTab(insertIndex);
            if (focusOmnibox)
            {
                addressBox.AutoCompleteMode = AutoCompleteMode.None;
                addressBox.Text = "";
                addressCommittedUrl = "";
                addressBox.Focus();
                addressBox.SelectAll();
            }

            try
            {
                await webView.EnsureCoreWebView2Async(sharedEnvironment);
                var core = webView.CoreWebView2;
                if (core != null)
                {
                    // Use WebView2's native autofill, password save, accelerator keys
                    // and status bar - explicit so the intent is clear and --disable-sync
                    // removal doesn't silently change behaviour.
                    core.Settings.IsGeneralAutofillEnabled = true;
                    core.Settings.IsPasswordAutosaveEnabled = true;
                    core.Settings.AreBrowserAcceleratorKeysEnabled = true;
                    core.Settings.IsStatusBarEnabled = true;
                    core.NavigationStarting += (_, navA) => { Logger.Log("NAV", $"Navigating: {navA.Uri}"); tab.IsLoading = true; tab.LoadProgress = 10; if (ActiveTab == tab) statusLabel.Text = "Loading..."; tabStrip.Invalidate(); };
                    core.NavigationCompleted += (_, e) =>
                    {
                        tab.IsLoading = false;
                        tab.LoadProgress = 100;
                        UpdateTabState(tab);
                        tabStrip.Invalidate();
                        if (!e.IsSuccess)
                        {
                            if (ActiveTab == tab && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                                statusLabel.Text = "Page failed to load (" + e.WebErrorStatus + ")";
                            return;
                        }
                        TryAutoFillCredentials(tab);
                        TryAutoFillWallet(tab);
                        InjectAdElementHider(tab);
                    };
                    core.DocumentTitleChanged += (_, _) => { tab.Title = core.DocumentTitle ?? "New Tab"; if (ActiveTab == tab) Text = tab.Title + " - Ceprkac"; tabStrip.Invalidate(); };
                    WireAddressAndAutofillEvents(tab, core);
                    core.NewWindowRequested += (_, args) =>
                    {
                        var uri = (args.Uri ?? "").ToLower();
                        if (IsAdUrl(uri))
                        {
                            args.Handled = true;
                            adsBlockedCount++;
                            return;
                        }
                        // Open window.open in a real tab and keep window.opener (GBrowser behaviour).
                        args.Handled = true;
                        var deferral = args.GetDeferral();
                        int idx = tabStrip.Tabs.IndexOf(tab);
                        BeginInvoke(async () =>
                        {
                            try
                            {
                                var child = await CreateTabForNewWindow(idx >= 0 ? idx : (int?)null);
                                if (child?.CoreWebView2 != null)
                                    args.NewWindow = child.CoreWebView2;
                            }
                            finally { deferral.Complete(); }
                        });
                    };
                    WireSharedCoreEvents(tab, core);
                    core.ProcessFailed += (_, e) =>
                    {
                        try
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if ((DateTime.UtcNow - lastProcessRecover).TotalSeconds < 3) return;
                                lastProcessRecover = DateTime.UtcNow;
                                statusLabel.Text = "Page process recovered - reloading...";
                                try { if (tab.WebView.CoreWebView2 != null) tab.WebView.Reload(); } catch { }
                            }));
                        }
                        catch { }
                    };
                    _ = core.AddScriptToExecuteOnDocumentCreatedAsync(AutofillAssistJs);
                    _ = core.AddScriptToExecuteOnDocumentCreatedAsync(ContextCaptureJs);
                    _ = core.AddScriptToExecuteOnDocumentCreatedAsync(FedCmSuppressJs);
                    _ = core.AddScriptToExecuteOnDocumentCreatedAsync(ExternalSchemeBridgeJs);

                    // Block navigations to ad domains - cancel and auto-close empty tabs
                    core.NavigationStarting += (_, navArgs) =>
                    {
                        var navUri = (navArgs.Uri ?? "").ToLower();
                        if (IsAdUrl(navUri))
                        {
                            navArgs.Cancel = true;
                            adsBlockedCount++;
                            // If this tab has no real content (was just opened for the ad), close it
                            var tabUrl = (tab.Url ?? "").ToLower();
                            bool isEmptyTab = string.IsNullOrEmpty(tabUrl) || tabUrl == "about:blank" ||
                                tabUrl.StartsWith("data:") || IsAdUrl(tabUrl);
                            if (isEmptyTab && tabStrip.Tabs.Count > 1)
                            {
                                _ = Task.Delay(100).ContinueWith(_ =>
                                {
                                    try { Invoke(() => { int ti = tabStrip.Tabs.IndexOf(tab); if (ti >= 0) CloseTab(ti); }); } catch { }
                                });
                            }
                            else
                            {
                                // Tab has real content - just go back
                                if (core.CanGoBack) core.GoBack();
                            }
                        }
                    };

                    // Handle window.close() from auth flows - close the tab
                    core.WindowCloseRequested += (_, _) =>
                    {
                        int tabIdx = tabStrip.Tabs.IndexOf(tab);
                        if (tabIdx >= 0) CloseTab(tabIdx);
                    };

                    // Auto-close tabs that show "close this window" auth completion messages
                    core.NavigationCompleted += (s2, e2) =>
                    {
                        var src = core.Source ?? "";
                        if (src.Contains("/callback") && (src.Contains("oauth") || src.Contains("auth")))
                        {
                            // Auth callback page - auto-close after a short delay
                            _ = Task.Delay(1500).ContinueWith(_ =>
                            {
                                try { Invoke(() => { int ti = tabStrip.Tabs.IndexOf(tab); if (ti >= 0) CloseTab(ti); }); } catch { }
                            });
                        }
                    };

                    // Ad blocker - network-level request blocking. Awaited so the YouTube
                    // main-world JSON stripper is registered before this tab navigates.
                    await SetupAdBlocker(core);
                }
                if (!string.IsNullOrWhiteSpace(navUrl)) NavigateTab(tab, navUrl);
                if (tab.FocusOmnibox) FocusAddressBar(selectAll: true);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Tab creation failed.";
                MessageBox.Show(this, $"Failed to create tab:\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SwitchToTab(int index)
        {
            if (index < 0 || index >= tabStrip.Tabs.Count) return;
            if (tabStrip.SelectedIndex >= 0 && tabStrip.SelectedIndex < tabStrip.Tabs.Count && tabStrip.SelectedIndex != index)
            {
                var prev = tabStrip.Tabs[tabStrip.SelectedIndex];
                prev.FocusOmnibox = false;
                prev.WebView.Visible = false;
            }
            tabStrip.SelectedIndex = index;
            var tab = tabStrip.Tabs[index];
            tab.WebView.Visible = true;
            tab.WebView.BringToFront();
            try { tab.WebView.ZoomFactor = tab.ZoomFactor; } catch { }
            SyncAddressBarFromTab(tab, force: !addressUserEditing);
            Text = tab.Title + " - Ceprkac";
            UpdateTabState(tab);
            tabStrip.Invalidate();
            if (tab.FocusOmnibox) FocusAddressBar(selectAll: true);
            else tab.WebView.Focus();
        }

        private async void OpenOAuthPopup(string url, BrowserTab parentTab)
        {
            if (sharedEnvironment == null) return;

            var popup = new Form
            {
                Text = "Sign In",
                ClientSize = new Size(500, 650),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Theme.TitleBar,
                MinimizeBox = false,
                MaximizeBox = false,
            };

            // Dark title bar
            try
            {
                int v = 1;
                DwmSetWindowAttribute(popup.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
            }
            catch { }

            var popupWebView = new WebView2 { Dock = DockStyle.Fill };
            popup.Controls.Add(popupWebView);

            try
            {
                // Use a separate environment for OAuth popups - no ad blocking scripts
                var popupUserData = Path.Combine(appDataFolder, "WebView2OAuthData");
                Directory.CreateDirectory(popupUserData);
                var popupEnv = await CoreWebView2Environment.CreateAsync(null, popupUserData);
                await popupWebView.EnsureCoreWebView2Async(popupEnv);
                var popupCore = popupWebView.CoreWebView2;
                if (popupCore == null) { popup.Dispose(); return; }
                Logger.Log("POPUP", $"OAuth/sign-in popup opened for {url} (parent {parentTab.Url}).");

                // No ad blocker on OAuth popups - auth providers get blocked otherwise
                // But DO enable the password features so a Google/OAuth sign-in popup on a
                // never-visited site can still offer/fill saved passwords. The context menu
                // handler lets the user right-click -> Fill password in the popup; the
                // capture script lets Lens/target detection work there too.
                try
                {
                    popupCore.Settings.IsGeneralAutofillEnabled = true;
                    popupCore.Settings.IsPasswordAutosaveEnabled = true;
                    popupCore.ContextMenuRequested += Core_ContextMenuRequested;
                    _ = popupCore.AddScriptToExecuteOnDocumentCreatedAsync(ContextCaptureJs);
                    _ = popupCore.AddScriptToExecuteOnDocumentCreatedAsync(ExternalSchemeBridgeJs);
                    _ = popupCore.AddScriptToExecuteOnDocumentCreatedAsync(AutofillAssistJs);
                    popupCore.WebMessageReceived += (_, args) => OnWebMessage(parentTab, args);
                }
                catch (Exception ex) { Logger.Error("POPUP", "wire password features", ex); }

                // Auto-close when the OAuth flow completes (redirects back to the original site)
                string? parentDomain = null;
                try { parentDomain = new Uri(parentTab.Url).Host.ToLower(); } catch { }

                popupCore.NavigationStarting += (_, navArgs) =>
                {
                    try
                    {
                        var navHost = new Uri(navArgs.Uri).Host.ToLower();
                        // If navigating back to the parent site, the auth is done
                        if (parentDomain != null && navHost.Contains(parentDomain))
                        {
                            popup.BeginInvoke(() =>
                            {
                                popup.Close();
                                // Refresh the parent tab to pick up the auth
                                parentTab.WebView.CoreWebView2?.Reload();
                            });
                        }
                    }
                    catch { }
                };

                // Also auto-close if the popup tries to close itself (window.close())
                popupCore.WindowCloseRequested += (_, _) =>
                {
                    popup.BeginInvoke(() => popup.Close());
                };

                // Update popup title
                popupCore.DocumentTitleChanged += (_, _) =>
                {
                    popup.BeginInvoke(() => popup.Text = popupCore.DocumentTitle ?? "Sign In");
                };

                popupCore.Navigate(url);
                popup.ShowDialog(this);
            }
            catch { }
            finally
            {
                popupWebView.Dispose();
                popup.Dispose();
            }
        }

        private void CloseTab(int index)
        {
            if (index < 0 || index >= tabStrip.Tabs.Count) return;
            if (tabStrip.Tabs.Count == 1) { NavigateTab(tabStrip.Tabs[0], homePageUrl); return; }
            var tab = tabStrip.Tabs[index];
            if (!string.IsNullOrWhiteSpace(tab.Url) && tab.Url != "about:blank")
            {
                closedTabs.Add(tab.Url);
                if (closedTabs.Count > 20) closedTabs.RemoveRange(0, closedTabs.Count - 20);
            }
            tab.WebView.Visible = false;
            webViewPanel.Controls.Remove(tab.WebView);
            tab.WebView.Dispose();
            tabStrip.Tabs.RemoveAt(index);
            SwitchToTab(Math.Min(index, tabStrip.Tabs.Count - 1));
        }

        private async Task<WebView2?> CreateTabForNewWindow(int? insertAfter)
        {
            if (sharedEnvironment == null) return null;
            var webView = new WebView2 { Dock = DockStyle.Fill, Visible = true, TabStop = false, DefaultBackgroundColor = Theme.ActiveTab };
            var tab = new BrowserTab { Url = "", WebView = webView, IsPopup = true };
            int insertIndex = insertAfter.HasValue ? insertAfter.Value + 1 : tabStrip.Tabs.Count;
            tabStrip.Tabs.Insert(insertIndex, tab);
            webViewPanel.Controls.Add(webView);
            webView.BringToFront();
            _ = webView.Handle;
            await webView.EnsureCoreWebView2Async(sharedEnvironment);
            var core = webView.CoreWebView2;
            if (core != null)
            {
                core.Settings.IsGeneralAutofillEnabled = true;
                core.Settings.IsPasswordAutosaveEnabled = true;
                core.Settings.AreBrowserAcceleratorKeysEnabled = true;
                core.Settings.IsStatusBarEnabled = true;
                core.NavigationStarting += (_, _) => { tab.IsLoading = true; tabStrip.Invalidate(); };
                core.NavigationCompleted += (_, e) =>
                {
                    tab.IsLoading = false;
                    UpdateTabState(tab);
                    tabStrip.Invalidate();
                    // New-window tabs (a video opened from a search-engine result via
                    // target=_blank/window.open) need the DOM + player ad scrubber too. Without
                    // this the new window relied solely on the main-world CDP script; if that
                    // missed the first document, ads played until a manual refresh. YouTubeAdBlockerJs
                    // hides ad elements and fast-forwards playing video ads as a reactive net.
                    InjectAdElementHider(tab);
                    if (e.IsSuccess)
                    {
                        TryAutoFillCredentials(tab);
                        TryAutoFillWallet(tab);
                    }
                };
                core.DocumentTitleChanged += (_, _) => { tab.Title = core.DocumentTitle ?? "New Tab"; if (ActiveTab == tab) Text = tab.Title + " - Ceprkac"; tabStrip.Invalidate(); };
                WireAddressAndAutofillEvents(tab, core);
                core.WindowCloseRequested += (_, _) => { int ti = tabStrip.Tabs.IndexOf(tab); if (ti >= 0) CloseTab(ti); };
                WireSharedCoreEvents(tab, core);
                // Awaited so the YouTube main-world JSON stripper is registered BEFORE the
                // opener starts navigating this new window. When a YouTube video is opened via
                // window.open / target=_blank (e.g. from a search-engine result), the parent
                // begins navigation as soon as args.NewWindow is assigned - which is right after
                // this method returns. Registering the CDP script first is what removes the
                // "ads until refresh" symptom on that path.
                await SetupAdBlocker(core);
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(AutofillAssistJs);
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(ContextCaptureJs);
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(FedCmSuppressJs);
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(ExternalSchemeBridgeJs);
            }
            SwitchToTab(insertIndex);
            return webView;
        }

        private void WireSharedCoreEvents(BrowserTab tab, CoreWebView2 core)
        {
            core.DownloadStarting += Core_DownloadStarting;
            core.ContextMenuRequested += Core_ContextMenuRequested;
            core.PermissionRequested += Core_PermissionRequested;
            core.LaunchingExternalUriScheme += Core_LaunchingExternalUriScheme;
            core.WebMessageReceived += (_, args) => OnWebMessage(tab, args);
            core.StatusBarTextChanged += (_, _) =>
            {
                try
                {
                    if (ActiveTab != tab) return;
                    var hover = core.StatusBarText ?? "";
                    if (!string.IsNullOrWhiteSpace(hover))
                        statusLabel.Text = hover;
                    else
                        statusLabel.Text = $"Ready | Ads blocked: {adsBlockedCount} | Domains: {BlockedAdDomains.Count}";
                }
                catch { }
            };
        }

        private void WireAddressAndAutofillEvents(BrowserTab tab, CoreWebView2 core)
        {
            core.SourceChanged += (_, _) =>
            {
                var src = core.Source ?? "";
                // Ignore about:blank - it's the transient state of a new tab
                // before real navigation. Don't update tab.Url or the address bar.
                if (src == "about:blank") return;
                tab.Url = src;
                // Always reflect the page the user is viewing - not the referrer / prior URL.
                // Skip only while the user is mid-edit in the omnibox.
                SyncAddressBarFromTab(tab);
                // SPA / client-side route changes (e.g. Google's identifier -> password
                // step) often do NOT raise NavigationCompleted. Retry autofill here too -
                // BUT only once per distinct URL. Autofill dispatches input/change events
                // when it fills a field; on some SPAs that pushes a new history entry,
                // which re-raises SourceChanged. Without this guard that formed a
                // self-feeding loop that fired autofill (and SetAddressText) continuously,
                // flickering the address bar many times a second. Re-triggering only when
                // the URL actually changed since the last SourceChanged-driven attempt
                // keeps the identifier -> password step working without the loop.
                if (!string.Equals(tab.LastSourceAutoFillUrl, tab.Url, StringComparison.OrdinalIgnoreCase))
                {
                    tab.LastSourceAutoFillUrl = tab.Url;
                    TryAutoFillCredentials(tab);
                    TryAutoFillWallet(tab);
                }
            };
            core.HistoryChanged += (_, _) =>
            {
                var src = core.Source ?? "";
                if (src != "about:blank") tab.Url = src;
                SyncAddressBarFromTab(tab);
                if (ActiveTab == tab) UpdateTabState(tab);
            };
        }

        private void RestoreClosedTab()
        {
            if (closedTabs.Count == 0) return;
            var url = closedTabs[closedTabs.Count - 1];
            closedTabs.RemoveAt(closedTabs.Count - 1);
            AddNewTab(url);
        }

        private void ZoomBy(double delta)
        {
            var tab = ActiveTab; if (tab == null) return;
            tab.ZoomFactor = Math.Max(0.3, Math.Min(3.0, tab.ZoomFactor + delta));
            try { tab.WebView.ZoomFactor = tab.ZoomFactor; } catch { }
            statusLabel.Text = $"Zoom: {(int)(tab.ZoomFactor * 100)}%";
        }

        private void ZoomReset()
        {
            var tab = ActiveTab; if (tab == null) return;
            tab.ZoomFactor = 1.0;
            try { tab.WebView.ZoomFactor = 1.0; } catch { }
            statusLabel.Text = "Zoom: 100%";
        }

        private void NavigateTab(BrowserTab tab, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (url == "about:blank")
            {
                // Navigate directly - don't treat as search query, don't update address bar.
                tab.Url = "about:blank";
                addressUserEditing = false;
                if (tab.WebView.CoreWebView2 != null)
                    tab.WebView.CoreWebView2.Navigate("about:blank");
                return;
            }
            // If it's not a URL, treat as search query
            if ((!url.Contains("://") && !url.Contains(".")) || (url.Contains(" ") && !url.Contains("://")))
            {
                url = string.Format(searchUrlTemplate, Uri.EscapeDataString(url));
            }
            else if (!url.Contains("://"))
            {
                url = "https://" + url;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
            tab.Url = uri.ToString();
            addressUserEditing = false;
            if (tab.WebView.CoreWebView2 != null)
                tab.WebView.CoreWebView2.Navigate(uri.ToString());
            // Don't write about:blank to the address bar - SyncAddressBarFromTab
            // maps it to "" so the bar stays clean for the user to type into.
            if (ActiveTab == tab && uri.ToString() != "about:blank")
                SetAddressText(uri.ToString(), force: true);
            if (uri.ToString() != "about:blank")
                AddToHistory(uri.ToString());
        }

        private void NavigateCurrentTab(string url)
        {
            if (ActiveTab == null) return;
            // Empty bar or about:blank on a new tab = go to home page
            if (string.IsNullOrWhiteSpace(url) || url == "about:blank")
                NavigateTab(ActiveTab, homePageUrl);
            else NavigateTab(ActiveTab, url);
        }

        private void UpdateTabState(BrowserTab tab)
        {
            if (ActiveTab != tab) return;
            var core = tab.WebView.CoreWebView2;
            backBtn.Enabled = core?.CanGoBack ?? false;
            backBtn.ForeColor = backBtn.Enabled ? Color.White : Theme.ForeDim;
            fwdBtn.Enabled = core?.CanGoForward ?? false;
            fwdBtn.ForeColor = fwdBtn.Enabled ? Color.White : Theme.ForeDim;
            SyncAddressBarFromTab(tab);
            statusLabel.Text = $"Ready | Ads blocked: {adsBlockedCount} | Domains: {BlockedAdDomains.Count}";
            var currentUrl = tab.WebView.Source?.AbsoluteUri ?? tab.Url ?? "";
            bool bookmarked = BookmarkExistsInTree(bookmarks, currentUrl);
            bookmarkBtn.StarFilled = bookmarked;
            bookmarkBtn.Invalidate();
        }

    }
}
