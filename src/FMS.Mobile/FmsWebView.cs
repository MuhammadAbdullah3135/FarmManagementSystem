using Microsoft.Maui.Controls;

namespace FMS.Mobile;

public partial class FmsWebView : WebView
{
    public static FmsWebView? Current { get; private set; }

    public FmsWebView()
    {
        Current = this;
    }

    public event EventHandler<WebErrorEventArgs>? ReceivedError;

    internal void RaiseReceivedError(int errorCode, string description, string failingUrl)
    {
        ReceivedError?.Invoke(this, new WebErrorEventArgs(errorCode, description, failingUrl));
    }
}

public class WebErrorEventArgs : EventArgs
{
    public int ErrorCode { get; }
    public string Description { get; }
    public string FailingUrl { get; }

    public WebErrorEventArgs(int errorCode, string description, string failingUrl)
    {
        ErrorCode = errorCode;
        Description = description;
        FailingUrl = failingUrl;
    }
}