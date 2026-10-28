using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using PDFToys.App.Services;
using System;

namespace PDFToys.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // PDFsharp's Core build has no default font resolver. Initialize this
        // before both headless conversion and desktop startup can create XFont.
        if (OperatingSystem.IsWindows() && PdfSharp.Fonts.GlobalFontSettings.FontResolver is null)
            PdfSharp.Fonts.GlobalFontSettings.FontResolver = new WindowsPdfFontResolver();

        if (PDFToys.Core.OS.Windows.OfficeWorkerHost.TryRun(args, out var officeExitCode))
        {
            Environment.ExitCode = officeExitCode;
            return;
        }

        if (TryRunHeadlessConversion(args, out var exitCode))
        {
            Environment.ExitCode = exitCode;
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static bool TryRunHeadlessConversion(string[] args, out int exitCode)
    {
        var request = new ArgumentParser().Parse(args);
        var route = new StartupOperationRouter().BuildRoute(request);
        if (route.Kind != StartupRouteKind.HeadlessConversion)
        {
            exitCode = 0;
            return false;
        }

        using var services = new ServiceCollection()
            .AddPdfToysCore()
            .AddSingleton<HeadlessConversionExecutor>()
            .BuildServiceProvider();

        var executor = services.GetRequiredService<HeadlessConversionExecutor>();
        var result = executor.Execute(route);
        exitCode = result.ExitCode;
        return true;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure(() => new App())
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
