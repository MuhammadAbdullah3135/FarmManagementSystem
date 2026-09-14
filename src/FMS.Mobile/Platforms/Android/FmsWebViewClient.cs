using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace FMS.Mobile;

internal class FmsWebViewClient : MauiWebViewClient
{
    private readonly Action<int, string, string>? _onMainFrameError;

    public FmsWebViewClient(Microsoft.Maui.Handlers.WebViewHandler handler, Action<int, string, string>? onMainFrameError)
        : base(handler)
    {
        _onMainFrameError = onMainFrameError;
    }

    public override void OnReceivedError(Android.Webkit.WebView? view, IWebResourceRequest? request, WebResourceError? error)
    {
        base.OnReceivedError(view, request, error);
        if (request?.IsForMainFrame == true && error != null)
        {
            _onMainFrameError?.Invoke(
                (int)error.ErrorCode,
                error.Description?.ToString() ?? string.Empty,
                request.Url?.ToString() ?? string.Empty);
        }
    }
}