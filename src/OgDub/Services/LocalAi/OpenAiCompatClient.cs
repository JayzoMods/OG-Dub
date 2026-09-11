using System.Net.Http;
using System.Text;
using System.Text.Json;
using OgDub.Core;

namespace OgDub.Services.LocalAi;

public static class OpenAiCompatClient
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
        using var response = await http.PostAsync(root + "/v1/chat/completions", content, jobCts.Token);
        var json = await response.Content.ReadAsStringAsync(jobCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var error = LocalModelCatalog.ParseErrorMessage(json);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "The local chat server refused (" + (int)response.StatusCode + ")."
                : error);
        }

        var reply = LocalModelCatalog.ParseOpenAiChat(json);
        if (string.IsNullOrWhiteSpace(reply))
            throw new InvalidOperationException("The local chat server returned an empty reply.");
        return reply;
    }
}
