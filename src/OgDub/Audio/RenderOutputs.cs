using NAudio.CoreAudioApi;

namespace OgDub.Audio;

public sealed class RenderDeviceItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }
}

public static class RenderOutputs
{
    public static IReadOnlyList<RenderDeviceItem> List()
    {
        var result = new List<RenderDeviceItem>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    var id = device.ID ?? "";
                    var name = device.FriendlyName;
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                        continue;
                    result.Add(new RenderDeviceItem { Id = id, Label = name });
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
}
