using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace OgDub.Audio;

public sealed class Station
{
    public required int ProcessId { get; init; }
    public required string Name { get; init; }
    public float Peak { get; init; }
    public bool IsWholeMix { get; init; }
}

public static class WasapiStations
{
    public static IReadOnlyList<Station> List()
    {
        var result = new List<Station>
        {
            new()
            {
                ProcessId = 0,
                Name = "Whole mix",
                Peak = 0,
                IsWholeMix = true
            }
        };

        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        device.AudioSessionManager.RefreshSessions();
        var sessions = device.AudioSessionManager.Sessions;
        var self = Environment.ProcessId;

        for (var i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            uint pid;
            try
            {
                pid = session.GetProcessID;
            }
            catch (Exception)
            {
                continue;
            }

            if (pid == 0 || pid == (uint)self)
                continue;

            string name;
            try
            {
                name = session.DisplayName;
            }
            catch (Exception)
            {
                name = "";
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    name = proc.ProcessName;
                }
                catch (Exception)
                {
                    name = "PID " + pid;
                }
            }

            float peak = 0;
            try
            {
                peak = session.AudioMeterInformation.MasterPeakValue;
            }
            catch (Exception)
            {
                peak = 0;
            }

            if (result.Exists(s => s.ProcessId == (int)pid))
                continue;

            result.Add(new Station
            {
                ProcessId = (int)pid,
                Name = name,
                Peak = peak,
                IsWholeMix = false
            });
        }

        return result;
    }
}
