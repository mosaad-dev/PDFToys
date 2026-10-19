using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using SkiaSharp;

namespace PDFToys.Core.Services.ImageProcessing;

public sealed class SkiaImageCompressor : IImageCompressor
{
    public CompressedImageResult? Compress(byte[] imageBytes, int quality)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(imageBytes);
            if (bitmap is null)
            {
                return null;
            }

            // The result is written as /DeviceRGB. In particular, decoding a
            // grayscale JPEG can produce Gray8; re-encoding that bitmap directly
            // would make a one-component JPEG inside a three-component PDF image.
            using var rgbBitmap = bitmap.Copy(SKColorType.Rgba8888);
            if (rgbBitmap is null) return null;
            using var image = SKImage.FromBitmap(rgbBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (data is null)
            {
                return null;
            }

            return new CompressedImageResult(data.ToArray(), bitmap.Width, bitmap.Height);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            return null;
        }
    }
}
