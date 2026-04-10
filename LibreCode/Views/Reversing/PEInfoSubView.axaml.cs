using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LibreCode.Features.Reversing;
using Microsoft.Extensions.DependencyInjection;

namespace LibreCode.Views.Reversing;

/// <summary>
/// Displays PE headers, sections, CLR metadata, and assembly references
/// in a grouped read-only layout.
/// </summary>
public partial class PEInfoSubView : UserControl
{
    public PEInfoSubView()
    {
        InitializeComponent();
    }

    private AssemblyAnalysisService Svc => App.Services.GetRequiredService<AssemblyAnalysisService>();

    /// <summary>Called by parent ReversingView when an assembly is loaded.</summary>
    public void OnAssemblyLoaded()
    {
        var info = Svc.GetPEInfo();
        if (info is null) return;

        InfoPanel.Children.Clear();

        AddSection("General");
        AddField("File", info.FileName);
        AddField("Size", FormatSize(info.FileSizeBytes));
        AddField("Managed", info.IsManaged ? "Yes" : "No");

        AddSection("PE Header");
        AddField("Machine", info.Machine);
        AddField("Subsystem", info.Subsystem);
        AddField("Image Base", $"0x{info.ImageBase:X}");
        AddField("File Alignment", $"0x{info.FileAlignment:X}");
        AddField("DLL Characteristics", info.DllCharacteristics);
        AddField("Entry Point Token", $"0x{info.EntryPointToken:X8}");

        if (info.IsManaged)
        {
            AddSection("CLR Metadata");
            AddField("Assembly", info.AssemblyName ?? "N/A");
            AddField("Version", info.AssemblyVersion ?? "N/A");
            AddField("CLR Version", info.ClrVersion ?? "N/A");
            AddField("Target Framework", info.TargetFramework ?? "N/A");
        }

        if (info.Sections.Count > 0)
        {
            AddSection("Sections");
            foreach (var s in info.Sections)
                AddField(s.Name, $"VA=0x{s.VirtualAddress:X}  VSize={s.VirtualSize}  RawSize={s.RawDataSize}");
        }

        if (info.References.Count > 0)
        {
            AddSection("Assembly References");
            foreach (var r in info.References)
            {
                var pkt = r.PublicKeyToken is not null ? $"  [{r.PublicKeyToken}]" : "";
                AddField(r.Name, $"v{r.Version}{pkt}");
            }
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

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F2} MB"
        };
    }
}
