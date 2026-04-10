using Avalonia.Controls;
using Avalonia.Interactivity;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Hex viewer with offset/hex/ASCII columns, go-to-offset, and page navigation.
/// Reads bytes on demand for large files.
/// </summary>
public partial class HexSubView : UserControl
{
    private const int LinesPerPage = 64;
    private const int BytesPerLine = 16;
    private long _currentOffset;
    private long _fileSize;

    public HexSubView()
    {
        InitializeComponent();
    }

    private AssemblyAnalysisService Svc => App.Services.GetRequiredService<AssemblyAnalysisService>();

    /// <summary>Called by parent ReversingView when an assembly is loaded.</summary>
    public async void OnAssemblyLoaded()
    {
        _currentOffset = 0;
        _fileSize = Svc.GetFileSizeBytes();
        await LoadPageAsync();
    }

    private async void OnGoToOffsetClick(object? sender, RoutedEventArgs e)
    {
        var text = OffsetBox.Text?.Trim() ?? "0";
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        if (long.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var offset))
        {
            _currentOffset = Math.Clamp(offset, 0, Math.Max(0, _fileSize - 1));
            await LoadPageAsync();
        }
    }

    private async void OnPrevClick(object? sender, RoutedEventArgs e)
    {
        _currentOffset = Math.Max(0, _currentOffset - (long)LinesPerPage * BytesPerLine);
        await LoadPageAsync();
    }

    private async void OnNextClick(object? sender, RoutedEventArgs e)
    {
        var next = _currentOffset + (long)LinesPerPage * BytesPerLine;
        if (next < _fileSize)
        {
            _currentOffset = next;
            await LoadPageAsync();
        }
    }

    private async Task LoadPageAsync()
    {
        try
        {
            var lines = await Svc.ReadBytesAsync(_currentOffset, LinesPerPage);
            HexList.ItemsSource = lines;
            PageLabel.Text = $"0x{_currentOffset:X} / 0x{_fileSize:X} ({_fileSize:N0} bytes)";
            OffsetBox.Text = $"0x{_currentOffset:X}";
        }
        catch { }
    }
}
