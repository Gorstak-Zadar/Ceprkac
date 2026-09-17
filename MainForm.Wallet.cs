using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Ceprkac
{
    public partial class MainForm
    {
        // 
        // Wallet = combined address + payment profiles. One record holds both an address and
        // (optionally) a card, so the user manages a single "Home"/"Work" entry instead of two
        // separate lists. Stored DPAPI-encrypted at rest (wallet.dat) and migrated once from the
        // legacy cards.dat / addresses.dat so nobody loses their existing data.
        // 

        private void LoadWallet()
        {
            try
            {
                if (File.Exists(walletFile))
                {
                    var decrypted = ProtectedData.Unprotect(File.ReadAllBytes(walletFile), null, DataProtectionScope.CurrentUser);
                    var json = Encoding.UTF8.GetString(decrypted);
                    savedProfiles.Clear();
                    foreach (var p in ParseWalletJson(json)) savedProfiles.Add(p);
                    Logger.Log("WALLET", $"Loaded {savedProfiles.Count} wallet profile(s).");
                    return;
                }

                // First run with the new format: migrate legacy cards + addresses into profiles.
                MigrateLegacyWallet();
            }
            catch (Exception ex) { Logger.Error("WALLET", "LoadWallet", ex); }
        }

        // Pair up legacy addresses and cards by index into combined profiles. Anything left over
        // (more cards than addresses or vice-versa) becomes its own profile. Runs once; the
        // resulting wallet.dat is authoritative afterwards.
        private void MigrateLegacyWallet()
        {
            if (savedAddresses.Count == 0 && savedCards.Count == 0)
            {
                Logger.Log("WALLET", "Nothing to migrate (no legacy cards/addresses).");
                return;
            }

            int max = Math.Max(savedAddresses.Count, savedCards.Count);
            for (int i = 0; i < max; i++)
            {
                var profile = new WalletProfile();
                if (i < savedAddresses.Count) profile.Address = savedAddresses[i];
                if (i < savedCards.Count) profile.Card = savedCards[i];
                profile.Label = !string.IsNullOrWhiteSpace(profile.Address.Label) ? profile.Address.Label
                              : (!string.IsNullOrWhiteSpace(profile.Card.Label) ? profile.Card.Label : "");
                savedProfiles.Add(profile);
            }
            Logger.Log("WALLET", $"Migrated {savedAddresses.Count} address(es) + {savedCards.Count} card(s) into {savedProfiles.Count} profile(s).");
            SaveWallet();
        }

        private void SaveWallet()
        {
            try
            {
                var json = SerializeWallet(savedProfiles);
                var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(walletFile, encrypted);
                Logger.Log("WALLET", $"Saved {savedProfiles.Count} wallet profile(s) to disk.");
            }
            catch (Exception ex) { Logger.Error("WALLET", "SaveWallet", ex); }
        }

        // 
        // Combined manager UI
        // 

        private void ManageWallet()
        {
            using var dlg = new ListManagerDialog<WalletProfile>(
                "Wallet - Addresses & Payment",
                savedProfiles,
                p => p.Display,
                () => EditWalletDialog(new WalletProfile()),
                existing => EditWalletDialog(existing));
            dlg.Font = _bookmarkFont ?? Font;
            dlg.ShowDialog(this);
            SaveWallet();
            statusLabel.Text = $"{savedProfiles.Count} wallet profile(s) saved.";
        }

        // One form that edits BOTH the address and the (optional) card of a profile.
        private WalletProfile? EditWalletDialog(WalletProfile profile)
        {
            using var form = new FieldEditorForm("Wallet Profile (address + payment)");
            var label = form.AddField("Nickname (e.g. Home)", profile.Label);
            // Address
            var name = form.AddField("Full name", profile.Address.FullName);
            var email = form.AddField("Email", profile.Address.Email);
            var phone = form.AddField("Phone", profile.Address.Phone);
            var l1 = form.AddField("Address line 1", profile.Address.Line1);
            var l2 = form.AddField("Address line 2", profile.Address.Line2);
            var city = form.AddField("City", profile.Address.City);
            var state = form.AddField("State / Region", profile.Address.State);
            var zip = form.AddField("Postal code", profile.Address.PostalCode);
            var country = form.AddField("Country", profile.Address.Country);
            // Payment (optional)
            var ccName = form.AddField("Cardholder name (optional)", profile.Card.CardholderName);
            var ccNum = form.AddField("Card number (optional)", profile.Card.Number);
            var ccMonth = form.AddField("Expiry month (MM)", profile.Card.ExpMonth);
            var ccYear = form.AddField("Expiry year (YYYY)", profile.Card.ExpYear);
            var ccCvc = form.AddField("CVC", profile.Card.Cvc, isPassword: true);
            form.Build();
            if (form.ShowDialog(this) != DialogResult.OK) return null;

            profile.Label = label.Text.Trim();
            profile.Address.Label = profile.Label;
            profile.Address.FullName = name.Text.Trim();
            profile.Address.Email = email.Text.Trim();
            profile.Address.Phone = phone.Text.Trim();
            profile.Address.Line1 = l1.Text.Trim();
            profile.Address.Line2 = l2.Text.Trim();
            profile.Address.City = city.Text.Trim();
            profile.Address.State = state.Text.Trim();
            profile.Address.PostalCode = zip.Text.Trim();
            profile.Address.Country = country.Text.Trim();

            profile.Card.Label = profile.Label;
            profile.Card.CardholderName = ccName.Text.Trim();
            profile.Card.Number = new string(ccNum.Text.Where(char.IsDigit).ToArray());
            profile.Card.ExpMonth = ccMonth.Text.Trim();
            profile.Card.ExpYear = ccYear.Text.Trim();
            profile.Card.Cvc = ccCvc.Text.Trim();

            if (!profile.HasAddress && !profile.HasCard)
            {
                MessageBox.Show(this, "Enter at least an address or a card.", "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return profile;
        }

        // 
        // Profile-based checkout autofill (replaces the separate card/address paths)
        // 

        private async void TryAutoFillWallet(BrowserTab tab)
        {
            if (savedProfiles.Count == 0) return;
            if ((DateTime.Now - tab.LastAutoFillFormsAttempt).TotalSeconds < 3) return;
            tab.LastAutoFillFormsAttempt = DateTime.Now;

            var core = tab.WebView.CoreWebView2;
            if (core == null) return;
            string pageUrl = core.Source ?? "";
            if (string.IsNullOrEmpty(pageUrl)) return;

            string pathLower;
            try { pathLower = (new Uri(pageUrl).PathAndQuery + " " + pageUrl).ToLowerInvariant(); }
            catch { pathLower = pageUrl.ToLowerInvariant(); }
            bool looksLikeCheckout = pathLower.Contains("checkout") || pathLower.Contains("payment") || pathLower.Contains("billing")
                || pathLower.Contains("shipping") || pathLower.Contains("address") || pathLower.Contains("cart")
                || pathLower.Contains("order") || pathLower.Contains("pay");

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
                catch (Exception ex) { Logger.Error("WALLET", "detect fields", ex); continue; }

                bool hasCardFields = result.StartsWith("card");
                bool hasAddrFields = result.EndsWith("addr");
                if (!hasCardFields && !hasAddrFields)
                {
                    if (!looksLikeCheckout) return;
                    continue;
                }

                Logger.Log("WALLET", $"Checkout fields on {pageUrl}: card={hasCardFields}, addr={hasAddrFields}, profiles={savedProfiles.Count}.");

                // Only profiles that actually carry the needed data are candidates.
                var candidates = savedProfiles.Where(p =>
                    (hasAddrFields && p.HasAddress) || (hasCardFields && p.HasCard)).ToList();
                if (candidates.Count == 0) return;

                if (candidates.Count == 1)
                {
                    await FillProfile(core, candidates[0], hasAddrFields, hasCardFields);
                    Invoke(() => statusLabel.Text = "Autofilled saved details.");
                }
                else
                {
                    Invoke(() => ShowWalletPicker(tab, candidates, hasAddrFields, hasCardFields));
                }
                return;
            }
        }

        private async Task FillProfile(CoreWebView2 core, WalletProfile p, bool fillAddr, bool fillCard)
        {
            if (fillAddr && p.HasAddress) await FillAddress(core, p.Address);
            if (fillCard && p.HasCard) await FillCard(core, p.Card);
            Logger.Log("WALLET", $"Filled profile '{p.Display}' (addr={fillAddr && p.HasAddress}, card={fillCard && p.HasCard}).");
        }

        private void ShowWalletPicker(BrowserTab tab, List<WalletProfile> profiles, bool fillAddr, bool fillCard)
        {
            var picker = new ContextMenuStrip { BackColor = Theme.ActiveTab, ForeColor = Color.White, ShowImageMargin = false };
            picker.Items.Add(new ToolStripMenuItem("Choose a profile:") { Enabled = false, ForeColor = Theme.ForeDim });
            picker.Items.Add(new ToolStripSeparator());
            foreach (var profile in profiles)
            {
                var p = profile;
                var item = new ToolStripMenuItem(p.Display) { ForeColor = Color.White, BackColor = Theme.ActiveTab };
                item.Click += async (_, _) =>
                {
                    picker.Close();
                    var core = tab.WebView.CoreWebView2;
                    if (core != null) { await FillProfile(core, p, fillAddr, fillCard); statusLabel.Text = $"Filled {p.Display}"; }
                };
                picker.Items.Add(item);
            }
            var pt = webViewPanel.PointToScreen(new Point(webViewPanel.Width / 2 - 100, 10));
            picker.Show(pt);
        }

        // 
        // Portable, passphrase-encrypted export / import. DPAPI files can't move between
        // machines, so the exported bundle uses PBKDF2 (SHA-256) + AES-256-CBC keyed on a
        // passphrase the user chooses. Restores on any Windows install after a reinstall.
        // The bundle includes wallet profiles AND saved passwords so a single file restores
        // everything the user has to re-enter otherwise.
        // 

        private const string ExportMagic = "CEPRKAC-WALLET-1";
        private const int Pbkdf2Iterations = 200_000;

        private void ExportAutofillData()
        {
            if (savedProfiles.Count == 0 && savedPasswords.Count == 0)
            {
                MessageBox.Show(this, "Nothing to export yet.", "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var dlg = new SaveFileDialog
            {
                Title = "Export Passwords & Wallet (encrypted)",
                Filter = "Ceprkac Backup (*.ceprkac)|*.ceprkac|All Files|*.*",
                FileName = "ceprkac-backup.ceprkac",
                RestoreDirectory = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var pass = PromptPassphrase("Set a passphrase to encrypt the backup", confirm: true);
            if (pass == null) return;

            try
            {
                string plain = BuildBackupJson();
                byte[] blob = EncryptBackup(plain, pass);
                File.WriteAllBytes(dlg.FileName, blob);
                Logger.Log("WALLET", $"Exported backup: {savedProfiles.Count} profile(s), {savedPasswords.Count} password(s) -> {dlg.FileName}");
                statusLabel.Text = $"Exported {savedProfiles.Count} profile(s) and {savedPasswords.Count} password(s).";
                MessageBox.Show(this,
                    "Backup exported.\r\n\r\nKeep the file AND the passphrase safe - without the passphrase the backup cannot be restored.",
                    "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("WALLET", "ExportAutofillData", ex);
                MessageBox.Show(this, $"Export failed:\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportAutofillData()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Import Passwords & Wallet (encrypted)",
                Filter = "Ceprkac Backup (*.ceprkac)|*.ceprkac|All Files|*.*",
                RestoreDirectory = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var pass = PromptPassphrase("Enter the passphrase for this backup", confirm: false);
            if (pass == null) return;

            try
            {
                byte[] blob = File.ReadAllBytes(dlg.FileName);
                string plain = DecryptBackup(blob, pass);
                var (profiles, creds) = ParseBackupJson(plain);

                int addedProfiles = MergeProfiles(profiles);
                int addedPasswords = MergePasswords(creds);

                SaveWallet();
                SavePasswords();
                Logger.Log("WALLET", $"Imported backup: +{addedProfiles} profile(s), +{addedPasswords} password(s) from {dlg.FileName}");
                statusLabel.Text = $"Imported {addedProfiles} profile(s) and {addedPasswords} password(s).";
                MessageBox.Show(this, $"Import complete.\r\n\r\nAdded {addedProfiles} wallet profile(s) and {addedPasswords} password(s).",
                    "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (CryptographicException)
            {
                Logger.Log("WALLET", "Import failed: wrong passphrase or corrupt file.");
                MessageBox.Show(this, "Could not decrypt - wrong passphrase or the file is corrupt.",
                    "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                Logger.Error("WALLET", "ImportAutofillData", ex);
                MessageBox.Show(this, $"Import failed:\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private int MergeProfiles(List<WalletProfile> incoming)
        {
            int added = 0;
            foreach (var p in incoming)
            {
                bool dup = savedProfiles.Any(e =>
                    string.Equals(e.Label, p.Label, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Address.Line1, p.Address.Line1, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Card.Number, p.Card.Number, StringComparison.Ordinal));
                if (dup) continue;
                savedProfiles.Add(p);
                added++;
            }
            return added;
        }

        private int MergePasswords(List<SavedCredential> incoming)
        {
            int added = 0;
            foreach (var c in incoming)
            {
                bool dup = savedPasswords.Any(e =>
                    string.Equals(e.Url, c.Url, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Username, c.Username, StringComparison.OrdinalIgnoreCase));
                if (dup) continue;
                savedPasswords.Add(c);
                added++;
            }
            return added;
        }

        // 
        // Backup (de)serialization + crypto
        // 

        private string BuildBackupJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"passwords\":[");
            for (int i = 0; i < savedPasswords.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var c = savedPasswords[i];
                sb.Append('{')
                  .Append($"\"u\":\"{EscapeJson(c.Url)}\",")
                  .Append($"\"n\":\"{EscapeJson(c.Username)}\",")
                  .Append($"\"p\":\"{EscapeJson(c.Password)}\"")
                  .Append('}');
            }
            sb.Append("],\"profiles\":");
            sb.Append(SerializeWallet(savedProfiles));
            sb.Append('}');
            return sb.ToString();
        }

        private static string SerializeWallet(List<WalletProfile> profiles)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < profiles.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var p = profiles[i];
                var a = p.Address;
                var c = p.Card;
                sb.Append('{');
                sb.Append($"\"label\":\"{EscapeJson(p.Label)}\",");
                sb.Append($"\"name\":\"{EscapeJson(a.FullName)}\",");
                sb.Append($"\"email\":\"{EscapeJson(a.Email)}\",");
                sb.Append($"\"phone\":\"{EscapeJson(a.Phone)}\",");
                sb.Append($"\"l1\":\"{EscapeJson(a.Line1)}\",");
                sb.Append($"\"l2\":\"{EscapeJson(a.Line2)}\",");
                sb.Append($"\"city\":\"{EscapeJson(a.City)}\",");
                sb.Append($"\"state\":\"{EscapeJson(a.State)}\",");
                sb.Append($"\"zip\":\"{EscapeJson(a.PostalCode)}\",");
                sb.Append($"\"country\":\"{EscapeJson(a.Country)}\",");
                sb.Append($"\"ccname\":\"{EscapeJson(c.CardholderName)}\",");
                sb.Append($"\"ccnum\":\"{EscapeJson(c.Number)}\",");
                sb.Append($"\"ccem\":\"{EscapeJson(c.ExpMonth)}\",");
                sb.Append($"\"ccey\":\"{EscapeJson(c.ExpYear)}\",");
                sb.Append($"\"cccvc\":\"{EscapeJson(c.Cvc)}\"");
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static List<WalletProfile> ParseWalletJson(string json)
        {
            var list = new List<WalletProfile>();
            int pos = 0;
            while (pos < json.Length)
            {
                int objStart = json.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = json.IndexOf('}', objStart);
                if (objEnd < 0) break;
                string obj = json.Substring(objStart + 1, objEnd - objStart - 1);
                var p = new WalletProfile
                {
                    Label = ExtractJsonValue(obj, "label"),
                    Address = new SavedAddress
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
                    },
                    Card = new SavedCard
                    {
                        Label = ExtractJsonValue(obj, "label"),
                        CardholderName = ExtractJsonValue(obj, "ccname"),
                        Number = ExtractJsonValue(obj, "ccnum"),
                        ExpMonth = ExtractJsonValue(obj, "ccem"),
                        ExpYear = ExtractJsonValue(obj, "ccey"),
                        Cvc = ExtractJsonValue(obj, "cccvc"),
                    },
                };
                if (p.HasAddress || p.HasCard) list.Add(p);
                pos = objEnd + 1;
            }
            return list;
        }

        private static (List<WalletProfile>, List<SavedCredential>) ParseBackupJson(string json)
        {
            var creds = new List<SavedCredential>();
            var profiles = new List<WalletProfile>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("passwords", out var pwArr) && pwArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in pwArr.EnumerateArray())
                    {
                        var c = new SavedCredential
                        {
                            Url = e.TryGetProperty("u", out var u) ? (u.GetString() ?? "") : "",
                            Username = e.TryGetProperty("n", out var n) ? (n.GetString() ?? "") : "",
                            Password = e.TryGetProperty("p", out var p) ? (p.GetString() ?? "") : "",
                        };
                        if (!string.IsNullOrEmpty(c.Url)) creds.Add(c);
                    }
                }
                if (root.TryGetProperty("profiles", out var prArr) && prArr.ValueKind == JsonValueKind.Array)
                    profiles = ParseWalletJson(prArr.GetRawText());
            }
            catch (Exception ex) { Logger.Error("WALLET", "ParseBackupJson", ex); throw; }
            return (profiles, creds);
        }

        // File layout: MAGIC(16) | salt(16) | iv(16) | ciphertext
        private static byte[] EncryptBackup(string plaintext, string passphrase)
        {
            byte[] salt = RandomBytes(16);
            byte[] iv = RandomBytes(16);
            byte[] key = DeriveKey(passphrase, salt);
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var enc = aes.CreateEncryptor();
            byte[] data = Encoding.UTF8.GetBytes(plaintext);
            byte[] cipher = enc.TransformFinalBlock(data, 0, data.Length);

            using var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes(ExportMagic), 0, ExportMagic.Length);
            ms.Write(salt, 0, salt.Length);
            ms.Write(iv, 0, iv.Length);
            ms.Write(cipher, 0, cipher.Length);
            return ms.ToArray();
        }

        private static string DecryptBackup(byte[] blob, string passphrase)
        {
            byte[] magic = Encoding.ASCII.GetBytes(ExportMagic);
            if (blob.Length < magic.Length + 32)
                throw new InvalidDataException("File is too small to be a valid backup.");
            for (int i = 0; i < magic.Length; i++)
                if (blob[i] != magic[i]) throw new InvalidDataException("Not a Ceprkac backup file.");

            int p = magic.Length;
            byte[] salt = new byte[16]; Array.Copy(blob, p, salt, 0, 16); p += 16;
            byte[] iv = new byte[16]; Array.Copy(blob, p, iv, 0, 16); p += 16;
            byte[] cipher = new byte[blob.Length - p]; Array.Copy(blob, p, cipher, 0, cipher.Length);

            byte[] key = DeriveKey(passphrase, salt);
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var dec = aes.CreateDecryptor();
            byte[] plain = dec.TransformFinalBlock(cipher, 0, cipher.Length); // throws CryptographicException on wrong key
            return Encoding.UTF8.GetString(plain);
        }

        private static byte[] DeriveKey(string passphrase, byte[] salt)
        {
            using var kdf = new Rfc2898DeriveBytes(passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            return kdf.GetBytes(32); // 256-bit key
        }

        private static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(b);
            return b;
        }

        // Simple modal passphrase prompt. When confirm=true, requires the entry twice.
        private string? PromptPassphrase(string title, bool confirm)
        {
            using var form = new FieldEditorForm(title);
            var p1 = form.AddField("Passphrase", "", isPassword: true);
            TextBox? p2 = confirm ? form.AddField("Confirm passphrase", "", isPassword: true) : null;
            form.Build();
            while (true)
            {
                if (form.ShowDialog(this) != DialogResult.OK) return null;
                string a = p1.Text;
                if (string.IsNullOrEmpty(a))
                {
                    MessageBox.Show(this, "Passphrase cannot be empty.", "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                if (confirm && p2 != null && a != p2.Text)
                {
                    MessageBox.Show(this, "The passphrases do not match.", "Ceprkac", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                return a;
            }
        }
    }
}
