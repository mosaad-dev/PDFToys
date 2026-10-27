using System.Globalization;
using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace PDFToys.Core.Tests;

/// <summary>Small deterministic, redistributable PDFs; no external photo or scratch executable.</summary>
internal static class CompressionFixtures
{
    public const int Width = 256;
    public const int Height = 192;

    public static string Create(string directory, string name, bool gray = false, int predictor = 1,
        bool jpeg = false, string extraImageEntries = "", bool nested = false, bool inherited = false,
        bool shared = false, bool cyclic = false, string? filterOverride = null, string? colorOverride = null,
        int bits = 8, bool inline = false, bool corrupt = false, bool tiny = false, bool mixed = false,
        bool signature = false, int? squareSize = null, bool minimal = false, bool maskAlias = false)
    {
        var width = squareSize ?? (tiny ? 1 : Width);
        var height = squareSize ?? (tiny ? 1 : Height);
        var components = gray ? 1 : 3;
        var samples = MakeSamples(width, height, components);
        var encoded = jpeg ? EncodeJpeg(samples, width, height, components) : Deflate(Predict(samples, width, height, components, predictor));
        if (corrupt) encoded = [1, 2, 3, 4];
        var filter = filterOverride ?? (jpeg ? "/DCTDecode" : "/FlateDecode");
        var color = colorOverride ?? (gray ? "/DeviceGray" : "/DeviceRGB");
        var decode = predictor == 1 || jpeg ? "" : $"/DecodeParms << /Predictor {predictor} /Colors {components} /BitsPerComponent 8 /Columns {width} >>";
        var resources = nested ? "<< /XObject << /Fm0 8 0 R >> /Font << /F1 6 0 R >> >>" :
            $"<< /XObject << /Im0 5 0 R /Again 5 0 R {(mixed ? "/Second 10 0 R" : "")} {(maskAlias ? "/SoftMask 7 0 R" : "")} >> /Font << /F1 6 0 R >> >>";
        var content = "q 256 0 0 192 16 36 cm " + (nested ? "/Fm0" : "/Im0") + " Do Q BT /F1 12 Tf 16 16 Td (Retained text) Tj ET";
        if (inline) content += " q 8 0 0 8 0 0 cm BI /W 1 /H 1 /BPC 8 /CS /RGB ID \u0000\u0000\u0000 EI Q";
        if (mixed) content += " q 64 0 0 48 16 180 cm /Second Do Q";
        var objects = new List<byte[]>
        {
            Ascii($"<< /Type /Catalog /Pages 2 0 R {(signature ? "/AcroForm << /Fields [10 0 R] >>" : "")} >>"),
            Ascii($"<< /Type /Pages /Kids [3 0 R{(shared ? " 9 0 R" : "")}] /Count {(shared ? 2 : 1)} {(inherited ? "/Resources " + resources : "")} >>"),
            Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 288 244] /Rotate 90 /Contents 4 0 R {(inherited ? "" : "/Resources " + resources)} >>"),
            Stream("", Ascii(content)),
            Stream($"/Type /XObject /Subtype /Image /Width {width} /Height {height} /BitsPerComponent {bits} /ColorSpace {color} /Filter {filter} {decode} {extraImageEntries}", encoded),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
            Stream($"/Type /XObject /Subtype /Image /Width {width} /Height {height} /BitsPerComponent 8 /ColorSpace /DeviceGray /Filter /FlateDecode", Deflate(Enumerable.Repeat((byte)120, width * height).ToArray())),
            Stream($"/Type /XObject /Subtype /Form /BBox [0 0 1 1] /Resources << /XObject << /Im0 5 0 R {(cyclic ? "/Self 8 0 R" : "")} >> >>", Ascii("/Im0 Do")),
            Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 288 244] /Contents 4 0 R /Resources {resources} >>")
        };
        if (mixed)
            objects.Add(Stream($"/Type /XObject /Subtype /Image /Width {width} /Height {height} /BitsPerComponent 8 /ColorSpace {color} /Filter {filter} {decode}", encoded));
        else if (signature)
            objects.Add(Ascii("<< /Type /Annot /Subtype /Widget /FT /Sig /T (Signature1) /Rect [0 0 0 0] >>"));
        if (minimal) objects.RemoveRange(6, objects.Count - 6);
        var path = Path.Combine(directory, name);
        WritePdf(path, objects);
        return path;
    }

    private static byte[] MakeSamples(int width, int height, int components)
    {
        var samples = new byte[width * height * components];
        uint seed = 0x12345678;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            seed = unchecked(seed * 1664525 + 1013904223);
            var noise = (int)(seed >> 27) - 16;
            for (var c = 0; c < components; c++)
                samples[(y * width + x) * components + c] = (byte)Math.Clamp(40 + (x * 120 / width) + (y * 40 / height) + c * 15 + noise, 0, 255);
        }
        return samples;
    }

    private static byte[] EncodeJpeg(byte[] samples, int width, int height, int components)
    {
        using var bitmap = new SKBitmap(width, height, components == 1 ? SKColorType.Gray8 : SKColorType.Rgba8888, SKAlphaType.Opaque);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * components;
            bitmap.SetPixel(x, y, new SKColor(samples[offset], samples[offset + (components == 1 ? 0 : 1)], samples[offset + (components == 1 ? 0 : 2)]));
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    private static byte[] Predict(byte[] samples, int width, int height, int components, int predictor)
    {
        if (predictor == 1) return samples;
        var stride = width * components;
        var rowSize = stride + (predictor >= 10 ? 1 : 0);
        var result = new byte[rowSize * height];
        for (var y = 0; y < height; y++)
        {
            var filter = predictor == 2 ? 1 : predictor == 15 ? y % 5 : predictor - 10;
            var rowStart = y * rowSize;
            if (predictor >= 10) result[rowStart++] = (byte)filter;
            for (var x = 0; x < stride; x++)
            {
                var index = y * stride + x;
                var left = x >= components ? samples[index - components] : 0;
                var up = y > 0 && predictor != 2 ? samples[index - stride] : 0;
                var upperLeft = y > 0 && x >= components && predictor != 2 ? samples[index - stride - components] : 0;
                var estimate = left + up - upperLeft;
                var a = Math.Abs(estimate - left);
                var b = Math.Abs(estimate - up);
                var c = Math.Abs(estimate - upperLeft);
                var prediction = filter switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, _ => a <= b && a <= c ? left : b <= c ? up : upperLeft };
                result[rowStart + x] = unchecked((byte)(samples[index] - prediction));
            }
        }
        return result;
    }

    private static byte[] Deflate(byte[] bytes)
    {
        using var memory = new MemoryStream();
        using (var deflate = new ZLibStream(memory, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(bytes);
        return memory.ToArray();
    }

    private static byte[] Stream(string dictionary, byte[] bytes) =>
        [..Ascii($"<< {dictionary} /Length {bytes.Length} >>\nstream\n"), ..bytes, ..Ascii("\nendstream")];

    private static byte[] Ascii(string text) => Encoding.Latin1.GetBytes(text);

    private static void WritePdf(string path, List<byte[]> objects)
    {
        using var output = File.Create(path);
        output.Write(Ascii("%PDF-1.7\n%âãÏÓ\n"));
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            output.Write(Ascii($"{i + 1} 0 obj\n"));
            output.Write(objects[i]);
            output.Write(Ascii("\nendobj\n"));
        }
        var xref = output.Position;
        output.Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1)) output.Write(Ascii(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
        output.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
    }
}
