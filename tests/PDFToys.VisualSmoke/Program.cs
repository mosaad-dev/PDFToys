using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PDFToys.App.ViewModels;
using PDFToys.App.Views;
using PDFToys.App.Models;
using PDFToys.App.Services;

AppBuilder.Configure<PDFToys.App.App>().UseSkia().WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
using var services = new ServiceCollection().AddPdfToysCore()
    .AddSingleton<StartupOperationRouter>()
    .AddSingleton<IUserSettingsStore>(new PreviewSettings())
    .AddSingleton<IFileDialogService, AvaloniaFileDialogService>()
    .AddSingleton<IPdfPagePreviewService, PdfPagePreviewService>()
    .AddSingleton<IPagePreviewDialogService, AvaloniaPagePreviewDialogService>()
    .BuildServiceProvider();
var pages = new (string Name, Func<Control> Create)[]
{
    ("home", () => new HomeView { DataContext = new HomeViewModel(_ => {}) }),
    ("merge", () => new MergeView { DataContext = Tool(home => home.GoToMergeCommand.Execute(null)) }),
    ("split", () => new PDFToys.App.Views.SplitView { DataContext = Tool(home => home.GoToSplitCommand.Execute(null)) }),
    ("compress", () => new CompressView { DataContext = Tool(home => home.GoToCompressCommand.Execute(null)) }),
    ("organize", () => new OrganizePagesView { DataContext = Tool(home => home.GoToOrganizePagesCommand.Execute(null)) }),
    ("protect", () => new ProtectView { DataContext = Tool(home => home.GoToProtectCommand.Execute(null)) }),
    ("unlock", () => new UnlockView { DataContext = Tool(home => home.GoToUnlockCommand.Execute(null)) }),
    ("convert", () => new ConvertExportView { DataContext = Tool(home => home.GoToConvertExportCommand.Execute(null)) })
};
foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
foreach (var width in new[] { 400, 780 })
foreach (var page in pages)
{
    var view = page.Create();
    var window = new Window { Width = width, Height = 720, Content = view, RequestedThemeVariant = theme };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    var buttons = view.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible).ToArray();
    foreach (var button in buttons)
    {
        var point = button.TranslatePoint(default, window);
        if (point is { } p && (p.X < -1 || p.X + button.Bounds.Width > width + 1))
            throw new Exception($"Horizontal button clipping: {page.Name}/{theme}/{width}: {button.Content} at {p}, {button.Bounds}");
    }
    var firstButton = buttons.First(button => button.IsEffectivelyEnabled && button.Focusable);
    if (!firstButton.Focus()) throw new Exception($"Unable to focus {page.Name}'s first action.");
    window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
    window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
    if (window.FocusManager?.GetFocusedElement() is null) throw new Exception($"Tab navigation lost focus in {page.Name}.");
    using var bitmap = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame");
    bitmap.Save(Path.Combine(output, $"{page.Name}-{theme}-{width}.png"));
    foreach (var density in new[] { 1.5, 2.0 })
    {
        using var scaled = new RenderTargetBitmap(new PixelSize((int)(width * density), (int)(720 * density)), new Vector(96 * density, 96 * density));
        scaled.Render(window);
        scaled.Save(Path.Combine(output, $"{page.Name}-{theme}-{width}-{density.ToString(System.Globalization.CultureInfo.InvariantCulture)}x.png"));
    }
    window.Close();
}
var lifecycleCases = LifecycleSmoke.Run(output);
File.WriteAllText(Path.Combine(output, "layout-smoke-results.json"), JsonSerializer.Serialize(new
{
    logicalCases = pages.Length * 4,
    snapshots = pages.Length * 12,
    lifecycleCases,
    widths = new[] { 400, 780 },
    themes = new[] { "Light", "Dark" },
    renderDensities = new[] { 1.0, 1.5, 2.0 },
    passed = new[] { "Real XAML and Home navigation factories", "No horizontal button clipping", "Actions accept keyboard focus and Tab navigation", "Skia rendering at all declared densities", "UI renders while a controlled step runs off-thread", "Cancel and window close wait for the safe boundary and preserve source bytes" },
    scope = "Headless initial layouts and renderer density. Native OS DPI transitions, dialogs and populated long-job interaction remain separate checks."
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Rendered {pages.Length * 12} snapshots across {pages.Length * 4} page/theme/width cases with clipping/focus checks to {output}");

ViewModelBase Tool(Action<HomeViewModel> choose)
{
    var main = new MainViewModel(services, new(PdfToysOperation.Home, []));
    choose((HomeViewModel)main.CurrentPage);
    return main.CurrentPage;
}

sealed class PreviewSettings : IUserSettingsStore
{
    public UserSettings Load() => new();
    public void Save(UserSettings settings) { }
}
