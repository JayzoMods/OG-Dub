using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using OgDub.Core;

namespace OgDub.Services.LocalAi;

public static class LocalAudioTransformClient
{
    public static async Task<byte[]> TransformAsync(
        HttpClient http,
        string baseUrl,
        string model,
        string wavPath,
        string prompt,
        CancellationToken cancel)
    {
        if (!LocalLoopback.TryNormalizeBase(baseUrl, out var root))
            throw new InvalidOperationException("That local URL was refused.");

        var dest = Path.GetFullPath(wavPath);
        if (!File.Exists(dest))
            throw new InvalidOperationException("That cassette WAV is missing.");

        var wavBytes = await File.ReadAllBytesAsync(dest, cancel);
        if (wavBytes.Length > LocalLoopback.MaxTransformBytes)
            throw new InvalidOperationException("That take is too large for a local pass (limit 80 MB).");
        if (!LocalLoopback.IsRiffWave(wavBytes))
            throw new InvalidOperationException("Edit only sends WAV cassettes.");

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        jobCts.CancelAfter(LocalLoopback.JobTimeoutMs);

        var urls = new[] { root + "/audio/transformations", root + "/audio/transform" };
        Exception? last = null;
        foreach (var url in urls)
        {
            using var content = BuildForm(model, prompt, dest, wavBytes);
            try
            {
                using var response = await http.PostAsync(url, content, jobCts.Token);
                var bytes = await response.Content.ReadAsByteArrayAsync(jobCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var text = System.Text.Encoding.UTF8.GetString(bytes);
                    var error = LocalModelCatalog.ParseErrorMessage(text);
                    last = new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? "The local audio runtime refused (" + (int)response.StatusCode + ")."
                        : error);
                    if ((int)response.StatusCode == 404)
                        continue;
                    throw last;
                }

                if (!LocalLoopback.IsRiffWave(bytes))
                    throw new InvalidOperationException("The local audio runtime did not return a WAV.");
                return bytes;
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("No local audio transform endpoint answered.");
    }

    private static MultipartFormDataContent BuildForm(string model, string prompt, string dest, byte[] wavBytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(model), "model");
        content.Add(new StringContent("wav"), "response_format");
        if (!string.IsNullOrWhiteSpace(prompt))
            content.Add(new StringContent(prompt.Trim()), "params[text]");

        var stream = new ByteArrayContent(wavBytes);
        stream.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(stream, "audio", Path.GetFileName(dest));
        return content;
    }
}
