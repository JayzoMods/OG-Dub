namespace OgDub.Core;

public static class LocalLoopback
{
    public const string OllamaDefault = "http://127.0.0.1:11434";
    public const string OpenAiCompatDefault = "http://127.0.0.1:1234";
    public const string LocalAiDefault = "http://127.0.0.1:8080";
    public const long MaxTransformBytes = 80L * 1024 * 1024;
    public const int ProbeTimeoutMs = 1500;
    public const int JobTimeoutMs = 10 * 60 * 1000;

    public static bool TryNormalizeBase(string? url, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (uri.UserInfo.Length > 0)
            return false;

        if (!IsLoopbackHost(uri.Host))
            return false;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Equals("/v1", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api", StringComparison.OrdinalIgnoreCase))
            path = "";

        var builder = new UriBuilder(uri.Scheme, uri.Host)
        {
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Path = path
        };
        normalized = builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return normalized.Length > 0;
    }

    public static bool IsRiffWave(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
            return false;
        return bytes[0] == (byte)'R'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W'
            && bytes[9] == (byte)'A'
            && bytes[10] == (byte)'V'
            && bytes[11] == (byte)'E';
    }

    public static string EditedStem(string sourceTitle, DateTimeOffset when)
    {
        var baseName = CassetteNaming.Sanitize(sourceTitle);
        if (baseName.EndsWith(" edit", StringComparison.OrdinalIgnoreCase))
            return CassetteNaming.FileStem(baseName, when);
        return CassetteNaming.FileStem(baseName + " edit", when);
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!System.Net.IPAddress.TryParse(host, out var ip))
            return false;

        return System.Net.IPAddress.IsLoopback(ip);
    }
}
