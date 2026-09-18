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
    /// <summary>
    /// Dark-themed vertical field editor. Call AddField() for each row, then Finalize(),
    /// then ShowDialog(). Returns DialogResult.OK when the user clicks Save.
    /// </summary>
    internal sealed class FieldEditorForm : Form
    {
        private readonly TableLayoutPanel _layout;
        private int _row;

        public FieldEditorForm(string title)
        {
            Text = title;
            BackColor = Theme.TitleBar;
            ForeColor = Theme.ForeLight;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(380, 100);
            _layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(12),
                BackColor = Theme.TitleBar,
            };
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(_layout);
        }

        public TextBox AddField(string label, string value, bool isPassword = false)
        {
            var lbl = new Label
            {
                Text = label,
                ForeColor = Theme.ForeLight,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = 26,
            };
            var box = new TextBox
            {
                Text = value ?? "",
                BackColor = Theme.AddressBox,
                ForeColor = Theme.ForeLight,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                UseSystemPasswordChar = isPassword,
            };
            _layout.Controls.Add(lbl, 0, _row);
            _layout.Controls.Add(box, 1, _row);
            _row++;
            return box;
        }

        public void Build()
        {
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 44,
                Padding = new Padding(8),
                BackColor = Theme.TitleBar,
            };
            var save = new Button { Text = "Save", DialogResult = DialogResult.OK, BackColor = Theme.ActiveTab, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Width = 90 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, BackColor = Theme.InactiveTab, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Width = 90 };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            Controls.Add(buttons);
            AcceptButton = save;
            CancelButton = cancel;
            // Size to content
            ClientSize = new Size(Math.Max(380, _layout.PreferredSize.Width + 24), _layout.PreferredSize.Height + buttons.Height + 8);
        }
    }

    /// <summary>
    /// Dark-themed confirmation asking whether to open an external application that a web
    /// page requested via a custom URI scheme (e.g. discord://). Returns whether the user
    /// allowed the launch and whether that choice should be remembered for the session.
    /// </summary>
    internal static class ExternalAppPrompt
    {
        public static (bool allow, bool remember) Ask(IWin32Window owner, string scheme, string origin)
        {
            string appName = string.IsNullOrWhiteSpace(scheme)
                ? "an application"
                : char.ToUpper(scheme[0]) + scheme.Substring(1);

            using var form = new Form
            {
                Text = "Open external app",
                BackColor = Theme.TitleBar,
                ForeColor = Theme.ForeLight,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(420, 168),
                ShowInTaskbar = false,
            };

            string where = string.IsNullOrWhiteSpace(origin) ? "This site" : origin;
            var message = new Label
            {
                Text = $"{where} wants to open \"{appName}\".\n\nAllow it to open this application on your device?",
                ForeColor = Theme.ForeLight,
                AutoSize = false,
                Location = new Point(16, 14),
                Size = new Size(388, 66),
                TextAlign = ContentAlignment.TopLeft,
            };

            var remember = new CheckBox
            {
                Text = "Remember my choice for this site",
                ForeColor = Theme.ForeLight,
                AutoSize = true,
                Location = new Point(16, 84),
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 46,
                Padding = new Padding(8),
                BackColor = Theme.TitleBar,
            };
            var open = new Button { Text = "Open", DialogResult = DialogResult.OK, BackColor = Theme.ActiveTab, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, BackColor = Theme.InactiveTab, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Width = 96, Height = 28 };
            buttons.Controls.Add(open);
            buttons.Controls.Add(cancel);

            form.Controls.Add(message);
            form.Controls.Add(remember);
            form.Controls.Add(buttons);
            form.AcceptButton = open;
            form.CancelButton = cancel;

            var result = form.ShowDialog(owner);
            return (result == DialogResult.OK, remember.Checked);
        }
    }

    /// <summary>
    /// Dark-themed list manager for a collection of items: shows items, and Add / Edit / Delete
    /// buttons. addNew returns a new item (or null if cancelled); editExisting mutates/returns the
    /// edited item (or null if cancelled). The backing list is mutated in place.
    /// </summary>
    internal sealed class ListManagerDialog<T> : Form where T : class
    {
        private readonly List<T> _items;
        private readonly Func<T, string> _display;
        private readonly Func<T?> _addNew;
        private readonly Func<T, T?> _editExisting;
        private readonly ListBox _list;

        public ListManagerDialog(string title, List<T> items, Func<T, string> display, Func<T?> addNew, Func<T, T?> editExisting)
        {
            _items = items;
            _display = display;
            _addNew = addNew;
            _editExisting = editExisting;

            Text = title;
            BackColor = Theme.TitleBar;
            ForeColor = Theme.ForeLight;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ClientSize = new Size(460, 320);
            MinimumSize = new Size(360, 240);

            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.AddressBox,
                ForeColor = Theme.ForeLight,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
            };
            _list.DoubleClick += (_, _) => EditSelected();

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.LeftToRight,
                Height = 46,
                Padding = new Padding(8),
                BackColor = Theme.TitleBar,
            };
            Button Mk(string t) => new Button { Text = t, BackColor = Theme.ActiveTab, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Width = 90, Height = 28 };
            var add = Mk("Add");
            var edit = Mk("Edit");
            var del = Mk("Delete");
            var close = Mk("Close");
            add.Click += (_, _) => { var n = _addNew(); if (n != null) { _items.Add(n); Refresh(); } };
            edit.Click += (_, _) => EditSelected();
            del.Click += (_, _) =>
            {
                if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _items.Count)
                {
                    _items.RemoveAt(_list.SelectedIndex);
                    Refresh();
                }
            };
            close.Click += (_, _) => Close();
            bar.Controls.Add(add);
            bar.Controls.Add(edit);
            bar.Controls.Add(del);
            bar.Controls.Add(close);

            Controls.Add(_list);
            Controls.Add(bar);
            Refresh();
        }

        private void EditSelected()
        {
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= _items.Count) return;
            var edited = _editExisting(_items[idx]);
            if (edited != null) { _items[idx] = edited; Refresh(); }
        }

        private new void Refresh()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var item in _items) _list.Items.Add(_display(item));
            _list.EndUpdate();
        }
    }
}
