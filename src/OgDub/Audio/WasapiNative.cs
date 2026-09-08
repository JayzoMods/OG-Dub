using System.Runtime.InteropServices;

namespace OgDub.Audio;

// GUIDs and vtables from mmdeviceapi.h / audioclient.h (Microsoft Learn).
internal static class WasapiGuids
{
    public static readonly Guid AudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    public static readonly Guid AudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
}

internal static class WasapiNative
{
    public const string ProcessLoopbackDevice = @"VAD\Process_Loopback";
    public const ushort VtBlob = 0x0041;
    public const uint StreamFlagsNone = 0;
    public const int BufferSilent = 0x1;
    public const int ShareModeShared = 0;

    public enum ActivationType
    {
        Default = 0,
        ProcessLoopback = 1
    }

    public enum ProcessLoopbackMode
    {
        IncludeTargetProcessTree = 0,
        ExcludeTargetProcessTree = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioClientActivationParams
    {
        public ActivationType ActivationType;
        public uint TargetProcessId;
        public ProcessLoopbackMode ProcessLoopbackMode;
    }

    // x64 PROPVARIANT: vt at 0, blob.cbSize at 8, blob.pBlobData at 16.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVariantBlob
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint blobSize;
        [FieldOffset(16)] public nint blobData;
    }

    [DllImport("mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    public static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        in Guid riid,
        in PropVariantBlob activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern void CoTaskMemFree(nint pv);
}

[ComImport]
[Guid("41D949AB-9862-444A-80F6-C9F7A2F4FE61")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    [PreserveSig]
    int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
}

[ComImport]
[Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    [PreserveSig]
    int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object? activatedInterface);
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, nint format, nint audioSessionGuid);
    [PreserveSig] int GetBufferSize(out uint bufferFrameCount);
    [PreserveSig] int GetStreamLatency(out long hnsLatency);
    [PreserveSig] int GetCurrentPadding(out uint numPaddingFrames);
    [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closestMatch);
    [PreserveSig] int GetMixFormat(out nint deviceFormat);
    [PreserveSig] int GetDevicePeriod(out long hnsDefault, out long hnsMinimum);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(nint eventHandle);
    [PreserveSig] int GetService(in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? service);
}

[ComImport]
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out nint data, out uint numFramesAvailable, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint numFramesRead);
    [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
}

internal sealed class ActivateHandler : IActivateAudioInterfaceCompletionHandler
{
    private readonly TaskCompletionSource<(int Hr, object? Client)> _done = new();

    public Task<(int Hr, object? Client)> Completed => _done.Task;

    public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
    {
        try
        {
            var hrOp = activateOperation.GetActivateResult(out var hr, out var obj);
            if (hrOp < 0)
                _done.TrySetResult((hrOp, null));
            else
                _done.TrySetResult((hr, obj));
        }
        catch (Exception ex)
        {
            _done.TrySetException(ex);
        }

        return 0;
    }
}
