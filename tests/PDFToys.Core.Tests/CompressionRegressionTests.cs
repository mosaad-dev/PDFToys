using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;
using PDFToys.Core.Services.ImageProcessing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace PDFToys.Core.Tests;

public sealed class CompressionRegressionTests : PdfTestBase
{
    private readonly CompressionService _service = new(new SkiaImageCompressor());

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 10)]
    [InlineData(false, 11)]
    [InlineData(false, 12)]
    [InlineData(false, 13)]
    [InlineData(false, 14)]
    [InlineData(false, 15)]
    [InlineData(true, 15)]
    public void Compress_FlateAndPredictors_ReducesRealImagesAndPreservesAppearance(bool gray, int predictor)
    {
        var source = CompressionFixtures.Create(TempDirectory, "flate.pdf", gray: gray, predictor: predictor);
        var result = Compress(source);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.Compression!.ImagesProcessed);
        Assert.Equal(1, result.Compression.ImagesChanged);
        Assert.Equal(0, result.Compression.ImagesSkipped);
        Assert.True(result.Compression.PercentSaved > 20, $"Expected actual image compression: {result.Compression}");
        Assert.Equal(new FileInfo(result.OutputPath).Length, result.Compression.FinalBytes);
        using var output = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(90, output.Pages[0].Rotate);
        Assert.Equal("/DCTDecode", Image(output).Elements.GetName("/Filter"));
        Assert.False(Image(output).Elements.ContainsKey("/DecodeParms"));
        using var text = UglyToad.PdfPig.PdfDocument.Open(result.OutputPath);
        Assert.Contains("Retained text", text.GetPage(1).Text);
        AssertRenderDifference(source, result.OutputPath, maximumMeanError: 12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compress_Jpeg_ReducesActualJpegAndPreservesAppearance(bool gray)
    {
        var source = CompressionFixtures.Create(TempDirectory, "jpeg.pdf", jpeg: true, gray: gray);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, result.Compression!.ImagesChanged);
        Assert.True(result.Compression.PercentSaved > 20);
        AssertRenderDifference(source, result.OutputPath, maximumMeanError: 12);
    }

    [Fact]
    public void Compress_OrdinaryPngImportedByPdfSharp_RecompressesImage()
    {
        var source = CreatePdfWithEmbeddedImage("ordinary-png.pdf", 400, 400);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, result.Compression!.ImagesProcessed);
        // This synthetic line graphic can already be smaller with Flate; the decoder
        // must support it even when JPEG would be an unhelpful replacement.
        Assert.DoesNotContain(result.Compression.SkipReasons, reason => reason.Contains("decoded safely"));
        Assert.DoesNotContain(result.Compression.SkipReasons, reason => reason.Contains("filter chain"));
        Assert.True(result.Compression.FinalBytes <= result.Compression.OriginalBytes);
        AssertRenderDifference(source, result.OutputPath, maximumMeanError: 15);
    }

    [Theory]
    [InlineData("/SMask 7 0 R", "/SMask")]
    [InlineData("/Mask [0 20 0 20 0 20]", "/Mask")]
    [InlineData("/Decode [1 0 1 0 1 0]", "/Decode")]
    public void Compress_MasksAndDecodeArrays_PreservesExactSourceAndReportsReason(string entries, string reason)
    {
        // JPEG + soft mask reproduces the former transparency-loss defect.
        var source = CompressionFixtures.Create(TempDirectory, "masked.pdf", jpeg: true, extraImageEntries: entries);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Unchanged, result.Outcome);
        Assert.Equal(1, result.Compression!.ImagesSkipped);
        Assert.Equal(0, result.Compression.ImagesChanged);
        Assert.Contains(result.Compression.SkipReasons, message => message.Contains(reason));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result.OutputPath));
        AssertRenderDifference(source, result.OutputPath, maximumMeanError: 0);
        using var output = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        Assert.True(Image(output).Elements.ContainsKey(reason));
    }

    [Fact]
    public void Compress_MixedMaskedAndSupportedImages_PreservesMaskWhileReducingSupportedImage()
    {
        var source = CompressionFixtures.Create(TempDirectory, "mixed.pdf", jpeg: true, extraImageEntries: "/SMask 7 0 R", mixed: true);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.Compression!.ImagesProcessed);
        Assert.Equal(1, result.Compression.ImagesChanged);
        Assert.Equal(1, result.Compression.ImagesSkipped);
        using var input = PdfReader.Open(source, PdfDocumentOpenMode.Import);
        using var output = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(Image(input).Stream!.Value, Image(output).Stream!.Value);
        Assert.Equal(Image(input).Elements.GetDictionary("/SMask")!.Stream!.Value,
            Image(output).Elements.GetDictionary("/SMask")!.Stream!.Value);
        AssertRenderDifference(source, result.OutputPath, maximumMeanError: 2);
    }

    [Fact]
    public void Compress_MaskAlsoListedAsXObject_IsNeverReencodedAsRgb()
    {
        var source = CompressionFixtures.Create(TempDirectory, "mask-alias.pdf", jpeg: true,
            extraImageEntries: "/SMask 7 0 R", mixed: true, maskAlias: true);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(3, result.Compression!.ImagesProcessed);
        Assert.Equal(1, result.Compression.ImagesChanged);
        Assert.Equal(2, result.Compression.ImagesSkipped);
        using var input = PdfReader.Open(source, PdfDocumentOpenMode.Import);
        using var output = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        var mask = Image(output).Elements.GetDictionary("/SMask")!;
        Assert.Equal("/DeviceGray", mask.Elements.GetName("/ColorSpace"));
        Assert.Equal(Image(input).Elements.GetDictionary("/SMask")!.Stream!.Value, mask.Stream!.Value);
        AssertRenderDifference(source, result.OutputPath, maximumMeanError: 2);
    }

    [Theory]
    [InlineData("/DeviceCMYK", 8)]
    [InlineData("[/ICCBased 7 0 R]", 8)]
    [InlineData("[/Indexed /DeviceRGB 1 <000000ffffff>]", 8)]
    [InlineData("/DeviceGray", 1)]
    public void Compress_SensitiveColorAndBitDepth_PreservesBytesAndReportsSkip(string color, int bits)
    {
        var source = CompressionFixtures.Create(TempDirectory, "sensitive.pdf", colorOverride: color, bits: bits);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Unchanged, result.Outcome);
        Assert.Equal(1, result.Compression!.ImagesSkipped);
        Assert.NotEmpty(result.Compression.SkipReasons);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result.OutputPath));
    }

    [Theory]
    [InlineData("/JPXDecode")]
    [InlineData("/CCITTFaxDecode")]
    [InlineData("[/ASCII85Decode /FlateDecode]")]
    public void Compress_UnsupportedFilters_PreservesStreams(string filter)
    {
        var source = CompressionFixtures.Create(TempDirectory, "unsupported.pdf", filterOverride: filter);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, result.Compression!.ImagesSkipped);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result.OutputPath));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Compress_NestedInheritedAndCyclicResources_FindsEachImageOnce(bool nested, bool inherited, bool cyclic)
    {
        var compressor = new CountingCompressor();
        var service = new CompressionService(compressor);
        var source = CompressionFixtures.Create(TempDirectory, "resources.pdf", nested: nested, inherited: inherited, shared: true, cyclic: cyclic);
        var result = service.Compress(new(source), new(TempDirectory, CompressionLevel.Normal));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, compressor.Calls);
        Assert.Equal(1, result.Compression!.ImagesProcessed);
        Assert.Equal(1, result.Compression.ImagesChanged);
        using var output = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(2, output.PageCount);
        AssertRenderDifference(source, result.OutputPath, maximumMeanError: 12);
    }

    [Fact]
    public void Compress_InlineImages_ArePreservedAndReported()
    {
        var source = CompressionFixtures.Create(TempDirectory, "inline.pdf", inline: true);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains(result.Warnings, warning => warning.Contains("inline images", StringComparison.OrdinalIgnoreCase));
        using var document = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        var content = document.Pages[0].Contents.Elements.GetDictionary(0)!;
        Assert.Contains("BI", System.Text.Encoding.Latin1.GetString(content.Stream!.UnfilteredValue));
        AssertRenderDifference(source, result.OutputPath, maximumMeanError: 12);
    }

    [Fact]
    public void Compress_MalformedSamples_PreservesSourceInsteadOfFailingOrInventingSuccess()
    {
        var source = CompressionFixtures.Create(TempDirectory, "corrupt.pdf", corrupt: true);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Unchanged, result.Outcome);
        Assert.Equal(1, result.Compression!.ImagesSkipped);
        Assert.Contains(result.Compression.SkipReasons, reason => reason.Contains("decoded safely"));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result.OutputPath));
    }

    [Fact]
    public void Compress_ExtremeDimensions_AreSkippedBeforeAllocating()
    {
        var source = CompressionFixtures.Create(TempDirectory, "dimension-source.pdf");
        var prepared = Path.Combine(TempDirectory, "dimensions.pdf");
        using (var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify))
        {
            Image(document).Elements.SetInteger("/Width", int.MaxValue);
            Image(document).Elements.SetInteger("/Height", int.MaxValue);
            document.Save(prepared);
        }
        var result = Compress(prepared);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Unchanged, result.Outcome);
        Assert.Contains(result.Compression!.SkipReasons, reason => reason.Contains("safe decoder limit"));
        Assert.Equal(File.ReadAllBytes(prepared), File.ReadAllBytes(result.OutputPath));
    }

    [Fact]
    public void Compress_DefaultColorSpaceSubstitution_IsPreservedAndReported()
    {
        var source = CompressionFixtures.Create(TempDirectory, "color-source.pdf", gray: true);
        var prepared = Path.Combine(TempDirectory, "color-default.pdf");
        using (var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify))
        {
            var spaces = new PdfDictionary(document);
            spaces.Elements.SetName("/DefaultGray", "/DeviceGray");
            document.Pages[0].Elements.GetDictionary("/Resources")!.Elements["/ColorSpace"] = spaces;
            document.Save(prepared);
        }
        var result = Compress(prepared);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Unchanged, result.Outcome);
        Assert.Contains(result.Compression!.SkipReasons, reason => reason.Contains("substitutions"));
        Assert.Equal(File.ReadAllBytes(prepared), File.ReadAllBytes(result.OutputPath));
    }

    [Fact]
    public void Compress_TextOnly_ReturnsHonestUnchangedCopy()
    {
        var source = CreatePdf("text-only.pdf", 2);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Unchanged, result.Outcome);
        Assert.Equal(0, result.Compression!.ImagesProcessed);
        Assert.Equal(0, result.Compression.BytesSaved);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result.OutputPath));
        Assert.Equal(result.OutputPath, Assert.Single(result.Outputs).Path);
    }

    [Fact]
    public void Compress_AlreadySmallImage_PreservesOriginalAndReportsNoImprovement()
    {
        var source = CompressionFixtures.Create(TempDirectory, "tiny.pdf", tiny: true);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Unchanged, result.Outcome);
        Assert.Equal(0, result.Compression!.ImagesChanged);
        Assert.Equal(1, result.Compression.ImagesSkipped);
        Assert.Contains(result.Compression.SkipReasons, reason => reason.Contains("already smaller"));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result.OutputPath));
    }

    [Fact]
    public void Compress_EncoderFailure_ReportsSafeSkipWithoutClaimingAlreadyOptimized()
    {
        var source = CompressionFixtures.Create(TempDirectory, "encoder-failure.pdf");
        var service = new CompressionService(new FailingCompressor());
        var result = service.Compress(new(source), new(TempDirectory, CompressionLevel.Normal));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Unchanged, result.Outcome);
        Assert.Equal(0, result.Compression!.ImagesChanged);
        Assert.Equal(1, result.Compression.ImagesSkipped);
        Assert.Equal(0, result.Compression.BytesSaved);
        Assert.Contains(result.Compression.SkipReasons, reason => reason.Contains("encoded safely"));
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("already smaller"));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result.OutputPath));
        Assert.Equal(result.OutputPath, Assert.Single(result.Outputs).Path);
    }

    [Fact]
    public void Compress_WholeDocumentCandidateLarger_RestoresExactOriginalAndReportsActualCounts()
    {
        var source = CompressionFixtures.Create(TempDirectory, "small-document.pdf", squareSize: 24, minimal: true);
        var result = Compress(source);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(OperationOutcome.Unchanged, result.Outcome);
        Assert.True(result.Compression!.CandidateBytes >= result.Compression.OriginalBytes);
        Assert.Equal(result.Compression.OriginalBytes, result.Compression.FinalBytes);
        Assert.Equal(0, result.Compression.ImagesChanged);
        Assert.Contains(result.Warnings, warning => warning.Contains("not smaller"));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result.OutputPath));
    }

    [Fact]
    public void Compress_SignatureFields_FailsBeforeWritingOutput()
    {
        var source = CompressionFixtures.Create(TempDirectory, "signature.pdf", signature: true);
        var bytes = File.ReadAllBytes(source);
        var result = Compress(source);
        Assert.False(result.IsSuccess);
        Assert.Equal("UnsupportedDocumentFeatures", result.ErrorCode);
        Assert.Empty(result.Outputs);
        Assert.Equal(bytes, File.ReadAllBytes(source));
        Assert.False(File.Exists(Path.Combine(TempDirectory, "signature_Compressed.pdf")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("reader-secret")]
    public void Compress_OwnerOnlyAndUserPasswordEncryption_DoesNotSilentlyRemoveProtection(string userPassword)
    {
        var source = CompressionFixtures.Create(TempDirectory, "unencrypted.pdf");
        var encrypted = Path.Combine(TempDirectory, "encrypted.pdf");
        using (var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify))
        {
            document.SecuritySettings.OwnerPassword = "owner-secret";
            if (userPassword.Length > 0) document.SecuritySettings.UserPassword = userPassword;
            document.Save(encrypted);
        }
        using (var inspection = PdfReader.Open(encrypted, userPassword, PdfDocumentOpenMode.Import))
        {
            // Owner-only encryption is readable without a password, and PDFsharp's
            // IsEncrypted flag can be false after import. Prove this fixture has a
            // real retained Standard encryption dictionary before testing refusal.
            Assert.Contains(inspection.Internals.GetAllObjects().OfType<PdfDictionary>(), item =>
                item.Elements["/Filter"] is PdfName { Value: "/Standard" } &&
                item.Elements.ContainsKey("/O") && item.Elements.ContainsKey("/U"));
            Assert.True(PdfDocumentSafety.IsEncryptedInput(inspection));
        }
        var bytes = File.ReadAllBytes(encrypted);
        var result = Compress(encrypted);
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationOutcome.Failed, result.Outcome);
        Assert.NotEmpty(result.ErrorMessage);
        Assert.Empty(result.Outputs);
        Assert.Equal(bytes, File.ReadAllBytes(encrypted));
        Assert.False(File.Exists(Path.Combine(TempDirectory, "encrypted_Compressed.pdf")));
        Assert.Empty(Directory.GetFiles(TempDirectory, ".pdftoys-*"));
    }

    [Fact]
    public void Compress_BookmarksAttachmentsAndFormValues_RetainsDocumentStructures()
    {
        var source = CompressionFixtures.Create(TempDirectory, "features.pdf");
        var prepared = Path.Combine(TempDirectory, "prepared.pdf");
        using (var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify))
        {
            document.Outlines.Add("Retained bookmark", document.Pages[0]);
            document.Info.Title = "Retained title";
            document.Internals.Catalog.Elements.SetString("/Lang", "en-US");
            document.AddEmbeddedFile("note.txt", new MemoryStream("Retained attachment"u8.ToArray()));
            var field = new PdfDictionary(document);
            field.Elements.SetName("/FT", "/Tx");
            field.Elements.SetString("/T", "RetainedField");
            field.Elements.SetString("/V", "Retained value");
            document.Internals.AddObject(field);
            var fields = new PdfArray(document);
            fields.Elements.Add(field.Reference!);
            var form = new PdfDictionary(document);
            form.Elements["/Fields"] = fields;
            document.Internals.Catalog.Elements["/AcroForm"] = form;
            document.Save(prepared);
        }
        var result = Compress(prepared);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, result.Compression!.ImagesChanged);
        using var output = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        Assert.Equal("Retained title", output.Info.Title);
        Assert.Equal("Retained bookmark", output.Outlines[0].Title);
        Assert.Equal("en-US", output.Internals.Catalog.Elements.GetString("/Lang"));
        var fieldsOutput = output.Internals.Catalog.Elements.GetDictionary("/AcroForm")!.Elements.GetArray("/Fields")!;
        Assert.Equal("Retained value", fieldsOutput.Elements.GetDictionary(0)!.Elements.GetString("/V"));
        var names = output.Internals.Catalog.Elements.GetDictionary("/Names")!.Elements.GetDictionary("/EmbeddedFiles")!.Elements.GetArray("/Names")!;
        Assert.Equal("note.txt", names.Elements.GetString(0));
        var attachment = names.Elements.GetDictionary(1)!.Elements.GetDictionary("/EF")!.Elements.GetDictionary("/F")!;
        Assert.Equal("Retained attachment", System.Text.Encoding.UTF8.GetString(attachment.Stream!.UnfilteredValue));
    }

    [Fact]
    public void Compress_CancelBetweenImages_DoesNotCommitOrChangeOriginal()
    {
        var source = CompressionFixtures.Create(TempDirectory, "cancel.pdf");
        var bytes = File.ReadAllBytes(source);
        using var cancellation = new CancellationTokenSource();
        var service = new CompressionService(new CancellingCompressor(cancellation));
        using var scope = OperationContext.Begin(cancellation.Token);
        var result = service.Compress(new(source), new(TempDirectory, CompressionLevel.Normal));
        Assert.Equal(OperationOutcome.Cancelled, result.Outcome);
        Assert.Empty(result.Outputs);
        Assert.Equal(bytes, File.ReadAllBytes(source));
        Assert.Empty(Directory.GetFiles(TempDirectory, ".pdftoys-*"));
        Assert.False(File.Exists(Path.Combine(TempDirectory, "cancel_Compressed.pdf")));
    }

    [Fact]
    public void Compress_RepeatedRuns_KeepExistingOutputsAndSource()
    {
        var source = CompressionFixtures.Create(TempDirectory, "collision.pdf");
        var bytes = File.ReadAllBytes(source);
        var first = Compress(source);
        var firstBytes = File.ReadAllBytes(first.OutputPath);
        var second = Compress(source);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.NotEqual(first.OutputPath, second.OutputPath);
        Assert.Equal(bytes, File.ReadAllBytes(source));
        Assert.Equal(firstBytes, File.ReadAllBytes(first.OutputPath));
    }

    private OperationResult Compress(string source) => _service.Compress(new(source), new(TempDirectory, CompressionLevel.Normal));

    private static PdfDictionary Image(PdfDocument document) =>
        (PdfDictionary)((PdfReference)document.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!.Elements["/Im0"]!).Value;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416", Justification = "The renderer helper checks the supported desktop platforms before invoking PDFtoImage.")]
    private static void AssertRenderDifference(string before, string after, double maximumMeanError)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("PDF rendering comparison requires a supported desktop platform.");
        using var original = PDFtoImage.Conversion.ToImage(File.ReadAllBytes(before));
        using var compressed = PDFtoImage.Conversion.ToImage(File.ReadAllBytes(after));
        Assert.Equal(original.Width, compressed.Width);
        Assert.Equal(original.Height, compressed.Height);
        long difference = 0;
        for (var y = 0; y < original.Height; y++)
        for (var x = 0; x < original.Width; x++)
        {
            var a = original.GetPixel(x, y);
            var b = compressed.GetPixel(x, y);
            difference += Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
        }
        var mean = difference / (original.Width * (double)original.Height * 3);
        Assert.True(mean <= maximumMeanError, $"Rendered RGB mean error {mean:F3} exceeded {maximumMeanError:F3}.");
    }

    private sealed class CountingCompressor : IImageCompressor
    {
        public int Calls { get; private set; }
        public CompressedImageResult? Compress(byte[] imageBytes, int quality)
        {
            Calls++;
            return new SkiaImageCompressor().Compress(imageBytes, quality);
        }
    }

    private sealed class FailingCompressor : IImageCompressor
    {
        public CompressedImageResult? Compress(byte[] imageBytes, int quality) => null;
    }

    private sealed class CancellingCompressor(CancellationTokenSource cancellation) : IImageCompressor
    {
        public CompressedImageResult? Compress(byte[] imageBytes, int quality)
        {
            cancellation.Cancel();
            return new SkiaImageCompressor().Compress(imageBytes, quality);
        }
    }
}
