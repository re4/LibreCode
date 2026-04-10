namespace LibreCode.Features.Marketplace;

/// <summary>
/// Represents a model available in the Ollama library scraped from ollama.com/library.
/// </summary>
public sealed class LibraryModel
{
    /// <summary>Unique model identifier (e.g. "llama3.1", "deepseek-r1").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Available parameter size variants (e.g. "8b", "70b", "405b").</summary>
    public List<string> Variants { get; set; } = [];

    /// <summary>Capability tags (e.g. "tools", "vision", "thinking", "embedding", "cloud").</summary>
    public List<string> Capabilities { get; set; } = [];

    /// <summary>Total pull count formatted as string (e.g. "112.9M").</summary>
    public string Pulls { get; set; } = string.Empty;

    /// <summary>Number of available tags/quantizations.</summary>
    public string TagCount { get; set; } = string.Empty;

    /// <summary>Relative last-updated string (e.g. "1 year ago").</summary>
    public string UpdatedAt { get; set; } = string.Empty;

    /// <summary>Full URL to the model page on ollama.com.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Estimated minimum VRAM in GB for the smallest variant, or -1 if unknown.</summary>
    public double EstimatedMinVramGb { get; set; } = -1;

    /// <summary>Whether this model is currently installed locally.</summary>
    public bool IsInstalled { get; set; }

    /// <summary>Whether a cloud-only model (cannot be pulled locally).</summary>
    public bool IsCloudOnly { get; set; }

    /// <summary>Numeric pull count parsed for sorting.</summary>
    public long PullsNumeric { get; set; }

    /// <summary>Formatted VRAM string for display, empty when unknown.</summary>
    public string VramDisplay => EstimatedMinVramGb > 0
        ? $"~{EstimatedMinVramGb:F0} GB VRAM"
        : string.Empty;
}

/// <summary>
/// Real-time progress during an Ollama model pull operation.
/// </summary>
public sealed class PullProgress
{
    public string Status { get; set; } = string.Empty;
    public string Digest { get; set; } = string.Empty;
    public long Total { get; set; }
    public long Completed { get; set; }

    /// <summary>Percentage complete (0-100), or -1 if indeterminate.</summary>
    public double Percent => Total > 0 ? Math.Round(Completed * 100.0 / Total, 1) : -1;
}

/// <summary>
/// GPU hardware information detected via DXGI (preferred) or WMI fallback.
/// </summary>
public sealed class GpuInfo
{
    public string Name { get; set; } = "Unknown GPU";
    public double VramGb { get; set; }

    /// <summary>Formatted display string.</summary>
    public string Display => VramGb > 0
        ? $"{Name} ({VramGb:F1} GB VRAM)"
        : Name;
}
