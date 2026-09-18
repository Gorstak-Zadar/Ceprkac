# Ceprkac

A Chrome-inspired tabbed browser for **Windows x64**, built with C# WinForms and **WebView2**. Same UI and features as [GBrowser](https://github.com/CroatiaSecurity/GBrowser), with Edge codecs so **H.264 / AAC** actually play (Discord embeds, typical HTML5 players).

[![version](https://img.shields.io/badge/version-0.9.0-blue?style=flat-square)](CHANGELOG.md)
[![.NET](https://img.shields.io/badge/.NET-Framework%204.8-512BD4?style=flat-square)](#requirements)
[![engine](https://img.shields.io/badge/engine-WebView2%20(Chromium)-orange?style=flat-square)](#requirements)

**Download:** [Ceprkac 0.9.0 Setup](https://github.com/CroatiaSecurity/Ceprkac/releases/latest) - **History:** [CHANGELOG](CHANGELOG.md)

---

## Features

- Chrome-like tabs, dark UI, nested bookmarks, history, zoom
- Omnibox search (Google, Bing, DuckDuckGo, Yahoo, Brave, Startpage) - new tab opens empty and focused, first keystroke kept; live URL while browsing
- Right-click image -> Google Lens / reverse-image search; video/media search; copy/open address
- Downloads via WebView2 native shelf; custom downloads badge + history in toolbar; blob/data URI save-as with cookie forwarding
- Passwords (DPAPI) with auto-fill, credential picker, save prompt, and a right-click **Fill password** that works on any site or login popup (including cross-domain OAuth prompts)
- Combined **Wallet** (address + payment in one profile) with checkout auto-fill; encrypted, portable **Export/Import Passwords & Wallet** (passphrase, PBKDF2 + AES-256) for moving data between machines
- Full activity/diagnostic log at `%AppData%\Ceprkac\ceprkac.log`
- Network + DOM ad blocking (`blocklist.txt`); YouTube embed ads blocked via fetch interceptor in iframes
- Native WebView2 Ctrl+F find bar, permission dialogs, passkeys, and accelerator keys
- **Open external app?** confirmation before a site launches a desktop app via a custom URI scheme (e.g. `discord://`), with an optional per-site "remember" for the session
- Hover link URL in the status bar; duplicate tab (`Ctrl+Shift+K`)
- Injected-module cleaner (identity unload on LDR load, all module extensions, WebView2 children)
- WebView2 Evergreen Runtime auto-installed if missing
- Optional **default browser**: http/https + HTML files; links open in the existing window

| Shortcut | Action | Shortcut | Action |
|---|---|---|---|
| `Ctrl+T` / `Ctrl+W` | New / close tab | `Ctrl+Shift+T` | Restore tab |
| `Ctrl+Shift+K` | Duplicate tab | `Ctrl+Tab` | Next tab |
| `Ctrl+L` | Focus address bar | `Ctrl+D` | Bookmark |
| `Ctrl+F` | Find in page | `Ctrl+I` | DevTools |
| `Ctrl+Plus` / `Minus` / `0` | Zoom | | |

## Requirements

- Windows 10/11 x64, **.NET Framework 4.8** (already on the OS)
- **WebView2 Evergreen Runtime** (installed automatically if needed)

To **build** the installer: .NET SDK + [Inno Setup 6+](https://jrsoftware.org/isinfo.php)

```bat
dotnet run --project Ceprkac.csproj
build.bat
```

Output: `releases\0.9.0\Ceprkac-0.9.0-Setup.exe`

## Default browser

The installer offers **Set Ceprkac as the default browser**. That registers http, https, and HTML files and opens Windows Settings so you can confirm (Windows 10/11 do not allow a silent switch).

Later: menu -> **Set as Default Browser...**, or:

```bat
Ceprkac.exe --register-browser
```

Opening a link while Ceprkac is already running adds a tab in that window.

## Data

All under `%AppData%\Ceprkac`: bookmarks, history, DPAPI passwords, DPAPI payment methods, DPAPI addresses, settings, downloads, window geometry, WebView2 profile.

## Disclaimer

Authorized defensive, administrative, research, or educational use only. Provided **AS IS**, no warranties. Authors are not liable for damage, data loss, or legal exposure. You are responsible for lawful use. Not affiliated with Google, Microsoft, or Discord.

Built by **Gorstak**
