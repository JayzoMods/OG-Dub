using System.Runtime.InteropServices;
using NAudio.Wave;

namespace OgDub.Audio;

internal sealed class ProcessLoopbackCapture : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _task;
    private Action<WaveFormat>? _onFormat;
    private Action<byte[], int>? _onSamples;

    public bool IsRunning => _task is { IsCompleted: false };
    public float Peak { get; private set; }
    public string Mode { get; private set; } = "";

    public void Start(uint processId, bool includeTree, Action<WaveFormat> onFormat, Action<byte[], int> onSamples)
    {
        Stop();
        Peak = 0;
        Mode = includeTree ? "APP" : "MIX-EXCL";
        _onFormat = onFormat;
        _onSamples = onSamples;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _task = Task.Factory.StartNew(
            () => Run(processId, includeTree, token, armed),
            token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        armed.Task.GetAwaiter().GetResult();
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _task?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // cancelled
        }

        _cts?.Dispose();
        _cts = null;
        _task = null;
        _onFormat = null;
        _onSamples = null;
    }

    public void Dispose() => Stop();

    private void Run(uint processId, bool includeTree, CancellationToken token, TaskCompletionSource armed)
    {
        var handler = new ActivateHandler();
        var activation = new WasapiNative.AudioClientActivationParams
        {
            ActivationType = WasapiNative.ActivationType.ProcessLoopback,
            TargetProcessId = processId,
            ProcessLoopbackMode = includeTree
                ? WasapiNative.ProcessLoopbackMode.IncludeTargetProcessTree
                : WasapiNative.ProcessLoopbackMode.ExcludeTargetProcessTree
        };

        var blob = Marshal.AllocHGlobal(Marshal.SizeOf<WasapiNative.AudioClientActivationParams>());
        IActivateAudioInterfaceAsyncOperation? op = null;
        try
        {
            Marshal.StructureToPtr(activation, blob, false);
            var prop = new WasapiNative.PropVariantBlob
            {
                vt = WasapiNative.VtBlob,
                blobSize = (uint)Marshal.SizeOf<WasapiNative.AudioClientActivationParams>(),
                blobData = blob
            };

            var iid = WasapiGuids.AudioClient;
            var hr = WasapiNative.ActivateAudioInterfaceAsync(
                WasapiNative.ProcessLoopbackDevice,
                in iid,
                in prop,
                handler,
                out op);
            if (hr < 0)
                throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException("ActivateAudioInterfaceAsync failed.");

            var (activateHr, clientObj) = handler.Completed.GetAwaiter().GetResult();
            if (activateHr < 0 || clientObj is not IAudioClient client)
                throw Marshal.GetExceptionForHR(activateHr) ?? new InvalidOperationException("Process loopback is not available on this Windows build.");

            hr = client.GetMixFormat(out var mixPtr);
            if (hr < 0 || mixPtr == 0)
                throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException("GetMixFormat failed.");

            WaveFormat format;
            try
            {
                format = WaveFormat.MarshalFromPtr(mixPtr);
                hr = client.Initialize(
                    WasapiNative.ShareModeShared,
                    WasapiNative.StreamFlagsNone,
                    20_0000,
                    0,
                    mixPtr,
                    0);
                if (hr < 0)
                    throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException("IAudioClient.Initialize failed.");
            }
            finally
            {
                WasapiNative.CoTaskMemFree(mixPtr);
            }

            hr = client.GetService(in WasapiGuids.AudioCaptureClient, out var captureObj);
            if (hr < 0 || captureObj is not IAudioCaptureClient capture)
                throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException("IAudioCaptureClient missing.");

            _onFormat?.Invoke(format);
            hr = client.Start();
            if (hr < 0)
                throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException("IAudioClient.Start failed.");

            armed.TrySetResult();
            var block = format.BlockAlign;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    hr = capture.GetNextPacketSize(out var frames);
                    if (hr < 0)
                        break;
                    if (frames == 0)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    hr = capture.GetBuffer(out var data, out var available, out var flags, out _, out _);
                    if (hr < 0)
                        break;

                    try
                    {
                        if (available > 0 && data != 0)
                        {
                            var bytes = (int)available * block;
                            var buffer = new byte[bytes];
                            if ((flags & WasapiNative.BufferSilent) == 0)
                                Marshal.Copy(data, buffer, 0, bytes);
                            Peak = Math.Max(Peak, PcmPeak.Max(buffer, bytes, format));
                            _onSamples?.Invoke(buffer, bytes);
                        }
                    }
                    finally
                    {
                        capture.ReleaseBuffer(available);
                    }
                }
            }
            finally
            {
                client.Stop();
            }
        }
        catch (Exception ex)
        {
            armed.TrySetException(ex);
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
            if (op is not null)
                Marshal.ReleaseComObject(op);
        }
    }
}
