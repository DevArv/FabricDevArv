using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Html;

namespace DevArv.Web.Services;

/// <summary>Markdown → HTML seguro: el HTML crudo se escapa y sólo se permiten enlaces http(s), mailto o relativos.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static IHtmlContent Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return HtmlString.Empty;

        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsSafeUrl(link.Url)) link.Url = "#";
        }
        foreach (var link in document.Descendants<AutolinkInline>())
        {
            if (!IsSafeUrl(link.Url)) link.Url = "#";
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return new HtmlString(writer.ToString());
    }

    private static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        var trimmed = url.Trim();
        if (trimmed.StartsWith('/') || trimmed.StartsWith('#')) return true;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https" or "mailto";
        // Relativa sin esquema (p. ej. "docs/a.md"): segura si no contiene ':' antes de '/'.
        var colon = trimmed.IndexOf(':');
        var slash = trimmed.IndexOf('/');
        return colon < 0 || (slash >= 0 && slash < colon);
    }
}
