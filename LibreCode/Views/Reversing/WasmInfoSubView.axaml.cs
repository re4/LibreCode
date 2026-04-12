using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Displays WASM module metadata: version, section layout, function/import/export counts,
/// globals, memory, and tables in a grouped read-only layout.
/// </summary>
public partial class WasmInfoSubView : UserControl
{
    public WasmInfoSubView()
    {
        InitializeComponent();
    }

    private WasmAnalysisService Svc => App.Services.GetRequiredService<WasmAnalysisService>();

    /// <summary>Called by parent view when a WASM binary is loaded.</summary>
    public void OnWasmLoaded()
    {
        var info = Svc.GetWasmInfo();
        if (info is null) return;

        InfoPanel.Children.Clear();

        AddSection("General");
        AddField("File", info.FileName);
        AddField("Size", FormatSize(info.FileSizeBytes));
        AddField("WASM Version", info.Version.ToString());

        AddSection("Module Statistics");
        AddField("Types", info.TypeCount.ToString());
        AddField("Functions", info.FunctionCount.ToString());
        AddField("Imports", info.ImportCount.ToString());
        AddField("Exports", info.ExportCount.ToString());
        AddField("Globals", info.GlobalCount.ToString());
        AddField("Tables", info.TableCount.ToString());
        AddField("Memories", info.MemoryCount.ToString());
        AddField("Data Segments", info.DataSegmentCount.ToString());
        AddField("Element Segments", info.ElementSegmentCount.ToString());
        AddField("Custom Sections", info.CustomSectionCount.ToString());

        if (info.StartFunction is not null)
            AddField("Start Function", info.StartFunction);

        if (info.Sections.Count > 0)
        {
            AddSection("Sections");
            foreach (var s in info.Sections)
                AddField(s.Name, $"offset=0x{s.Offset:X}  size={s.Size} bytes");
        }
    }

    private void AddSection(string title)
    {
        InfoPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#4ec9b0")),
            Margin = new Thickness(0, 8, 0, 2)
        });

        InfoPanel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#3c3c3c")),
            Margin = new Thickness(0, 0, 0, 4)
        });
    }

    private void AddField(string label, string value)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#569cd6")),
            Width = 160
        });
        sp.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#d4d4d4")),
            TextWrapping = TextWrapping.Wrap
        });
        InfoPanel.Children.Add(sp);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F2} MB"
    };
}
