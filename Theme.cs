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
    //  colour palette (Chrome-dark inspired) 
    internal static class Theme
    {
        public static readonly Color TitleBar      = Color.FromArgb(32, 33, 36);
        public static readonly Color TabBar        = Color.FromArgb(32, 33, 36);
        public static readonly Color ActiveTab     = Color.FromArgb(53, 54, 58);
        public static readonly Color InactiveTab   = Color.FromArgb(40, 41, 45);
        public static readonly Color TabHover      = Color.FromArgb(48, 49, 53);
        public static readonly Color Toolbar       = Color.FromArgb(53, 54, 58);
        public static readonly Color AddressBox    = Color.FromArgb(41, 42, 45);
        public static readonly Color BookmarkBar   = Color.FromArgb(53, 54, 58);
        public static readonly Color StatusBar     = Color.FromArgb(32, 33, 36);
        public static readonly Color ForeLight     = Color.White;
        public static readonly Color ForeDim       = Color.FromArgb(180, 184, 190);
        public static readonly Color Accent        = Color.FromArgb(138, 180, 248);
        public static readonly Color CloseHover    = Color.FromArgb(200, 60, 60);
        public static readonly Color Border        = Color.FromArgb(60, 64, 67);
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Theme.Toolbar;
        public override Color ToolStripGradientMiddle => Theme.Toolbar;
        public override Color ToolStripGradientEnd => Theme.Toolbar;
        public override Color ToolStripBorder => Theme.Toolbar;
        public override Color ToolStripDropDownBackground => Theme.ActiveTab;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Border;
        public override Color MenuItemSelected => Theme.TabHover;
        public override Color MenuItemSelectedGradientBegin => Theme.TabHover;
        public override Color MenuItemSelectedGradientEnd => Theme.TabHover;
        public override Color ImageMarginGradientBegin => Theme.ActiveTab;
        public override Color ImageMarginGradientMiddle => Theme.ActiveTab;
        public override Color ImageMarginGradientEnd => Theme.ActiveTab;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color StatusStripGradientBegin => Theme.StatusBar;
        public override Color StatusStripGradientEnd => Theme.StatusBar;
        public override Color ButtonSelectedBorder => Theme.Border;
        public override Color ButtonSelectedHighlight => Theme.TabHover;
        public override Color ButtonSelectedGradientBegin => Theme.TabHover;
        public override Color ButtonSelectedGradientEnd => Theme.TabHover;
        public override Color OverflowButtonGradientBegin => Theme.Toolbar;
        public override Color OverflowButtonGradientMiddle => Theme.Toolbar;
        public override Color OverflowButtonGradientEnd => Theme.Toolbar;
    }

    internal sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable()) { RoundedEdges = false; }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var b = new SolidBrush(e.ToolStrip is StatusStrip ? Theme.StatusBar : e.ToolStrip.BackColor);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
    }

    internal enum ChromeIconKind { Back, Forward, Reload, Go, Star, Download, Menu }

    /// <summary>Flat nav button. Icons are drawn as lines - WinForms GDI throws "Parameter is not valid" on several Unicode glyphs ( ) at 175% DPI.</summary>
    internal sealed class ChromeButton : Button
    {
        public ChromeIconKind Kind { get; }
        public bool StarFilled { get; set; }
        public string Badge { get; set; } = "";
        private bool _hover;

        public ChromeButton(ChromeIconKind kind)
        {
            Kind = kind;
            Text = "";
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Toolbar;
            ForeColor = Color.White;
            TabStop = false;
            Cursor = Cursors.Hand;
            Margin = new Padding(0);
            Padding = new Padding(0);
            UseVisualStyleBackColor = false;
            AutoSize = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var rc = ClientRectangle;
                if (rc.Width < 4 || rc.Height < 4) return;
                using (var bg = new SolidBrush(_hover && Enabled ? Theme.TabHover : BackColor))
                    g.FillRectangle(bg, rc);
                var color = Enabled ? Color.White : Theme.ForeDim;
                int side = Math.Max(8, Math.Min(rc.Width, rc.Height) * 5 / 10);
                var icon = new Rectangle(rc.X + (rc.Width - side) / 2, rc.Y + (rc.Height - side) / 2, side, side);
                using var pen = new Pen(color, Math.Max(1.4f, side / 10f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                DrawKind(g, pen, color, icon);
                if (!string.IsNullOrEmpty(Badge))
                {
                    using var f = new Font("Segoe UI", Math.Max(8f, rc.Height / 3.5f), FontStyle.Regular, GraphicsUnit.Pixel);
                    TextRenderer.DrawText(g, Badge, f, rc, Theme.Accent,
                        TextFormatFlags.Bottom | TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
            catch { }
        }

        private void DrawKind(Graphics g, Pen pen, Color color, Rectangle r)
        {
            int x = r.X, y = r.Y, w = r.Width, h = r.Height;
            switch (Kind)
            {
                case ChromeIconKind.Back:
                    g.DrawLines(pen, new[] { new Point(x + w * 2 / 3, y + h / 6), new Point(x + w / 4, y + h / 2), new Point(x + w * 2 / 3, y + h * 5 / 6) });
                    break;
                case ChromeIconKind.Forward:
                    g.DrawLines(pen, new[] { new Point(x + w / 3, y + h / 6), new Point(x + w * 3 / 4, y + h / 2), new Point(x + w / 3, y + h * 5 / 6) });
                    break;
                case ChromeIconKind.Reload:
                    int t = Math.Max(2, w / 6);
                    g.DrawArc(pen, x + t, y + t, w - t * 2, h - t * 2, 45, 280);
                    g.DrawLines(pen, new[] {
                        new Point(x + w - t, y + h / 2),
                        new Point(x + w - t, y + t),
                        new Point(x + w * 2 / 3, y + t + 2)
                    });
                    break;
                case ChromeIconKind.Go:
                    using (var br = new SolidBrush(color))
                    {
                        g.FillPolygon(br, new[] {
                            new Point(x + w / 5, y + h / 6),
                            new Point(x + w * 4 / 5, y + h / 2),
                            new Point(x + w / 5, y + h * 5 / 6)
                        });
                    }
                    break;
                case ChromeIconKind.Star:
                    var star = StarPoints(r);
                    if (StarFilled) { using var br = new SolidBrush(Theme.Accent); g.FillPolygon(br, star); }
                    else g.DrawPolygon(pen, star);
                    break;
                case ChromeIconKind.Download:
                    g.DrawLine(pen, x + w / 2, y + h / 6, x + w / 2, y + h * 2 / 3);
                    g.DrawLines(pen, new[] { new Point(x + w / 4, y + h / 2), new Point(x + w / 2, y + h * 2 / 3), new Point(x + w * 3 / 4, y + h / 2) });
                    g.DrawLine(pen, x + w / 5, y + h * 5 / 6, x + w * 4 / 5, y + h * 5 / 6);
                    break;
                case ChromeIconKind.Menu:
                    int m = h / 5;
                    g.DrawLine(pen, x + w / 5, y + m * 1, x + w * 4 / 5, y + m * 1);
                    g.DrawLine(pen, x + w / 5, y + m * 2 + 1, x + w * 4 / 5, y + m * 2 + 1);
                    g.DrawLine(pen, x + w / 5, y + m * 4, x + w * 4 / 5, y + m * 4);
                    break;
            }
        }

        private static Point[] StarPoints(Rectangle r)
        {
            var pts = new Point[10];
            double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0;
            double orad = Math.Min(r.Width, r.Height) / 2.0, irad = orad * 0.4;
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5;
                double rad = (i % 2 == 0) ? orad : irad;
                pts[i] = new Point((int)Math.Round(cx + rad * Math.Cos(a)), (int)Math.Round(cy + rad * Math.Sin(a)));
            }
            return pts;
        }
    }
}
