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
        private static readonly HashSet<string> BlockedAdDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            // Google Ads & Analytics
            "doubleclick.net","googleadservices.com","googlesyndication.com","adservice.google.com",
            "ads.google.com","google-analytics.com","googletagmanager.com","googletagservices.com",
            "pagead2.googlesyndication.com","pagead2.googleadservices.com",
            // Major ad networks
            "adnxs.com","taboola.com","outbrain.com","criteo.com","scorecardresearch.com","pubmatic.com",
            "rubiconproject.com","quantserve.com","quantcast.com","omniture.com","comscore.com",
            "krux.com","bluekai.com","exelate.com","adform.com","adroll.com","vungle.com","inmobi.com",
            "flurry.com","mixpanel.com","heap.io","amplitude.com","optimizely.com","bizible.com",
            "pardot.com","hubspot.com","marketo.com","eloqua.com","media.net","appnexus.com","adbrite.com",
            "admob.com","adsonar.com","zergnet.com","revcontent.com","mgid.com","adblade.com","adcolony.com",
            "chartbeat.com","newrelic.com","pingdom.net","kissmetrics.com","tradedesk.com","turn.com",
            "adscale.com","bannerflow.com","nativeads.com","contentad.com","displayads.com",
            "smartadserver.com","openx.net","casalemedia.com","indexww.com","sharethrough.com",
            "33across.com","triplelift.com","sovrn.com","lijit.com","bidswitch.net","yieldmo.com",
            "teads.tv","spotxchange.com","springserve.com","contextweb.com","liveintent.com",
            "adtech.de","adform.net","serving-sys.com","adsafeprotected.com","moatads.com",
            // Facebook / Meta
            "connect.facebook.net","pixel.facebook.com","analytics.facebook.com","ads.facebook.com","an.facebook.com",
            // Twitter / X
            "ads-twitter.com","static.ads-twitter.com","analytics.twitter.com","ads-api.twitter.com","advertising.twitter.com",
            // Reddit
            "pixel.reddit.com","rereddit.com","ads.reddit.com","events.reddit.com","events.redditmedia.com","d.reddit.com",
            // LinkedIn
            "ads.linkedin.com","analytics.pointdrive.linkedin.com",
            // TikTok
            "analytics.tiktok.com","ads.tiktok.com","ads-sg.tiktok.com","analytics-sg.tiktok.com",
            // Pinterest
            "ads.pinterest.com","log.pinterest.com","ads-dev.pinterest.com","analytics.pinterest.com",
            "trk.pinterest.com","trk2.pinterest.com","widgets.pinterest.com",
            // Amazon
            "amazon-adsystem.com","advertising-api-eu.amazon.com","amazonaax.com","amazonclix.com","assoc-amazon.com",
            // YouTube
            "youtubeads.googleapis.com","ads.youtube.com","analytics.youtube.com","video-stats.video.google.com",
            "youtube.cleverads.vn",
            // Yahoo
            "advertising.yahoo.com","ads.yahoo.com","adserver.yahoo.com","global.adserver.yahoo.com",
            "adspecs.yahoo.com","analytics.yahoo.com","analytics.query.yahoo.com","comet.yahoo.com",
            "log.fc.yahoo.com","ganon.yahoo.com","gemini.yahoo.com","beap.gemini.yahoo.com",
            "geo.yahoo.com","marketingsolutions.yahoo.com","pclick.yahoo.com",
            "ads.yap.yahoo.com","m.yap.yahoo.com","partnerads.ysm.yahoo.com",
            // Yandex
            "appmetrica.yandex.com","yandexadexchange.net","adfox.yandex.ru","adsdk.yandex.ru",
            "an.yandex.ru","awaps.yandex.ru","awsync.yandex.ru","bs.yandex.ru","bs-meta.yandex.ru",
            "clck.yandex.ru","informer.yandex.ru","kiks.yandex.ru","mc.yandex.ru","metrika.yandex.ru",
            "share.yandex.ru","offerwall.yandex.net",
            // Hotjar / Session recording
            "hotjar.com","api-hotjar.com","hotjar-analytics.com","fullstory.com","mouseflow.com",
            "luckyorange.com","luckyorange.net","freshmarketer.com",
            // Segment / Analytics
            "segment.io","segment.com","stats.wp.com",
            // Error trackers
            "notify.bugsnag.com","sessions.bugsnag.com","api.bugsnag.com","app.bugsnag.com",
            "browser.sentry-cdn.com","app.getsentry.com",
            // FastClick
            "fastclick.com","fastclick.net",
            // Samsung
            "samsungadhub.com","samsungads.com","smetrics.samsung.com","nmetrics.samsung.com",
            "analytics.samsungknox.com","bigdata.ssp.samsung.com","config.samsungads.com",
            // Apple metrics
            "metrics.apple.com","securemetrics.apple.com","supportmetrics.apple.com",
            "metrics.icloud.com","metrics.mzstatic.com","books-analytics-events.apple.com",
            "stocks-analytics-events.apple.com",
            // Xiaomi
            "api.ad.xiaomi.com","data.mistat.xiaomi.com","sdkconfig.ad.xiaomi.com",
            "globalapi.ad.xiaomi.com","tracking.miui.com","tracking.intl.miui.com",
            // Huawei
            "metrics.data.hicloud.com","logservice.hicloud.com","logbak.hicloud.com",
            // OPPO / Realme / OnePlus
            "adsfs.oppomobile.com","bdapi-in-ads.realmemobile.com",
            "analytics.oneplus.cn","click.oneplus.cn","click.oneplus.com","open.oneplus.net",
            // Missing from d3ward test
            "events.hotjar.io","extmaps-api.yandex.net","metrics2.data.hicloud.com",
            "logservice1.hicloud.com","iot-eu-logser.realme.com","click.googleanalytics.com",
            "grs.hicloud.com","udcm.yahoo.com","auction.unityads.unity3d.com",
            "config.unityads.unity3d.com","adserver.unityads.unity3d.com","webview.unityads.unity3d.com",
            "adfstat.yandex.ru","iadsdk.apple.com","appmetrica.yandex.ru",
            "business-api.tiktok.com","log.byteoversea.com","ads-api.tiktok.com",
            "iot-logser.realme.com","tracking.rus.miui.com","adtech.yahooinc.com",
            "bdapi-ads.realmemobile.com","ck.ads.oppomobile.com","data.ads.oppomobile.com",
            "adx.ads.oppomobile.com","data.mistat.india.xiaomi.com","data.mistat.rus.xiaomi.com",
            "notes-analytics-events.apple.com","weather-analytics-events.apple.com",
            "api-adservices.apple.com","samsung-com.112.2o7.net","analytics-api.samsunghealthcn.com",
            "unityads.unity3d.com","byteoversea.com","yahooinc.com",
            // S3-hosted ad/analytics buckets
            "adtago.s3.amazonaws.com","analyticsengine.s3.amazonaws.com",
            "analytics.s3.amazonaws.com","advice-ads.s3.amazonaws.com",
            // Adult site ad networks
            "trafficjunky.com","trafficjunky.net","trafficstars.com","tsyndicate.com",
            "exoclick.com","exosrv.com","exoticads.com","juicyads.com","realsrv.com",
            "adsrv.org","padsdel.com","tsyndicate.com","syndication.exoclick.com",
            "main.exoclick.com","static.exoclick.com","ads.trafficjunky.net",
            "cdn.trafficjunky.net","adsrv.eacdn.com","a.realsrv.com",
            "mc.yandex.ru","syndication.realsrv.com","s.magsrv.com","magsrv.com",
            // Additional missing
            "sdkconfig.ad.intl.xiaomi.com","iot-eu-logser.realme.com","iot-logser.realme.com",
            "bdapi-ads.realmemobile.com","analytics-api.samsunghealthcn.com",
        };

        private static readonly HashSet<string> AdBlockWhitelist = new(StringComparer.OrdinalIgnoreCase)
        {
            "discord.com", "discordapp.com", "discordapp.net", "discord.gg", "discord.media",
            "cloudflare.com", "challenges.cloudflare.com", "cdnjs.cloudflare.com",
            "youtube-nocookie.com",
            "apple.com", "icloud.com",
            "ebay.com",
            "paypal.com",
            "mediafire.com",
            // Auth/OAuth providers
            "accounts.google.com", "accounts.youtube.com", "myaccount.google.com",
            "google.com", "www.google.com", "google.hr", "google.co.uk",
            "youtube.com", "www.youtube.com",
            "login.microsoftonline.com", "login.live.com", "login.microsoft.com",
            "appleid.apple.com", "idmsa.apple.com",
            "github.com", "auth0.com", "okta.com",
            "apis.google.com", "ssl.gstatic.com",
            "pay.google.com", "payments.google.com",
            "gog.com", "auth.gog.com", "login.gog.com",
            "suno.com", "suno.ai", "clerk.suno.com",
            // AI services
            "openai.com", "chat.openai.com", "chatgpt.com",
            "claude.ai", "anthropic.com",
            "gemini.google.com", "bard.google.com",
            "perplexity.ai", "you.com",
            "midjourney.com", "stability.ai",
            "huggingface.co", "replicate.com",
            "udio.com", "poe.com", "character.ai",
            "copilot.microsoft.com",
            // Banking & financial
            "chase.com", "bankofamerica.com", "wellsfargo.com", "citibank.com",
            "usbank.com", "capitalone.com", "discover.com", "americanexpress.com",
            "hsbc.com", "barclays.com", "natwest.com", "lloydsbank.com",
            "revolut.com", "wise.com", "transferwise.com", "stripe.com",
            "squareup.com", "venmo.com", "zelle.com", "cash.app",
            "ing.com", "raiffeisen.hr", "pbz.hr", "zaba.hr", "erstebank.hr",
            "n26.com", "monzo.com", "starlingbank.com",
            // Gaming clients & stores
            "steampowered.com", "store.steampowered.com", "steamcommunity.com",
            "epicgames.com", "unrealengine.com",
            "gog.com", "gogalaxy.com",
            "ea.com", "origin.com",
            "ubisoft.com", "ubi.com",
            "blizzard.com", "battle.net", "battlenet.com.cn",
            "riotgames.com", "leagueoflegends.com",
            "xbox.com", "xboxlive.com",
            "playstation.com", "sonyentertainmentnetwork.com",
            "nintendo.com", "nintendo.net",
            "humblebundle.com", "itch.io", "indiegala.com",
            "twitch.tv",
        };

        private static string BaseDomain(string host)
        {
            var p = host.Split('.');
            if (p.Length >= 3 && (p[p.Length - 1] == "uk" || p[p.Length - 1] == "au" || p[p.Length - 1] == "jp" || p[p.Length - 1] == "br" || p[p.Length - 1] == "za" || p[p.Length - 1] == "nz" || p[p.Length - 1] == "kr" || p[p.Length - 1] == "in"))
                return string.Join(".", p[p.Length - 3], p[p.Length - 2], p[p.Length - 1]);
            return p.Length >= 2 ? string.Join(".", p[p.Length - 2], p[p.Length - 1]) : host;
        }

        private static bool SameSite(string a, string b) =>
            string.Equals(BaseDomain(a), BaseDomain(b), StringComparison.OrdinalIgnoreCase);

        private static bool IsAdBlockWhitelisted(string host)
        {
            while (host.Contains('.'))
            {
                if (AdBlockWhitelist.Contains(host)) return true;
                int dot = host.IndexOf('.');
                host = host.Substring(dot + 1);
            }
            return false;
        }

        /// <summary>
        /// Checks if a URL points to a known ad/tracking domain.
        /// Used to block navigations and new windows to ad destinations.
        /// </summary>
        private bool IsAdUrl(string url)
        {
            try
            {
                var uri = new Uri(url.Contains("://") ? url : "https://" + url);
                var host = uri.Host.ToLower();
                // Don't block whitelisted domains
                if (IsAdBlockWhitelisted(host)) return false;
                // Check against blocklist
                var checkHost = host;
                while (checkHost.Contains('.'))
                {
                    if (BlockedAdDomains.Contains(checkHost)) return true;
                    int dot = checkHost.IndexOf('.');
                    checkHost = checkHost.Substring(dot + 1);
                }
                // Check common ad URL patterns
                if (url.Contains("/pagead/") || url.Contains("/adclick") ||
                    url.Contains("/aclk?") || url.Contains("googleadservices.com") ||
                    url.Contains("doubleclick.net") || url.Contains("googlesyndication.com"))
                    return true;
            }
            catch { }
            return false;
        }

        private int adsBlockedCount = 0;

        private async Task SetupAdBlocker(CoreWebView2 core)
        {
            // Register filters for resource types that serve ads - NOT All, which would
            // intercept upload streams and add IPC overhead on every data chunk
            var adResourceTypes = new[]
            {
                CoreWebView2WebResourceContext.Script,
                CoreWebView2WebResourceContext.Image,
                CoreWebView2WebResourceContext.Stylesheet,
                CoreWebView2WebResourceContext.XmlHttpRequest,  // covers XHR, Fetch, EventSource
                CoreWebView2WebResourceContext.Media,
                CoreWebView2WebResourceContext.Font,
            };
            foreach (var resourceType in adResourceTypes)
                core.AddWebResourceRequestedFilter("*://*", resourceType);
            core.WebResourceRequested += (_, args) =>
            {
                try
                {
                    var uri = new Uri(args.Request.Uri);
                    var host = uri.Host.ToLower();
                    // Skip whitelisted *request* hosts (Discord CDN, Google accounts, etc.)
                    if (IsAdBlockWhitelisted(host)) return;
                    // Same-site (first-party) requests are never blocked
                    try
                    {
                        var pageHost = new Uri(core.Source ?? "").Host.ToLower();
                        if (SameSite(host, pageHost)) return;
                    }
                    catch { }

                    // Do NOT skip blocking just because the *top-level* page is whitelisted
                    // (Discord, etc.). Embedded YouTube iframes still need ad requests cancelled.
                    // First-party SameSite + request-host whitelist above protect the host site.

                    // Check if the host or any parent domain is in the block list
                    var checkHost = host;
                    while (checkHost.Contains('.'))
                    {
                        if (BlockedAdDomains.Contains(checkHost))
                        {
                            args.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                            adsBlockedCount++;
                            return;
                        }
                        int dot = checkHost.IndexOf('.');
                        checkHost = checkHost.Substring(dot + 1);
                    }
                }
                catch { }
            };

            // YouTube ads live in ytInitialData / player JSON and must be stripped in the
            // MAIN world before page scripts run. Isolated-world <script> tags are blocked
            // by YouTube CSP, which is why ads came back after 0.6.8.
            //
            // The main-world script is installed ONCE, unconditionally, at tab setup via
            // Page.addScriptToEvaluateOnNewDocument. It is self-guarded: YouTubeMainWorldCode
            // bails immediately on any non-YouTube host and on auth/OAuth pages, so registering
            // it globally never tags Cloudflare forums as a bot. Installing it here (instead of
            // lazily on a cancellable top-level NavigationStarting) means it runs before page
            // scripts on EVERY document - including SPA soft-navigations (clicking a related
            // video), back/forward, and renderer recovery - so ad-blocking no longer depends on
            // the direction the user arrived at the video from.
            //
            // This is AWAITED (callers await SetupAdBlocker before the tab navigates) so the
            // CDP registration is in place BEFORE the first document loads. Previously this was
            // fire-and-forget, which lost a race when a YouTube video was opened directly (e.g.
            // clicked from a search-engine result into a new tab): the first document's
            // ytInitialData/ytInitialPlayerResponse loaded with ads intact because the JSON
            // stripper had not registered yet - hence "ads until you refresh".
            await InstallYouTubeMainWorld(core);

            // Suppress Google One Tap / FedCM prompts in the MAIN world. The isolated-world
            // AddScriptToExecuteOnDocumentCreated registration cannot see the page's real
            // navigator.credentials, so - like the YouTube stripper - this must go through
            // Page.addScriptToEvaluateOnNewDocument to actually patch the page context.
            // This is what stops the address-bar blink storm on pages that show the
            // "Continue with google.com" prompt (e.g. The Guardian). Passkeys are untouched.
            await InstallFedCmSuppressor(core);

            // Intercept custom-scheme (discord:// etc.) launches in the MAIN world. WebView2's
            // LaunchingExternalUriScheme event does not fire for iframe/anchor-initiated launches
            // in this SDK, and an isolated-world script cannot patch the page's window.open /
            // location / iframe.src. So this must run in the main world via CDP, exactly like the
            // FedCM suppressor above. It reports catches through a CustomEvent that the isolated
            // ExternalSchemeBridgeJs relays to the host.
            await InstallExternalSchemeInterceptor(core);

            // Inject fetch/XHR blocker into main world via DevTools Protocol
            core.NavigationCompleted += (_, _) => InjectMainWorldBlocker(core);
        }

        // Install the external-scheme interceptor into the main world, once per CoreWebView2,
        // via Page.addScriptToEvaluateOnNewDocument so it runs before page scripts on every
        // document and in every frame.
        private static async Task InstallExternalSchemeInterceptor(CoreWebView2 core)
        {
            try
            {
                try { await core.CallDevToolsProtocolMethodAsync("Page.enable", "{}"); } catch { }
                string escapedJs = ExternalSchemeMainWorldJs.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string cdpParams = "{\"source\":\"" + escapedJs + "\"}";
                await core.CallDevToolsProtocolMethodAsync("Page.addScriptToEvaluateOnNewDocument", cdpParams);
            }
            catch { }
        }

        // Install the FedCM / One Tap suppressor into the main world, once per CoreWebView2,
        // via Page.addScriptToEvaluateOnNewDocument so it runs before page scripts on every
        // document and in every frame. Only the FedCM ({identity:...}) credential path is
        // neutralised; passkeys ({publicKey:...}) and classic Credential Management pass
        // through. Awaited so the patch is in place before the first document loads.
        private static async Task InstallFedCmSuppressor(CoreWebView2 core)
        {
            try
            {
                try { await core.CallDevToolsProtocolMethodAsync("Page.enable", "{}"); } catch { }
                string escapedJs = FedCmSuppressJs.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string cdpParams = "{\"source\":\"" + escapedJs + "\"}";
                await core.CallDevToolsProtocolMethodAsync("Page.addScriptToEvaluateOnNewDocument", cdpParams);
            }
            catch { }
        }

        // Install the main-world YouTube ad blocker once per CoreWebView2, independent of
        // navigation. Page.addScriptToEvaluateOnNewDocument runs the script in the main world
        // before any page script on every subsequent document - top-level loads, SPA
        // soft-navigations, and back/forward alike. The script self-guards on hostname, so it
        // is inert everywhere except YouTube. Falls back to AddScriptToExecuteOnDocumentCreated
        // (isolated-world wrapper) if CDP is unavailable.
        private static async Task InstallYouTubeMainWorld(CoreWebView2 core)
        {
            try
            {
                // Page domain must be enabled before addScriptToEvaluateOnNewDocument works.
                try { await core.CallDevToolsProtocolMethodAsync("Page.enable", "{}"); } catch { }

                string escapedJs = YouTubeMainWorldCode.Replace("\\", "\\\\").Replace("\"", "\\\"");
                // includeCommandLineAPI false, worldName empty = main world, runs in all frames.
                string cdpParams = "{\"source\":\"" + escapedJs + "\"}";
                await core.CallDevToolsProtocolMethodAsync("Page.addScriptToEvaluateOnNewDocument", cdpParams);
            }
            catch { }

            // AddScriptToExecuteOnDocumentCreated runs in iframes (isolated world).
            // The injector wraps the main-world code in a <script> tag so it runs there too.
            try { _ = core.AddScriptToExecuteOnDocumentCreatedAsync(YouTubeMainWorldInjectorJs); } catch { }
            // DOM scrubber / skip-ad clicker for embed players.
            try { _ = core.AddScriptToExecuteOnDocumentCreatedAsync(BuildYouTubeEmbedScrubberJs()); } catch { }

            // When an iframe is created, inject into it immediately via CDP Runtime.evaluate
            // (runs in the main world of that frame before any page script has a chance to fire)
            // plus delayed scrub passes to catch dynamically-inserted ad elements.
            try
            {
                core.FrameCreated += (_, args) =>
                {
                    try
                    {
                        var frame = args.Frame;

                        // Immediate main-world injection via script-tag trick in isolated world.
                        // This is the earliest we can touch the frame.
                        void InjectFrame()
                        {
                            try { _ = frame.ExecuteScriptAsync(BuildYouTubeEmbedScrubberJs()); }
                            catch { }
                        }

                        InjectFrame();
                        _ = Task.Delay(200).ContinueWith(_ => InjectFrame());
                        _ = Task.Delay(600).ContinueWith(_ => InjectFrame());
                        _ = Task.Delay(1500).ContinueWith(_ => InjectFrame());
                        _ = Task.Delay(3000).ContinueWith(_ => InjectFrame());
                    }
                    catch { }
                };
            }
            catch { }
        }

        private static string BuildYouTubeEmbedScrubberJs()
        {
            // Injected into every youtube*/youtube-nocookie* iframe via a <script> tag
            // (main world). Combines:
            //  1. A fetch interceptor that strips adPlacements from /youtubei/v1/player
            //     responses BEFORE the embed player JS reads them - stops pre-roll ads
            //     at the data level rather than skipping them after they start.
            //  2. The DOM scrubber + skip-ad clicker (YouTubeAdBlockerJs) as a fallback
            //     for any ad elements that slip through.
            const string fetchInterceptor =
                "(function(){" +
                "if(window.__ceprkacEmbedFetch)return;window.__ceprkacEmbedFetch=true;" +
                "var adKeys=['adPlacements','adSlots','playerAds','adBreakHeartbeatParams'," +
                "'ad3Module','adSafetyReason','adLoggingData','showAdSlots','adBreakParams'," +
                "'adBreakStatus','adVideoId','adLayoutLoggingData','instreamAdPlayerOverlayRenderer'," +
                "'adPlacementConfig','adVideoStitcherConfig'];" +
                "function strip(o,d){if(!o||typeof o!=='object'||d>12)return;" +
                "for(var i=0;i<adKeys.length;i++)if(o.hasOwnProperty(adKeys[i]))delete o[adKeys[i]];" +
                "var k=Object.keys(o);for(var j=0;j<k.length;j++){" +
                "var v=o[k[j]];" +
                "if(Array.isArray(v)){for(var m=v.length-1;m>=0;m--){" +
                "var it=v[m];if(it&&typeof it==='object'){" +
                "var ik=Object.keys(it);var bad=false;" +
                "for(var n=0;n<ik.length;n++){if(/^(ad|promoted|sponsor)/i.test(ik[n])){bad=true;break;}}" +
                "if(bad)v.splice(m,1);else strip(it,d+1);}}}" +
                "else if(v&&typeof v==='object')strip(v,d+1);}}" +
                "var op=JSON.parse;JSON.parse=function(){var r=op.apply(this,arguments);" +
                "try{if(r&&typeof r==='object')strip(r,0);}catch(e){}return r;};" +
                "var oF=window.fetch;window.fetch=function(){var a=arguments;" +
                "var url=typeof a[0]==='string'?a[0]:(a[0]&&a[0].url?a[0].url:'');" +
                "if(!/youtubei\\/v1\\/(player|browse|next)/.test(url))return oF.apply(this,a);" +
                "return oF.apply(this,a).then(function(resp){" +
                "if(!resp||!resp.ok)return resp;" +
                "return resp.clone().text().then(function(txt){" +
                "try{var data=op.call(JSON,txt);strip(data,0);" +
                "return new Response(JSON.stringify(data),{status:resp.status,statusText:resp.statusText,headers:resp.headers});}" +
                "catch(e){return resp;}});});};" +
                "})();";

            string domScrubber = JsonSerializer.Serialize(YouTubeAdBlockerJs);

            return
                "(function(){" +
                "var h=(location.hostname||'').toLowerCase();" +
                "if(h.indexOf('youtube')===-1)return;" +
                // Fetch interceptor - installs unconditionally so every frame load gets it.
                fetchInterceptor +
                // DOM scrubber injected as a <script> tag into the main world.
                "if(window.__ceprkacYtScrub)return;window.__ceprkacYtScrub=true;" +
                "var s=document.createElement('script');" +
                "s.textContent=" + domScrubber + ";" +
                "(document.documentElement||document.head||document.body).appendChild(s);s.remove();" +
                "})()";
        }

        private const string AdElementHiderJs = @"(function() {
            if (window.__ceprkacAdHider) return;
            window.__ceprkacAdHider = true;
            /* XenForo / Discourse: generic ad CSS and <article> scrapers blank the whole page. */
            var root = document.documentElement;
            if (root && (root.id === 'XF' || root.getAttribute('data-app') === 'public'
                || document.querySelector('.p-pageWrapper, [data-xf-init], #d-splash, .d-header')))
                return;

            var host = (location.hostname || '').toLowerCase();

            /* CSS-based hiding - catches ads before JS runs */
            var css = document.createElement('style');
            css.textContent = [
                'ins.adsbygoogle','[id*=""google_ads""]','[class*=""ad-slot""]','[class*=""advert""]',
                '[class*=""ad-banner""]','[class*=""ad-container""]','[class*=""ad-wrapper""]',
                '[class*=""ad-placement""]',
                '[data-adunit]','[data-ad-slot]','[data-google-query-id]',
                '.sponsored-content','.ad-banner','.ad-container','.ad-wrapper',
                '.native-ad','.ad-unit','.ad-zone','.ad-area','.ad-block','.ad-box','.ad-frame',
                '.ad-header','.ad-footer','.ad-leaderboard','.ad-sidebar','.ad-skyscraper',
                '.ad-rectangle','.ad-interstitial','.ad-overlay','.ad-popup','.ad-modal',
                'div[id*=""taboola""]','div[id*=""outbrain""]','div[class*=""taboola""]',
                'div[class*=""outbrain""]','div[id*=""zergnet""]','div[id*=""revcontent""]',
                'div[id*=""mgid""]','div[class*=""mgid""]',
                'iframe[src*=""doubleclick""]','iframe[src*=""googlesyndication""]',
                'iframe[src*=""googletagmanager""]','iframe[id*=""google_ads""]','iframe[id*=""aswift""]',
                'iframe[src*=""ad""][width]','iframe[data-ad]',
                '.video-ad-overlay','.preroll-ad','.midroll-ad',
                'a[href*=""doubleclick.net""]','a[href*=""googleadservices""]',
                'div[aria-label=""Advertisement""]','div[aria-label=""advertisement""]',
                'section[aria-label=""Sponsored""]',
                /* Pornhub / adult site ads */
                '.adBanner','.ad-banner','#hd-rightColAd','#pb_ad','.advertisement',
                '.mgbox','[class*=""mgbox""]','div[id*=""snigelAdStack""]',
                '.trafficStars','[class*=""trafficStars""]','[id*=""trafficStars""]',
                '[class*=""exoclick""]','[id*=""exoclick""]',
                'iframe[src*=""trafficstars""]','iframe[src*=""exoclick""]',
                'iframe[src*=""trafficjunky""]','iframe[src*=""adsrv""]',
                'iframe[src*=""juicyads""]','iframe[src*=""exosrv""]',
                'iframe[src*=""tsyndicate""]','iframe[src*=""realsrv""]',
                'div[class*=""abovePlayer""]',
                /* DuckDuckGo sponsored results and self-promo */
                '.result--ad','.is-ad','[data-testid=""ad""]','[data-testid=""result--ad""]',
                '.badge--ad','.result__extras__url--ad',
                '.ddg-extension-hide','.js-sidebar-ads','.sidebar-modules--ads',
                '.header-aside',
                /* Google sponsored results */
                '#tads','#tadsb','#bottomads','.commercial-unit-desktop-top',
                '.commercial-unit-desktop-rhs','.cu-container',
                'div[data-text-ad]','div[data-hveid] .uEierd',
                /* Bing sponsored results */
                '.b_ad','.b_adSlug','li.b_ad','#b_results > .b_ad',
                /* Yahoo sponsored results */
                '.searchCenterTopAds','.searchCenterBottomAds','.compDlink',
                /* Reddit promoted posts (GSecurity Ad Shield) */
                'shreddit-ad-post','[data-testid=""ad-post""]','[data-testid=""promoted-post""]',
                'div[data-promoted=""true""]','.promotedlink','.sponsorshipbox','.sponsor-logo',
                'faceplate-tracker[source=""ad""]','faceplate-tracker[noun=""ad""]',
                '[data-testid=""sidebar-ad""]','[data-testid=""subreddit-sidebar-ad""]',
                '.sidebar-ad','div[class*=""promotedlink""]','.premium-banner-outer',
                '[data-testid=""premium-upsell""]',
                'shreddit-experience-tree[bundlename*=""ad""]','shreddit-experience-tree[bundlename*=""Ad""]',
                '.thing.promoted','.thing.stickied.promotedlink',
                /* LinkedIn ads */
                '[data-ad-banner-id]','[data-is-sponsored=""true""]',
                '.ad-banner-container','.ads-container',
                /* Twitch ads */
                '[data-a-target=""video-ad-label""]','.video-ad','.advertisement-banner',
                '[data-test-selector=""ad-banner-default-id""]','.stream-display-ad',
                /* TikTok ads */
                '[class*=""DivAdBanner""]','[data-e2e=""ad""]'
            ].join(',') + '{display:none!important;height:0!important;min-height:0!important;overflow:hidden!important}';
            (document.head || document.documentElement).appendChild(css);

            /* DOM removal selectors */
            var sels = [
                'ins.adsbygoogle','iframe[src*=""doubleclick""]','iframe[src*=""googlesyndication""]',
                'iframe[src*=""googletagmanager""]','iframe[id*=""google_ads""]','iframe[id*=""aswift""]',
                'iframe[src*=""ad""][width]','iframe[data-ad]',
                '[id*=""google_ads""]','[class*=""ad-slot""]','[class*=""advert""]','[class*=""ad-banner""]',
                '[class*=""ad-container""]','[class*=""ad-wrapper""]',
                '[class*=""ad-placement""]',
                '[data-adunit]','[data-ad-slot]','[data-google-query-id]',
                '.sponsored-content','.ad-banner','.ad-container','.ad-wrapper',
                '.native-ad','.ad-unit','.ad-zone','.ad-area','.ad-block','.ad-box','.ad-frame',
                '.ad-header','.ad-footer','.ad-leaderboard','.ad-sidebar','.ad-skyscraper',
                '.ad-rectangle','.ad-interstitial','.ad-overlay','.ad-popup','.ad-modal',
                'div[id*=""taboola""]','div[id*=""outbrain""]','div[class*=""taboola""]',
                'div[class*=""outbrain""]','div[id*=""zergnet""]','div[id*=""revcontent""]',
                'div[id*=""mgid""]','div[class*=""mgid""]',
                '.video-ad-overlay','.preroll-ad','.midroll-ad',
                'div[aria-label=""Advertisement""]','div[aria-label=""advertisement""]',
                /* Search engine sponsored results */
                '.result--ad','.is-ad','[data-testid=""ad""]','[data-testid=""result--ad""]',
                '.badge--ad','.ddg-extension-hide','.js-sidebar-ads','.header-aside',
                '#tads','#tadsb','#bottomads','.commercial-unit-desktop-top',
                '.commercial-unit-desktop-rhs','div[data-text-ad]',
                '.b_ad','.b_adSlug','li.b_ad',
                '.searchCenterTopAds','.searchCenterBottomAds',
                /* Reddit (GSecurity Ad Shield) */
                'shreddit-ad-post','[data-testid=""ad-post""]','[data-testid=""promoted-post""]',
                'div[data-promoted=""true""]','.promotedlink','.sponsorshipbox','.sponsor-logo',
                '#ad-frame','#ad_main',
                'faceplate-tracker[source=""ad""]','faceplate-tracker[noun=""ad""]',
                '[data-testid=""sidebar-ad""]','[data-testid=""subreddit-sidebar-ad""]',
                'shreddit-experience-tree[bundlename*=""ad""]','shreddit-experience-tree[bundlename*=""Ad""]',
                '.premium-banner-outer','[data-testid=""premium-upsell""]',
                /* LinkedIn */
                '[data-ad-banner-id]','[data-is-sponsored=""true""]',
                '.ad-banner-container','.ads-container',
                /* Twitch */
                '[data-a-target=""video-ad-label""]','.video-ad','.advertisement-banner',
                '[data-test-selector=""ad-banner-default-id""]','.stream-display-ad',
                /* TikTok */
                '[class*=""DivAdBanner""]','[data-e2e=""ad""]'
            ];
            function scrub() {
                for (var i = 0; i < sels.length; i++) {
                    try {
                        var els = document.querySelectorAll(sels[i]);
                        for (var j = 0; j < els.length; j++) {
                            if (els[j] && els[j].parentElement) els[j].remove();
                        }
                    } catch(e) {}
                }
                /* Reddit / Facebook / X / Instagram only - XenForo posts are <article> */
                if (/(^|\.)reddit\.com$|(^|\.)redditmedia\.com$/.test(host)) {
                    try {
                        document.querySelectorAll('article, [data-testid=""post-container""], .thing').forEach(function(post) {
                            var badges = post.querySelectorAll('span, faceplate-tracker, [slot=""credit-bar""], .tagline');
                            for (var k = 0; k < badges.length; k++) {
                                var text = (badges[k].textContent || '').trim().toLowerCase();
                                if (text === 'promoted' || text === 'sponsored') { post.remove(); break; }
                            }
                        });
                        document.querySelectorAll('shreddit-post').forEach(function(post) {
                            if (post.hasAttribute('is-promoted') || post.getAttribute('post-type') === 'promoted') post.remove();
                        });
                    } catch(e) {}
                }
                if (/(^|\.)facebook\.com$|(^|\.)fb\.com$/.test(host)) {
                    try {
                        document.querySelectorAll('div[role=""article""], div[role=""feed""] > div').forEach(function(article) {
                            var spans = article.querySelectorAll('span');
                            for (var k = 0; k < spans.length; k++) {
                                if ((spans[k].textContent || '').trim().toLowerCase() === 'sponsored') {
                                    article.style.display = 'none'; break;
                                }
                            }
                        });
                    } catch(e) {}
                }
                if (/(^|\.)twitter\.com$|(^|\.)x\.com$/.test(host)) {
                    try {
                        document.querySelectorAll('article, [data-testid=""placementTracking""]').forEach(function(el) {
                            var text = (el.textContent || '').toLowerCase();
                            if (/\bpromoted\b/.test(text) || /\bad\s*-/.test(text) || el.matches('[data-testid=""placementTracking""]')) {
                                el.style.display = 'none';
                            }
                        });
                    } catch(e) {}
                }
                if (/(^|\.)instagram\.com$/.test(host)) {
                    try {
                        document.querySelectorAll('article').forEach(function(a) {
                            if (/\bsponsored\b/i.test(a.textContent || '')) a.style.display = 'none';
                        });
                        document.querySelectorAll('[data-testid=""reel-ad""]').forEach(function(el) { el.remove(); });
                    } catch(e) {}
                }
            }
            scrub();
            setInterval(scrub, 1500);
            new MutationObserver(scrub).observe(document.documentElement, {childList:true, subtree:true});
        })()";

        private const string YouTubeAdBlockerJs = @"(function() {
            if (window.__ceprkacYtAdBlock) return;
            window.__ceprkacYtAdBlock = true;
            var s = document.createElement('style');
            s.textContent = 'ytd-display-ad-renderer,ytd-ad-slot-renderer,ytd-promoted-video-renderer,ytd-promoted-sparkles-web-renderer,ytd-promoted-sparkles-text-search-renderer,ytd-banner-promo-renderer,ytd-statement-banner-renderer,ytd-in-feed-ad-layout-renderer,ytd-masthead-ad-renderer,ytd-primetime-promo-renderer,ytd-compact-promoted-video-renderer,ytd-action-companion-ad-renderer,ytd-mealbar-promo-renderer,ytd-enforcement-message-view-model,ytd-engagement-panel-section-list-renderer[target-id=engagement-panel-ads],#masthead-ad,#player-ads,.video-ads,.ytp-ad-module,.ytp-ad-overlay-container,.ytp-ad-player-overlay,.ytp-ad-action-interstitial,.ytp-ad-image-overlay,.ytp-ad-text-overlay,.ytp-ad-skip-ad-slot,.ad-showing .ytp-ad-module,ytd-search-pyv-renderer,ytd-movie-offer-module-renderer,tp-yt-paper-dialog:has(#dismiss-button),ytd-popup-container:has(a[href*=""/premium""]),ytd-rich-item-renderer:has(ytd-ad-slot-renderer),ytd-rich-item-renderer:has(ytd-display-ad-renderer),ytd-rich-item-renderer:has(ytd-promoted-video-renderer),ytd-rich-item-renderer:has(ytd-promoted-sparkles-web-renderer),ytd-rich-section-renderer:has(ytd-ad-slot-renderer){display:none!important}';
            (document.head||document.documentElement).appendChild(s);
            var adKeys=['adPlacements','adSlots','playerAds','adBreakHeartbeatParams','ad3Module','adSafetyReason','adLoggingData','showAdSlots','adBreakParams','adBreakStatus','adVideoId','adLayoutLoggingData','instreamAdPlayerOverlayRenderer','adPlacementConfig','adVideoStitcherConfig','promotedSparklesWebRenderer','promotedSparklesTextSearchRenderer','promotedVideoRenderer','sponsoredCardRenderer','adSlotRenderer','displayAdRenderer','inFeedAdLayoutRenderer','mastheadAdRenderer','compactPromotedVideoRenderer','actionCompanionAdRenderer','bannerPromoRenderer','statementBannerRenderer','primeTimePromoRenderer','searchPyvRenderer','movieOfferModuleRenderer','adPlacementRenderer','sparklesAdRenderer'];
            function stripAds(o,d){if(!o||typeof o!=='object'||d>12)return;for(var i=0;i<adKeys.length;i++)if(o.hasOwnProperty(adKeys[i]))delete o[adKeys[i]];var k=Object.keys(o);for(var j=0;j<k.length;j++){var key=k[j],val=o[key];if(Array.isArray(val)){for(var m=val.length-1;m>=0;m--){var item=val[m];if(item&&typeof item==='object'){var ik=Object.keys(item);for(var n=0;n<ik.length;n++){if(/^(ad|promoted|sponsor)/i.test(ik[n])){val.splice(m,1);break;}}}}}else if(val&&typeof val==='object')stripAds(val,d+1);}}
            var op=JSON.parse;JSON.parse=function(){var r=op.apply(this,arguments);try{if(r&&typeof r==='object')stripAds(r,0);}catch(e){}return r;};
            ['ytInitialPlayerResponse','ytInitialData','ytcfg'].forEach(function(p){var v=window[p];try{Object.defineProperty(window,p,{configurable:true,get:function(){return v;},set:function(n){if(n&&typeof n==='object')stripAds(n,0);v=n;}});if(v)window[p]=v;}catch(e){}});
            var adS=['.video-ads','.ytp-ad-module','.ytp-ad-overlay-container','.ytp-ad-player-overlay','.ytp-ad-action-interstitial','.ytp-ad-image-overlay','.ytp-ad-text-overlay','#player-ads','#masthead-ad','ytd-display-ad-renderer','ytd-ad-slot-renderer','ytd-promoted-video-renderer','ytd-promoted-sparkles-web-renderer','ytd-banner-promo-renderer','ytd-in-feed-ad-layout-renderer','ytd-mealbar-promo-renderer','ytd-enforcement-message-view-model','ytd-search-pyv-renderer','ytd-movie-offer-module-renderer','ytd-compact-promoted-video-renderer','ytd-action-companion-ad-renderer','ytd-primetime-promo-renderer','ytd-masthead-ad-renderer'];
            var skS=['.ytp-ad-skip-button','.ytp-skip-ad-button','.ytp-ad-skip-button-modern','.ytp-skip-ad-button__text','button[class*=""skip""]','.ytp-ad-overlay-close-button','.ytp-ad-skip-button-slot'];
            /* Localized sponsored/ad badge words - covers major YouTube UI languages */
            var sponsorWords=['sponsored','sponzorirano','gesponsert','sponsoris','patrocinado','sponsorizzato','gesponsord','','','','','reklam','promowane','sponzorovan','szponzorlt','annonce','reklama','hirdets','','commandit','gesponsord','publicidad','pubblicit','anncio','reklame','sponzorovno','sponzorovan','sponzorirane',''];
            function isSponsoredText(t){t=t.trim().toLowerCase();for(var i=0;i<sponsorWords.length;i++){if(t===sponsorWords[i])return true;}return false;}
            function scrub(){for(var i=0;i<adS.length;i++)document.querySelectorAll(adS[i]).forEach(function(e){var p=e.closest('ytd-rich-item-renderer,ytd-rich-section-renderer,ytd-reel-shelf-renderer');if(p)p.remove();else e.remove();});for(var j=0;j<skS.length;j++)document.querySelectorAll(skS[j]).forEach(function(b){if(b.click)b.click();});/* Walk homepage rich grid items and remove sponsored cards by badge text */try{document.querySelectorAll('ytd-rich-item-renderer,ytd-rich-section-renderer').forEach(function(item){if(item.querySelector('ytd-ad-slot-renderer,ytd-display-ad-renderer,ytd-promoted-video-renderer,ytd-promoted-sparkles-web-renderer,ytd-in-feed-ad-layout-renderer')){item.remove();return;}var badges=item.querySelectorAll('span.ytd-badge-supported-renderer,ytd-badge-supported-renderer span,div.ytd-badge-supported-renderer,ytd-badge-supported-renderer,[class*=""badge""],.badge,.badge-style-type-ad,span[aria-label]');for(var k=0;k<badges.length;k++){if(isSponsoredText(badges[k].textContent||'')){item.remove();return;}}/* Check inline-block ad metadata text */var metas=item.querySelectorAll('#metadata-line span,#byline-container span,yt-formatted-string.ytd-channel-name');for(var m=0;m<metas.length;m++){if(isSponsoredText(metas[m].textContent||'')){item.remove();return;}}});}catch(e){}/* Walk search results for promoted items */try{document.querySelectorAll('ytd-video-renderer,ytd-compact-video-renderer').forEach(function(item){var badges=item.querySelectorAll('span.ytd-badge-supported-renderer,ytd-badge-supported-renderer span,[class*=""badge""]');for(var k=0;k<badges.length;k++){if(isSponsoredText(badges[k].textContent||'')){item.remove();return;}}});}catch(e){}var p=document.querySelector('.html5-video-player'),v=document.querySelector('video');if(p&&v&&(p.classList.contains('ad-showing')||p.classList.contains('ad-interrupting'))){if(Number.isFinite(v.duration)&&v.duration>0){v.currentTime=Math.max(0,v.duration-0.1);}v.muted=true;v.playbackRate=16;try{v.play();}catch(e){}p.classList.remove('ad-showing');p.classList.remove('ad-interrupting');p.classList.remove('ad-created');document.querySelectorAll('.ytp-ad-skip-button,.ytp-skip-ad-button,.ytp-ad-skip-button-modern').forEach(function(b){b.click();});setTimeout(function(){v.muted=false;v.playbackRate=1;},500);}document.querySelectorAll('ytd-rich-item-renderer').forEach(function(el){var hasAd=!!el.querySelector('ytd-ad-slot-renderer,ytd-display-ad-renderer,ytd-promoted-video-renderer,ytd-promoted-sparkles-web-renderer');if(hasAd){el.remove();return;}});document.querySelectorAll('tp-yt-paper-dialog').forEach(function(d){var t=(d.textContent||'').toLowerCase();if(t.includes('ad blocker')||t.includes('allow ads')){var b=d.querySelector('#dismiss-button,.dismiss-button,button');if(b&&b.click)b.click();d.remove();}});}
            scrub();setInterval(scrub,200);new MutationObserver(scrub).observe(document.documentElement,{childList:true,subtree:true});
        })()";

        // Main-world YouTube ad blocker - built at runtime to handle nested quotes cleanly
        private static readonly string YouTubeMainWorldCode = BuildYouTubeMainWorldCode();
        // Hostname-guarded <script> injector for AddScriptToExecuteOnDocumentCreatedAsync
        private static readonly string YouTubeMainWorldInjectorJs = BuildYouTubeInjector();

        private static string BuildYouTubeMainWorldCode()
        {
            return
                "(function(){" +
                // Strict YouTube-only guard - never run on auth/OAuth domains
                "var h=location.hostname.toLowerCase();" +
                // Include youtube-nocookie.com - Discord/Twitter/etc. embeds often use it.
                "if(h!=='youtube.com'&&h!=='www.youtube.com'&&h!=='m.youtube.com'&&h!=='music.youtube.com'" +
                "&&h!=='youtube-nocookie.com'&&h!=='www.youtube-nocookie.com'" +
                "&&!h.endsWith('.youtube.com')&&!h.endsWith('.youtube-nocookie.com'))return;" +
                // Extra safety: bail on any auth/OAuth page that might be in a YouTube subdomain
                "if(/accounts\\.google|login\\.microsoft|appleid\\.apple|auth0\\.com|clerk\\.|oauth/.test(h))return;" +
                "if(window.__ceprkacYtMain)return;window.__ceprkacYtMain=true;" +
                // Extended ad keys list
                "var adKeys=['adPlacements','adSlots','playerAds','adBreakHeartbeatParams','ad3Module'," +
                "'adSafetyReason','adLoggingData','showAdSlots','adBreakParams','adBreakStatus'," +
                "'adVideoId','adLayoutLoggingData','instreamAdPlayerOverlayRenderer'," +
                "'adPlacementConfig','adVideoStitcherConfig'," +
                "'promotedSparklesWebRenderer','promotedSparklesTextSearchRenderer'," +
                "'promotedVideoRenderer','sponsoredCardRenderer','adSlotRenderer'," +
                "'displayAdRenderer','inFeedAdLayoutRenderer','mastheadAdRenderer'," +
                "'compactPromotedVideoRenderer','actionCompanionAdRenderer'," +
                "'bannerPromoRenderer','statementBannerRenderer','primeTimePromoRenderer'," +
                "'searchPyvRenderer','movieOfferModuleRenderer','adPlacementRenderer','sparklesAdRenderer'];" +
                // Recursive strip function - deletes ad keys and splices ad items from arrays
                "function strip(o,d){if(!o||typeof o!=='object'||d>15)return;" +
                "for(var i=0;i<adKeys.length;i++)if(o.hasOwnProperty(adKeys[i]))delete o[adKeys[i]];" +
                "var k=Object.keys(o);for(var j=0;j<k.length;j++){" +
                "var key=k[j],val=o[key];" +
                "if(Array.isArray(val)){for(var m=val.length-1;m>=0;m--){" +
                "var item=val[m];if(item&&typeof item==='object'){" +
                "var ik=Object.keys(item);var isAd=false;" +
                "for(var n=0;n<ik.length;n++){" +
                "if(/^(ad|promoted|sponsor)/i.test(ik[n])){isAd=true;break;}}" +
                // Also check for adSlotRenderer or promotedVideoRenderer nested inside richItemRenderer
                "if(!isAd&&item.richItemRenderer&&item.richItemRenderer.content){" +
                "var ck=Object.keys(item.richItemRenderer.content);" +
                "for(var c=0;c<ck.length;c++){if(/^(ad|promoted|sponsor)/i.test(ck[c])){isAd=true;break;}}}" +
                // Check for badge text indicating sponsored content (BADGE_STYLE_TYPE_AD or localized label)
                "if(!isAd){try{var js=JSON.stringify(item);" +
                "if(/\"style\":\"BADGE_STYLE_TYPE_AD\"/.test(js)||" +
                "/\"label\":\"(?:Sponsored|Sponzorirano|Gesponsert|Sponsoris|Patrocinado|Sponsorizzato|Gesponsord||a||||Reklam|Promowane|Sponzorovan|Szponzorlt|Annonce|Reklama|Hirdets|Commandit|Publicidad|Pubblicit|Anncio|Reklame|Sponzorovno|Sponzorirane|)\"/.test(js))" +
                "{isAd=true;}}catch(e){}}" +
                "if(isAd){val.splice(m,1);}" +
                "else{strip(item,d+1);}" +
                "}}" +
                "}else if(val&&typeof val==='object')strip(val,d+1);}}" +
                // Intercept JSON.parse - catches ytInitialData embedded in <script> tags
                "var op=JSON.parse;JSON.parse=function(){var r=op.apply(this,arguments);" +
                "try{if(r&&typeof r==='object')strip(r,0);}catch(e){}return r;};" +
                // Intercept ytInitialPlayerResponse, ytInitialData - catches direct assignments
                "['ytInitialPlayerResponse','ytInitialData'].forEach(function(p){var v=window[p];" +
                "try{Object.defineProperty(window,p,{configurable:true," +
                "get:function(){return v;},set:function(n){if(n&&typeof n==='object')strip(n,0);v=n;}});" +
                "if(v)window[p]=v;}catch(e){}});" +
                // Intercept fetch responses for YouTube API calls (browse/search/next/player)
                "var oFetch=window.fetch;window.fetch=function(){var args=arguments;" +
                "var url=typeof args[0]==='string'?args[0]:(args[0]&&args[0].url?args[0].url:'');" +
                "if(!/youtubei\\/v1\\/(browse|search|next|player|reel)/.test(url))return oFetch.apply(this,args);" +
                "return oFetch.apply(this,args).then(function(resp){" +
                "if(!resp||!resp.ok)return resp;" +
                "return resp.clone().text().then(function(txt){" +
                "try{var data=op.call(JSON,txt);strip(data,0);" +
                "return new Response(JSON.stringify(data),{status:resp.status,statusText:resp.statusText,headers:resp.headers});" +
                "}catch(e){return resp;}});});};" +
                "})()";
        }

        // Fallback injector - wraps the main world code in a <script> tag for AddScriptToExecuteOnDocumentCreatedAsync
        private static string BuildYouTubeInjector()
        {
            string escaped = YouTubeMainWorldCode.Replace("\\", "\\\\").Replace("'", "\\'");
            // indexOf('youtube') also matches youtube-nocookie.com embeds.
            return "(function(){if(location.hostname.toLowerCase().indexOf('youtube')===-1)return;" +
                   "var sc=document.createElement('script');" +
                   "sc.textContent='" + escaped + "';" +
                   "(document.head||document.documentElement).appendChild(sc);sc.remove();})()";
        }

        private async Task LoadOrUpdateBlocklistAsync()
        {
            // Load bundled blocklist from app directory
            var bundledList = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blocklist.txt");
            if (File.Exists(bundledList))
            {
                int count = 0;
                foreach (var line in File.ReadAllLines(bundledList))
                {
                    var domain = line.Trim();
                    if (!string.IsNullOrEmpty(domain) && !domain.StartsWith("#") && domain.Contains('.'))
                    {
                        BlockedAdDomains.Add(domain);
                        count++;
                    }
                }
                statusLabel.Text = $"Ad blocker: {BlockedAdDomains.Count} domains loaded.";
            }

            // Also try to load/update from appdata (user can drop a custom blocklist.txt there)
            var userList = Path.Combine(appDataFolder, "blocklist.txt");
            if (File.Exists(userList))
            {
                foreach (var line in File.ReadAllLines(userList))
                {
                    var domain = line.Trim();
                    if (!string.IsNullOrEmpty(domain) && !domain.StartsWith("#") && domain.Contains('.'))
                        BlockedAdDomains.Add(domain);
                }
            }
            await Task.CompletedTask;
        }

        private static bool IsChallengePage(CoreWebView2 core)
        {
            try
            {
                var src = (core.Source ?? "").ToLowerInvariant();
                if (src.Contains("cdn-cgi/") || src.Contains("__cf_chl") || src.Contains("challenges.cloudflare"))
                    return true;
                var title = (core.DocumentTitle ?? "").ToLowerInvariant();
                if (title.Contains("just a moment") || title.Contains("attention required") ||
                    title.Contains("checking your browser") || title.Contains("please wait"))
                    return true;
            }
            catch { }
            return false;
        }

        private async void InjectMainWorldBlocker(CoreWebView2 core)
        {
            if (BlockedAdDomains.Count == 0) return;
            if (IsChallengePage(core)) return;
            // Skip YouTube - it gets its own dedicated main-world injection
            try
            {
                var pageHost = new Uri(core.Source ?? "").Host.ToLower();
                if (pageHost == "www.youtube.com" || pageHost == "youtube.com" ||
                    pageHost == "m.youtube.com" || pageHost == "music.youtube.com" ||
                    pageHost.EndsWith(".youtube.com"))
                    return;
            }
            catch { }
            try
            {
                var xf = await core.ExecuteScriptAsync(
                    "(document.documentElement&&(document.documentElement.id==='XF'||document.documentElement.getAttribute('data-app')==='public'||!!document.querySelector('.p-pageWrapper,[data-xf-init]')))");
                if (xf != null && xf.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
            }
            catch { }
            try
            {
                // Build the blocker JS
                var topDomains = BlockedAdDomains
                    .Where(d => !d.Contains('*') && d.Length > 3 && d.Length < 60)
                    .Take(15000)
                    .ToList();

                var sb = new System.Text.StringBuilder();
                sb.Append("(function(){if(window.__cFB)return;window.__cFB=1;var b=new Set([");
                bool first = true;
                foreach (var d in topDomains)
                {
                    if (!first) sb.Append(',');
                    sb.Append('"');
                    sb.Append(d.Replace("\"", "").Replace("\\", ""));
                    sb.Append('"');
                    first = false;
                }
                sb.Append("]);");
                sb.Append("var wl=new Set(['google.com','youtube.com','accounts.google.com','apis.google.com','ssl.gstatic.com','gstatic.com','discord.com','discordapp.com','github.com','paypal.com','ebay.com','apple.com','icloud.com','mediafire.com','login.microsoftonline.com','login.live.com','pay.google.com','gog.com','steampowered.com','steamcommunity.com','epicgames.com','ea.com','origin.com','ubisoft.com','blizzard.com','battle.net','riotgames.com','xbox.com','playstation.com','nintendo.com','twitch.tv','chase.com','bankofamerica.com','wellsfargo.com','citibank.com','capitalone.com','revolut.com','wise.com','stripe.com','n26.com','cloudflare.com','challenges.cloudflare.com']);");
                sb.Append("function isWl(h){while(h){if(wl.has(h))return 1;var i=h.indexOf('.');if(i<0)break;h=h.substr(i+1);}return 0};");
                sb.Append("function chk(u){try{if(isWl(location.hostname))return 0;var l=u.toLowerCase();var h=new URL(l).hostname;if(isWl(h))return 0;while(h){if(b.has(h))return 1;var i=h.indexOf('.');if(i<0)break;h=h.substr(i+1);}");
                sb.Append("if(/(\\/ads?\\/|\\/ad[sx]?\\b|\\/pagead\\/|\\/ptracking|\\/advert|\\/sponsored|\\/promotion|\\/tracking\\/|\\/analytics\\/|\\/collect\\?|\\/beacon|\\/pixel|\\/imp\\?|\\/impression|\\/click\\?|ad_banner|ad_frame|sponsored_content|promo_banner|[?&](ad|ads|adunit|adformat|adtag)=)/i.test(l))return 1;");
                sb.Append("if(/(?:\\/(?:adcontent|img\\/adv|web-ad|iframead|contentad|ad\\/image|video-ad|stats\\/event|xtclicks|adscript|bannerad|googlead|adhandler|adimages|adconfig|tracking\\/track|tracker\\/track|adrequest|nativead|adman|advertisement|adframe|adcontrol|adoverlay|adserver|adsense|google-ads|ad-banner|banner-ad|adplacement|adblockdetect|advertising|admanagement|adprovider|adrotation|adunit|adcall|adlog|adcount|adserve|adsrv|adsys|adtrack|adview|adwidget|adzone|sidebar-ads|footer-ads|top-ads|bottom-ads|ads\\.php|ad\\.js|ad\\.css))/i.test(l))return 1;");
                sb.Append("if(/\\/api\\/stats\\/(ads|atr)/i.test(l))return 1;");
                sb.Append("var hh=new URL(l).hostname;");
                sb.Append("if(/^(?:.*[-_.])?(ads?|adv(ert(s|ising)?)?|banners?|track(er|ing|s)?|beacons?|doubleclick|adservice|adnxs|adtech|googleads|gads|adwords|partner|sponsor(ed)?|click(s|bank|tale|through)?|pop(up|under)s?|promo(tion)?|market(ing|er)?|affiliates?|metrics?|stat(s|counter|istics)?|analytics?|pixels?|campaign|traff(ic|iq)|monetize|syndicat(e|ion)|revenue|yield|impress(ion)?s?|conver(sion|t)?|audience|target(ing)?|behavior|profil(e|ing)|telemetry|survey|outbrain|taboola|quantcast|scorecard|omniture|comscore|krux|bluekai|exelate|adform|adroll|rubicon|vungle|inmobi|flurry|mixpanel|heap|amplitude|optimizely|bizible|pardot|hubspot|marketo|eloqua|media(math|net)|criteo|appnexus|turn|adbrite|admob|adsonar|adscale|zergnet|revcontent|mgid|nativeads|contentad|displayads|bannerflow|adblade|adcolony|chartbeat|newrelic|pingdom|kissmetrics|tradedesk|bidder|auction|rtb|programmatic|interstitial|overlay|trafficjunky|trafficstars|exoclick|juicyads|realsrv|magsrv)\\./i.test(hh))return 1;");
                sb.Append("if(/^(?:adcreative(s)?|imageserv|media(mgr)?|stats|switch|track(2|er)?|view|ads?\\d{0,3}|banners?\\d{0,3}|clicks?\\d{0,3}|count(er)?\\d{0,3}|servedby\\d{0,3}|toolbar\\d{0,3}|pageads\\d{0,3}|pops\\d{0,3}|promos?\\d{0,3})\\./i.test(hh))return 1;");
                sb.Append("if(/(?:\\/(1|blank|b|clear|pixel|transp|spacer)\\.gif|\\.swf)$/i.test(l))return 1;");
                sb.Append("return 0}catch(e){return 0}};");
                sb.Append("var F=fetch;window.fetch=function(a,o){var u=typeof a==='string'?a:a&&a.url?a.url:'';if(chk(u))return Promise.reject(new TypeError('blocked'));return F.apply(this,arguments)};");
                sb.Append("var X=XMLHttpRequest.prototype.open;XMLHttpRequest.prototype.open=function(){var u=arguments[1]||'';if(typeof u==='string'&&chk(u)){this.__blk=1;return}return X.apply(this,arguments)};");
                sb.Append("var S=XMLHttpRequest.prototype.send;XMLHttpRequest.prototype.send=function(){if(this.__blk)return;return S.apply(this,arguments)};");
                sb.Append("})()");

                // Use DevTools Protocol to inject into main world - bypasses CSP
                string escapedJs = sb.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"");
                string cdpParams = "{\"expression\":\"" + escapedJs + "\",\"allowUnsafeEvalBlockedByCSP\":true}";
                await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", cdpParams);
            }
            catch { }
        }

        private async void InjectAdElementHider(BrowserTab tab)
        {
            try
            {
                var core = tab.WebView.CoreWebView2;
                if (core == null) return;
                if (IsChallengePage(core)) return;
                var url = core.Source ?? "";
                string pageHost = "";
                try { pageHost = new Uri(url).Host.ToLower(); } catch { }

                // YouTube gets its own dedicated ad blocking - DevTools main-world injection
                // handles JSON stripping, and YouTubeAdBlockerJs handles DOM scrubbing
                bool isYouTube = pageHost == "www.youtube.com" || pageHost == "youtube.com" ||
                    pageHost == "m.youtube.com" || pageHost == "music.youtube.com" ||
                    pageHost.EndsWith(".youtube.com") || pageHost.EndsWith(".youtube-nocookie.com");

                if (isYouTube)
                {
                    await core.ExecuteScriptAsync(YouTubeAdBlockerJs);
                    return;
                }

                // Skip generic element hiding on whitelisted sites (non-YouTube)
                if (IsAdBlockWhitelisted(pageHost)) return;

                await core.ExecuteScriptAsync(AdElementHiderJs);
            }
            catch { }
        }

        //  Password Manager 
    }
}
