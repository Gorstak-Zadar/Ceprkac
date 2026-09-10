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
        // Captures the real right-click target before WebView2 builds its menu.
        // ContextMenuTarget often reports Kind=Page with empty SourceUri on Discord/CDN
        // images, CSS backgrounds, and in-page viewers - so without this, Lens never appears.
        private const string ContextCaptureJs = @"
(function(){
  if (window.__ceprkacCtxCap) return;
  window.__ceprkacCtxCap = true;
  window.__ceprkacLastCtx = null;
  function absUrl(u){
    if(!u) return '';
    try { return new URL(u, location.href).href; } catch(e){ return u; }
  }
  function mediaFrom(el){
    if (!el || el===document || el===window) return null;
    var tag = (el.tagName||'').toUpperCase();
    if (tag==='IMG' || tag==='IMAGE' || tag==='PICTURE') {
      var s = el.currentSrc || el.src || el.getAttribute('src') || '';
      if (!s && tag==='PICTURE') {
        var im = el.querySelector('img');
        if (im) s = im.currentSrc || im.src || '';
      }
      if (s) return {kind:'image', src:absUrl(s)};
    }
    if (tag==='VIDEO' || tag==='AUDIO') {
      var s2 = el.currentSrc || el.src || '';
      if (!s2 && el.querySelector) {
        var srcEl = el.querySelector('source');
        if (srcEl) s2 = srcEl.src || srcEl.getAttribute('src') || '';
      }
      if (s2) return {kind:'video', src:absUrl(s2)};
    }
    if (tag==='A') {
      var href = el.href || '';
      var img = el.querySelector && el.querySelector('img,video');
      if (img) {
        var is = img.currentSrc || img.src || '';
        if (is) return {kind: (img.tagName||'').toUpperCase()==='VIDEO' ? 'video' : 'image', src:absUrl(is), href:href};
      }
      if (href) return {kind:'link', href:href};
    }
    if (tag==='SOURCE' && el.parentElement) return mediaFrom(el.parentElement);
    try {
      var bg = (window.getComputedStyle(el).backgroundImage || '');
      var m = /url\(\s*[""']?([^""')]+)[""']?\s*\)/i.exec(bg);
      if (m && m[1] && m[1].indexOf('data:')!==0) return {kind:'image', src:absUrl(m[1])};
    } catch(e){}
    return null;
  }
  document.addEventListener('contextmenu', function(e){
    var t = e.target;
    var info = null;
    for (var i=0; i<8 && t; i++) {
      info = mediaFrom(t);
      if (info) break;
      t = t.parentElement;
    }
    var sel = '';
    try { sel = (window.getSelection() && window.getSelection().toString()) || ''; } catch(x){}
    window.__ceprkacLastCtx = {
      kind: info ? info.kind : 'page',
      src: info && info.src ? info.src : '',
      href: info && info.href ? info.href : '',
      sel: sel
    };
  }, true);
})();";

        private void Core_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            if (sharedEnvironment == null) return;
            var core = sender as CoreWebView2;
            if (core == null) return;

            // Defer so we can read the JS-captured target (sync ContextMenuTarget is often empty).
            CoreWebView2Deferral? deferral = null;
            try { deferral = e.GetDeferral(); } catch { }

            string sourceUri = "";
            string linkUri = "";
            string selectionText = "";
            CoreWebView2ContextMenuTargetKind kind = CoreWebView2ContextMenuTargetKind.Page;
            try
            {
                var target = e.ContextMenuTarget;
                kind = target.Kind;
                if (target.HasSourceUri) sourceUri = target.SourceUri ?? "";
                if (target.HasLinkUri) linkUri = target.LinkUri ?? "";
                try { selectionText = target.SelectionText ?? ""; } catch { }
            }
            catch { }

            var menuItems = e.MenuItems;
            ReplaceNativeSaveAs(menuItems, "saveImageAs", core, sourceUri);
            ReplaceNativeSaveAs(menuItems, "saveVideoAs", core, sourceUri);
            ReplaceNativeSaveAs(menuItems, "saveAudioAs", core, sourceUri);
            ReplaceNativeSaveAs(menuItems, "saveLinkAs", core, linkUri);

            if (deferral == null)
            {
                AddSearchMenuItems(menuItems, kind, selectionText, sourceUri, linkUri);
                return;
            }

            _ = CompleteContextMenuAsync(core, menuItems, kind, selectionText, sourceUri, linkUri, deferral);
        }

        private async Task CompleteContextMenuAsync(
            CoreWebView2 core,
            IList<CoreWebView2ContextMenuItem> items,
            CoreWebView2ContextMenuTargetKind kind,
            string selectionText,
            string sourceUri,
            string linkUri,
            CoreWebView2Deferral deferral)
        {
            try
            {
                try
                {
                    var raw = await core.ExecuteScriptAsync(
                        "window.__ceprkacLastCtx ? JSON.stringify(window.__ceprkacLastCtx) : 'null'");
                    // ExecuteScript returns a JSON-encoded string value.
                    string? json = null;
                    try { json = JsonSerializer.Deserialize<string>(raw); } catch { json = raw?.Trim('"'); }
                    if (!string.IsNullOrWhiteSpace(json) && json != "null")
                    {
                        using var doc = JsonDocument.Parse(json!);
                        var root = doc.RootElement;
                        string jsKind = root.TryGetProperty("kind", out var k) ? (k.GetString() ?? "") : "";
                        string jsSrc = root.TryGetProperty("src", out var s) ? (s.GetString() ?? "") : "";
                        string jsHref = root.TryGetProperty("href", out var h) ? (h.GetString() ?? "") : "";
                        string jsSel = root.TryGetProperty("sel", out var sel) ? (sel.GetString() ?? "") : "";

                        if (string.IsNullOrWhiteSpace(selectionText) && !string.IsNullOrWhiteSpace(jsSel))
                            selectionText = jsSel;
                        if (string.IsNullOrWhiteSpace(sourceUri) && !string.IsNullOrWhiteSpace(jsSrc))
                            sourceUri = jsSrc;
                        if (string.IsNullOrWhiteSpace(linkUri) && !string.IsNullOrWhiteSpace(jsHref))
                            linkUri = jsHref;

                        if (string.Equals(jsKind, "image", StringComparison.OrdinalIgnoreCase))
                            kind = CoreWebView2ContextMenuTargetKind.Image;
                        else if (string.Equals(jsKind, "video", StringComparison.OrdinalIgnoreCase))
                            kind = CoreWebView2ContextMenuTargetKind.Video;
                    }
                }
                catch { }

                AddSearchMenuItems(items, kind, selectionText, sourceUri, linkUri);
            }
            finally
            {
                try { deferral.Complete(); } catch { }
            }
        }

        // Adds "Search {engine} for ..." / Google Lens entries based on what was right-clicked.
        private void AddSearchMenuItems(
            IList<CoreWebView2ContextMenuItem> items,
            CoreWebView2ContextMenuTargetKind kind,
            string selectionText,
            string sourceUri,
            string linkUri)
        {
            if (sharedEnvironment == null) return;
            var engine = CurrentSearchEngineName();
            var toAdd = new List<CoreWebView2ContextMenuItem>();

            void AddItem(string label, string url)
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                try
                {
                    var item = sharedEnvironment.CreateContextMenuItem(
                        label, null, CoreWebView2ContextMenuItemKind.Command);
                    var captured = url;
                    item.CustomItemSelected += (_, _) =>
                    {
                        try { BeginInvoke(new Action(() => AddNewTab(captured, focusOmnibox: false))); }
                        catch { }
                    };
                    toAdd.Add(item);
                }
                catch { }
            }

            void AddCopy(string label, string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                try
                {
                    var item = sharedEnvironment.CreateContextMenuItem(
                        label, null, CoreWebView2ContextMenuItemKind.Command);
                    var captured = text;
                    item.CustomItemSelected += (_, _) =>
                    {
                        try { BeginInvoke(new Action(() => { try { Clipboard.SetText(captured); statusLabel.Text = "Copied."; } catch { } })); }
                        catch { }
                    };
                    toAdd.Add(item);
                }
                catch { }
            }

            var sel = (selectionText ?? "").Trim();
            if (sel.Length > 0)
            {
                var shown = sel.Length > 40 ? sel.Substring(0, 40) + "..." : sel;
                AddItem($"Search {engine} for \"{shown}\"", BuildTextSearchUrl(sel));
            }

            var mediaUri = !string.IsNullOrWhiteSpace(sourceUri) ? sourceUri : linkUri;

            bool isImage = kind == CoreWebView2ContextMenuTargetKind.Image
                           || LooksLikeImageUrl(sourceUri) || LooksLikeImageUrl(linkUri)
                           || LooksLikeImageHost(sourceUri) || LooksLikeImageHost(linkUri);
            if (kind == CoreWebView2ContextMenuTargetKind.Image && !string.IsNullOrWhiteSpace(sourceUri))
                isImage = true;

            bool isVideo = kind == CoreWebView2ContextMenuTargetKind.Video
                           || kind == CoreWebView2ContextMenuTargetKind.Audio
                           || LooksLikeVideoUrl(sourceUri) || LooksLikeVideoUrl(linkUri);

            if (isImage && !string.IsNullOrWhiteSpace(mediaUri))
            {
                // Always offer Lens for images (independent of default search engine).
                AddItem("Search image with Google Lens", "https://lens.google.com/uploadbyurl?url=" + Uri.EscapeDataString(mediaUri));
                if (!SearchHost().Contains("google."))
                    AddItem($"Search {engine} for this image", BuildImageSearchUrl(mediaUri));
                AddItem("Open image in new tab", mediaUri);
                AddCopy("Copy image address", mediaUri);
            }
            else if (isVideo && !string.IsNullOrWhiteSpace(mediaUri))
            {
                AddItem($"Search {engine} for this video", BuildVideoSearchUrl(mediaUri));
                AddItem("Open media in new tab", mediaUri);
                AddCopy("Copy media address", mediaUri);
            }
            else if (!string.IsNullOrWhiteSpace(linkUri) && sel.Length == 0)
            {
                AddItem("Open link in new tab", linkUri);
                AddCopy("Copy link address", linkUri);
            }

            if (toAdd.Count == 0) return;

            int idx = 0;
            foreach (var it in toAdd)
            {
                try { items.Insert(idx, it); idx++; }
                catch { }
            }
            try
            {
                // Empty string label - null has failed to construct a separator on some runtimes.
                var sep = sharedEnvironment.CreateContextMenuItem(
                    "", null, CoreWebView2ContextMenuItemKind.Separator);
                items.Insert(idx, sep);
            }
            catch { }
        }

        private static readonly string[] ImageExtensions =
            { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".avif", ".ico", ".tiff", ".jfif", ".heic" };
        private static readonly string[] VideoExtensions =
            { ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v", ".ogv", ".mpeg", ".mpg", ".m3u8" };
        private static readonly string[] ImageHostHints =
        {
            "googleusercontent.com", "ggpht.com", "gstatic.com", "ytimg.com",
            "twimg.com", "fbcdn.net", "cdninstagram.com", "pinimg.com",
            "imgur.com", "i.imgur.com", "wikimedia.org", "cloudinary.com",
            "imgix.net", "akamaihd.net", "discordapp.net", "discordcdn.com",
            "discordapp.com", "media.discordapp.net", "cdn.discordapp.com",
            "media.tenor.com", "giphy.com"
        };

        private static bool LooksLikeImageUrl(string? url) => UrlHasExtension(url, ImageExtensions);
        private static bool LooksLikeVideoUrl(string? url) => UrlHasExtension(url, VideoExtensions);

        private static bool LooksLikeImageHost(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                var u = new Uri(url!);
                var host = u.Host.ToLowerInvariant();
                foreach (var h in ImageHostHints)
                    if (host == h || host.EndsWith("." + h, StringComparison.Ordinal)) return true;
                var path = u.AbsolutePath.ToLowerInvariant();
                if (path.Contains("/image") || path.Contains("/img/") || path.Contains("/thumb")
                    || path.Contains("/photo") || path.Contains("/media/") || path.Contains("/avatar")
                    || path.Contains("/attachments/") || path.Contains("/icons/"))
                    return true;
            }
            catch { }
            return false;
        }

        private static bool UrlHasExtension(string? url, string[] exts)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string path;
            try { path = new Uri(url!).AbsolutePath.ToLowerInvariant(); }
            catch { path = url!.ToLowerInvariant(); }
            int q = path.IndexOf('@');
            if (q > 0) path = path.Substring(0, q);
            foreach (var e in exts)
                if (path.EndsWith(e, StringComparison.Ordinal)) return true;
            return false;
        }

        private string CurrentSearchEngineName()
        {
            foreach (var (name, _, search) in SearchEngines)
            {
                if (string.Equals(search, searchUrlTemplate, StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            try { return new Uri(string.Format(searchUrlTemplate, "x")).Host.Replace("www.", ""); }
            catch { return "web"; }
        }

        private string BuildTextSearchUrl(string query) =>
            string.Format(searchUrlTemplate, Uri.EscapeDataString(query));

        private string BuildImageSearchUrl(string imageUrl)
        {
            var host = SearchHost();
            var enc = Uri.EscapeDataString(imageUrl);
            if (host.Contains("google."))
                return "https://lens.google.com/uploadbyurl?url=" + enc;
            if (host.Contains("bing."))
                return "https://www.bing.com/images/search?q=imgurl:" + enc + "&view=detailv2&iss=sbi";
            if (host.Contains("yandex."))
                return "https://yandex.com/images/search?rpt=imageview&url=" + enc;
            return "https://lens.google.com/uploadbyurl?url=" + enc;
        }

        private string BuildVideoSearchUrl(string videoUrl)
        {
            var host = SearchHost();
            var enc = Uri.EscapeDataString(videoUrl);
            if (host.Contains("google."))
                return "https://www.google.com/search?q=" + enc + "&tbm=vid";
            if (host.Contains("bing."))
                return "https://www.bing.com/videos/search?q=" + enc;
            return BuildTextSearchUrl(videoUrl);
        }

        private string SearchHost()
        {
            try { return new Uri(string.Format(searchUrlTemplate, "x")).Host.ToLowerInvariant(); }
            catch { return ""; }
        }

        private void ReplaceNativeSaveAs(IList<CoreWebView2ContextMenuItem> items, string name, CoreWebView2 core, string uri)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Kind == CoreWebView2ContextMenuItemKind.Submenu)
                {
                    try { ReplaceNativeSaveAs(it.Children, name, core, uri); } catch { }
                    continue;
                }
                if (!string.Equals(it.Name, name, StringComparison.Ordinal)) continue;
                if (string.IsNullOrWhiteSpace(uri) || sharedEnvironment == null) break;
                try
                {
                    var custom = sharedEnvironment.CreateContextMenuItem(it.Label, null, CoreWebView2ContextMenuItemKind.Command);
                    var capturedCore = core;
                    var capturedUri = uri;
                    custom.CustomItemSelected += (_, _) =>
                    {
                        try { BeginInvoke(new Action(() => { _ = SaveUrlWithDialogAsync(capturedCore, capturedUri); })); }
                        catch { }
                    };
                    items.RemoveAt(i);
                    items.Insert(i, custom);
                }
                catch { }
                break;
            }
        }

    }
}
