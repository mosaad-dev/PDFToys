using System.IO.Compression;
using System.Runtime.InteropServices;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using SkiaSharp;

namespace PDFToys.Core.Services.ImageProcessing;

/// <summary>
/// Interprets PDF image dictionaries before passing a self-contained image to Skia.
/// Unsupported colour/mask semantics must never be discarded to force decoding.
/// </summary>
internal static class PdfImageDecoder
{
    // Bound allocations and decompression rather than trusting dimensions in an input file.
    private const long MaximumBitmapBytes = 128L * 1024 * 1024;

    public static bool TryDecode(PdfDictionary image, out byte[] encoded, out string reason)
    {
        encoded = [];
        reason = string.Empty;
        try
        {
            foreach (var key in new[] { "/SMask", "/Mask", "/SMaskInData", "/Matte", "/Alternates", "/OPI", "/F", "/Decode" })
            {
                if (Resolve(image.Elements[key]) is { } value && value is not PdfNull)
                {
                    reason = $"Images with {key} are preserved to retain their original appearance.";
                    return false;
                }
            }

            if (image.Elements.GetBoolean("/ImageMask"))
            {
                reason = "Stencil masks are preserved.";
                return false;
            }

            var color = Resolve(image.Elements["/ColorSpace"]) as PdfName;
            var components = color?.Value switch { "/DeviceRGB" => 3, "/DeviceGray" => 1, _ => 0 };
            if (components == 0)
            {
                reason = "Only DeviceRGB and DeviceGray images are recompressed; indexed, ICC, CMYK and other colour spaces are preserved.";
                return false;
            }

            var width = image.Elements.GetInteger("/Width");
            var height = image.Elements.GetInteger("/Height");
            if (width <= 0 || height <= 0 || (long)width * height > MaximumBitmapBytes / 4)
            {
                reason = "Image dimensions exceed the safe decoder limit or are invalid.";
                return false;
            }

            if (image.Elements.GetInteger("/BitsPerComponent") != 8)
            {
                reason = "Only 8-bit images are recompressed; other bit depths are preserved.";
                return false;
            }

            if (image.Stream is null)
            {
                reason = "Image stream is missing.";
                return false;
            }

            var filterItem = Resolve(image.Elements["/Filter"]);
            if (filterItem is PdfArray filters && filters.Elements.Count == 1)
                filterItem = Resolve(filters.Elements[0]);
            var filter = (filterItem as PdfName)?.Value;
            if (filter is not ("/DCTDecode" or "/FlateDecode") && filterItem is not null and not PdfNull)
            {
                reason = "The image filter or filter chain is not supported and is preserved.";
                return false;
            }

            var parametersItem = Resolve(image.Elements["/DecodeParms"]);
            if (parametersItem is PdfArray parameters && parameters.Elements.Count == 1)
                parametersItem = Resolve(parameters.Elements[0]);
            if (parametersItem is not null and not PdfNull and not PdfDictionary)
            {
                reason = "The image decode parameters are unsupported and are preserved.";
                return false;
            }

            if (filter == "/DCTDecode")
            {
                if (parametersItem is PdfDictionary)
                {
                    reason = "JPEG images with explicit decode parameters are preserved.";
                    return false;
                }
                // Also check the decoder's dimensions/colour type: malformed dictionaries
                // and CMYK JPEGs labelled RGB should not be silently reinterpreted.
                using var jpegStream = new SKMemoryStream(image.Stream.Value);
                using var codec = SKCodec.Create(jpegStream);
                if (codec is null || codec.Info.Width != width || codec.Info.Height != height ||
                    !HasSupportedJpegComponents(image.Stream.Value, components))
                {
                    reason = "JPEG dimensions or encoded colour information do not match the PDF image.";
                    return false;
                }
                encoded = image.Stream.Value;
                return true;
            }

            var predictor = 1;
            if (parametersItem is PdfDictionary decode)
            {
                predictor = GetInteger(decode, "/Predictor", 1);
                if (predictor != 1 && (GetInteger(decode, "/Colors", 1) != components ||
                                      GetInteger(decode, "/BitsPerComponent", 8) != 8 ||
                                      GetInteger(decode, "/Columns", 1) != width))
                {
                    reason = "Predictor dimensions or colour components do not match the image.";
                    return false;
                }
            }
            if (predictor is not (1 or 2 or >= 10 and <= 15) || (filter is null && predictor != 1))
            {
                reason = "The image predictor is unsupported and is preserved.";
                return false;
            }

            var stride = checked(width * components);
            var expectedLength = checked((stride + (predictor >= 10 ? 1 : 0)) * height);
            byte[] raw;
            if (filter == "/FlateDecode")
            {
                using var source = new MemoryStream(image.Stream.Value, writable: false);
                using var inflater = new ZLibStream(source, CompressionMode.Decompress);
                raw = new byte[expectedLength];
                inflater.ReadExactly(raw);
                if (inflater.ReadByte() != -1)
                    throw new InvalidDataException("Image has excess sample data.");
            }
            else
            {
                raw = image.Stream.Value;
                if (raw.Length != expectedLength)
                    throw new InvalidDataException("Image sample length does not match its dimensions.");
            }

            var samples = UndoPredictor(raw, stride, height, components, predictor);
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            var rgba = new byte[checked(width * height * 4)];
            for (int pixel = 0, sample = 0; pixel < rgba.Length; pixel += 4, sample += components)
            {
                rgba[pixel] = samples[sample];
                rgba[pixel + 1] = samples[sample + (components == 1 ? 0 : 1)];
                rgba[pixel + 2] = samples[sample + (components == 1 ? 0 : 2)];
                rgba[pixel + 3] = 255;
            }
            Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
            using var skImage = SKImage.FromBitmap(bitmap);
            using var png = skImage.Encode(SKEncodedImageFormat.Png, 100);
            encoded = png.ToArray();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            reason = "The image sample data could not be decoded safely and is preserved.";
            return false;
        }
    }

