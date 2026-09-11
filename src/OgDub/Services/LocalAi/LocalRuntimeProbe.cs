using System.Net.Http;
using OgDub.Core;

namespace OgDub.Services.LocalAi;

public sealed class LocalRuntimeProbe : IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async Task<IReadOnlyList<LocalRuntimeItem>> ProbeAsync(
        string? extraBaseUrl,
        CancellationToken cancel)
    {
        var found = new List<LocalRuntimeItem>();
        await TryAdd(found, LocalRuntimeIds.Ollama, "Ollama", LocalLoopback.OllamaDefault, LocalRuntimeKind.Ollama, cancel);
        await TryAdd(found, LocalRuntimeIds.OpenAi, "Local OpenAI (LM Studio)", LocalLoopback.OpenAiCompatDefault, LocalRuntimeKind.OpenAiCompat, cancel);
        await TryAdd(found, LocalRuntimeIds.LocalAi, "LocalAI", LocalLoopback.LocalAiDefault, LocalRuntimeKind.LocalAi, cancel);

        if (LocalLoopback.TryNormalizeBase(extraBaseUrl, out var extra)
            && found.All(item => !string.Equals(item.BaseUrl, extra, StringComparison.OrdinalIgnoreCase)))
        {
            await TryAdd(found, LocalRuntimeIds.Extra, "Custom local", extra, LocalRuntimeKind.OpenAiCompat, cancel);
        }

        return found;
    }

    public Task<string> ChatAsync(LocalRuntimeItem runtime, string model, IReadOnlyList<ChatTurn> history, CancellationToken cancel)
    {
        if (runtime.Kind == LocalRuntimeKind.Ollama)
            return OllamaClient.ChatAsync(_http, runtime.BaseUrl, model, history, cancel);
        return OpenAiCompatClient.ChatAsync(_http, runtime.BaseUrl, model, history, cancel);
    }

    public Task<byte[]> TransformAsync(
        LocalRuntimeItem runtime,
        string model,
        string wavPath,
        string prompt,
        CancellationToken cancel)
    {
        return LocalAudioTransformClient.TransformAsync(_http, runtime.BaseUrl, model, wavPath, prompt, cancel);
    }

    public void Dispose() => _http.Dispose();

    private async Task TryAdd(
        List<LocalRuntimeItem> found,
        string id,
        string label,
        string rawUrl,
        LocalRuntimeKind preferredKind,
        CancellationToken cancel)
    {
        if (!LocalLoopback.TryNormalizeBase(rawUrl, out var baseUrl))
            return;

        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        probeCts.CancelAfter(LocalLoopback.ProbeTimeoutMs);
        try
        {
            var item = await InspectAsync(id, label, baseUrl, preferredKind, probeCts.Token);
            if (item is not null)
                found.Add(item);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
        }
        catch (HttpRequestException)
        {
        }
        catch (UriFormatException)
        {
        }
    }

    private async Task<LocalRuntimeItem?> InspectAsync(
        string id,
        string label,
        string baseUrl,
        LocalRuntimeKind preferredKind,
        CancellationToken cancel)
    {
        var ollamaModels = await GetJsonList(baseUrl + "/api/tags", LocalModelCatalog.ParseOllamaTags, cancel);
        var openAiModels = ollamaModels.Count == 0
            ? await GetJsonList(baseUrl + "/v1/models", LocalModelCatalog.ParseOpenAiModels, cancel)
            : [];
        var canTransform = await EndpointLooksPresent(baseUrl + "/audio/transformations", cancel)
            || await EndpointLooksPresent(baseUrl + "/audio/transform", cancel);

        var kind = ollamaModels.Count > 0
            ? LocalRuntimeKind.Ollama
            : canTransform
                ? LocalRuntimeKind.LocalAi
                : preferredKind;

        var models = ollamaModels.Count > 0 ? ollamaModels : openAiModels;
        var canChat = models.Count > 0;
        if (!canChat && !canTransform)
            return null;

        var kindLabel = kind switch
        {
            LocalRuntimeKind.Ollama => "Ollama",
            LocalRuntimeKind.LocalAi => "LocalAI",
            _ => label
        };

        return new LocalRuntimeItem
        {
            Id = id,
            Label = kindLabel + " · " + baseUrl.Replace("http://", "", StringComparison.OrdinalIgnoreCase),
            BaseUrl = baseUrl,
            Kind = kind,
            CanChat = canChat,
            CanTransform = canTransform,
            Models = models
        };
    }

    private async Task<IReadOnlyList<string>> GetJsonList(
        string url,
        Func<string, IReadOnlyList<string>> parse,
        CancellationToken cancel)
    {
        try
        {
            using var response = await _http.GetAsync(url, cancel);
            if (!response.IsSuccessStatusCode)
                return [];
            var json = await response.Content.ReadAsStringAsync(cancel);
            return parse(json);
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return [];
        }
    }

    private async Task<bool> EndpointLooksPresent(string url, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            var code = (int)response.StatusCode;
            if (code is 404 or 502 or 503)
                return false;
            return code is >= 200 and < 500 || code == 405;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return false;
        }
    }
}
