using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services;

public sealed class ProtectService : ServiceBase, IProtectService
{
    /// <summary>
    /// Encrypts the specified input PDF with a user password and saves it to the output directory.
    /// </summary>
    /// <param name="input">The source PDF file to protect.</param>
    /// <param name="options">The protection options containing the password and output destination.</param>
    /// <returns>An OperationResult indicating success or failure, containing the path to the protected file if successful.</returns>
    public OperationResult Protect(PdfFile input, ProtectOptions options)
    {
        return ExecuteSafe(() =>
        {
            if (options is null) return new(false, "", "Options are required.");
            var validationError = ValidateStandardInputs(input, options.OutputDirectory);
            if (validationError != null)
            {
                return validationError;
            }

            if (string.IsNullOrWhiteSpace(options.Password))
            {
                return new OperationResult(false, string.Empty, "Password is required.");
            }

            OperationContext.ThrowIfCancellationRequested();
            OperationContext.Report(new("Protecting document", input.FilePath));
            using var transaction = OutputFileTransaction.CreatePdf(input.FilePath, options.OutputDirectory, "Protected");

            using var document = PdfReader.Open(input.FilePath, PdfDocumentOpenMode.Modify);
            var compatibility = PdfDocumentSafety.CheckRewrite(document);
            if (compatibility is not null) return compatibility;
            document.SecuritySettings.UserPassword = options.Password;
            document.Save(transaction.CandidatePath);
            var outputPath = transaction.CommitPdf(options.Password);
            return Complete(input.FilePath, outputPath, options.ReplaceOriginal, options.Password);
        });
    }

    public OperationResult Unlock(PdfFile input, UnlockOptions options)
    {
        return ExecuteSafe(() =>
        {
            if (options is null) return new(false, "", "Options are required.");
            var validationError = ValidateStandardInputs(input, options.OutputDirectory);
            if (validationError != null)
            {
                return validationError;
            }

            if (string.IsNullOrWhiteSpace(options.Password))
            {
                return new OperationResult(false, string.Empty, "Password is required.");
            }

            OperationContext.ThrowIfCancellationRequested();
            OperationContext.Report(new("Unlocking document", input.FilePath));
            using var transaction = OutputFileTransaction.CreatePdf(input.FilePath, options.OutputDirectory, "Unlocked");
            
            using var inputDocument = PdfReader.Open(input.FilePath, options.Password, PdfDocumentOpenMode.Modify);
            var compatibility = PdfDocumentSafety.CheckRewrite(inputDocument);
            if (compatibility is not null) return compatibility;
            inputDocument.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
            inputDocument.Save(transaction.CandidatePath);
            var outputPath = transaction.CommitPdf();
            return Complete(input.FilePath, outputPath, options.ReplaceOriginal, null);
        });
    }

    private static OperationResult Complete(string input, string output, bool replaceOriginal, string? password)
    {
        if (!replaceOriginal) return new(true, output, "");
        try
        {
            OperationContext.Report(new("Replacing original", input, 0, 1));
            if (SafeOutputFiles.TryReplaceOriginal(output, input, out var backup, out var error, password))
                return new(true, input, "") { Warnings = [$"Recovery backup: {backup}"] };
            return new(false, output, $"The original was preserved. The generated copy is available at {output}. {error}")
            {
                ErrorCode = "ReplacementFailed", Outcome = OperationOutcome.PartialSuccess, Outputs = [new(output)]
            };
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled("Replacement cancelled. The original and generated copy were preserved.") with
            { OutputPath = output, Outputs = [new(output)], Warnings = ["The original file was not replaced."] };
        }
    }
}
