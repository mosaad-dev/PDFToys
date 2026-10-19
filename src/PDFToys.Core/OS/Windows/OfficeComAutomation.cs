using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Runtime.Versioning;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.Core.OS.Windows;

[SupportedOSPlatform("windows")]
internal sealed class OfficeComAutomation : IConversionStrategy
{
    private const int WdExportFormatPdf = 17;
    private const int XlFixedFormatTypePdf = 0;
    private const int PpSaveAsPdf = 32;
    private const int MsoTriStateFalse = 0;
    private const int PpAlertsNone = 1;

    public bool CanHandle(string inputExtension)
    {
        var ext = inputExtension.ToLowerInvariant();
        return ext is ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx";
    }

    public OperationResult Execute(string inputFilePath, ConversionOptions options, string outputFilePath)
    {
        var ext = Path.GetExtension(inputFilePath).ToLowerInvariant();

        switch (ext)
        {
            case ".doc":
            case ".docx":
                ConvertWord(inputFilePath, outputFilePath);
                break;
            case ".xls":
            case ".xlsx":
                ConvertExcel(inputFilePath, outputFilePath);
                break;
            case ".ppt":
            case ".pptx":
                ConvertPowerPoint(inputFilePath, outputFilePath);
                break;
            default:
                return new OperationResult(false, string.Empty, $"Unsupported Office format: {ext}");
        }

        return new OperationResult(true, Path.GetFullPath(outputFilePath), string.Empty);
    }

    private void ConvertWord(string inputPath, string outputPath)
    {
        object? app = null;
        object? document = null;
        object? documents = null;
        object? settings = null;
        object? previousUpdateLinks = null;

        try
        {
            app = CreateComInstance("Word.Application", "Microsoft Word");
            SetProperty(app, "Visible", false);
            SetProperty(app, "DisplayAlerts", MsoTriStateFalse);
            SetProperty(app, "AutomationSecurity", 3); // msoAutomationSecurityForceDisable
            settings = GetProperty(app, "Options");
            previousUpdateLinks = GetProperty(settings, "UpdateLinksAtOpen");
            SetProperty(settings, "UpdateLinksAtOpen", false);

            documents = GetProperty(app, "Documents");
            document = InvokeMethod(documents, "Open", inputPath, false, true, false);

            InvokeMethod(document, "ExportAsFixedFormat", outputPath, WdExportFormatPdf);
        }
        finally
        {
            if (document is not null)
            {
                try { InvokeMethod(document, "Close", false); } catch { }
                ReleaseComObject(document);
            }
            if (documents is not null) ReleaseComObject(documents);
            if (settings is not null)
            {
                if (previousUpdateLinks is not null) TrySetProperty(settings, "UpdateLinksAtOpen", previousUpdateLinks);
                ReleaseComObject(settings);
            }
            if (app is not null)
            {
                try { InvokeMethod(app, "Quit"); } catch { }
                ReleaseComObject(app);
            }
        }
    }

    private void ConvertExcel(string inputPath, string outputPath)
    {
        object? app = null;
        object? workbook = null;
        object? workbooks = null;

        try
        {
            app = CreateComInstance("Excel.Application", "Microsoft Excel");
            SetProperty(app, "Visible", false);
            SetProperty(app, "DisplayAlerts", false);
            SetProperty(app, "AutomationSecurity", 3);
            SetProperty(app, "AskToUpdateLinks", false);
            SetProperty(app, "EnableEvents", false);

            workbooks = GetProperty(app, "Workbooks");
            workbook = InvokeMethod(workbooks, "Open", inputPath, 0, true);

            InvokeMethod(workbook, "ExportAsFixedFormat", XlFixedFormatTypePdf, outputPath);
        }
        finally
        {
            if (workbook is not null)
            {
                try { InvokeMethod(workbook, "Close", false); } catch { }
                ReleaseComObject(workbook);
            }
            if (workbooks is not null) ReleaseComObject(workbooks);
            if (app is not null)
            {
                try { InvokeMethod(app, "Quit"); } catch { }
                ReleaseComObject(app);
            }
        }
    }

