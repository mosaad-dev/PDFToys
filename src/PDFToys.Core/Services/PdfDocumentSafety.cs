using PdfSharp.Pdf;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services;

/// <summary>Rejects transforms whose current engine cannot preserve the requested document semantics.</summary>
public static class PdfDocumentSafety
{
    public static OperationResult? CheckRewrite(PdfDocument document)
    {
        var catalog = document.Internals.Catalog;
        var signed = catalog.Elements.ContainsKey("/Perms") || document.Internals.GetAllObjects().OfType<PdfDictionary>()
            .Any(item => item.Elements.GetName("/Type") == "/Sig" || item.Elements.GetName("/FT") == "/Sig" || item.Elements.ContainsKey("/ByteRange"));
        return signed ? Unsupported("digital signatures") : null;
    }

    public static OperationResult? CheckPageCopy(PdfDocument document)
    {
        var signed = CheckRewrite(document);
        if (signed is not null) return signed;
        if (document.SecuritySettings.IsEncrypted) return Unsupported("encryption; unlock this PDF first");
        var catalog = document.Internals.Catalog;
        var features = new (string Key, string Name)[]
        {
            ("/AcroForm", "interactive forms"), ("/Outlines", "bookmarks"),
            ("/Names", "named destinations or attachments"), ("/AF", "associated files"),
            ("/StructTreeRoot", "accessibility tags"), ("/OCProperties", "optional content layers")
        };
        var present = features.Where(feature => catalog.Elements.ContainsKey(feature.Key)).Select(feature => feature.Name).ToArray();
        return present.Length == 0 ? null : Unsupported(string.Join(", ", present));
    }

    private static OperationResult Unsupported(string features) => new(false, "",
        $"This PDF contains {features}. PDFToys cannot safely preserve these features in this operation. The original has been preserved.")
        { ErrorCode = "UnsupportedDocumentFeatures" };
}
