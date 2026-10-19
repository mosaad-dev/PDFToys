using System;
using SkiaSharp;

namespace PDFToys.App.Services;

public static class PreviewRotation
{
    public static byte[] Apply(byte[] png, int delta)
    {
        delta = ((delta % 360) + 360) % 360;
        if (delta == 0) return png;
        if (delta % 90 != 0) throw new ArgumentOutOfRangeException(nameof(delta));
        using var source = SKBitmap.Decode(png) ?? throw new InvalidOperationException("Could not decode page preview.");
        var swap = delta is 90 or 270;
        using var surface = SKSurface.Create(new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (delta == 90) canvas.Translate(source.Height, 0);
        else if (delta == 180) canvas.Translate(source.Width, source.Height);
        else canvas.Translate(0, source.Width);
        canvas.RotateDegrees(delta);
        canvas.DrawBitmap(source, 0, 0);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
