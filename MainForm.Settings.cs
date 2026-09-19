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
    public partial class MainForm
    {
        // Per-session remembered permission decisions, keyed by "kind|host".
        private readonly Dictionary<string, bool> _permissionChoices =
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);

        private void Core_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            string uri = "";
            try { uri = e.Uri ?? ""; } catch { }
            var kind = e.PermissionKind;
            Logger.Log("PERM", $"Permission requested: kind={kind}, uri='{uri}'");

            // The "Apps on device" permission (Chromium getInstalledRelatedApps / local app
            // discovery) and WindowManagement are not reliably shown by WebView2's native UI in
            // this runtime/SDK combination - they resolve silently, so the user was never asked.
            // For these we show our own Allow/Block prompt so the choice is restored. Everything
            // else defers to the browser's native, OS-themed dialog.
            bool needsCustomPrompt =
                kind == CoreWebView2PermissionKind.UnknownPermission
                || kind == CoreWebView2PermissionKind.WindowManagement;

            if (!needsCustomPrompt)
            {
                e.State = CoreWebView2PermissionState.Default;
                return;
            }

            string host;
            try { host = new System.Uri(uri).Host; } catch { host = uri; }
            string key = kind + "|" + host;

            if (_permissionChoices.TryGetValue(key, out bool remembered))
            {
                e.State = remembered ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                Logger.Log("PERM", $"Using remembered choice for {key}: {(remembered ? "ALLOW" : "DENY")}.");
                return;
            }

            // Show our own modal prompt; take a deferral so WebView2 waits for the answer.
            var deferral = e.GetDeferral();
            try
            {
                string what = kind == CoreWebView2PermissionKind.WindowManagement
                    ? "manage windows on your device"
                    : "access apps on your device";
                var (allow, remember) = PermissionPrompt.Ask(this, what, host);
                e.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                if (remember) _permissionChoices[key] = allow;
                Logger.Log("PERM", $"Prompted for {key}: user chose {(allow ? "ALLOW" : "DENY")}{(remember ? " (remembered)" : "")}.");
            }
            catch (Exception ex)
            {
                e.State = CoreWebView2PermissionState.Deny;
                Logger.Error("PERM", "PermissionRequested prompt", ex);
            }
            finally
            {
                deferral.Complete();
            }
        }

        // Per-session remembered decisions for launching external apps via custom URI
        // schemes (e.g. discord://, slack://, spotify://, tg://). Keyed by "scheme|host".
        private readonly Dictionary<string, bool> _externalSchemeChoices =
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);

        // Raised when a page tries to open an external application through a custom
        // protocol. Without handling this, WebView2 may silently launch the app (or
        // suppress its own prompt), so we take over and always ask the user - restoring
        // the "Open <app>?" choice that browsers normally show for links like discord://.
        private void Core_LaunchingExternalUriScheme(object? sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e)
        {
            string rawUri = "";
            try { rawUri = e.Uri ?? ""; } catch { }
            Logger.Log("EXTERNAL", $"LaunchingExternalUriScheme fired: uri='{rawUri}', origin='{SafeOrigin(e)}'");

            // We are showing our own modal dialog; take a deferral so WebView2 waits.
            var deferral = e.GetDeferral();
            try
            {
                string uri = rawUri;
                string scheme;
                string host;
                try
                {
                    var u = new System.Uri(uri);
                    scheme = u.Scheme;
                    host = string.IsNullOrEmpty(u.Host) ? scheme : u.Host;
                }
                catch
                {
                    int idx = uri.IndexOf(':');
                    scheme = idx > 0 ? uri.Substring(0, idx) : uri;
                    host = scheme;
                }

                string key = scheme + "|" + host;

                // Honor a remembered choice for this scheme+host for the session.
                if (_externalSchemeChoices.TryGetValue(key, out bool remembered))
                {
                    e.Cancel = !remembered;
                    Logger.Log("EXTERNAL", $"Using remembered choice for {key}: {(remembered ? "ALLOW" : "BLOCK")}.");
                    return;
                }

                var (allow, remember) = ExternalAppPrompt.Ask(this, scheme, e.InitiatingOrigin ?? "");
                e.Cancel = !allow;
                if (remember) _externalSchemeChoices[key] = allow;
                Logger.Log("EXTERNAL", $"Prompted for {key}: user chose {(allow ? "OPEN" : "CANCEL")}{(remember ? " (remembered)" : "")}.");
            }
            catch (Exception ex)
            {
                // On any failure, err on the side of NOT launching an external app silently.
                e.Cancel = true;
                Logger.Error("EXTERNAL", "LaunchingExternalUriScheme handler", ex);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private static string SafeOrigin(CoreWebView2LaunchingExternalUriSchemeEventArgs e)
        {
            try { return e.InitiatingOrigin ?? ""; } catch { return ""; }
        }

        // Handles an external-scheme launch that our injected ExternalSchemeJs caught in the page
        // (a discord:// iframe/anchor/window.open that WebView2's own event never surfaced). Shows
        // the same Open/Cancel prompt, honors a remembered per-site choice, and launches via the
        // OS shell only when allowed. Everything is logged so the log shows exactly what happened.
        private void HandleExternalLaunch(string uri, string initiatingOrigin)
        {
            Logger.Log("EXTERNAL", $"Page requested external launch (script-intercepted): uri='{uri}', origin='{initiatingOrigin}'");
            try
            {
                string scheme;
                string host;
                try
                {
                    var u = new System.Uri(uri);
                    scheme = u.Scheme;
                    host = string.IsNullOrEmpty(u.Host) ? scheme : u.Host;
                }
                catch
                {
                    int idx = uri.IndexOf(':');
                    scheme = idx > 0 ? uri.Substring(0, idx) : uri;
                    host = scheme;
                }

                string key = scheme + "|" + host;

                bool allow;
                if (_externalSchemeChoices.TryGetValue(key, out bool remembered))
                {
                    allow = remembered;
                    Logger.Log("EXTERNAL", $"Using remembered choice for {key}: {(allow ? "ALLOW" : "BLOCK")}.");
                }
                else
                {
                    var (a, remember) = ExternalAppPrompt.Ask(this, scheme, initiatingOrigin);
                    allow = a;
                    if (remember) _externalSchemeChoices[key] = allow;
                    Logger.Log("EXTERNAL", $"Prompted for {key}: user chose {(allow ? "OPEN" : "CANCEL")}{(remember ? " (remembered)" : "")}.");
                }

                if (!allow) return;

                // Launch through the OS shell. UseShellExecute routes the custom scheme to its
                // registered handler (e.g. Discord) exactly as a normal browser would.
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
                    Logger.Log("EXTERNAL", $"Launched external app for {key}.");
                    try { statusLabel.Text = $"Opened external app ({scheme})"; } catch { }
                }
                catch (Exception ex)
                {
                    Logger.Error("EXTERNAL", $"Process.Start for {uri}", ex);
                    try { statusLabel.Text = "Could not open the external app."; } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("EXTERNAL", "HandleExternalLaunch", ex);
            }
        }


        private void RefreshAddressSuggest()
        {
            addressSuggest.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void add(string? u)
            {
                if (string.IsNullOrWhiteSpace(u) || !seen.Add(u!)) return;
                addressSuggest.Add(u);
            }
            foreach (var h in history) add(h);
            void walk(List<BookmarkNode> nodes)
            {
                foreach (var n in nodes)
                {
                    if (n.Type == "link") add(n.Href);
                    else walk(n.Children);
                }
            }
            walk(bookmarks);
        }

        private void LoadWindowState()
        {
            try
            {
                if (!File.Exists(configFile)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(configFile));
                if (!doc.RootElement.TryGetProperty("geometry", out var g)) return;
                int x = g.GetProperty("x").GetInt32();
                int y = g.GetProperty("y").GetInt32();
                int w = g.GetProperty("width").GetInt32();
                int h = g.GetProperty("height").GetInt32();
                bool max = g.TryGetProperty("maximized", out var m) && m.GetBoolean();
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(x, y, Math.Max(600, w), Math.Max(400, h));
                if (max) WindowState = FormWindowState.Maximized;
            }
            catch { }
        }

        private void SaveWindowState()
        {
            try
            {
                var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                var json = JsonSerializer.Serialize(new
                {
                    geometry = new { x = b.X, y = b.Y, width = b.Width, height = b.Height, maximized = WindowState == FormWindowState.Maximized }
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFile, json);
            }
            catch { }
        }


        //  Bookmarks 
        //  Settings 
        private static readonly (string Name, string Home, string Search)[] SearchEngines = new[]
        {
            ("Google",      "https://www.google.com",       "https://www.google.com/search?q={0}"),
            ("Bing",        "https://www.bing.com",         "https://www.bing.com/search?q={0}"),
            ("DuckDuckGo",  "https://duckduckgo.com",       "https://duckduckgo.com/?q={0}"),
            ("Yahoo",       "https://search.yahoo.com",     "https://search.yahoo.com/search?p={0}"),
            ("Brave Search","https://search.brave.com",     "https://search.brave.com/search?q={0}"),
            ("Startpage",   "https://www.startpage.com",    "https://www.startpage.com/do/search?q={0}"),
        };

        private void LoadSettings()
        {
            if (!File.Exists(settingsFile)) return;
            try
            {
                foreach (var line in File.ReadAllLines(settingsFile))
                {
                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;
                    switch (parts[0].Trim().ToLower())
                    {
                        case "homepage": homePageUrl = parts[1].Trim(); break;
                        case "searchurl": searchUrlTemplate = parts[1].Trim(); break;
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllLines(settingsFile, new[]
                {
                    $"homepage={homePageUrl}",
                    $"searchurl={searchUrlTemplate}",
                });
            }
            catch { }
        }

        private void ShowSearchEnginePicker()
        {
            using var dlg = new Form
            {
                Text = "Choose Your Search Engine",
                ClientSize = new Size(360, 340),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Theme.ActiveTab,
                ForeColor = Color.White,
            };

            var label = new Label
            {
                Text = "Select your default search engine:",
                Location = new Point(20, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.White,
            };
            dlg.Controls.Add(label);

            var list = new ListBox
            {
                Location = new Point(20, 48),
                Size = new Size(320, 220),
                Font = new Font("Segoe UI", 11f),
                BackColor = Theme.TitleBar,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
            };
            foreach (var (name, _, _) in SearchEngines)
                list.Items.Add(name);
            list.SelectedIndex = 0;
            dlg.Controls.Add(list);

            var okBtn = new Button
            {
                Text = "OK",
                Location = new Point(240, 280),
                Size = new Size(100, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Accent,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 10f),
                DialogResult = DialogResult.OK,
            };
            okBtn.FlatAppearance.BorderSize = 0;
            dlg.Controls.Add(okBtn);
            dlg.AcceptButton = okBtn;

            if (dlg.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0)
            {
                var choice = SearchEngines[list.SelectedIndex];
                homePageUrl = choice.Home;
                searchUrlTemplate = choice.Search;
                SaveSettings();
                if (ActiveTab != null) NavigateCurrentTab(homePageUrl);
            }
            else
                SaveSettings();
        }

        //  History 
        private void LoadHistory()
        {
            if (!File.Exists(historyFile)) return;
            history.Clear();
            var lines = File.ReadAllLines(historyFile)
                .Where(l => !string.IsNullOrWhiteSpace(l)
                         && l != "about:blank"
                         && !l.Contains("about%3Ablank", StringComparison.OrdinalIgnoreCase)
                         && !l.Contains("about:blank", StringComparison.OrdinalIgnoreCase))
                .Distinct().ToList();
            history.AddRange(lines.Count <= 100 ? lines : lines.GetRange(lines.Count - 100, 100));
        }

        private void SaveHistory() { File.WriteAllLines(historyFile, history); }

        private void AddToHistory(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (url == "about:blank") return;
            if (url.Contains("about%3Ablank", StringComparison.OrdinalIgnoreCase)) return;
            if (url.Contains("about:blank", StringComparison.OrdinalIgnoreCase)) return;
            history.RemoveAll(item => string.Equals(item, url, StringComparison.OrdinalIgnoreCase));
            history.Add(url);
            if (history.Count > 100) history.RemoveRange(0, history.Count - 100);
            SaveHistory();
            if (!addressSuggest.Contains(url)) addressSuggest.Add(url);
        }

        private void ClearHistory()
        {
            if (MessageBox.Show(this, "Clear all history?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            history.Clear(); SaveHistory(); statusLabel.Text = "History cleared.";
        }

        //  Ad Blocker (powered by GSecurity Ad Shield + EasyList + EasyPrivacy) 
    }
}
