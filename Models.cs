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
    internal sealed class SavedCredential
    {
        public string Url { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// A stored payment method. Encrypted at rest with DPAPI (CurrentUser), same as passwords.
    /// The card number and CVC are sensitive; they never leave the local encrypted store.
    /// </summary>
    internal sealed class SavedCard
    {
        public string Label { get; set; } = "";        // user-friendly nickname e.g. "Personal Visa"
        public string CardholderName { get; set; } = "";
        public string Number { get; set; } = "";        // digits only
        public string ExpMonth { get; set; } = "";      // "01".."12"
        public string ExpYear { get; set; } = "";        // 4-digit
        public string Cvc { get; set; } = "";

        /// <summary>Last 4 digits for display without exposing the full number.</summary>
        public string Last4 => Number.Length >= 4 ? Number.Substring(Number.Length - 4) : Number;
        public string Display => string.IsNullOrWhiteSpace(Label)
            ? $"---- {Last4}  ({ExpMonth}/{ExpYear})"
            : $"{Label} - ---- {Last4}  ({ExpMonth}/{ExpYear})";
    }

    /// <summary>
    /// A stored postal address / contact profile for checkout autofill. DPAPI-encrypted at rest.
    /// </summary>
    internal sealed class SavedAddress
    {
        public string Label { get; set; } = "";        // e.g. "Home", "Work"
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Line1 { get; set; } = "";
        public string Line2 { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string PostalCode { get; set; } = "";
        public string Country { get; set; } = "";

        public string Display => string.IsNullOrWhiteSpace(Label)
            ? $"{FullName} - {Line1}, {City}"
            : $"{Label}: {FullName} - {Line1}, {City}";
    }
}
