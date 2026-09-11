using NAudio.CoreAudioApi;

namespace OgDub.Audio;

public sealed class MicDeviceItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }
}

public static class MicInputs
{
    public static IReadOnlyList<MicDeviceItem> List()
    {
        var result = new List<MicDeviceItem>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                try
                {
                    var id = device.ID ?? "";
                    var name = device.FriendlyName;
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                        continue;
                    result.Add(new MicDeviceItem { Id = id, Label = name });
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception)
        {
            return result;
        }

        return result;
    }

    public static string? DefaultCaptureId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            return device.ID;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
