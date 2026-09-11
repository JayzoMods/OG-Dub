using System.Net.Http;
using System.Text;
using System.Text.Json;
using OgDub.Core;

namespace OgDub.Services.LocalAi;

public static class OllamaClient
{
    public static async Task<string> ChatAsync(
        HttpClient http,
        string baseUrl,
        string model,
        IReadOnlyList<ChatTurn> history,
        CancellationToken cancel)
    {
        if (!LocalLoopback.TryNormalizeBase(baseUrl, out var root))
            throw new InvalidOperationException("That local URL was refused.");

        var messages = history.Select(turn => new Dictionary<string, string>
        {
            ["role"] = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
            ["content"] = turn.Text
        }).ToList();

        var body = JsonSerializer.Serialize(new
        {
            model,
            messages,
            stream = false
        });

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        jobCts.CancelAfter(LocalLoopback.JobTimeoutMs);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(root + "/api/chat", content, jobCts.Token);
        var json = await response.Content.ReadAsStringAsync(jobCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var error = LocalModelCatalog.ParseErrorMessage(json);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "Ollama refused the chat (" + (int)response.StatusCode + ")."
                : error);
        }

        var reply = LocalModelCatalog.ParseOllamaChat(json);
        if (string.IsNullOrWhiteSpace(reply))
            throw new InvalidOperationException("Ollama returned an empty reply.");
        return reply;
    }
}
