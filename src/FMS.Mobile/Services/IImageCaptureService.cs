namespace FMS.Mobile.Services;

public interface IImageCaptureService
{
    /// <summary>
    /// Capture an image from camera or gallery, compress it, and return the saved file path.
    /// Returns null if the user cancelled.
    /// </summary>
    Task<string?> CaptureAndCompressAsync();
}
