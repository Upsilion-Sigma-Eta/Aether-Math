using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AetherMath.Persistence;

namespace AetherMath.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var workspaceState = TryCreateWorkspaceStateService();
            var viewModel = workspaceState is null
                ? new MainWindowViewModel(new WorkspaceFileService())
                : new MainWindowViewModel(new WorkspaceFileService(), workspaceState);
            desktop.MainWindow = new MainWindow(viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IWorkspaceStateService? TryCreateWorkspaceStateService()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData) || !Path.IsPathFullyQualified(localApplicationData))
            return null;

        try
        {
            var rootDirectory = Path.GetFullPath(Path.Combine(localApplicationData, "AetherMath"));
            return Path.IsPathFullyQualified(rootDirectory)
                ? new WorkspaceStateService(rootDirectory)
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }
}