    internal static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

    private static int GetInteger(PdfDictionary dictionary, string key, int fallback) =>
        dictionary.Elements.ContainsKey(key) ? dictionary.Elements.GetInteger(key) : fallback;

    private static bool HasSupportedJpegComponents(byte[] jpeg, int components)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xff || jpeg[1] != 0xd8) return false;
        var matchedFrame = false;
        for (var offset = 2; offset < jpeg.Length;)
        {
            if (jpeg[offset++] != 0xff) return false;
            while (offset < jpeg.Length && jpeg[offset] == 0xff) offset++;
            if (offset >= jpeg.Length) return false;
            var marker = jpeg[offset++];
            if (marker == 0xda) return matchedFrame; // scan data follows
            if (marker is 0xd8 or 0xd9 or >= 0xd0 and <= 0xd7 or 0x01) continue;
            if (offset + 2 > jpeg.Length) return false;
            var length = (jpeg[offset] << 8) | jpeg[offset + 1];
            if (length < 2 || offset + length > jpeg.Length) return false;
            if (marker == 0xe2 && length >= 14 &&
                jpeg.AsSpan(offset + 2, 12).SequenceEqual("ICC_PROFILE\0"u8)) return false;
            if (marker is >= 0xc0 and <= 0xcf and not (0xc4 or 0xc8 or 0xcc))
            {
                if (length < 8 || jpeg[offset + 2] != 8 || jpeg[offset + 7] != components) return false;
                matchedFrame = true;
            }
            offset += length;
        }
        return false;
    }

    private static byte[] UndoPredictor(byte[] raw, int stride, int height, int components, int predictor)
    {
        if (predictor == 1) return raw;
        var samples = new byte[checked(stride * height)];
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = row * (stride + (predictor >= 10 ? 1 : 0));
            var filter = predictor == 2 ? 1 : raw[sourceOffset++];
            if (filter > 4) throw new InvalidDataException("Invalid PNG row filter.");
            for (var x = 0; x < stride; x++)
            {
                var offset = row * stride + x;
                var left = x >= components ? samples[offset - components] : 0;
                var up = row > 0 && predictor != 2 ? samples[offset - stride] : 0;
                var upperLeft = row > 0 && x >= components && predictor != 2 ? samples[offset - stride - components] : 0;
                var prediction = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    _ => Paeth(left, up, upperLeft)
                };
                samples[offset] = unchecked((byte)(raw[sourceOffset + x] + prediction));
            }
        }
        return samples;
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        var estimate = left + up - upperLeft;
        var a = Math.Abs(estimate - left);
        var b = Math.Abs(estimate - up);
        var c = Math.Abs(estimate - upperLeft);
        return a <= b && a <= c ? left : b <= c ? up : upperLeft;
    }
}
