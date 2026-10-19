using PDFToys.Core.Models;

namespace PDFToys.Core.Contracts;

public interface IImageCompressor
{
    /// <summary>
    /// Re-encodes a self-contained encoded image (for example JPEG or PNG) as
    /// an RGB JPEG. PDF raw samples and predictors must be decoded first.
    /// Returns null when decode or encode fails.
    /// </summary>
    CompressedImageResult? Compress(byte[] imageBytes, int quality);
}
