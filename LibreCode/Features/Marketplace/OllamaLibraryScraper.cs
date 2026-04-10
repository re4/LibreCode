using System.Globalization;
using System.Text.RegularExpressions;

namespace LibreCode.Features.Marketplace;

/// <summary>
/// Scrapes the Ollama model library from ollama.com/library to provide a fresh
/// catalog each time the marketplace is opened. Parses the semantic HTML structure
/// using x-test-* data attributes that ollama.com emits for each model entry.
/// </summary>
public sealed partial class OllamaLibraryScraper
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders =
        {
            { "User-Agent", "LibreCode/1.0" },
            { "Accept", "text/html" }
        }
    };

    private static readonly Dictionary<string, double> VramEstimates = new()
    {
        ["135m"] = 0.3, ["270m"] = 0.5, ["278m"] = 0.5, ["300m"] = 0.5, ["335m"] = 0.6,
        ["350m"] = 0.6, ["360m"] = 0.5, ["500m"] = 0.8, ["567m"] = 0.8, ["568m"] = 0.8,
        ["0.5b"] = 0.5, ["0.6b"] = 0.6, ["0.8b"] = 0.8, ["1b"] = 1.2, ["1.1b"] = 1.2,
        ["1.2b"] = 1.4, ["1.3b"] = 1.5, ["1.5b"] = 1.8, ["1.7b"] = 2.0, ["1.8b"] = 2.0,
        ["2b"] = 2.5, ["2.4b"] = 3.0, ["2.7b"] = 3.2, ["3b"] = 3.5, ["3.8b"] = 4.0,
        ["4b"] = 4.5, ["6b"] = 5.5, ["6.7b"] = 6.0, ["7b"] = 6.0, ["7.8b"] = 6.5,
        ["8b"] = 6.5, ["9b"] = 7.0, ["10b"] = 8.0, ["10.7b"] = 8.5, ["11b"] = 8.5,
        ["12b"] = 9.0, ["13b"] = 10.0, ["14b"] = 10.5, ["15b"] = 11.0, ["16b"] = 12.0,
        ["20b"] = 14.0, ["22b"] = 15.0, ["24b"] = 16.0, ["26b"] = 18.0, ["27b"] = 18.0,
        ["30b"] = 20.0, ["31b"] = 21.0, ["32b"] = 22.0, ["33b"] = 22.0, ["34b"] = 23.0,
        ["35b"] = 24.0, ["40b"] = 28.0, ["70b"] = 42.0, ["72b"] = 44.0, ["80b"] = 48.0,
        ["90b"] = 54.0, ["104b"] = 64.0, ["110b"] = 68.0, ["111b"] = 68.0, ["120b"] = 72.0,
        ["122b"] = 74.0, ["123b"] = 74.0, ["128x17b"] = 48.0, ["132b"] = 80.0, ["141b"] = 84.0,
        ["180b"] = 110.0, ["235b"] = 130.0, ["236b"] = 130.0, ["405b"] = 240.0,
        ["480b"] = 280.0, ["671b"] = 400.0, ["16x17b"] = 16.0, ["8x7b"] = 26.0,
        ["8x22b"] = 80.0, ["e2b"] = 2.0, ["e4b"] = 4.0
    };

    /// <summary>
    /// Fetches and parses the complete model catalog from ollama.com/library.
    /// </summary>
    public async Task<List<LibraryModel>> FetchCatalogAsync(CancellationToken ct = default)
    {
        var html = await SharedClient.GetStringAsync("https://ollama.com/library", ct);
        return ParseLibraryHtml(html);
    }

    /// <summary>
    /// Searches ollama.com for models matching the given query, the same way
    /// the website's search bar works at ollama.com/search?q={query}.
    /// </summary>
    public async Task<List<LibraryModel>> SearchAsync(string query, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(query);
        var html = await SharedClient.GetStringAsync(
            $"https://ollama.com/search?q={encoded}", ct);
        return ParseLibraryHtml(html);
    }

    /// <summary>
    /// Parses the ollama.com/library HTML to extract model information.
    /// Each model is an &lt;li x-test-model&gt; containing an &lt;a href="/library/{id}"&gt;
    /// with nested x-test-model-title, x-test-capability, x-test-size,
    /// x-test-pull-count, x-test-tag-count, and x-test-updated elements.
    /// </summary>
    internal static List<LibraryModel> ParseLibraryHtml(string html)
    {
        var models = new List<LibraryModel>();
        var itemMatches = ModelItemRegex().Matches(html);

        foreach (Match item in itemMatches)
        {
            var block = item.Value;

            var idMatch = ModelIdRegex().Match(block);
            if (!idMatch.Success) continue;
            var modelId = idMatch.Groups["id"].Value;

            var descMatch = DescriptionRegex().Match(block);
            var description = descMatch.Success ? HtmlDecode(descMatch.Groups[1].Value.Trim()) : "";

            var capabilities = new List<string>();
            foreach (Match cap in CapabilityRegex().Matches(block))
                capabilities.Add(cap.Groups[1].Value.Trim().ToLowerInvariant());

            var variants = new List<string>();
            foreach (Match sz in SizeRegex().Matches(block))
                variants.Add(sz.Groups[1].Value.Trim().ToLowerInvariant());

            var pullMatch = PullCountRegex().Match(block);
            var pulls = pullMatch.Success ? pullMatch.Groups[1].Value.Trim() : "0";

            var tagMatch = TagCountRegex().Match(block);
            var tagCount = tagMatch.Success ? tagMatch.Groups[1].Value.Trim() : "0";

            var updatedMatch = UpdatedRegex().Match(block);
            var updatedAt = updatedMatch.Success ? updatedMatch.Groups[1].Value.Trim() : "Unknown";

            var isCloudOnly = capabilities.Contains("cloud") && variants.Count == 0;
            var minVram = EstimateMinVram(variants.Distinct().ToList());

            if (description.Length > 300)
                description = description[..297] + "...";

            models.Add(new LibraryModel
            {
                Id = modelId,
                Description = description,
                Capabilities = capabilities.Distinct().ToList(),
                Variants = variants.Distinct().ToList(),
                Pulls = pulls,
                PullsNumeric = ParsePullsToNumeric(pulls),
                TagCount = tagCount,
                UpdatedAt = updatedAt,
                Url = $"https://ollama.com/library/{modelId}",
                EstimatedMinVramGb = minVram,
                IsCloudOnly = isCloudOnly
            });
        }

        return models;
    }

    /// <summary>
    /// Decodes common HTML entities in scraped text.
    /// </summary>
    private static string HtmlDecode(string text) =>
        text.Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");

    private static double EstimateMinVram(List<string> variants)
    {
        if (variants.Count == 0) return -1;

        double min = double.MaxValue;
        foreach (var v in variants)
        {
            if (VramEstimates.TryGetValue(v, out var vram) && vram < min)
                min = vram;
        }
        return min < double.MaxValue ? min : -1;
    }

    private static long ParsePullsToNumeric(string pulls)
    {
        if (string.IsNullOrWhiteSpace(pulls)) return 0;

        var cleaned = pulls.Replace(",", "").Trim();

        if (cleaned.EndsWith('M'))
        {
            if (double.TryParse(cleaned[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
                return (long)(m * 1_000_000);
        }
        else if (cleaned.EndsWith('K'))
        {
            if (double.TryParse(cleaned[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var k))
                return (long)(k * 1_000);
        }
        else if (cleaned.EndsWith('B'))
        {
            if (double.TryParse(cleaned[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                return (long)(b * 1_000_000_000);
        }
        else if (long.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        return 0;
    }

    /// <summary>Matches each &lt;li x-test-model&gt; block including all nested content.</summary>
    [GeneratedRegex(@"<li\s+x-test-model\b[^>]*>(?<body>.*?)</li>", RegexOptions.Singleline)]
    private static partial Regex ModelItemRegex();

    /// <summary>Extracts the model ID from the anchor href="/library/{id}".</summary>
    [GeneratedRegex(@"href=""/library/(?<id>[a-zA-Z0-9._-]+)""")]
    private static partial Regex ModelIdRegex();

    /// <summary>Extracts the model description from the &lt;p&gt; inside x-test-model-title.</summary>
    [GeneratedRegex(@"x-test-model-title[\s\S]*?<p[^>]*>(.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex DescriptionRegex();

    /// <summary>Extracts capability tags from x-test-capability spans.</summary>
    [GeneratedRegex(@"x-test-capability[^>]*>\s*(.*?)\s*</span>")]
    private static partial Regex CapabilityRegex();

    /// <summary>Extracts size/variant tags from x-test-size spans.</summary>
    [GeneratedRegex(@"x-test-size[^>]*>\s*(.*?)\s*</span>")]
    private static partial Regex SizeRegex();

    /// <summary>Extracts the pull count from x-test-pull-count spans.</summary>
    [GeneratedRegex(@"x-test-pull-count[^>]*>\s*(.*?)\s*</span>")]
    private static partial Regex PullCountRegex();

    /// <summary>Extracts the tag count from x-test-tag-count spans.</summary>
    [GeneratedRegex(@"x-test-tag-count[^>]*>\s*(.*?)\s*</span>")]
    private static partial Regex TagCountRegex();

    /// <summary>Extracts the "updated" time from x-test-updated spans.</summary>
    [GeneratedRegex(@"x-test-updated[^>]*>\s*(.*?)\s*</span>")]
    private static partial Regex UpdatedRegex();
}
