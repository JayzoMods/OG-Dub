namespace OgDub.Core;

public static class SmtcSessionMatch
{
    public static bool Matches(string? sourceAppUserModelId, string? processName)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId) || string.IsNullOrWhiteSpace(processName))
            return false;

        var aumid = sourceAppUserModelId.Trim();
        var proc = processName.Trim();
        if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            proc = proc[..^4];
        if (proc.Length == 0)
            return false;

        if (aumid.Equals(proc, StringComparison.OrdinalIgnoreCase)
            || aumid.Equals(proc + ".exe", StringComparison.OrdinalIgnoreCase))
            return true;

        var tokens = aumid.Split(['!', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (token.Equals(proc, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
