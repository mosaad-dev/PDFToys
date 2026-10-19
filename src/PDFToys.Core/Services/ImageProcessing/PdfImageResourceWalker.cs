using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services.ImageProcessing;

/// <summary>Visits each image object once, including inherited resources and nested forms.</summary>
internal sealed class PdfImageResourceWalker
{
    private readonly HashSet<PdfDictionary> _images = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PdfDictionary> _resources = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PdfDictionary> _content = new(ReferenceEqualityComparer.Instance);
    private readonly Action<string> _warning;

    public PdfImageResourceWalker(Action<string> warning) => _warning = warning;

    public bool HasDefaultColorSpaces { get; private set; }

    public IEnumerable<PdfDictionary> ImagesOnPage(PdfPage page)
    {
        InspectContents(PdfImageDecoder.Resolve(page.Elements["/Contents"]));
        var resources = FindInheritedResources(page);
        if (resources is null) yield break;
        var pending = new Stack<PdfDictionary>();
        pending.Push(resources);
        while (pending.TryPop(out var current))
        {
            OperationContext.ThrowIfCancellationRequested();
            if (!_resources.Add(current)) continue;
            if (PdfImageDecoder.Resolve(current.Elements["/ColorSpace"]) is PdfDictionary colors &&
                (colors.Elements.ContainsKey("/DefaultRGB") || colors.Elements.ContainsKey("/DefaultGray")))
                HasDefaultColorSpaces = true;
            if (PdfImageDecoder.Resolve(current.Elements["/XObject"]) is not PdfDictionary objects) continue;
            foreach (var item in objects.Elements.Values)
            {
                OperationContext.ThrowIfCancellationRequested();
                if (PdfImageDecoder.Resolve(item) is not PdfDictionary xObject) continue;
                switch (xObject.Elements.GetName("/Subtype"))
                {
                    case "/Image":
                        if (_images.Add(xObject)) yield return xObject;
                        break;
                    case "/Form":
                        InspectContents(xObject);
                        // A missing form resource dictionary uses the calling resource scope.
                        if (PdfImageDecoder.Resolve(xObject.Elements["/Resources"]) is PdfDictionary nested)
                            pending.Push(nested);
                        break;
                }
            }
        }
    }

    private static PdfDictionary? FindInheritedResources(PdfDictionary page)
    {
        var parents = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        PdfDictionary? current = page;
        while (current is not null && parents.Add(current))
        {
            if (PdfImageDecoder.Resolve(current.Elements["/Resources"]) is PdfDictionary resources)
                return resources;
            current = PdfImageDecoder.Resolve(current.Elements["/Parent"]) as PdfDictionary;
        }
        return null;
    }

    private void InspectContents(PdfItem? item)
    {
        if (item is PdfArray array)
        {
            foreach (var child in array.Elements) InspectContents(PdfImageDecoder.Resolve(child));
            return;
        }
        if (item is not PdfDictionary dictionary || !_content.Add(dictionary) || dictionary.Stream is null) return;
        try
        {
            // Parsing operators avoids mistaking text strings containing "BI" for inline images.
            var content = ContentReader.ReadContent(dictionary.Stream.UnfilteredValue);
            if (content.OfType<COperator>().Any(op => op.OpCode.Name == "BI"))
                _warning("Inline images are preserved and are not included in image-object counts.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            _warning("A content stream could not be inspected for inline images; its content is preserved.");
        }
    }
}