    private void ConvertPowerPoint(string inputPath, string outputPath)
    {
        object? app = null;
        object? presentation = null;
        object? presentations = null;
        var outputFullPath = Path.GetFullPath(outputPath);

        try
        {
            app = CreateComInstance("PowerPoint.Application", "Microsoft PowerPoint");
            TrySetProperty(app, "Visible", MsoTriStateFalse);
            SetProperty(app, "DisplayAlerts", PpAlertsNone);
            SetProperty(app, "AutomationSecurity", 3);

            presentations = GetProperty(app, "Presentations");
            presentation = InvokeMethod(presentations, "Open", Path.GetFullPath(inputPath), -1, MsoTriStateFalse, MsoTriStateFalse);

            InvokeMethod(presentation, "SaveAs", outputFullPath, PpSaveAsPdf);

            if (!File.Exists(outputFullPath))
            {
                throw new InvalidOperationException($"PowerPoint did not create the PDF at '{outputFullPath}'.");
            }
        }
        finally
        {
            if (presentation is not null)
            {
                try { InvokeMethod(presentation, "Close"); } catch { }
                ReleaseComObject(presentation);
            }
            if (presentations is not null) ReleaseComObject(presentations);
            if (app is not null)
            {
                try { InvokeMethod(app, "Quit"); } catch { }
                ReleaseComObject(app);
            }
        }
    }

    private Exception UnwrapInvocationException(Exception exception)
    {
        if (exception is TargetInvocationException { InnerException: { } inner })
        {
            return inner;
        }
        return exception;
    }

    private object CreateComInstance(string progId, string displayName)
    {
        var processName = progId switch { "Word.Application" => "WINWORD", "Excel.Application" => "EXCEL", _ => "POWERPNT" };
        var existingOffice = Process.GetProcessesByName(processName);
        try
        {
            if (existingOffice.Length > 0)
                throw new InvalidOperationException($"Close {displayName} before converting. PDFToys will not automate an existing Office session.");
        }
        finally { foreach (var process in existingOffice) process.Dispose(); }
        var existingProcesses = Process.GetProcesses().Select(process =>
        {
            using (process) return process.Id;
        }).ToHashSet();
        var type = Type.GetTypeFromProgID(progId, throwOnError: false);
        if (type is null) throw new InvalidOperationException($"{displayName} is not installed or cannot be started.");

        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"{displayName} is not installed or cannot be started.");
        try
        {
            uint processId;
            if (progId != "Excel.Application")
            {
                // Word and hidden PowerPoint do not expose an Application.Hwnd here.
                // Require a single newly created process before opening any document.
                var candidates = Process.GetProcessesByName(processName);
                try { processId = candidates.Length == 1 ? (uint)candidates[0].Id : 0; }
                finally { foreach (var process in candidates) process.Dispose(); }
            }
            else
            {
                var handle = Convert.ToInt64(GetProperty(instance, "Hwnd"));
                GetWindowThreadProcessId(new IntPtr(handle), out processId);
            }
            if (processId == 0 || existingProcesses.Contains((int)processId))
                throw new InvalidOperationException($"{displayName} attached to an existing session. Close {displayName} and retry; PDFToys will not modify an existing Office session.");
            return instance;
        }
        catch
        {
            ReleaseComObject(instance);
            throw;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private object GetProperty(object target, string propertyName)
    {
        try
        {
            return target.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, target, null)!;
        }
        catch (Exception ex) { throw new InvalidOperationException($"Office property '{propertyName}' could not be read: {UnwrapInvocationException(ex).Message}", ex); }
    }

    private void SetProperty(object target, string propertyName, object value)
    {
        try
        {
            target.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, target, [value]);
        }
        catch (Exception ex) { throw new InvalidOperationException($"Office property '{propertyName}' could not be set: {UnwrapInvocationException(ex).Message}", ex); }
    }

    private void TrySetProperty(object target, string propertyName, object value)
    {
        try { SetProperty(target, propertyName, value); }
        catch { /* Some Office installs block hiding the app window */ }
    }

    private object InvokeMethod(object target, string methodName, params object[] args)
    {
        try
        {
            return target.GetType().InvokeMember(methodName, BindingFlags.InvokeMethod, null, target, args)!;
        }
        catch (Exception ex) { throw new InvalidOperationException($"Office step '{methodName}' failed: {UnwrapInvocationException(ex).Message}", ex); }
    }

    private void ReleaseComObject(object comObject)
    {
        if (Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
