using Android.Webkit;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace FMS.Mobile;

/// <summary>
/// Grants the WebView the camera, and nothing else.
///
/// <para>
/// The scanner screen asks the page for a camera through `getUserMedia`. Android answers that
/// question in <c>WebChromeClient.OnPermissionRequest</c>, and MAUI's own chrome client does not
/// implement it — so without this the page's camera request is silently refused and the scan
/// screen can only ever show "the camera is not available", on a device whose camera works
/// perfectly. This is the whole of the native work the QR feature needs: the offline logic,
/// the resolver and the codes themselves are the web client's, unchanged.
/// </para>
///
/// <para>
/// It insists on the Android runtime permission first. A WebView grant without it looks
/// successful to the page and then fails to produce frames, which is the worst of both worlds;
/// asking here means the user sees the real system dialog, and a refusal reaches the page as a
/// refusal — the message the scan screen already knows how to show.
/// </para>
///
/// <para>
/// Subclassing <see cref="MauiWebChromeClient"/> rather than deriving from
/// <c>WebChromeClient</c> directly keeps everything MAUI does in this callback (file choosers,
/// the console bridge, video full screen) intact. Only the camera is overridden.
/// </para>
/// </summary>
public class FmsWebChromeClient : MauiWebChromeClient
{
    public FmsWebChromeClient(IWebViewHandler handler)
        : base(handler)
    {
    }

    public override void OnPermissionRequest(PermissionRequest? request)
    {
        if (request is null)
            return;

        if (!RequestsCamera(request))
        {
            // A page that asks for something this app has no feature for does not get it. There
            // is no code path here that grants a microphone, a location or a protected media
            // stream, which is the point of denying rather than granting what was asked.
            request.Deny();
            return;
        }

        // Fire-and-forget: the override is void, and the WebView holds the request valid until
        // it is granted or denied. The result is reported to the page through the request
        // itself, so there is nothing to await at the call site.
        _ = GrantCameraAsync(request);
    }

    private static bool RequestsCamera(PermissionRequest request)
    {
        var resources = request.GetResources();
        return resources is not null
            && Array.IndexOf(resources, PermissionRequest.ResourceVideoCapture!) >= 0;
    }

    private static async Task GrantCameraAsync(PermissionRequest request)
    {
        try
        {
            // `Permissions` maps onto the Android runtime permission this app declares in its
            // manifest, and asks for it when it has not been granted yet. Called from the
            // WebView callback and therefore already on the UI thread.
            var status = await Permissions.RequestAsync<Permissions.Camera>();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (status == PermissionStatus.Granted)
                {
                    // The camera alone, even though the request was for the camera alone:
                    // `Grant` takes the list of resources being allowed.
                    request.Grant(new[] { PermissionRequest.ResourceVideoCapture! });
                }
                else
                {
                    request.Deny();
                }
            });
        }
        catch (Exception ex)
        {
            // A failure here must still answer the page, or the scanner waits on a callback that
            // never comes and the user sees a spinner where the refusal message belongs.
            System.Diagnostics.Debug.WriteLine($"[FMS] Camera permission request failed: {ex.Message}");

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    request.Deny();
                }
                catch (Exception denyEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[FMS] Camera permission denial failed: {denyEx.Message}");
                }
            });
        }
    }
}
