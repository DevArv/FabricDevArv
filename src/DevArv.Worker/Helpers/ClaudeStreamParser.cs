using System.Text.Json;
using DevArv.Worker.Models;

namespace DevArv.Worker.Helpers;

/// <summary>
/// Interpreta la salida de `claude -p --output-format stream-json --verbose` (un JSON por línea)
/// y la convierte en un log legible, capturando el mensaje final "result" con costo y tokens.
/// </summary>
public sealed class ClaudeStreamParser
{
    private const int MAX_TOOL_DETAIL = 220;

    public ClaudeResult? Result { get; private set; }
    public string? Model { get; private set; }
    public string? SessionId { get; private set; }
    public string? LastAssistantText { get; private set; }

    public IEnumerable<string> Parse(string Line)
    {
        if (string.IsNullOrWhiteSpace(Line)) return [];
        if (!Line.TrimStart().StartsWith('{')) return [Line];

        try
        {
            using var doc = JsonDocument.Parse(Line);
            var root = doc.RootElement;
            return Str(root, "type") switch
            {
                "system" => ParseSystem(root),
                "assistant" => ParseAssistant(root),
                "user" => ParseUser(root),
                "result" => ParseResult(root),
                _ => [],
            };
        }
        catch (JsonException)
        {
            return [Line];
        }
    }

    private List<string> ParseSystem(JsonElement Root)
    {
        if (Str(Root, "subtype") != "init") return [];
        Model = Str(Root, "model") ?? Model;
        SessionId = Str(Root, "session_id") ?? SessionId;
        return [$"⚙ Sesión de Claude iniciada · modelo {Model ?? "?"} · permisos {Str(Root, "permissionMode") ?? "?"}"];
    }

    private List<string> ParseAssistant(JsonElement Root)
    {
        var lines = new List<string>();
        if (!Root.TryGetProperty("message", out var message)) return lines;
        if (Str(message, "model") is { } model) Model = model;
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return lines;

        foreach (var block in content.EnumerateArray())
        {
            switch (Str(block, "type"))
            {
                case "text":
                    var text = Str(block, "text");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        LastAssistantText = text;
                        lines.Add("💬 " + text.Trim());
                    }
                    break;
                case "tool_use":
                    lines.Add($"🔧 {Str(block, "name") ?? "herramienta"}: {DescribeToolInput(block)}");
                    break;
            }
        }
        return lines;
    }

    private static List<string> ParseUser(JsonElement Root)
    {
        var lines = new List<string>();
        if (!Root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array) return lines;

        foreach (var block in content.EnumerateArray())
        {
            if (Str(block, "type") != "tool_result") continue;
            var isError = block.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
            var text = ToolResultText(block);
            if (isError)
                lines.Add("   ✖ " + Truncate(text.ReplaceLineEndings(" "), 300));
            else if (text.Length > 0)
                lines.Add($"   ↳ {CountLines(text)} línea(s)");
        }
        return lines;
    }

    private List<string> ParseResult(JsonElement Root)
    {
        long input = 0, output = 0, cacheRead = 0, cacheWrite = 0;
        if (Root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            input = Long(usage, "input_tokens");
            output = Long(usage, "output_tokens");
            cacheRead = Long(usage, "cache_read_input_tokens");
            cacheWrite = Long(usage, "cache_creation_input_tokens");
        }

        var cost = Dec(Root, "total_cost_usd") ?? Dec(Root, "cost_usd") ?? 0m;
        var isError = Root.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
        var subtype = Str(Root, "subtype");
        int? turns = Root.TryGetProperty("num_turns", out var nt) && nt.TryGetInt32(out var n) ? n : null;
        SessionId = Str(Root, "session_id") ?? SessionId;

        Result = new ClaudeResult(
            IsError: isError || (subtype is not null && subtype != "success"),
            Subtype: subtype,
            ResultText: Str(Root, "result") ?? LastAssistantText,
            CostUsd: cost,
            InputTokens: input,
            OutputTokens: output,
            CacheReadTokens: cacheRead,
            CacheWriteTokens: cacheWrite,
            NumTurns: turns,
            SessionId: SessionId);

        return [$"🏁 Fin ({subtype ?? "?"}) · {turns?.ToString() ?? "?"} turnos · US$ {cost:0.0000} · " +
                $"tokens in {input:N0} / out {output:N0} / caché {cacheRead + cacheWrite:N0}"];
    }

    private static string DescribeToolInput(JsonElement Block)
    {
        if (!Block.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object) return "";
        foreach (var key in new[] { "command", "file_path", "path", "pattern", "url", "query", "description" })
        {
            if (Str(input, key) is { Length: > 0 } value)
                return Truncate(value.ReplaceLineEndings(" ⏎ "), MAX_TOOL_DETAIL);
        }
        return Truncate(input.GetRawText(), MAX_TOOL_DETAIL);
    }

    private static string ToolResultText(JsonElement Block)
    {
        if (!Block.TryGetProperty("content", out var content)) return "";
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array) return "";
        return string.Join("\n", content.EnumerateArray()
            .Where(c => Str(c, "type") == "text")
            .Select(c => Str(c, "text") ?? ""));
    }

    private static string? Str(JsonElement Element, string Name) =>
        Element.ValueKind == JsonValueKind.Object && Element.TryGetProperty(Name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long Long(JsonElement Element, string Name) =>
        Element.TryGetProperty(Name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    private static decimal? Dec(JsonElement Element, string Name) =>
        Element.TryGetProperty(Name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    private static int CountLines(string Text) => Text.Count(c => c == '\n') + 1;

    private static string Truncate(string Text, int Max) => Text.Length <= Max ? Text : Text[..Max] + "…";
}
