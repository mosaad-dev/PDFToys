using PdfSharp.Pdf;
using PDFToys.Core.Models;
using System.Xml.Linq;
using System.Xml;

namespace PDFToys.Core.Services;

/// <summary>Rejects transforms whose current engine cannot preserve the requested document semantics.</summary>
public static class PdfDocumentSafety
{
    public static OperationResult? CheckRewrite(PdfDocument document)
    {
        var catalog = document.Internals.Catalog;
        if (catalog.Elements.ContainsKey("/Metadata") && catalog.Elements.GetDictionary("/Metadata")?.Stream is null)
            return Unsupported("invalid XMP metadata");
        var signed = catalog.Elements.ContainsKey("/Perms") || ContainsSignature(document);
        if (signed) return Unsupported("digital signatures");
        return CheckXmp(document);
    }

    private static bool ContainsSignature(PdfDocument document)
    {
        var pending = new Stack<PdfItem>(document.Internals.GetAllObjects());
        pending.Push(document.Internals.Catalog);
        var visited = new HashSet<PdfItem>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var item))
        {
            OperationContext.ThrowIfCancellationRequested();
            if (!visited.Add(item)) continue;
            if (item is PdfSharp.Pdf.Advanced.PdfReference reference && reference.Value is not null) pending.Push(reference.Value);
            else if (item is PdfDictionary dictionary)
            {
                if (dictionary.Elements.GetName("/Type") == "/Sig" || dictionary.Elements.GetName("/FT") == "/Sig" || dictionary.Elements.ContainsKey("/ByteRange")) return true;
                foreach (var child in dictionary.Elements.Values) if (child is not null) pending.Push(child);
            }
            else if (item is PdfArray array)
                foreach (var child in array.Elements) if (child is not null) pending.Push(child);
        }
        return false;
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
            ("/StructTreeRoot", "accessibility tags"), ("/OCProperties", "optional content layers"),
            ("/PageLabels", "custom page labels")
        };
        var present = features.Where(feature => catalog.Elements.ContainsKey(feature.Key)).Select(feature => feature.Name).ToArray();
        return present.Length == 0 ? null : Unsupported(string.Join(", ", present));
    }

    public static void CopyDocumentProperties(PdfDocument source, PdfDocument destination)
    {
        foreach (var key in new[] { "/Title", "/Author", "/Subject", "/Keywords", "/Creator", "/Producer", "/CreationDate", "/ModDate" })
            if (source.Info.Elements[key] is { } value) destination.Info.Elements[key] = value.Clone();
        destination.Language = source.Language;
    }

    // PDFsharp 6.2.4 regenerates XMP on Save. Reject properties the engine would discard,
    // rather than accepting a candidate that silently loses custom metadata.
    private static OperationResult? CheckXmp(PdfDocument document)
    {
        var metadata = document.Internals.Catalog.Elements.GetDictionary("/Metadata");
        if (metadata?.Stream is null) return null;
        try
        {
            using var stream = new MemoryStream(metadata.Stream.UnfilteredValue);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var packet = XDocument.Load(reader);
            XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            XNamespace pdf = "http://ns.adobe.com/pdf/1.3/";
            XNamespace xmp = "http://ns.adobe.com/xap/1.0/";
            XNamespace mm = "http://ns.adobe.com/xap/1.0/mm/";
            XNamespace meta = "adobe:ns:meta/";
            var supported = new HashSet<XName>
            {
                meta + "xmpmeta", rdf + "RDF", rdf + "Description", rdf + "Alt", rdf + "Seq", rdf + "li",
                pdf + "Producer", pdf + "Keywords", dc + "title", dc + "creator", dc + "description",
                xmp + "CreatorTool", xmp + "CreateDate", xmp + "ModifyDate", mm + "DocumentID", mm + "InstanceID"
            };
            foreach (var element in packet.Root!.DescendantsAndSelf())
            {
                if (!supported.Contains(element.Name)) return Unsupported("custom or unsupported XMP metadata");
                foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
                {
                    if (attribute.Name == rdf + "about" && attribute.Value.Length == 0) continue;
                    if (attribute.Name == XNamespace.Xml + "lang" && attribute.Value == "x-default") continue;
                    if (attribute.Name == meta + "xmptk") continue;
                    if (!supported.Contains(attribute.Name)) return Unsupported("custom or unsupported XMP metadata");
                }
            }
            var values = new Dictionary<XName, string>
            {
                [dc + "title"] = document.Info.Title, [dc + "creator"] = document.Info.Author,
                [dc + "description"] = document.Info.Subject, [pdf + "Keywords"] = document.Info.Keywords,
                [xmp + "CreatorTool"] = document.Info.Creator
            };
            foreach (var pair in values)
            {
                foreach (var element in packet.Descendants(pair.Key))
                    if (element.Value != pair.Value || element.Descendants(rdf + "li").Count() > 1)
                        return Unsupported("XMP metadata that differs from standard document properties");
                foreach (var attribute in packet.Descendants().Attributes(pair.Key))
                    if (attribute.Value != pair.Value) return Unsupported("XMP metadata that differs from standard document properties");
            }
            return null;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or InvalidDataException)
        {
            return Unsupported("invalid or unsupported XMP metadata");
        }
    }

    private static OperationResult Unsupported(string features) => new(false, "",
        $"This PDF contains {features}. PDFToys cannot safely preserve these features in this operation. The original has been preserved.")
        { ErrorCode = "UnsupportedDocumentFeatures" };
}
