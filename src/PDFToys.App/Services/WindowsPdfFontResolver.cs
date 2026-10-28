using PdfSharp.Fonts;
using System;
using System.Collections.Generic;
using System.IO;

namespace PDFToys.App.Services;

/// <summary>Resolves the standard text-conversion font from the Windows fonts directory.</summary>
public sealed class WindowsPdfFontResolver : IFontResolver
{
    private static readonly HashSet<string> Faces = new(StringComparer.Ordinal)
    {
        "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (!string.Equals(familyName, "Arial", StringComparison.OrdinalIgnoreCase)) return null;
        return new FontResolverInfo((isBold, isItalic) switch
        {
            (true, true) => "arialbi.ttf",
            (true, false) => "arialbd.ttf",
            (false, true) => "ariali.ttf",
            _ => "arial.ttf"
        });
    }

    public byte[]? GetFont(string faceName)
    {
        if (!OperatingSystem.IsWindows() || !Faces.Contains(faceName)) return null;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), faceName);
        if (!File.Exists(path)) throw new FileNotFoundException("The Windows Arial font required for text-to-PDF conversion is unavailable.", path);
        return File.ReadAllBytes(path);
    }
}
