using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LibreCode.Services.FileSystem;
using LibreCode.ViewModels;

namespace LibreCode.Views;

/// <summary>File explorer sidebar with tree view and context menu.</summary>
public partial class FileExplorerView : UserControl
{
    public FileExplorerView() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || Vm is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Open Project Folder",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            Vm.OpenProject(path);
        }
    }

    private async void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileTree.SelectedItem is FileNode { IsDirectory: false } node && Vm is not null)
            await Vm.OpenFileAsync(node.Path);
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is FileNode { IsDirectory: false } node && Vm is not null)
            await Vm.OpenFileAsync(node.Path);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is FileNode node && Vm is not null)
        {
            if (node.IsDirectory)
                Vm.FileSystem.DeleteDirectory(node.Path);
            else
                Vm.FileSystem.DeleteFile(node.Path);
            Vm.RefreshFileTree();
        }
    }

    private async void OnNewFileClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var parent = FileTree.SelectedItem is FileNode { IsDirectory: true } dir
            ? dir.Path : "";

        var name = await PromptNameAsync("New File", "Enter file name:");
        if (name is null) return;

        var path = string.IsNullOrEmpty(parent) ? name : Path.Combine(parent, name);
        await Vm.FileSystem.WriteFileAsync(path, "");
        Vm.RefreshFileTree();
    }

    private async void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var parent = FileTree.SelectedItem is FileNode { IsDirectory: true } dir
            ? dir.Path : "";

        var name = await PromptNameAsync("New Folder", "Enter folder name:");
        if (name is null) return;

        var path = string.IsNullOrEmpty(parent) ? name : Path.Combine(parent, name);
        Vm.FileSystem.CreateDirectory(path);
        Vm.RefreshFileTree();
    }

    private void OnRunInTerminalClick(object? sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is FileNode { IsDirectory: false } node && Vm is not null)
        {
            Vm.ShowTerminal = true;
        }
    }

    private async Task<string?> PromptNameAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 350,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Black
        };

        var input = new TextBox { Watermark = message, Margin = new Avalonia.Thickness(12) };
        string? result = null;

        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                result = input.Text;
                dialog.Close();
            }
            else if (e.Key == Key.Escape)
            {
                dialog.Close();
            }
        };

        dialog.Content = input;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window owner)
            await dialog.ShowDialog(owner);

        return result;
    }
}
