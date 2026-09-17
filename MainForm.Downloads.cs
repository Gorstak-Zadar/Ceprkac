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
        private void Core_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        {
            // Hide WebView2's default save UI (often suggests a dummy "aaaa" name for
            // Save image as) and defer until our dialog has actually set the path.
            e.Handled = true;
            var deferral = e.GetDeferral();
            var op = e.DownloadOperation;
            void Prompt()
            {
                try
                {
                    if (IsDisposed) { e.Cancel = true; return; }
                    var filename = SuggestDownloadFileName(e.ResultFilePath, op.Uri);
                    using var dialog = new SaveFileDialog
                    {
                        FileName = filename,
                        Filter = "All Files|*.*",
                        Title = "Save Download",
                        RestoreDirectory = true,
                        OverwritePrompt = true,
                    };
                    try
                    {
                        var dir = Path.GetDirectoryName(e.ResultFilePath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            dialog.InitialDirectory = dir;
                    }
                    catch { }
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        e.Cancel = true;
                        statusLabel.Text = "Download canceled.";
                        return;
                    }
                    e.ResultFilePath = dialog.FileName;
                    WatchWebViewDownload(op, dialog.FileName);
                }
                catch (Exception ex)
                {
                    e.Cancel = true;
                    statusLabel.Text = "Download failed.";
                    try { MessageBox.Show(this, ex.Message, "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
                }
                finally
                {
                    deferral.Complete();
                }
            }
            try
            {
                if (!IsHandleCreated || IsDisposed) { e.Cancel = true; deferral.Complete(); return; }
                BeginInvoke(new Action(Prompt));
            }
            catch
            {
                e.Cancel = true;
                deferral.Complete();
            }
        }

        // "Save image as" (and save link/media as) shows Chromium's file picker
        // *before* DownloadStarting. That first pick is discarded, then we would
        // prompt again with WebView2's dummy "aaaa" name. Replace those items so
        // only our dialog runs and the file is actually written.
        private async Task SaveUrlWithDialogAsync(CoreWebView2 core, string uri)
        {
            if (string.IsNullOrWhiteSpace(uri) || IsDisposed) return;
            var filename = SuggestDownloadFileName("", uri);
            if (string.IsNullOrEmpty(Path.GetExtension(filename)))
            {
                var ext = GuessExtensionFromUri(uri);
                filename += ext.Length > 0 ? ext : ".jpg";
            }
            using var dialog = new SaveFileDialog
            {
                FileName = filename,
                Filter = "All Files|*.*",
                Title = "Save Download",
                RestoreDirectory = true,
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                statusLabel.Text = "Download canceled.";
                return;
            }
            var item = new DownloadItem
            {
                Filename = Path.GetFileName(dialog.FileName),
                Path = dialog.FileName,
                Url = uri,
                Status = "Downloading",
            };
            downloads.Add(item);
            if (downloads.Count > 40) downloads.RemoveRange(0, downloads.Count - 40);
            statusLabel.Text = $"Downloading {item.Filename}...";
            RefreshDownloadsButton();
            try
            {
                await DownloadUriToFileAsync(core, uri, dialog.FileName, item);
                item.Status = "Complete";
                if (item.Total <= 0) item.Total = item.Received;
                statusLabel.Text = $"Download complete: {item.Filename}";
            }
            catch (Exception ex)
            {
                item.Status = "Interrupted";
                statusLabel.Text = $"Download interrupted: {item.Filename}";
                try { MessageBox.Show(this, $"Could not save file:\r\n{ex.Message}", "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            }
            SaveDownloads();
            RefreshDownloadsButton();
        }

        private async Task DownloadUriToFileAsync(CoreWebView2 core, string uri, string dest, DownloadItem item)
        {
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                WriteDataUriToFile(uri, dest);
                item.Received = item.Total = new FileInfo(dest).Length;
                return;
            }
            if (uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            {
                await WriteBlobUriToFileAsync(core, uri, dest);
                item.Received = item.Total = new FileInfo(dest).Length;
                return;
            }
            if (uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(new Uri(uri).LocalPath, dest, overwrite: true);
                item.Received = item.Total = new FileInfo(dest).Length;
                return;
            }

            string? ua = null;
            try
            {
                var raw = await core.ExecuteScriptAsync("navigator.userAgent");
                ua = JsonSerializer.Deserialize<string>(raw);
            }
            catch { }

            var cookieParts = new List<string>();
            try
            {
                foreach (var c in await core.CookieManager.GetCookiesAsync(uri))
                    cookieParts.Add(c.Name + "=" + c.Value);
            }
            catch { }

            using var handler = new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrEmpty(ua))
                req.Headers.TryAddWithoutValidation("User-Agent", ua);
            try
            {
                var referer = core.Source;
                if (!string.IsNullOrEmpty(referer) && referer.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    req.Headers.TryAddWithoutValidation("Referer", referer);
            }
            catch { }
            if (cookieParts.Count > 0)
                req.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookieParts));
            req.Headers.TryAddWithoutValidation("Accept", "*/*");

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentLength is long len && len > 0)
                item.Total = len;

            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var input = await resp.Content.ReadAsStreamAsync();
            using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await output.WriteAsync(buffer, 0, read);
                item.Received += read;
                var copy = item;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (copy.Status != "Downloading") return;
                        statusLabel.Text = copy.Total > 0
                            ? $"Downloading {copy.Filename}: {copy.Received:N0} / {copy.Total:N0}"
                            : $"Downloading {copy.Filename}: {copy.Received:N0}";
                    }));
                }
                catch { }
            }
        }

        private static void WriteDataUriToFile(string uri, string dest)
        {
            int comma = uri.IndexOf(',');
            if (comma < 0) throw new InvalidOperationException("Invalid data URL.");
            var meta = uri.Substring(0, comma);
            var payload = uri.Substring(comma + 1);
            byte[] bytes = meta.IndexOf("base64", StringComparison.OrdinalIgnoreCase) >= 0
                ? Convert.FromBase64String(payload)
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(dest, bytes);
        }

        private static async Task WriteBlobUriToFileAsync(CoreWebView2 core, string uri, string dest)
        {
            var escaped = uri.Replace("\\", "\\\\").Replace("'", "\\'");
            var script = "(async()=>{const r=await fetch('" + escaped + "');const b=new Uint8Array(await r.arrayBuffer());let s='';for(let i=0;i<b.length;i++)s+=String.fromCharCode(b[i]);return btoa(s);})()";
            var json = await core.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                throw new InvalidOperationException("Could not read image data from the page.");
            var b64 = JsonSerializer.Deserialize<string>(json);
            if (string.IsNullOrEmpty(b64))
                throw new InvalidOperationException("Could not read image data from the page.");
            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(dest, Convert.FromBase64String(b64));
        }

        private void WatchWebViewDownload(CoreWebView2DownloadOperation op, string path)
        {
            var item = new DownloadItem
            {
                Filename = Path.GetFileName(path),
                Path = path,
                Url = op.Uri ?? "",
                Status = "Downloading",
            };
            downloads.Add(item);
            if (downloads.Count > 40) downloads.RemoveRange(0, downloads.Count - 40);
            statusLabel.Text = $"Downloading {item.Filename}...";
            RefreshDownloadsButton();
            op.BytesReceivedChanged += (_, _) => BeginInvoke(() =>
            {
                if (item.Status != "Downloading") return;
                item.Received = op.BytesReceived;
                item.Total = (long)op.TotalBytesToReceive.GetValueOrDefault();
                statusLabel.Text = item.Total > 0
                    ? $"Downloading {item.Filename}: {item.Received:N0} / {item.Total:N0}"
                    : $"Downloading {item.Filename}: {item.Received:N0}";
            });
            op.StateChanged += (_, _) => BeginInvoke(() =>
            {
                if (op.State == CoreWebView2DownloadState.Completed)
                {
                    item.Status = "Complete";
                    if (item.Total <= 0) item.Total = item.Received;
                    statusLabel.Text = $"Download complete: {item.Filename}";
                    SaveDownloads();
                    RefreshDownloadsButton();
                }
                else if (op.State == CoreWebView2DownloadState.Interrupted)
                {
                    item.Status = "Interrupted";
                    statusLabel.Text = $"Download interrupted: {item.Filename}";
                    SaveDownloads();
                    RefreshDownloadsButton();
                }
            });
        }

        private static string SuggestDownloadFileName(string? resultFilePath, string? uri)
        {
            var name = "";
            try { name = Path.GetFileName(resultFilePath ?? ""); } catch { }
            if (!string.IsNullOrWhiteSpace(name) && !IsPlaceholderDownloadName(name))
                return SanitizeFileName(name);

            string fromUri = "";
            if (!string.IsNullOrWhiteSpace(uri))
            {
                try { fromUri = Path.GetFileName(new Uri(uri).LocalPath); } catch { }
            }
            fromUri = SanitizeFileName(fromUri);
            if (!string.IsNullOrWhiteSpace(fromUri) && fromUri.IndexOf('.') >= 0 && !IsPlaceholderDownloadName(fromUri)
                && fromUri.Length > 2
                && !fromUri.Equals("images", StringComparison.OrdinalIgnoreCase)
                && !fromUri.Equals("image", StringComparison.OrdinalIgnoreCase)
                && !fromUri.Equals("img", StringComparison.OrdinalIgnoreCase))
                return fromUri;

            var ext = GuessExtensionFromUri(uri);
            if (!string.IsNullOrWhiteSpace(fromUri) && fromUri.Length > 1 && !IsPlaceholderDownloadName(fromUri))
                return fromUri + ext;
            return (ext.Length > 0 ? "image" + ext : "download");
        }

        private static bool IsPlaceholderDownloadName(string fileName)
        {
            var stem = (Path.GetFileNameWithoutExtension(fileName ?? "") ?? "").Trim();
            if (stem.Length == 0) return true;
            return stem.Equals("aaaa", StringComparison.OrdinalIgnoreCase);
        }

        private static string GuessExtensionFromUri(string? uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return "";
            var lower = uri!.ToLowerInvariant();
            if (lower.StartsWith("data:image/png", StringComparison.Ordinal)) return ".png";
            if (lower.StartsWith("data:image/jpeg", StringComparison.Ordinal) || lower.StartsWith("data:image/jpg", StringComparison.Ordinal)) return ".jpg";
            if (lower.StartsWith("data:image/gif", StringComparison.Ordinal)) return ".gif";
            if (lower.StartsWith("data:image/webp", StringComparison.Ordinal)) return ".webp";
            if (lower.StartsWith("data:image/svg", StringComparison.Ordinal)) return ".svg";
            if (lower.Contains(".png")) return ".png";
            if (lower.Contains(".webp")) return ".webp";
            if (lower.Contains(".gif")) return ".gif";
            if (lower.Contains(".jpg") || lower.Contains(".jpeg")) return ".jpg";
            if (lower.Contains(".svg")) return ".svg";
            if (lower.Contains(".mp4")) return ".mp4";
            if (lower.Contains(".webm")) return ".webm";
            if (lower.Contains(".pdf")) return ".pdf";
            if (lower.Contains("gstatic.com") || lower.Contains("googleusercontent.com") || lower.Contains("ggpht.com")
                || lower.Contains("/image") || lower.Contains("=image") || lower.Contains("tbn:"))
                return ".jpg";
            return "";
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Trim().Trim('.');
            if (name.Length == 0 || name == "." || name == "..") return "";
            if (name.Length > 120) name = name.Substring(0, 120);
            return name;
        }

        private void RefreshDownloadsButton()
        {
            int active = downloads.Count(d => d.Status == "Downloading");
            downloadsBtn.Text = active > 0 ? $"\u2913 {active}" : "\u2913";
            chromeTip.SetToolTip(downloadsBtn, active > 0 ? $"Downloads - {active} in progress" : "Downloads");
        }

        private void RebuildDownloadsMenu()
        {
            downloadsMenu.Items.Clear();
            var recent = downloads.AsEnumerable().Reverse().Take(15).ToList();
            if (recent.Count == 0)
            {
                downloadsMenu.Items.Add(new ToolStripMenuItem("No downloads yet.") { Enabled = false, ForeColor = Theme.ForeDim });
                return;
            }
            foreach (var dl in recent)
            {
                string extra = dl.Status == "Downloading" && dl.Total > 0
                    ? $"{dl.Received * 100 / Math.Max(dl.Total, 1)}%"
                    : dl.Status;
                var itemDl = dl;
                var mi = new ToolStripMenuItem($"{dl.Filename}  -  {extra}")
                {
                    ForeColor = Color.White, BackColor = Theme.ActiveTab,
                };
                mi.Click += (_, _) => { try { if (File.Exists(itemDl.Path)) Process.Start(new ProcessStartInfo(itemDl.Path) { UseShellExecute = true }); } catch { } };
                downloadsMenu.Items.Add(mi);
            }
            downloadsMenu.Items.Add(new ToolStripSeparator());
            var clear = new ToolStripMenuItem("Clear") { ForeColor = Color.White, BackColor = Theme.ActiveTab };
            clear.Click += (_, _) =>
            {
                downloads.RemoveAll(d => d.Status != "Downloading");
                SaveDownloads();
                RefreshDownloadsButton();
            };
            downloadsMenu.Items.Add(clear);
        }

        private void LoadDownloads()
        {
            try
            {
                if (!File.Exists(downloadsFile)) return;
                var list = JsonSerializer.Deserialize<List<DownloadItem>>(File.ReadAllText(downloadsFile));
                if (list == null) return;
                foreach (var d in list.Skip(Math.Max(0, list.Count - 40)))
                {
                    if (d.Status == "Downloading") d.Status = "Complete";
                    downloads.Add(d);
                }
            }
            catch { }
        }

        private void SaveDownloads()
        {
            try
            {
                var doneAll = downloads.Where(d => d.Status != "Downloading").ToList();
                var done = doneAll.Skip(Math.Max(0, doneAll.Count - 40)).ToList();
                File.WriteAllText(downloadsFile, JsonSerializer.Serialize(done, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

    }
}
