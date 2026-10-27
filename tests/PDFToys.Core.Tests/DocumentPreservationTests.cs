using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFToys.Core.Models;
using PDFToys.Core.Services;

namespace PDFToys.Core.Tests;

public sealed class DocumentPreservationTests : PdfTestBase
{
    [Theory]
    [InlineData("/AcroForm", "forms")]
    [InlineData("/Outlines", "bookmarks")]
    [InlineData("/Names", "attachments")]
    [InlineData("/AF", "associated files")]
    [InlineData("/StructTreeRoot", "tags")]
    [InlineData("/OCProperties", "layers")]
    [InlineData("/PageLabels", "labels")]
    public void PageCopy_GuardsUnsupportedCatalogFeatures(string key, string feature)
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.Internals.Catalog.Elements[key] = new PdfDictionary(document);
        var result = PdfDocumentSafety.CheckPageCopy(document);
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal("UnsupportedDocumentFeatures", result.ErrorCode);
        Assert.Contains(feature, result.ErrorMessage);
    }

    [Fact]
    public void Rewrite_GuardsSignatureNestedInDirectArray()
    {
        using var document = new PdfDocument();
        document.AddPage();
        var form = new PdfDictionary(document);
        var fields = new PdfArray(document);
        var field = new PdfDictionary(document);
        field.Elements.SetName("/FT", "/Sig");
        fields.Elements.Add(field);
        form.Elements["/Fields"] = fields;
        document.Internals.Catalog.Elements["/AcroForm"] = form;
        Assert.NotNull(PdfDocumentSafety.CheckRewrite(document));
    }

    [Fact]
    public void Split_BookmarkedDocument_FailsWithoutCreatingOutput()
    {
        var input = CreateBookmarkedPdf();
        var bytes = File.ReadAllBytes(input);
        var result = new SplitService().Split(new(input), new(TempDirectory, 1));
        Assert.False(result.IsSuccess);
        Assert.Contains("bookmarks", result.ErrorMessage);
        Assert.Empty(result.Outputs);
        Assert.Equal(bytes, File.ReadAllBytes(input));
        Assert.Single(Directory.GetFiles(TempDirectory, "*.pdf"));
    }

    [Fact]
    public void Split_EncryptedWithEmptyUserPassword_DoesNotSilentlyRemoveEncryption()
    {
        var path = Path.Combine(TempDirectory, "encrypted-owner.pdf");
        using (var document = new PdfDocument())
        {
            document.AddPage(); document.SecuritySettings.OwnerPassword = "owner-secret";
            document.Save(path);
        }
        var bytes = File.ReadAllBytes(path);
        var result = new SplitService().Split(new(path), new(TempDirectory, 1));
        Assert.False(result.IsSuccess);
        Assert.Contains("encryption", result.ErrorMessage);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(result.Outputs);
    }

    [Fact]
    public void ProtectThenUnlock_PreservesBookmarksAndMetadata()
    {
        var input = CreateBookmarkedPdf();
        var service = new ProtectService();
        var protectedResult = service.Protect(new(input), new("secret", TempDirectory));
        Assert.True(protectedResult.IsSuccess, protectedResult.ErrorMessage);
        var result = service.Unlock(new(protectedResult.OutputPath), new("secret", TempDirectory));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var unlocked = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Modify);
        Assert.False(unlocked.SecuritySettings.IsEncrypted);
        Assert.Equal("Preservation fixture", unlocked.Info.Title);
        Assert.Single(unlocked.Outlines);
        Assert.Equal("First chapter", unlocked.Outlines[0].Title);
    }

    [Fact]
    public void Protect_ReplaceOriginal_CreatesRecoverableBackupAndReportsActualPath()
    {
        var input = CreatePdf("replace.pdf", 1);
        var bytes = File.ReadAllBytes(input);
        var result = new ProtectService().Protect(new(input), new("secret", TempDirectory, true));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(input, result.OutputPath);
        var backup = Assert.Single(Directory.GetFiles(TempDirectory, "*.pdftoys.bak"));
        Assert.Equal(bytes, File.ReadAllBytes(backup));
        Assert.Contains(result.Warnings, message => message.Contains(backup));
        Assert.ThrowsAny<Exception>(() => PdfReader.Open(input, PdfDocumentOpenMode.Import));
        using var protectedDocument = PdfReader.Open(input, "secret", PdfDocumentOpenMode.Import);
        Assert.Equal(1, protectedDocument.PageCount);
    }

    [Fact]
    public void Protect_CancelAfterGeneratedCopy_KeepsManifestAndOriginal()
    {
        var input = CreatePdf("cancel-replace.pdf", 1);
        var original = File.ReadAllBytes(input);
        using var cancellation = new CancellationTokenSource();
        using var scope = OperationContext.Begin(cancellation.Token,
            new ImmediateProgress(value => { if (value.Stage == "Replacing original") cancellation.Cancel(); }));
        var result = new ProtectService().Protect(new(input), new("secret", TempDirectory, true));
        Assert.Equal(OperationOutcome.Cancelled, result.Outcome);
        var generated = Assert.Single(result.Outputs);
        Assert.True(File.Exists(generated.Path));
        Assert.Equal(original, File.ReadAllBytes(input));
        Assert.Empty(Directory.GetFiles(TempDirectory, "*.pdftoys.bak"));
    }

    [Fact]
    public void SplitAndRearrange_PreserveStandardDocumentPropertiesAndLanguage()
    {
        var input = Path.Combine(TempDirectory, "metadata.pdf");
        using (var document = new PdfDocument())
        {
            document.AddPage(); document.AddPage();
            document.Info.Title = "Retained title"; document.Info.Author = "Fixture author";
            document.Info.Subject = "Retained subject"; document.Info.Keywords = "fixture, regression";
            document.Language = "de-DE";
            document.Save(input);
        }
        var split = new SplitService().Split(new(input), new(TempDirectory, 1));
        var rearranged = new RearrangeService().Rearrange(new(input), [new(1), new(0)], new(TempDirectory));
        Assert.True(split.IsSuccess, split.ErrorMessage); Assert.True(rearranged.IsSuccess, rearranged.ErrorMessage);
        foreach (var output in split.Outputs.Concat(rearranged.Outputs))
        {
            using var document = PdfReader.Open(output.Path, PdfDocumentOpenMode.Import);
            Assert.Equal("Retained title", document.Info.Title);
            Assert.Equal("Fixture author", document.Info.Author);
            Assert.Equal("Retained subject", document.Info.Subject);
            Assert.Equal("fixture, regression", document.Info.Keywords);
            Assert.Equal("de-DE", document.Language);
        }
    }

    [Fact]
    public void Rearrange_RejectsCustomXmpWithoutChangingOriginal()
    {
        var input = Path.Combine(TempDirectory, "xmp.pdf");
        const string packet = "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:pdftoys='urn:pdftoys:test'><pdftoys:fixture>Retained custom XMP</pdftoys:fixture></rdf:Description></rdf:RDF></x:xmpmeta>";
        WriteRawPdfWithXmp(input, packet);
        var original = File.ReadAllBytes(input);
        var result = new RearrangeService().Rearrange(new(input), [new(0)], new(TempDirectory));
        Assert.False(result.IsSuccess);
        Assert.Contains("XMP", result.ErrorMessage);
        Assert.Equal(original, File.ReadAllBytes(input));
        Assert.Empty(result.Outputs);
    }

    private static void WriteRawPdfWithXmp(string path, string packet)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
            $"<< /Type /Metadata /Subtype /XML /Length {packet.Length} >>\nstream\n{packet}\nendstream"
        };
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var startXref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{startXref}\n%%EOF\n");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes(pdf.ToString()));
    }

    private sealed class ImmediateProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }

    private string CreateBookmarkedPdf()
    {
        var path = Path.Combine(TempDirectory, "bookmarked.pdf");
        using var pdf = new PdfDocument();
        var page = pdf.AddPage();
        pdf.Outlines.Add("First chapter", page);
        pdf.Info.Title = "Preservation fixture";
        pdf.Save(path);
        return path;
    }
}

