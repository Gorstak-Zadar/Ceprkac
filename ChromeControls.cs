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
    //  tab data model 
    internal sealed class BrowserTab
    {
        public string Title { get; set; } = "New Tab";
        public string Url { get; set; } = "";
        public WebView2 WebView { get; set; } = null!;
        public bool IsLoading { get; set; }
        public int LoadProgress { get; set; }
        public double ZoomFactor { get; set; } = 1.0;
        public bool IsPopup { get; set; }
        public bool FocusOmnibox { get; set; }
        public DateTime LastAutoFillAttempt { get; set; } = DateTime.MinValue;
        public DateTime LastAutoFillFormsAttempt { get; set; } = DateTime.MinValue;
        // The URL the last credential autofill actually ran against. A genuinely new
        // URL (e.g. Google's identifier -> password page) must always re-attempt even
        // if it happens inside the time-based debounce window.
        public string LastAutoFillUrl { get; set; } = "";
        // Monotonic token identifying the most recent autofill loop. A loop only owns the
        // "in progress" state while its token is current; a newer invocation (for a new URL)
        // bumps the token, so the older loop self-cancels and does not clear the newer guard.
        public long AutoFillToken { get; set; }
        public bool AutoFillInProgress { get; set; }
        // The URL that the SourceChanged handler last kicked an autofill attempt for. Autofill
        // writes to input fields dispatch input/change events, which on some SPAs push a new
        // history entry -> SourceChanged fires again -> autofill re-runs -> events -> ... a
        // self-feeding loop that flickered the address bar. The SourceChanged handler only
        // re-triggers autofill when core.Source differs from this, breaking the loop.
        public string LastSourceAutoFillUrl { get; set; } = "";
    }

    //  custom tab strip control 
    internal sealed class ChromeTabStrip : Control
    {
        public List<BrowserTab> Tabs { get; } = new();
        public int SelectedIndex { get; set; } = -1;
        public int HoverIndex { get; private set; } = -1;
        public int HoverCloseIndex { get; private set; } = -1;
        private Point? _dragStart;
        private int _dragTab = -1;

        public event EventHandler<int>? TabClicked;
        public event EventHandler<int>? TabCloseClicked;
        public event EventHandler? NewTabClicked;

        private const int TabHeight = 34;
        private const int TabMaxWidth = 240;
        private const int TabMinWidth = 60;
        private const int CloseSize = 16;
        private const int NewTabBtnWidth = 28;
        private const int TopPadding = 6;
        private const int LeftPadding = 8;

        private float _dpi = 1f;
        private int Dip(int v) => Math.Max(1, (int)Math.Round(v * _dpi));

        public ChromeTabStrip()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.TabBar;
            Font = new Font("Segoe UI", 8.5f);
            ApplyDpiScale(1f);
        }

        /// <summary>Scale from 96-DPI design pixels (Qt-style device-independent height). Always pass monitorDpi/96.</summary>
        public void ApplyDpiScale(float scale)
        {
            if (scale < 0.5f || float.IsNaN(scale) || float.IsInfinity(scale)) scale = 1f;
            _dpi = scale;
            // Pixel fonts: GDI point fonts ignore PerMonitorV2 and stay 96-DPI tiny on 4K.
            Font = new Font("Segoe UI", Math.Max(10f, 13f * _dpi), FontStyle.Regular, GraphicsUnit.Pixel);
            int h = Dip(TabHeight + TopPadding + 2);
            MinimumSize = new Size(0, h);
            Height = h;
            Invalidate();
        }

        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            int min = Dip(TabHeight + TopPadding + 2);
            if (height < min) height = min;
            base.SetBoundsCore(x, y, width, height, specified);
        }

        private int GetTabWidth()
        {
            if (Tabs.Count == 0) return Dip(TabMaxWidth);
            int available = Width - Dip(LeftPadding) - Dip(NewTabBtnWidth) - Dip(16);
            int w = available / Math.Max(Tabs.Count, 1);
            return Math.Max(Dip(TabMinWidth), Math.Min(Dip(TabMaxWidth), w));
        }

        private Rectangle GetTabRect(int index)
        {
            int w = GetTabWidth();
            int x = Dip(LeftPadding) + index * (w + 1);
            return new Rectangle(x, Dip(TopPadding), w, Dip(TabHeight));
        }

        private Rectangle GetCloseRect(Rectangle tabRect)
        {
            int cs = Dip(CloseSize);
            int x = tabRect.Right - cs - Dip(8);
            int y = tabRect.Y + (tabRect.Height - cs) / 2;
            return new Rectangle(x, y, cs, cs);
        }

        private Rectangle GetNewTabRect()
        {
            int w = GetTabWidth();
            int x = Dip(LeftPadding) + Tabs.Count * (w + 1);
            return new Rectangle(x + Dip(4), Dip(TopPadding) + Dip(4), Dip(NewTabBtnWidth), Dip(TabHeight) - Dip(8));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintTabs(e.Graphics); }
            catch { }
        }

        private void PaintTabs(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.TabBar);

            for (int i = 0; i < Tabs.Count; i++)
            {
                if (i == SelectedIndex) continue;
                DrawTab(g, i);
            }
            if (SelectedIndex >= 0 && SelectedIndex < Tabs.Count)
                DrawTab(g, SelectedIndex);

            // New tab button (+)
            var newRect = GetNewTabRect();
            using (var brush = new SolidBrush(Theme.InactiveTab))
            using (var path = RoundedRect(newRect, Dip(8)))
                g.FillPath(brush, path);
            using (var pen = new Pen(Theme.ForeLight, Math.Max(1f, 1.5f * _dpi)))
            {
                int cx = newRect.X + newRect.Width / 2;
                int cy = newRect.Y + newRect.Height / 2;
                int arm = Dip(5);
                g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
                g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
            }

            // Bottom line under inactive area
            if (SelectedIndex >= 0 && SelectedIndex < Tabs.Count)
            {
                using var pen = new Pen(Theme.ActiveTab, 2);
                var selRect = GetTabRect(SelectedIndex);
                g.DrawLine(pen, 0, Height - 1, selRect.Left, Height - 1);
                g.DrawLine(pen, selRect.Right, Height - 1, Width, Height - 1);
            }
        }

        private void DrawTab(Graphics g, int index)
        {
            var rect = GetTabRect(index);
            bool active = index == SelectedIndex;
            bool hover = index == HoverIndex && !active;
            Color bg = active ? Theme.ActiveTab : (hover ? Theme.TabHover : Theme.InactiveTab);

            int radius = active ? Dip(10) : Dip(8);
            using (var path = RoundedRectTop(rect, radius))
            using (var brush = new SolidBrush(bg))
                g.FillPath(brush, path);

            var tab = Tabs[index];
            int textRight = rect.Right - Dip(CloseSize) - Dip(16);
            int textLeft = rect.X + Dip(12);
            var textRect = new Rectangle(textLeft, rect.Y + 2, textRight - textLeft, rect.Height - 2);
            var textColor = active ? Theme.ForeLight : Theme.ForeDim;
            TextRenderer.DrawText(g, tab.Title, Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (Tabs.Count > 1 || active)
            {
                var closeRect = GetCloseRect(rect);
                bool closeHover = index == HoverCloseIndex;
                if (closeHover)
                {
                    using var closeBrush = new SolidBrush(Theme.CloseHover);
                    g.FillEllipse(closeBrush, closeRect);
                }
                using var closePen = new Pen(closeHover ? Color.White : Theme.ForeDim, Math.Max(1f, 1.2f * _dpi));
                int m = Dip(4);
                g.DrawLine(closePen, closeRect.X + m, closeRect.Y + m, closeRect.Right - m, closeRect.Bottom - m);
                g.DrawLine(closePen, closeRect.Right - m, closeRect.Y + m, closeRect.X + m, closeRect.Bottom - m);
            }

            if (tab.IsLoading)
            {
                using var loadPen = new Pen(Theme.Accent, 2);
                int pw = tab.LoadProgress > 0
                    ? Math.Max(1, (rect.Width - 8) * Math.Min(tab.LoadProgress, 100) / 100)
                    : (rect.Width - 8) / 3;
                g.DrawLine(loadPen, rect.X + 4, rect.Bottom - 2, rect.X + 4 + pw, rect.Bottom - 2);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragStart.HasValue && _dragTab >= 0)
            {
                int delta = e.X - _dragStart.Value.X;
                int tabW = GetTabWidth() + 1;
                if (Math.Abs(delta) > tabW / 2)
                {
                    int dir = delta > 0 ? 1 : -1;
                    int newIdx = _dragTab + dir;
                    if (newIdx >= 0 && newIdx < Tabs.Count)
                    {
                        (Tabs[_dragTab], Tabs[newIdx]) = (Tabs[newIdx], Tabs[_dragTab]);
                        if (SelectedIndex == _dragTab) SelectedIndex = newIdx;
                        else if (SelectedIndex == newIdx) SelectedIndex = _dragTab;
                        _dragTab = newIdx;
                        _dragStart = e.Location;
                        Invalidate();
                        TabClicked?.Invoke(this, SelectedIndex);
                    }
                }
                return;
            }
            int oldHover = HoverIndex, oldClose = HoverCloseIndex;
            HoverIndex = -1;
            HoverCloseIndex = -1;
            for (int i = 0; i < Tabs.Count; i++)
            {
                var rect = GetTabRect(i);
                if (rect.Contains(e.Location))
                {
                    HoverIndex = i;
                    if (GetCloseRect(rect).Contains(e.Location))
                        HoverCloseIndex = i;
                    break;
                }
            }
            if (oldHover != HoverIndex || oldClose != HoverCloseIndex) Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (HoverIndex != -1 || HoverCloseIndex != -1) { HoverIndex = -1; HoverCloseIndex = -1; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (GetNewTabRect().Contains(e.Location)) { NewTabClicked?.Invoke(this, EventArgs.Empty); return; }
            for (int i = 0; i < Tabs.Count; i++)
            {
                var rect = GetTabRect(i);
                if (!rect.Contains(e.Location)) continue;
                if (GetCloseRect(rect).Contains(e.Location)) TabCloseClicked?.Invoke(this, i);
                else TabClicked?.Invoke(this, i);
                return;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Middle)
            {
                for (int i = 0; i < Tabs.Count; i++)
                    if (GetTabRect(i).Contains(e.Location)) { TabCloseClicked?.Invoke(this, i); return; }
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                for (int i = 0; i < Tabs.Count; i++)
                {
                    var rect = GetTabRect(i);
                    if (rect.Contains(e.Location) && !GetCloseRect(rect).Contains(e.Location))
                    {
                        _dragStart = e.Location;
                        _dragTab = i;
                        break;
                    }
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragStart = null;
            _dragTab = -1;
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (r.Width < 2 || r.Height < 2)
            {
                if (r.Width > 0 && r.Height > 0) path.AddRectangle(r);
                return path;
            }
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath RoundedRectTop(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (r.Width < 2 || r.Height < 2)
            {
                if (r.Width > 0 && r.Height > 0) path.AddRectangle(r);
                return path;
            }
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            path.CloseFigure();
            return path;
        }
    }

    //  bookmark data model (tree) 
    internal sealed class BookmarkNode
    {
        public string Type { get; set; } = "link"; // "link" or "folder"
        public string Title { get; set; } = "";
        public string Href { get; set; } = "";
        public List<BookmarkNode> Children { get; set; } = new();
    }

    internal sealed class DownloadItem
    {
        public string Filename { get; set; } = "";
        public string Path { get; set; } = "";
        public string Url { get; set; } = "";
        public long Received { get; set; }
        public long Total { get; set; }
        public string Status { get; set; } = "Downloading";
    }

}
