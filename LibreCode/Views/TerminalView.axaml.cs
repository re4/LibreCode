using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LibreCode.ViewModels;

namespace LibreCode.Views;

/// <summary>
/// Terminal panel using Iciclecreek.Avalonia.Terminal for cross-platform shell support.
/// Launches PowerShell on Windows (with built-in Tab autocomplete) and bash on Unix.
/// Sets StartingDirectory to the open project root.
/// </summary>
public partial class TerminalView : UserControl
{
    private string? _lastProjectRoot;
    private bool _shellConfigured;

    public TerminalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (Vm is null) return;

        Vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ProjectName))
                UpdateTerminalDirectory();
        };

        ConfigureShell();
        UpdateTerminalDirectory();
    }

    /// <summary>
    /// Selects the shell process once. On Windows, prefers pwsh.exe (PowerShell 7+)
    /// for modern tab completion, falling back to powershell.exe (Windows PowerShell 5.1).
    /// Unix shells (bash/sh) already provide tab completion by default.
    /// </summary>
    private void ConfigureShell()
    {
        if (_shellConfigured) return;
        _shellConfigured = true;

        if (!OperatingSystem.IsWindows()) return;

        var shell = ResolvePowerShell();
        Terminal.Process = shell;
        Terminal.Args = ["-NoLogo", "-NoExit", "-Command", "Set-PSReadLineOption -EditMode Windows"];
    }

    private static string ResolvePowerShell()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "pwsh.exe",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(startInfo);
            var output = proc?.StandardOutput.ReadToEnd().Trim() ?? "";
            proc?.WaitForExit(2000);

            if (proc?.ExitCode == 0 && !string.IsNullOrEmpty(output))
                return "pwsh.exe";
        }
        catch { }

        return "powershell.exe";
    }

    private void UpdateTerminalDirectory()
    {
        var projectRoot = Vm?.FileSystem.ProjectRoot;
        if (!string.IsNullOrEmpty(projectRoot) && projectRoot != _lastProjectRoot)
        {
            _lastProjectRoot = projectRoot;
            Terminal.StartingDirectory = projectRoot;
            Terminal.Kill();
            Terminal.LaunchProcess();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null)
            Vm.ShowTerminal = false;
    }
}
