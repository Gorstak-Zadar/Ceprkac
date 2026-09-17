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
        private void LoadPasswords()
        {
            if (!File.Exists(passwordsFile)) { Logger.Log("PASSWORDS", "No passwords file yet."); return; }
            try
            {
                var encrypted = File.ReadAllBytes(passwordsFile);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
                savedPasswords.Clear();
                // Simple JSON array parse: [{"u":"url","n":"username","p":"password"},...]
                foreach (var entry in ParseCredentialJson(json))
                    savedPasswords.Add(entry);
                Logger.Log("PASSWORDS", $"Loaded {savedPasswords.Count} saved password(s).");
            }
            catch (Exception ex) { Logger.Error("PASSWORDS", "LoadPasswords (corrupted or wrong user)", ex); }
        }

        private void SavePasswords()
        {
            try
            {
                var sb = new StringBuilder("[");
                for (int i = 0; i < savedPasswords.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var c = savedPasswords[i];
                    sb.Append($"{{\"u\":\"{EscapeJson(c.Url)}\",\"n\":\"{EscapeJson(c.Username)}\",\"p\":\"{EscapeJson(c.Password)}\"}}");
                }
                sb.Append(']');
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(passwordsFile, encrypted);
                Logger.Log("PASSWORDS", $"Saved {savedPasswords.Count} password(s) to disk.");
            }
            catch (Exception ex) { Logger.Error("PASSWORDS", "SavePasswords", ex); }
        }

        //  Payment methods (cards) - DPAPI at rest, same scheme as passwords 
        private void LoadCards()
        {
            if (!File.Exists(cardsFile)) return;
            try
            {
                var decrypted = ProtectedData.Unprotect(File.ReadAllBytes(cardsFile), null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
                savedCards.Clear();
                foreach (var c in ParseCardJson(json)) savedCards.Add(c);
            }
            catch { /* corrupted or wrong user - ignore */ }
        }

        private void SaveCards()
        {
            try
            {
                var sb = new StringBuilder("[");
                for (int i = 0; i < savedCards.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var c = savedCards[i];
                    sb.Append('{');
                    sb.Append($"\"label\":\"{EscapeJson(c.Label)}\",");
                    sb.Append($"\"name\":\"{EscapeJson(c.CardholderName)}\",");
                    sb.Append($"\"num\":\"{EscapeJson(c.Number)}\",");
                    sb.Append($"\"em\":\"{EscapeJson(c.ExpMonth)}\",");
                    sb.Append($"\"ey\":\"{EscapeJson(c.ExpYear)}\",");
                    sb.Append($"\"cvc\":\"{EscapeJson(c.Cvc)}\"");
                    sb.Append('}');
                }
                sb.Append(']');
                var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(sb.ToString()), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(cardsFile, encrypted);
            }
            catch { }
        }

        //  Addresses / contact profiles - DPAPI at rest 
        private void LoadAddresses()
        {
            if (!File.Exists(addressesFile)) return;
            try
            {
                var decrypted = ProtectedData.Unprotect(File.ReadAllBytes(addressesFile), null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
                savedAddresses.Clear();
                foreach (var a in ParseAddressJson(json)) savedAddresses.Add(a);
            }
            catch { /* corrupted or wrong user - ignore */ }
        }

        private void SaveAddresses()
        {
            try
            {
                var sb = new StringBuilder("[");
                for (int i = 0; i < savedAddresses.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var a = savedAddresses[i];
                    sb.Append('{');
                    sb.Append($"\"label\":\"{EscapeJson(a.Label)}\",");
                    sb.Append($"\"name\":\"{EscapeJson(a.FullName)}\",");
                    sb.Append($"\"email\":\"{EscapeJson(a.Email)}\",");
                    sb.Append($"\"phone\":\"{EscapeJson(a.Phone)}\",");
                    sb.Append($"\"l1\":\"{EscapeJson(a.Line1)}\",");
                    sb.Append($"\"l2\":\"{EscapeJson(a.Line2)}\",");
                    sb.Append($"\"city\":\"{EscapeJson(a.City)}\",");
                    sb.Append($"\"state\":\"{EscapeJson(a.State)}\",");
                    sb.Append($"\"zip\":\"{EscapeJson(a.PostalCode)}\",");
                    sb.Append($"\"country\":\"{EscapeJson(a.Country)}\"");
                    sb.Append('}');
                }
                sb.Append(']');
                var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(sb.ToString()), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(addressesFile, encrypted);
            }
            catch { }
        }

        private static List<SavedCard> ParseCardJson(string json)
        {
            var list = new List<SavedCard>();
            int pos = 0;
            while (pos < json.Length)
            {
                int objStart = json.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = json.IndexOf('}', objStart);
                if (objEnd < 0) break;
                string obj = json.Substring(objStart + 1, objEnd - objStart - 1);
                var card = new SavedCard
                {
                    Label = ExtractJsonValue(obj, "label"),
                    CardholderName = ExtractJsonValue(obj, "name"),
                    Number = ExtractJsonValue(obj, "num"),
                    ExpMonth = ExtractJsonValue(obj, "em"),
                    ExpYear = ExtractJsonValue(obj, "ey"),
                    Cvc = ExtractJsonValue(obj, "cvc"),
                };
                if (!string.IsNullOrEmpty(card.Number)) list.Add(card);
                pos = objEnd + 1;
            }
            return list;
        }

        private static List<SavedAddress> ParseAddressJson(string json)
        {
            var list = new List<SavedAddress>();
            int pos = 0;
            while (pos < json.Length)
            {
                int objStart = json.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = json.IndexOf('}', objStart);
                if (objEnd < 0) break;
                string obj = json.Substring(objStart + 1, objEnd - objStart - 1);
                var addr = new SavedAddress
                {
                    Label = ExtractJsonValue(obj, "label"),
                    FullName = ExtractJsonValue(obj, "name"),
                    Email = ExtractJsonValue(obj, "email"),
                    Phone = ExtractJsonValue(obj, "phone"),
                    Line1 = ExtractJsonValue(obj, "l1"),
                    Line2 = ExtractJsonValue(obj, "l2"),
                    City = ExtractJsonValue(obj, "city"),
                    State = ExtractJsonValue(obj, "state"),
                    PostalCode = ExtractJsonValue(obj, "zip"),
                    Country = ExtractJsonValue(obj, "country"),
                };
                if (!string.IsNullOrEmpty(addr.FullName) || !string.IsNullOrEmpty(addr.Line1)) list.Add(addr);
                pos = objEnd + 1;
            }
            return list;
        }

        private void ImportPasswordsCsv()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Import Passwords (Chrome/Edge CSV format)",
                Filter = "CSV Files (*.csv)|*.csv|All Files|*.*",
                RestoreDirectory = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var lines = File.ReadAllLines(dlg.FileName);
                int count = 0;
                // Chrome CSV format: name,url,username,password
                // Skip header row
                for (int i = 1; i < lines.Length; i++)
                {
                    var fields = ParseCsvLine(lines[i]);
                    if (fields.Count < 4) continue;
                    string url = fields[1].Trim();
                    string username = fields[2].Trim();
                    string password = fields[3].Trim();
                    if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(username)) continue;

                    // Avoid duplicates
                    if (!savedPasswords.Any(p => string.Equals(p.Url, url, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(p.Username, username, StringComparison.OrdinalIgnoreCase)))
                    {
                        savedPasswords.Add(new SavedCredential { Url = url, Username = username, Password = password });
                        count++;
                    }
                }
                SavePasswords();
                statusLabel.Text = $"Imported {count} passwords.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Import failed:\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClearPasswords()
        {
            if (MessageBox.Show(this, "Clear all saved passwords?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            savedPasswords.Clear();
            SavePasswords();
            statusLabel.Text = "Passwords cleared.";
        }

        private void ManagePasswords()
        {
            using var dlg = new ListManagerDialog<SavedCredential>(
                "Saved Passwords",
                savedPasswords,
                c =>
                {
                    string host = c.Url;
                    try { host = new Uri(c.Url).Host; } catch { }
                    return $"{c.Username}  -  {host}";
                },
                () => EditCredentialDialog(new SavedCredential()),
                existing => EditCredentialDialog(CloneCredential(existing)));
            dlg.Font = _bookmarkFont ?? Font;
            dlg.ShowDialog(this);
            SavePasswords();
            statusLabel.Text = $"{savedPasswords.Count} password(s) saved.";
        }

        private static SavedCredential CloneCredential(SavedCredential c) =>
            new SavedCredential { Url = c.Url, Username = c.Username, Password = c.Password };

        private SavedCredential? EditCredentialDialog(SavedCredential cred)
        {
            using var form = new FieldEditorForm("Saved Password");
            var url = form.AddField("Site URL", cred.Url);
            var user = form.AddField("Username", cred.Username);
            var pwd = form.AddField("Password", cred.Password, isPassword: true);
            form.Build();
            if (form.ShowDialog(this) != DialogResult.OK) return null;
            cred.Url = url.Text.Trim();
            cred.Username = user.Text.Trim();
            cred.Password = pwd.Text;
            if (string.IsNullOrWhiteSpace(cred.Url) || string.IsNullOrWhiteSpace(cred.Username))
            {
                MessageBox.Show(this, "URL and username are required.", "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            if (!cred.Url.Contains("://")) cred.Url = "https://" + cred.Url;
            return cred;
        }

        // 
        // Payment / Address managers (list + add/edit/delete dialogs)
        // 

        private void ManageCards()
        {
            using var dlg = new ListManagerDialog<SavedCard>(
                "Payment Methods",
                savedCards,
                c => c.Display,
                () => EditCardDialog(new SavedCard()),
                existing => EditCardDialog(existing));
            dlg.Font = _bookmarkFont ?? Font;
            dlg.ShowDialog(this);
            SaveCards();
            statusLabel.Text = $"{savedCards.Count} payment method(s) saved.";
        }

        private void ManageAddresses()
        {
            using var dlg = new ListManagerDialog<SavedAddress>(
                "Addresses",
                savedAddresses,
                a => a.Display,
                () => EditAddressDialog(new SavedAddress()),
                existing => EditAddressDialog(existing));
            dlg.Font = _bookmarkFont ?? Font;
            dlg.ShowDialog(this);
            SaveAddresses();
            statusLabel.Text = $"{savedAddresses.Count} address(es) saved.";
        }

        /// <summary>Modal editor for a card. Returns the edited card or null if cancelled.</summary>
        private SavedCard? EditCardDialog(SavedCard card)
        {
            using var form = new FieldEditorForm("Payment Method");
            var label = form.AddField("Nickname (optional)", card.Label);
            var name = form.AddField("Cardholder name", card.CardholderName);
            var number = form.AddField("Card number", card.Number);
            var month = form.AddField("Expiry month (MM)", card.ExpMonth);
            var year = form.AddField("Expiry year (YYYY)", card.ExpYear);
            var cvc = form.AddField("CVC", card.Cvc, isPassword: true);
            form.Build();
            if (form.ShowDialog(this) != DialogResult.OK) return null;

            card.Label = label.Text.Trim();
            card.CardholderName = name.Text.Trim();
            card.Number = new string(number.Text.Where(char.IsDigit).ToArray());
            card.ExpMonth = month.Text.Trim();
            card.ExpYear = year.Text.Trim();
            card.Cvc = cvc.Text.Trim();
            if (card.Number.Length < 12)
            {
                MessageBox.Show(this, "Card number looks too short.", "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return card;
        }

        /// <summary>Modal editor for an address. Returns the edited address or null if cancelled.</summary>
        private SavedAddress? EditAddressDialog(SavedAddress addr)
        {
            using var form = new FieldEditorForm("Address");
            var label = form.AddField("Nickname (optional)", addr.Label);
            var name = form.AddField("Full name", addr.FullName);
            var email = form.AddField("Email", addr.Email);
            var phone = form.AddField("Phone", addr.Phone);
            var l1 = form.AddField("Address line 1", addr.Line1);
            var l2 = form.AddField("Address line 2", addr.Line2);
            var city = form.AddField("City", addr.City);
            var state = form.AddField("State / Region", addr.State);
            var zip = form.AddField("Postal code", addr.PostalCode);
            var country = form.AddField("Country", addr.Country);
            form.Build();
            if (form.ShowDialog(this) != DialogResult.OK) return null;

            addr.Label = label.Text.Trim();
            addr.FullName = name.Text.Trim();
            addr.Email = email.Text.Trim();
            addr.Phone = phone.Text.Trim();
            addr.Line1 = l1.Text.Trim();
            addr.Line2 = l2.Text.Trim();
            addr.City = city.Text.Trim();
            addr.State = state.Text.Trim();
            addr.PostalCode = zip.Text.Trim();
            addr.Country = country.Text.Trim();
            if (string.IsNullOrWhiteSpace(addr.FullName) && string.IsNullOrWhiteSpace(addr.Line1))
            {
                MessageBox.Show(this, "Enter at least a name or a street address.", "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return addr;
        }

        // 
        // Checkout autofill - card + address
        // 

        private async void TryAutoFillPaymentAndAddress(BrowserTab tab)
        {
            if (savedCards.Count == 0 && savedAddresses.Count == 0) return;
            // Debounce
            if ((DateTime.Now - tab.LastAutoFillFormsAttempt).TotalSeconds < 3) return;
            tab.LastAutoFillFormsAttempt = DateTime.Now;

            var core = tab.WebView.CoreWebView2;
            if (core == null) return;
            string pageUrl = core.Source ?? "";
            if (string.IsNullOrEmpty(pageUrl)) return;

            string pathLower = "";
            try { pathLower = (new Uri(pageUrl).PathAndQuery + " " + pageUrl).ToLowerInvariant(); } catch { pathLower = pageUrl.ToLowerInvariant(); }
            bool looksLikeCheckout = pathLower.Contains("checkout") || pathLower.Contains("payment") || pathLower.Contains("billing")
                || pathLower.Contains("shipping") || pathLower.Contains("address") || pathLower.Contains("cart")
                || pathLower.Contains("order") || pathLower.Contains("pay");

            // Detect the presence of card / address fields even when the URL is opaque.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(700 + attempt * 500);
                if (tab.WebView.IsDisposed || tab.WebView.CoreWebView2 == null) return;
                core = tab.WebView.CoreWebView2;

                string detectJs = @"(function(){
                    function has(sel){ try { return !!document.querySelector(sel); } catch(e){ return false; } }
                    var card = has('input[autocomplete=""cc-number""], input[name*=""card"" i][name*=""num"" i], input[id*=""card"" i][id*=""num"" i], input[autocomplete=""cc-csc""]');
                    var addr = has('input[autocomplete=""street-address""], input[autocomplete=""address-line1""], input[name*=""address"" i], input[id*=""address"" i], input[autocomplete=""postal-code""], input[name*=""zip"" i], input[name*=""postal"" i]');
                    return (card?'card':'') + '|' + (addr?'addr':'');
                })()";

                string result;
                try { result = (await core.ExecuteScriptAsync(detectJs)).Trim('"'); }
                catch { continue; }

                bool hasCardFields = result.StartsWith("card");
                bool hasAddrFields = result.EndsWith("addr");
                if (!hasCardFields && !hasAddrFields)
                {
                    if (!looksLikeCheckout) return; // nothing to fill and not a checkout - stop
                    continue;
                }

                // Fill address first (billing/shipping usually precedes card entry).
                if (hasAddrFields && savedAddresses.Count > 0)
                {
                    if (savedAddresses.Count == 1) await FillAddress(core, savedAddresses[0]);
                    else Invoke(() => ShowAddressPicker(tab));
                }
                if (hasCardFields && savedCards.Count > 0)
                {
                    if (savedCards.Count == 1) await FillCard(core, savedCards[0]);
                    else Invoke(() => ShowCardPicker(tab));
                }
                Invoke(() => statusLabel.Text = "Autofilled saved details.");
                return;
            }
        }

        private static string JsStr(string s) =>
            "'" + (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", "") + "'";

        private async Task FillCard(CoreWebView2 core, SavedCard c)
        {
            string js = $@"(function(){{
                function setVal(el, val){{
                    if(!el) return;
                    var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value').set;
                    setter.call(el, val);
                    el.dispatchEvent(new Event('input',{{bubbles:true}}));
                    el.dispatchEvent(new Event('change',{{bubbles:true}}));
                }}
                function pick(){{ for(var i=0;i<arguments.length;i++){{ try{{ var e=document.querySelector(arguments[i]); if(e) return e; }}catch(x){{}} }} return null; }}
                setVal(pick('input[autocomplete=""cc-number""]','input[name*=""cardnumber"" i]','input[name*=""card"" i][name*=""num"" i]','input[id*=""card"" i][id*=""num"" i]'), {JsStr(c.Number)});
                setVal(pick('input[autocomplete=""cc-name""]','input[name*=""cardholder"" i]','input[name*=""ccname"" i]','input[id*=""cardname"" i]'), {JsStr(c.CardholderName)});
                setVal(pick('input[autocomplete=""cc-csc""]','input[name*=""cvc"" i]','input[name*=""cvv"" i]','input[id*=""cvc"" i]','input[id*=""cvv"" i]'), {JsStr(c.Cvc)});
                // Combined MM/YY field
                var exp = pick('input[autocomplete=""cc-exp""]','input[name*=""exp"" i]','input[id*=""exp"" i]');
                if(exp) setVal(exp, {JsStr(c.ExpMonth + "/" + (c.ExpYear.Length >= 2 ? c.ExpYear.Substring(c.ExpYear.Length - 2) : c.ExpYear))});
                setVal(pick('input[autocomplete=""cc-exp-month""]','select[autocomplete=""cc-exp-month""]','input[name*=""expmonth"" i]','[id*=""expmonth"" i]'), {JsStr(c.ExpMonth)});
                setVal(pick('input[autocomplete=""cc-exp-year""]','select[autocomplete=""cc-exp-year""]','input[name*=""expyear"" i]','[id*=""expyear"" i]'), {JsStr(c.ExpYear)});
            }})()";
            try { await core.ExecuteScriptAsync(js); } catch { }
        }

        private async Task FillAddress(CoreWebView2 core, SavedAddress a)
        {
            string js = $@"(function(){{
                function setVal(el, val){{
                    if(!el || !val) return;
                    var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value').set
                              || Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype,'value').set;
                    setter.call(el, val);
                    el.dispatchEvent(new Event('input',{{bubbles:true}}));
                    el.dispatchEvent(new Event('change',{{bubbles:true}}));
                }}
                function pick(){{ for(var i=0;i<arguments.length;i++){{ try{{ var e=document.querySelector(arguments[i]); if(e) return e; }}catch(x){{}} }} return null; }}
                setVal(pick('input[autocomplete=""name""]','input[name*=""fullname"" i]','input[name=""name""]','input[id*=""fullname"" i]'), {JsStr(a.FullName)});
                setVal(pick('input[autocomplete=""email""]','input[type=""email""]','input[name*=""email"" i]'), {JsStr(a.Email)});
                setVal(pick('input[autocomplete=""tel""]','input[type=""tel""]','input[name*=""phone"" i]'), {JsStr(a.Phone)});
                setVal(pick('input[autocomplete=""address-line1""]','input[autocomplete=""street-address""]','input[name*=""address1"" i]','input[name*=""street"" i]','input[id*=""address1"" i]'), {JsStr(a.Line1)});
                setVal(pick('input[autocomplete=""address-line2""]','input[name*=""address2"" i]','input[id*=""address2"" i]'), {JsStr(a.Line2)});
                setVal(pick('input[autocomplete=""address-level2""]','input[name*=""city"" i]','input[id*=""city"" i]'), {JsStr(a.City)});
                setVal(pick('input[autocomplete=""address-level1""]','input[name*=""state"" i]','input[name*=""region"" i]','input[id*=""state"" i]'), {JsStr(a.State)});
                setVal(pick('input[autocomplete=""postal-code""]','input[name*=""zip"" i]','input[name*=""postal"" i]','input[id*=""zip"" i]','input[id*=""postal"" i]'), {JsStr(a.PostalCode)});
                setVal(pick('input[autocomplete=""country""]','input[name*=""country"" i]','select[name*=""country"" i]','[id*=""country"" i]'), {JsStr(a.Country)});
            }})()";
            try { await core.ExecuteScriptAsync(js); } catch { }
        }

        private void ShowCardPicker(BrowserTab tab)
        {
            var picker = new ContextMenuStrip { BackColor = Theme.ActiveTab, ForeColor = Color.White, ShowImageMargin = false };
            picker.Items.Add(new ToolStripMenuItem("Choose a card:") { Enabled = false, ForeColor = Theme.ForeDim });
            picker.Items.Add(new ToolStripSeparator());
            foreach (var card in savedCards)
            {
                var c = card;
                var item = new ToolStripMenuItem(c.Display) { ForeColor = Color.White, BackColor = Theme.ActiveTab };
                item.Click += async (_, _) =>
                {
                    picker.Close();
                    var core = tab.WebView.CoreWebView2;
                    if (core != null) { await FillCard(core, c); statusLabel.Text = $"Filled card ---- {c.Last4}"; }
                };
                picker.Items.Add(item);
            }
            var pt = webViewPanel.PointToScreen(new Point(webViewPanel.Width / 2 - 100, 10));
            picker.Show(pt);
        }

        private void ShowAddressPicker(BrowserTab tab)
        {
            var picker = new ContextMenuStrip { BackColor = Theme.ActiveTab, ForeColor = Color.White, ShowImageMargin = false };
            picker.Items.Add(new ToolStripMenuItem("Choose an address:") { Enabled = false, ForeColor = Theme.ForeDim });
            picker.Items.Add(new ToolStripSeparator());
            foreach (var address in savedAddresses)
            {
                var a = address;
                var item = new ToolStripMenuItem(a.Display) { ForeColor = Color.White, BackColor = Theme.ActiveTab };
                item.Click += async (_, _) =>
                {
                    picker.Close();
                    var core = tab.WebView.CoreWebView2;
                    if (core != null) { await FillAddress(core, a); statusLabel.Text = $"Filled address for {a.FullName}"; }
                };
                picker.Items.Add(item);
            }
            var pt = webViewPanel.PointToScreen(new Point(webViewPanel.Width / 2 - 100, 10));
            picker.Show(pt);
        }

        private async void TryAutoFillCredentials(BrowserTab tab)
        {
            if (savedPasswords.Count == 0) return;
            var core = tab.WebView.CoreWebView2;
            if (core == null) return;

            string pageUrl = core.Source ?? "";
            if (string.IsNullOrEmpty(pageUrl)) return;

            // Per-URL de-dupe: if a loop is already running for THIS exact URL, skip.
            // But a genuinely different URL (identifier -> password step) always proceeds
            // even while an older loop is still retrying - the older loop self-cancels when
            // it notices core.Source moved on. This is what removes both the "need to
            // refresh" symptom and the stuck-on-email-page symptom.
            if (tab.AutoFillInProgress
                && string.Equals(tab.LastAutoFillUrl, pageUrl, StringComparison.OrdinalIgnoreCase))
                return;
            if (!tab.AutoFillInProgress
                && string.Equals(tab.LastAutoFillUrl, pageUrl, StringComparison.OrdinalIgnoreCase)
                && (DateTime.Now - tab.LastAutoFillAttempt).TotalSeconds < 3)
                return;

            string? pageDomain = null;
            try { pageDomain = new Uri(pageUrl).Host.ToLower(); } catch { return; }

            var domainMatches = savedPasswords.Where(p =>
            {
                try
                {
                    var savedHost = new Uri(p.Url).Host.ToLower();
                    // Match exact host or registrable-domain suffix so accounts.google.com
                    // credentials fill on the google.com password step and vice versa.
                    return savedHost == pageDomain
                        || pageDomain!.EndsWith("." + savedHost, StringComparison.Ordinal)
                        || savedHost.EndsWith("." + pageDomain, StringComparison.Ordinal);
                }
                catch { return false; }
            }).ToList();

            // When the page domain matches saved logins we can auto-fill silently. When it does
            // NOT (a Google/OAuth login popup on a site never visited, or a domain that simply
            // isn't saved yet), we still want to OFFER every saved login through the picker so
            // the user can pick one - never silently. This is what makes cross-domain login
            // popups (e.g. a Google sign-in on fmbase.co.uk) fillable.
            bool hasDomainMatch = domainMatches.Count > 0;
            var matches = hasDomainMatch ? domainMatches : savedPasswords.ToList();

            Logger.Log("AUTOFILL", $"Credential check for {pageDomain} ({pageUrl}): "
                + $"domainMatches={domainMatches.Count}, offering={matches.Count}, hasDomainMatch={hasDomainMatch}");

            if (matches.Count == 0) return;
            // User already chose "type manually" (or closed the menu) for this host - leave them alone.
            if (IsCredentialOfferDismissed(pageUrl)) { Logger.Log("AUTOFILL", $"Offer dismissed/suppressed for {pageDomain}."); return; }

            // Login-like page heuristic (path keywords). Password fields always count as login
            // regardless of path. Username-only uses explicit email/username selectors - not a
            // generic text-box fallback - so we can offer on any site size/layout consistently.
            string pathLower = "";
            try { pathLower = new Uri(pageUrl).PathAndQuery.ToLower(); } catch { }
            bool isLoginPage = pathLower.Contains("login") || pathLower.Contains("signin") || pathLower.Contains("sign-in")
                || pathLower.Contains("auth") || pathLower.Contains("account") || pathLower.Contains("sso")
                || pathLower.Contains("challenge") || pathLower.Contains("pwd") || pathLower.Contains("identifier")
                || pathLower.Contains("register") || pathLower.Contains("signup") || pathLower.Contains("sign-up")
                || pathLower.Contains("session") || pathLower.Contains("oauth") || pathLower.Contains("passwd");

            // Mark this URL as claimed up front so a concurrent NavigationCompleted /
            // SourceChanged pair does not run two loops against the same page.
            tab.LastAutoFillAttempt = DateTime.Now;
            tab.LastAutoFillUrl = pageUrl;
            tab.AutoFillInProgress = true;
            long myToken = ++tab.AutoFillToken;
            try
            {
            // Retry up to 8 times with increasing delays for SPA pages / slow forms
            for (int attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(attempt == 0 ? 400 : (600 + (attempt * 450)));

                if (tab.WebView.IsDisposed || tab.WebView.CoreWebView2 == null) return;
                core = tab.WebView.CoreWebView2;

                // Self-cancel if a newer autofill invocation superseded this one, or the
                // page navigated away from the URL this loop started for (email -> password
                // step). Either way the newer invocation owns the current page.
                if (tab.AutoFillToken != myToken) return;
                if (!string.Equals(core.Source ?? "", pageUrl, StringComparison.OrdinalIgnoreCase))
                    return;

                // Strong username selectors only - never treat a random search box as login.
                string checkJs = @"(function() {
                    function visible(el){ return el && el.offsetParent !== null && el.offsetWidth > 0 && el.offsetHeight > 0; }
                    var pws = document.querySelectorAll('input[type=""password""]');
                    var pw = null;
                    for (var i = 0; i < pws.length; i++) { if (visible(pws[i])) { pw = pws[i]; break; } }
                    if (!pw && pws.length) pw = pws[0];
                    var emailOrUser = document.querySelector(
                        'input[type=""email""], input[type=""tel""], input[name=""email""], input[name=""username""], ' +
                        'input[name=""login""], input[name=""user""], input[name=""identifier""], input[id*=""email"" i], ' +
                        'input[id*=""user"" i], input[id*=""login"" i], input[autocomplete=""username""], ' +
                        'input[autocomplete=""email""], input[aria-label*=""mail"" i], input[aria-label*=""user"" i], ' +
                        'input[aria-label*=""phone"" i], input[aria-label*=""login"" i], input[aria-label*=""Email""], ' +
                        'input[aria-label*=""Phone""], input[placeholder*=""email"" i], input[placeholder*=""user"" i]'
                    );
                    if (emailOrUser && !visible(emailOrUser)) {
                        var cands = document.querySelectorAll(
                            'input[type=""email""], input[type=""tel""], input[name=""email""], input[name=""username""], ' +
                            'input[autocomplete=""username""], input[autocomplete=""email""]');
                        emailOrUser = null;
                        for (var j = 0; j < cands.length; j++) { if (visible(cands[j])) { emailOrUser = cands[j]; break; } }
                    }
                    if (pw && emailOrUser) return 'both';
                    if (pw) return 'pwonly';
                    if (emailOrUser) return 'useronly';
                    return 'none';
                })()";

                try
                {
                    var result = await core.ExecuteScriptAsync(checkJs);
                    var fieldStatus = result.Trim('"');

                    if (fieldStatus == "none") continue;
                    Logger.Log("AUTOFILL", $"Login fields detected ('{fieldStatus}') on {pageDomain}, attempt {attempt + 1}, hasDomainMatch={hasDomainMatch}.");

                    // A password field present means the page is a login step regardless of the
                    // URL path - this is what makes Google's separate password page work.
                    if (fieldStatus == "pwonly")
                    {
                        // Only auto-fill silently for a single credential that actually belongs
                        // to this domain. Otherwise show the picker so the user chooses.
                        if (hasDomainMatch && matches.Count == 1)
                        {
                            await FillPasswordOnly(core, matches[0].Password);
                            Logger.Log("AUTOFILL", $"Auto-filled password for {pageDomain}.");
                            Invoke(() => statusLabel.Text = $"Auto-filled password for {pageDomain}");
                        }
                        else
                        {
                            Logger.Log("AUTOFILL", $"Offering password picker for {pageDomain} ({matches.Count} option(s)).");
                            Invoke(() => ShowCredentialPicker(tab, matches, passwordOnly: true));
                        }
                        return;
                    }

                    if (fieldStatus == "both")
                    {
                        if (hasDomainMatch && matches.Count == 1)
                        {
                            await FillCredentials(core, matches[0].Username, matches[0].Password);
                            Logger.Log("AUTOFILL", $"Auto-filled credentials for {pageDomain}.");
                            Invoke(() => statusLabel.Text = $"Auto-filled credentials for {pageDomain}");
                        }
                        else
                        {
                            Logger.Log("AUTOFILL", $"Offering credential picker for {pageDomain} ({matches.Count} option(s)).");
                            Invoke(() => ShowCredentialPicker(tab, matches));
                        }
                        return;
                    }

                    if (fieldStatus == "useronly")
                    {
                        // Explicit username/email field found. Offer even on non-keyword paths
                        // (some sites use /welcome or /). Auto-fill silently only on login-like paths
                        // when there is a single matching credential for THIS domain; otherwise
                        // always show the picker so small windows / odd layouts / cross-domain
                        // popups still get an offer.
                        if (hasDomainMatch && matches.Count == 1 && isLoginPage)
                        {
                            await FillUsernameOnly(core, matches[0].Username);
                            Logger.Log("AUTOFILL", $"Filled username for {pageDomain} (continue to password step).");
                            Invoke(() => statusLabel.Text = $"Filled username for {pageDomain} (continue to password step)");
                        }
                        else
                        {
                            Logger.Log("AUTOFILL", $"Offering username picker for {pageDomain} ({matches.Count} option(s)).");
                            Invoke(() => ShowCredentialPicker(tab, matches));
                        }
                        return;
                    }
                }
                catch (Exception ex) { Logger.Error("AUTOFILL", $"field check attempt {attempt + 1} on {pageDomain}", ex); }
            }
            }
            finally
            {
                // Only clear the guard if we are still the current loop; a newer invocation
                // may already own it.
                if (tab.AutoFillToken == myToken)
                    tab.AutoFillInProgress = false;
            }
        }

        // Fill a chosen credential from the right-click "Fill password" menu. Unlike the
        // automatic path this makes no assumptions about the domain or page type - the user
        // explicitly asked for it. It fills whatever login fields it can find, preferring the
        // form nearest the element that was focused/right-clicked, and it walks same-origin
        // iframes so OAuth/embedded login boxes are covered. Always logs the outcome.
        private async Task FillCredentialFromMenu(CoreWebView2? core, SavedCredential cred)
        {
            if (core == null)
            {
                Logger.Log("AUTOFILL", "FillCredentialFromMenu: core was null.");
                try { statusLabel.Text = "Could not fill - page not ready."; } catch { }
                return;
            }

            string safeUser = JsRaw(cred.Username);
            string safePwd = JsRaw(cred.Password);

            // Runs in the top document AND every reachable same-origin iframe. Returns a short
            // status token describing what was filled so we can log/report it.
            string js = $@"(function() {{
                var USER = '{safeUser}';
                var PWD  = '{safePwd}';
                function setVal(el, val) {{
                    if (!el) return false;
                    try {{
                        var proto = el.tagName === 'TEXTAREA' ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;
                        var setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
                        setter.call(el, val);
                        el.dispatchEvent(new Event('input', {{bubbles:true}}));
                        el.dispatchEvent(new Event('change', {{bubbles:true}}));
                        el.dispatchEvent(new Event('blur', {{bubbles:true}}));
                        return true;
                    }} catch(e) {{ return false; }}
                }}
                function visible(el) {{ return el && el.offsetParent !== null && el.offsetWidth > 0 && el.offsetHeight > 0; }}
                function isUserField(el) {{
                    if (!el || el.tagName !== 'INPUT') return false;
                    var t = (el.type||'').toLowerCase();
                    if (t === 'password' || t === 'hidden' || t === 'submit' || t === 'button' || t === 'checkbox' || t === 'radio' || t === 'file') return false;
                    if (t === 'email' || t === 'tel') return true;
                    var n = ((el.name||'')+' '+(el.id||'')+' '+(el.autocomplete||'')+' '+(el.placeholder||'')+' '+(el.getAttribute('aria-label')||'')).toLowerCase();
                    return t === 'text' || t === '' || /user|email|login|identifier|phone|account|name/.test(n);
                }}
                function fillDoc(doc) {{
                    var filledUser = false, filledPw = false;
                    // Anchor on the focused element if it's a login field, else the first visible password.
                    var active = doc.activeElement;
                    var scope = null;
                    if (active && active.tagName === 'INPUT') scope = active.form || doc;
                    var pw = null;
                    var pws = doc.querySelectorAll('input[type=""password""]');
                    for (var i = 0; i < pws.length; i++) {{ if (visible(pws[i])) {{ pw = pws[i]; break; }} }}
                    if (!pw && pws.length) pw = pws[0];
                    var root = (pw && pw.form) ? pw.form : (scope || doc);
                    // Username
                    if (USER) {{
                        var user = null;
                        if (active && isUserField(active)) user = active;
                        if (!user) {{
                            var cands = root.querySelectorAll('input');
                            for (var j = 0; j < cands.length; j++) {{ if (isUserField(cands[j]) && visible(cands[j])) {{ user = cands[j]; break; }} }}
                        }}
                        if (!user && root !== doc) {{
                            var cands2 = doc.querySelectorAll('input');
                            for (var k = 0; k < cands2.length; k++) {{ if (isUserField(cands2[k]) && visible(cands2[k])) {{ user = cands2[k]; break; }} }}
                        }}
                        if (user) filledUser = setVal(user, USER);
                    }}
                    // Password
                    if (PWD && pw) filledPw = setVal(pw, PWD);
                    return (filledUser ? 'u' : '') + (filledPw ? 'p' : '');
                }}
                var result = fillDoc(document);
                // Same-origin iframes (embedded login boxes / OAuth frames).
                if (!result) {{
                    var frames = document.querySelectorAll('iframe');
                    for (var f = 0; f < frames.length; f++) {{
                        try {{
                            var idoc = frames[f].contentDocument;
                            if (idoc) {{ var r = fillDoc(idoc); if (r) {{ result = r; break; }} }}
                        }} catch(e) {{ /* cross-origin - cannot reach */ }}
                    }}
                }}
                return result || 'none';
            }})()";

            try
            {
                var raw = await core.ExecuteScriptAsync(js);
                string status = (raw ?? "").Trim('"');
                Logger.Log("AUTOFILL", $"FillCredentialFromMenu result='{status}' for {cred.Username} on {SafeSourceForLog(core)}");
                try
                {
                    statusLabel.Text = status switch
                    {
                        "up" => $"Filled username and password for {cred.Username}",
                        "u" => $"Filled username for {cred.Username}",
                        "p" => $"Filled password for {cred.Username}",
                        _ => "No login field found to fill on this page.",
                    };
                }
                catch { }
            }
            catch (Exception ex)
            {
                Logger.Error("AUTOFILL", "FillCredentialFromMenu execute", ex);
                try { statusLabel.Text = $"Autofill error: {ex.Message}"; } catch { }
            }
        }

        private static string SafeSourceForLog(CoreWebView2? core)
        {
            try { return core?.Source ?? ""; } catch { return ""; }
        }

        // Escape an arbitrary string for embedding inside a single-quoted JS literal.
        private static string JsRaw(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", "");

        private async Task FillUsernameOnly(CoreWebView2 core, string username)
        {
            string safeUser = username.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "");
            string js = $@"(function() {{
                var user = document.querySelector(
                    'input[type=""email""], input[type=""tel""], input[name=""email""], input[name=""username""], ' +
                    'input[name=""login""], input[name=""user""], input[autocomplete=""username""], ' +
                    'input[autocomplete=""email""], input[aria-label*=""mail"" i], input[aria-label*=""user"" i], ' +
                    'input[aria-label*=""phone"" i], input[aria-label*=""login"" i], input[aria-label*=""Email""], ' +
                    'input[aria-label*=""Phone""]'
                );
                if (!user) {{
                    var all = document.querySelectorAll('input[type=""text""], input:not([type])');
                    for (var i = 0; i < all.length; i++) {{
                        if (all[i].offsetParent !== null && all[i].offsetWidth > 0) {{ user = all[i]; break; }}
                    }}
                }}
                if (user) {{
                    var nativeSet = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                    nativeSet.call(user, '{safeUser}');
                    user.dispatchEvent(new Event('input', {{bubbles:true}}));
                    user.dispatchEvent(new Event('change', {{bubbles:true}}));
                    user.dispatchEvent(new Event('blur', {{bubbles:true}}));
                }}
            }})()";
            await core.ExecuteScriptAsync(js);
        }

        private async Task FillPasswordOnly(CoreWebView2 core, string password)
        {
            // Fill ONLY the visible password field. Password-only steps (Google's
            // /signin/v2/challenge/pwd, re-auth prompts) carry a hidden username input
            // that the site populates itself; writing to it can break the flow.
            string safePwd = password.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "");
            string js = $@"(function() {{
                var pws = document.querySelectorAll('input[type=""password""]');
                var pw = null;
                for (var i = 0; i < pws.length; i++) {{
                    // Prefer a visible password field over a hidden/offscreen one.
                    if (pws[i].offsetParent !== null && pws[i].offsetWidth > 0) {{ pw = pws[i]; break; }}
                }}
                if (!pw && pws.length) pw = pws[0];
                if (!pw) return;
                var nativeSet = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                nativeSet.call(pw, '{safePwd}');
                pw.dispatchEvent(new Event('input', {{bubbles:true}}));
                pw.dispatchEvent(new Event('change', {{bubbles:true}}));
                pw.dispatchEvent(new Event('blur', {{bubbles:true}}));
            }})()";
            await core.ExecuteScriptAsync(js);
        }

        private async Task FillCredentials(CoreWebView2 core, string username, string password)
        {
            string safeUser = username.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "");
            string safePwd = password.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "");

            string fillJs = $@"(function() {{
                var pw = document.querySelector('input[type=""password""]');
                if (!pw) return;
                var form = pw.closest('form') || document.body;
                var user = form.querySelector([
                    'input[type=""email""]',
                    'input[name=""email""]',
                    'input[name=""username""]',
                    'input[name=""login""]',
                    'input[name=""user""]',
                    'input[autocomplete=""username""]',
                    'input[autocomplete=""email""]',
                    'input[type=""text""][name*=""user""]',
                    'input[type=""text""][name*=""login""]',
                    'input[type=""text""][name*=""email""]',
                    'input[type=""text""][autocomplete*=""user""]',
                    'input[aria-label*=""mail""]',
                    'input[aria-label*=""user""]',
                    'input[aria-label*=""login""]',
                    'input[aria-label*=""phone""]'
                ].join(', '));
                if (!user) {{
                    var inputs = form.querySelectorAll('input[type=""text""], input[type=""email""], input:not([type])');
                    for (var i = 0; i < inputs.length; i++) {{
                        var inp = inputs[i];
                        if (inp !== pw && inp.offsetParent !== null) {{ user = inp; break; }}
                    }}
                }}
                if (user) {{
                    var nativeSet = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                    nativeSet.call(user, '{safeUser}');
                    user.dispatchEvent(new Event('input', {{bubbles:true}}));
                    user.dispatchEvent(new Event('change', {{bubbles:true}}));
                }}
                var nativeSet2 = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                nativeSet2.call(pw, '{safePwd}');
                pw.dispatchEvent(new Event('input', {{bubbles:true}}));
                pw.dispatchEvent(new Event('change', {{bubbles:true}}));
            }})()";

            await core.ExecuteScriptAsync(fillJs);
        }

        // Non-modal menu - never ShowDialog. A modal dialog blocked the page so the user
        // could not type a password by hand (especially with multiple saved logins).
        private void ShowCredentialPicker(BrowserTab tab, List<SavedCredential> matches, bool passwordOnly = false)
        {
            if (matches.Count == 0) return;
            string pageUrl = "";
            try { pageUrl = tab.WebView.CoreWebView2?.Source ?? tab.Url ?? ""; } catch { pageUrl = tab.Url ?? ""; }
            if (IsCredentialOfferDismissed(pageUrl)) return;
            if ((DateTime.Now - lastCredentialOfferUi).TotalMilliseconds < 1500) return;
            lastCredentialOfferUi = DateTime.Now;

            void ShowUi()
            {
                if (credentialPickerMenu != null)
                {
                    var old = credentialPickerMenu;
                    credentialPickerMenu = null;
                    try { old.Close(); } catch { }
                    // Defer dispose so ModalMenuFilter finishes with it first.
                    BeginInvoke(new Action(() => { try { old.Dispose(); } catch { } }));
                }

                bool chose = false;
                var picker = new ContextMenuStrip
                {
                    BackColor = Theme.ActiveTab,
                    ForeColor = Color.White,
                    ShowImageMargin = false,
                    AutoClose = true,
                };
                credentialPickerMenu = picker;
                picker.Items.Add(new ToolStripMenuItem(
                    passwordOnly ? "Choose a password:" : "Choose an account:")
                { Enabled = false, ForeColor = Theme.ForeDim });
                picker.Items.Add(new ToolStripSeparator());

                foreach (var cred in matches)
                {
                    var c = cred;
                    string host = c.Url;
                    try { host = new Uri(c.Url).Host; } catch { }
                    var item = new ToolStripMenuItem($"{c.Username}  ({host})")
                    {
                        ForeColor = Color.White,
                        BackColor = Theme.ActiveTab,
                    };
                    item.Click += async (_, _) =>
                    {
                        chose = true;
                        // Capture CoreWebView2 BEFORE closing - picker.Close() fires the
                        // Closed event synchronously which can trigger focus/navigation
                        // events that dispose the CoreWebView2 before we get to use it.
                        var core = tab.WebView?.IsDisposed == false
                            ? tab.WebView.CoreWebView2 : null;
                        picker.Close();
                        if (core == null)
                        {
                            try { Invoke(() => statusLabel.Text = "Could not fill - page not ready."); } catch { }
                            return;
                        }
                        try
                        {
                            if (passwordOnly)
                            {
                                await FillPasswordOnly(core, c.Password);
                                try { Invoke(() => statusLabel.Text = $"Filled password for {c.Username}"); } catch { }
                            }
                            else
                            {
                                await FillCredentials(core, c.Username, c.Password);
                                try { Invoke(() => statusLabel.Text = $"Filled credentials for {c.Username}"); } catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            try { Invoke(() => statusLabel.Text = $"Autofill error: {ex.Message}"); } catch { }
                        }
                    };
                    picker.Items.Add(item);
                }

                picker.Items.Add(new ToolStripSeparator());
                var dismiss = new ToolStripMenuItem("Type password manually...")
                {
                    ForeColor = Color.White,
                    BackColor = Theme.ActiveTab,
                };
                dismiss.Click += (_, _) =>
                {
                    chose = true;
                    DismissCredentialOffer(pageUrl);
                    picker.Close();
                    try { tab.WebView.Focus(); } catch { }
                };
                picker.Items.Add(dismiss);

                picker.Closed += (_, _) =>
                {
                    // Closed without picking - temporarily suppress so an accidental
                    // click-away doesn't permanently hide the picker for this session.
                    if (!chose) TemporarilySuppressCredentialOffer(pageUrl);
                    if (ReferenceEquals(credentialPickerMenu, picker))
                        credentialPickerMenu = null;
                    // Never Dispose() synchronously inside Closed - WinForms'
                    // ModalMenuFilter still holds a reference and will call
                    // set_Visible on the next message pump tick, throwing
                    // ObjectDisposedException. Defer to let WinForms finish first.
                    BeginInvoke(new Action(() => { try { picker.Dispose(); } catch { } }));
                };

                // Clamp to the working area so a tiny window still shows the menu on-screen.
                Point pt;
                try
                {
                    var screen = Screen.FromControl(this).WorkingArea;
                    pt = webViewPanel.PointToScreen(new Point(Math.Max(8, webViewPanel.Width / 2 - 80), 10));
                    if (webViewPanel.Height < 120)
                        pt = new Point(pt.X, PointToScreen(new Point(0, navPanel.Bottom)).Y + 4);
                    pt = new Point(
                        Math.Max(screen.Left + 4, Math.Min(pt.X, screen.Right - 200)),
                        Math.Max(screen.Top + 4, Math.Min(pt.Y, screen.Bottom - 80)));
                }
                catch
                {
                    pt = webViewPanel.PointToScreen(new Point(20, 10));
                }
                picker.Show(pt);
            }

            if (InvokeRequired) BeginInvoke(new Action(ShowUi));
            else ShowUi();
        }

        private bool IsCredentialOfferDismissed(string? pageUrl)
        {
            if (string.IsNullOrEmpty(pageUrl)) return false;
            string host;
            try { host = new Uri(pageUrl!).Host.ToLowerInvariant(); }
            catch { host = pageUrl!; }

            // Permanently dismissed - user clicked "Type password manually..."
            if (dismissedCredentialHosts.Contains(host)) return true;

            // Temporarily suppressed - user closed without picking (maybe by accident)
            if (recentlyClosedCredentialHosts.TryGetValue(host, out var closedAt))
            {
                if ((DateTime.Now - closedAt).TotalSeconds < 20)
                    return true;
                recentlyClosedCredentialHosts.Remove(host);
            }
            return false;
        }

        // Permanent dismiss - user explicitly chose "Type password manually..."
        private void DismissCredentialOffer(string? pageUrl)
        {
            if (string.IsNullOrEmpty(pageUrl)) return;
            string host;
            try { host = new Uri(pageUrl!).Host.ToLowerInvariant(); }
            catch { host = pageUrl!; }
            dismissedCredentialHosts.Add(host);
        }

        // Temporary suppress - user closed without picking (accident or changed mind)
        private void TemporarilySuppressCredentialOffer(string? pageUrl)
        {
            if (string.IsNullOrEmpty(pageUrl)) return;
            string host;
            try { host = new Uri(pageUrl!).Host.ToLowerInvariant(); }
            catch { host = pageUrl!; }
            recentlyClosedCredentialHosts[host] = DateTime.Now;
        }

        // Save-password prompt only - do NOT re-offer on every field focus (that blocked typing).
        private const string AutofillAssistJs = @"
(function(){
  if (window.__ceprkacAutofillAssist) return;
  window.__ceprkacAutofillAssist = true;
  function post(obj){
    try { chrome.webview.postMessage(JSON.stringify(obj)); } catch(e) {}
  }
  function isUserField(el){
    if (!el || el.tagName !== 'INPUT') return false;
    var t = (el.type||'').toLowerCase();
    if (t === 'password' || t === 'hidden' || t === 'submit' || t === 'button' || t === 'checkbox' || t === 'radio' || t === 'file') return false;
    if (t === 'email' || t === 'tel') return true;
    var n = ((el.name||'') + ' ' + (el.id||'') + ' ' + (el.autocomplete||'') + ' ' + (el.placeholder||'') + ' ' + (el.getAttribute('aria-label')||'')).toLowerCase();
    return /user|email|login|identifier|phone|account/.test(n);
  }
  function collect(form){
    var root = form || document;
    var pw = root.querySelector('input[type=""password""]');
    if (!pw || !pw.value) return null;
    var user = null;
    var cands = root.querySelectorAll('input[type=""email""],input[type=""tel""],input[type=""text""],input:not([type])');
    for (var i=0;i<cands.length;i++){
      if (isUserField(cands[i]) && cands[i].value) { user = cands[i]; break; }
    }
    return { url: location.href, username: user ? user.value : '', password: pw.value };
  }
  document.addEventListener('submit', function(e){
    try {
      var data = collect(e.target);
      if (data && data.password) post({type:'password-submit', url: data.url, username: data.username, password: data.password});
    } catch(ex) {}
  }, true);
})();";

        // Suppress Google One Tap / FedCM prompts. The FedCM credential UI is a
        // WebView2-managed overlay; when it appears (e.g. The Guardian shows the
        // ""Continue to theguardian.com with google.com"" prompt) it fights the WinForms
        // omnibox for OS focus the instant you click the address bar, and the window
        // blinks ~100x/sec. Ceprkac has its own password manager, so we neutralise the
        // FedCM path only:
        //   - navigator.credentials.get({identity:...})  -> the FedCM / One Tap request
        // Passkeys ({publicKey:...}) and classic Credential Management
        // ({password:...}/{federated:...}) are passed straight through untouched, so the
        // 0.8.6 passkey support is preserved.
        // Runs in the main world before page scripts, in every frame.
        private const string FedCmSuppressJs = @"
(function(){
  if (window.__ceprkacNoFedCm) return;
  window.__ceprkacNoFedCm = true;
  try {
    var creds = navigator.credentials;
    if (creds && typeof creds.get === 'function') {
      var origGet = creds.get.bind(creds);
      creds.get = function(options){
        try {
          if (options && options.identity) {
            // FedCM / Google One Tap request - refuse quietly so no prompt appears.
            return Promise.reject(new DOMException('FedCM disabled', 'NotAllowedError'));
          }
        } catch(e) {}
        return origGet(options);
      };
    }
  } catch(e) {}
  // Belt-and-suspenders: neutralise the GSI One Tap client if the page loads it.
  try {
    function neuter(){
      try {
        if (window.google && google.accounts && google.accounts.id) {
          google.accounts.id.prompt = function(){};
          google.accounts.id.renderButton = google.accounts.id.renderButton || function(){};
        }
      } catch(e) {}
    }
    var n = 0, iv = setInterval(function(){ neuter(); if (++n > 40) clearInterval(iv); }, 250);
    neuter();
  } catch(e) {}
})();";

        private void OnWebMessage(BrowserTab tab, CoreWebView2WebMessageReceivedEventArgs args)
        {
            string raw;
            try { raw = args.TryGetWebMessageAsString(); }
            catch
            {
                try { raw = args.WebMessageAsJson; } catch { return; }
            }
            if (string.IsNullOrWhiteSpace(raw)) return;
            string json = raw.Trim();
            if (json.Length >= 2 && json[0] == '"')
            {
                try { json = JsonSerializer.Deserialize<string>(json) ?? json; }
                catch { }
            }
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl)) return;
                var type = typeEl.GetString() ?? "";
                // Ignore legacy autofill-focus messages if an older document script is still around.
                if (type == "password-submit")
                {
                    string url = root.TryGetProperty("url", out var u) ? (u.GetString() ?? "") : "";
                    string username = root.TryGetProperty("username", out var n) ? (n.GetString() ?? "") : "";
                    string password = root.TryGetProperty("password", out var p) ? (p.GetString() ?? "") : "";
                    if (string.IsNullOrEmpty(password)) return;
                    Logger.Log("PASSWORDS", $"Password-submit detected for {url} (user={username}).");
                    BeginInvoke(new Action(() => OfferSavePassword(url, username, password)));
                }
            }
            catch { }
        }

        private void OfferSavePassword(string url, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(password)) return;
            if (string.IsNullOrWhiteSpace(url)) url = ActiveTab?.Url ?? "";
            if (string.IsNullOrWhiteSpace(url)) return;
            string host;
            try { host = new Uri(url).Host; }
            catch { return; }

            // Already saved identical credentials?
            if (savedPasswords.Any(p =>
            {
                try
                {
                    return string.Equals(new Uri(p.Url).Host, host, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(p.Username, username, StringComparison.Ordinal)
                        && p.Password == password;
                }
                catch { return false; }
            })) return;

            var existing = savedPasswords.FirstOrDefault(p =>
            {
                try
                {
                    return string.Equals(new Uri(p.Url).Host, host, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(p.Username, username ?? "", StringComparison.Ordinal);
                }
                catch { return false; }
            });

            string msg = existing == null
                ? $"Save password for {host}" + (string.IsNullOrEmpty(username) ? "?" : $" ({username})?")
                : $"Update saved password for {host}" + (string.IsNullOrEmpty(username) ? "?" : $" ({username})?");
            var result = MessageBox.Show(this, msg, "Ceprkac", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            if (existing != null)
            {
                existing.Password = password;
                if (!string.IsNullOrEmpty(url)) existing.Url = url;
            }
            else
            {
                savedPasswords.Add(new SavedCredential
                {
                    Url = url,
                    Username = username ?? "",
                    Password = password,
                });
            }
            SavePasswords();
            Logger.Log("PASSWORDS", $"{(existing != null ? "Updated" : "Saved new")} password for {host} (user={username}).");
            statusLabel.Text = $"Password saved for {host}";
        }

        //  CSV/JSON helpers 
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ',' && !inQuotes) { fields.Add(current.ToString()); current.Clear(); continue; }
                current.Append(c);
            }
            fields.Add(current.ToString());
            return fields;
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private static List<SavedCredential> ParseCredentialJson(string json)
        {
            var list = new List<SavedCredential>();
            // Minimal JSON array parser for our known format
            int pos = 0;
            while (pos < json.Length)
            {
                int objStart = json.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = json.IndexOf('}', objStart);
                if (objEnd < 0) break;
                string obj = json.Substring(objStart + 1, objEnd - objStart - 1);

                string url = ExtractJsonValue(obj, "u");
                string user = ExtractJsonValue(obj, "n");
                string pwd = ExtractJsonValue(obj, "p");
                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(user))
                    list.Add(new SavedCredential { Url = url, Username = user, Password = pwd });

                pos = objEnd + 1;
            }
            return list;
        }

        private static string ExtractJsonValue(string obj, string key)
        {
            string search = $"\"{key}\":\"";
            int start = obj.IndexOf(search, StringComparison.Ordinal);
            if (start < 0) return "";
            start += search.Length;
            var sb = new StringBuilder();
            for (int i = start; i < obj.Length; i++)
            {
                if (obj[i] == '\\' && i + 1 < obj.Length) { sb.Append(obj[i + 1]); i++; continue; }
                if (obj[i] == '"') break;
                sb.Append(obj[i]);
            }
            return sb.ToString();
        }
    }
}
