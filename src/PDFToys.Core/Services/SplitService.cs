using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PDFToys.Core.Services;

public sealed class SplitService : ServiceBase, ISplitService
{
    /// <summary>
    /// Splits the specified input PDF based on the provided configuration options.
    /// </summary>
    /// <param name="input">The input PDF file to split.</param>
    /// <param name="options">The configuration defining how to split the document (e.g., chunk size, custom ranges).</param>
    /// <returns>An OperationResult indicating success or failure, containing the output directory path if successful.</returns>
    public OperationResult Split(PdfFile input, SplitOptions options)
    {
        var outputs = new List<OperationOutput>();
        var result = ExecuteSafe(() =>
        {
            if (options is null) return new(false, "", "Options are required.");
            if (!Enum.IsDefined(options.Preset)) return new(false, "", "Unsupported split preset.");
            var validation = ValidateStandardInputs(input, options.OutputDirectory);
            if (validation is not null) return validation;
            OperationContext.ThrowIfCancellationRequested();
            using var document = PdfReader.Open(input.FilePath, PdfDocumentOpenMode.Import);
            var compatibility = PdfDocumentSafety.CheckPageCopy(document);
            if (compatibility is not null) return compatibility;
            IReadOnlyList<SplitRange> ranges;
            var namedRanges = options.CustomRanges is { Count: > 0 } || options.Preset != SplitPreset.None;
            if (options.CustomRanges is { Count: > 0 }) ranges = options.CustomRanges.ToArray();
            else if (options.Preset != SplitPreset.None) ranges = BuildPresetRanges(document.PageCount, options.Preset);
            else
            {
                if (options.SplitEveryPages <= 0) return new(false, "", "SplitEveryPages must be greater than 0.");
                ranges = Enumerable.Range(0, (int)(((long)document.PageCount + options.SplitEveryPages - 1) / options.SplitEveryPages))
                    .Select(index => new SplitRange(index * options.SplitEveryPages + 1, Math.Min(document.PageCount, (index + 1) * options.SplitEveryPages))).ToArray();
            }
            var rangeValidation = ValidateRanges(ranges, document.PageCount);
            if (!rangeValidation.IsSuccess) return rangeValidation;
            var sourceName = Path.GetFileNameWithoutExtension(input.FilePath);
            for (var part = 0; part < ranges.Count; part++)
            {
                OperationContext.ThrowIfCancellationRequested();
                var range = ranges[part];
                using var output = new PdfDocument();
                PdfDocumentSafety.CopyDocumentProperties(document, output);
                for (var page = range.StartPage; page <= range.EndPage; page++)
                {
                    OperationContext.ThrowIfCancellationRequested();
                    OperationContext.Report(new("Splitting pages", input.FilePath, part, ranges.Count, page, document.PageCount));
                    output.AddPage(document.Pages[page - 1]);
                }
                var name = namedRanges ? $"{sourceName}_Part{part + 1}_p{range.StartPage}-{range.EndPage}.pdf" : $"{sourceName}_Part{part + 1}.pdf";
                using var transaction = OutputFileTransaction.Create(Path.Combine(options.OutputDirectory, name), input.FilePath);
                output.Save(transaction.CandidatePath);
                outputs.Add(new(transaction.CommitPdf()));
            }
            return new(true, Path.GetFullPath(options.OutputDirectory), "") { Outputs = outputs.ToArray() };
        });
        return result.IsSuccess || outputs.Count == 0 ? result : result with
        {
            Outcome = result.Outcome == OperationOutcome.Cancelled ? OperationOutcome.Cancelled : OperationOutcome.PartialSuccess,
            OutputPath = Path.GetFullPath(options.OutputDirectory),
            Outputs = outputs.ToArray(),
            Warnings = ["Previously completed parts were kept."]
        };
    }
    private static OperationResult ValidateRanges(IReadOnlyList<SplitRange> ranges, int pageCount)
    {
        var ordered = ranges.OrderBy(range => range.StartPage).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var range = ordered[i];
            if (range.StartPage < 1 || range.EndPage < 1)
            {
                return new OperationResult(false, string.Empty, "Range pages must be greater than or equal to 1.");
            }

            if (range.EndPage < range.StartPage)
            {
                return new OperationResult(false, string.Empty, "EndPage must be greater than or equal to StartPage.");
            }

            if (range.EndPage > pageCount)
            {
                return new OperationResult(false, string.Empty, $"Range exceeds page count ({pageCount}).");
            }

            if (i == 0)
            {
                continue;
            }

            var previous = ordered[i - 1];
            if (range.StartPage <= previous.EndPage)
            {
                return new OperationResult(false, string.Empty, "Custom ranges must not overlap.");
            }
        }

        return new OperationResult(true, string.Empty, string.Empty);
    }

    private static IReadOnlyList<SplitRange> BuildPresetRanges(int pageCount, SplitPreset preset)
    {
        return preset switch
        {
            SplitPreset.Half => BuildEvenRanges(pageCount, 2),
            SplitPreset.Quarter => BuildEvenRanges(pageCount, 4),
            _ => []
        };
    }

    private static IReadOnlyList<SplitRange> BuildEvenRanges(int pageCount, int parts)
    {
        var ranges = new List<SplitRange>(parts);
        var baseSize = pageCount / parts;
        var remainder = pageCount % parts;
        var currentStart = 1;

        for (var part = 0; part < parts; part++)
        {
            var thisPartSize = baseSize + (part < remainder ? 1 : 0);
            if (thisPartSize <= 0)
            {
                continue;
            }

            var end = currentStart + thisPartSize - 1;
            ranges.Add(new SplitRange(currentStart, end));
            currentStart = end + 1;
        }

        return ranges;
    }

}

