using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace FMS.Mobile;

public partial class FmsWebViewHandler : WebViewHandler
{
    protected override void ConnectHandler(Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);

        platformView.Settings.DomStorageEnabled = true;
        platformView.Settings.JavaScriptEnabled = true;

        // Revalidate the app shell against the server instead of trusting the WebView cache.
        // GitHub Pages serves index.html with Cache-Control: max-age=600 and Android's WebView
        // caches harder than that on top, so a relaunch could keep loading an old bundle after a
        // deploy. NoCache = use the cache but revalidate, and the hashed assets answer 304.
        platformView.Settings.CacheMode = CacheModes.NoCache;

        if (CookieManager.Instance is { } cookieManager)
        {
            cookieManager.SetAcceptThirdPartyCookies(platformView, true);
        }

        platformView!.SetWebViewClient(new FmsWebViewClient(this, (errorCode, description, failingUrl) =>
        {
            if (VirtualView is FmsWebView fms)
                fms.RaiseReceivedError(errorCode, description, failingUrl);
        }));
    }
}