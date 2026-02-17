using System.IO;
using System.Windows;
using System.Windows.Threading;
using tmuxlike.Services;

namespace tmuxlike;

/// <summary>
/// Application entry point. Validates the current directory is a git repository
/// and initializes the main window.
/// </summary>
public partial class App : Application
{
    /// <summary>The absolute path to the root of the detected git repository.</summary>
    public static string RepoRoot { get; private set; } = string.Empty;

    /// <summary>The repository folder name, used as the window title suffix.</summary>
    public static string RepoName { get; private set; } = string.Empty;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            var startPath = Directory.GetCurrentDirectory();

            if (!GitService.IsGitRepository(startPath))
            {
                MessageBox.Show(
                    $"Not a git repository:\n{startPath}\n\nPlease run tmuxlike from inside a git repository.",
                    "tmuxlike - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            RepoRoot = GitService.GetRepoRoot(startPath);
            RepoName = Path.GetFileName(RepoRoot);

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Startup failed:\n\n{ex}",
                "tmuxlike - Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Unhandled exception:\n\n{e.Exception}",
            "tmuxlike - Fatal Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Fatal error:\n\n{e.ExceptionObject}",
            "tmuxlike - Fatal Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
