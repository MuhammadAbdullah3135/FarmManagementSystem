using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

// Alias conflicting types
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpSize = SixLabors.ImageSharp.Size;
using ImageSharpResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
using ImageSharpResizeOptions = SixLabors.ImageSharp.Processing.ResizeOptions;

namespace FMS.Mobile.Services;

public class ImageCaptureService : IImageCaptureService
{
    private const long TargetMaxBytes = 500 * 1024;
    private const int MaxDimension = 1920;
    private const int InitialQuality = 85;
    private const int MinQuality = 20;
    private const int QualityStep = 10;

    public async Task<string?> CaptureAndCompressAsync()
    {
        var choice = await Shell.Current.DisplayActionSheet(
            "Add Photo", "Cancel", null, "Take Photo", "Choose from Gallery");

        FileResult? fileResult = choice switch
        {
            "Take Photo" => await MediaPicker.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = "Capture animal photo"
            }),
            "Choose from Gallery" => await MediaPicker.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Select animal photo"
            }),
            _ => null
        };

        if (fileResult is null) return null;
        return await CompressAndSaveAsync(fileResult);
    }

    private static async Task<string?> CompressAndSaveAsync(FileResult fileResult)
    {
        try
        {
            using var inputStream = await fileResult.OpenReadAsync();

            if (inputStream.Length <= TargetMaxBytes)
            {
                var directPath = GetOutputPath(fileResult.FileName);
                inputStream.Position = 0;
                using (var fs = File.Create(directPath))
                    await inputStream.CopyToAsync(fs);
                return directPath;
            }

            using var image = await ImageSharpImage.LoadAsync(inputStream);

            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                image.Mutate(x => x.Resize(new ImageSharpResizeOptions
                {
                    Mode = ImageSharpResizeMode.Max,
                    Size = new ImageSharpSize(MaxDimension, MaxDimension)
                }));
            }

            var quality = InitialQuality;
            var outputPath = GetOutputPath(fileResult.FileName);

            while (quality >= MinQuality)
            {
                var encoder = new JpegEncoder { Quality = quality };
                using var ms = new MemoryStream();
                await image.SaveAsJpegAsync(ms, encoder);

                if (ms.Length <= TargetMaxBytes)
                {
                    ms.Position = 0;
                    using var fs = File.Create(outputPath);
                    await ms.CopyToAsync(fs);
                    return outputPath;
                }

                quality -= QualityStep;
            }

            var fallbackEncoder = new JpegEncoder { Quality = MinQuality };
            using var finalMs = new MemoryStream();
            await image.SaveAsJpegAsync(finalMs, fallbackEncoder);
            finalMs.Position = 0;
            using var finalFs = File.Create(outputPath);
            await finalMs.CopyToAsync(finalFs);
            return outputPath;
        }
        catch
        {
            return null;
        }
    }

    private static string GetOutputPath(string originalFileName)
    {
        var imagesDir = Path.Combine(FileSystem.AppDataDirectory, "animal_images");
        Directory.CreateDirectory(imagesDir);
        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        return Path.Combine(imagesDir, $"{Guid.NewGuid():N}{ext}");
    }
}
