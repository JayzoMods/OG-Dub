using OgDub.Core;

namespace OgDub.Services.LocalAi;

public enum LocalRuntimeKind
{
    Ollama,
    OpenAiCompat,
    LocalAi
}

public sealed class LocalRuntimeItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string BaseUrl { get; init; }
    public required LocalRuntimeKind Kind { get; init; }
    public bool CanChat { get; init; }
    public bool CanTransform { get; init; }
    public IReadOnlyList<string> Models { get; init; } = [];
}

public sealed class ChatTurn
{
    public required string Role { get; init; }
    public required string Text { get; init; }
}

public static class LocalRuntimeIds
{
    public const string Ollama = "ollama";
    public const string OpenAi = "openai";
    public const string LocalAi = "localai";
    public const string Extra = "extra";
}
