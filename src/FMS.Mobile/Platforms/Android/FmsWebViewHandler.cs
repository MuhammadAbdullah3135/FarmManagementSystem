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