using System.Text.Json;

namespace OgDub.Core;

public static class LocalModelCatalog
{
    public static IReadOnlyList<string> ParseOllamaTags(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models)
                || models.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<string>();
            foreach (var model in models.EnumerateArray())
            {
                var name = ReadString(model, "name") ?? ReadString(model, "model");
                AddUnique(list, name);
            }

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> ParseOpenAiModels(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<string>();
            foreach (var model in data.EnumerateArray())
                AddUnique(list, ReadString(model, "id"));

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string ParseOllamaChat(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("message", out var message))
                return "";
            return ReadString(message, "content") ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public static string ParseOpenAiChat(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return "";

            var first = choices[0];
            if (!first.TryGetProperty("message", out var message))
                return "";
            return ReadString(message, "content") ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public static string ParseErrorMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString() ?? "";
                return ReadString(error, "message") ?? "";
            }

            return ReadString(root, "message") ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static void AddUnique(List<string> list, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (list.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            return;
        list.Add(name);
    }
}
