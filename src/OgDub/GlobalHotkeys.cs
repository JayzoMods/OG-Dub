using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OgDub;

internal sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const int RecId = 1;
    private const int StopId = 2;
    private const uint VkR = 0x52;
    private const uint VkS = 0x53;

    private readonly Window _window;
    private readonly Action _onRec;
    private readonly Action _onStop;
    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _registered;

    public GlobalHotkeys(Window window, Action onRec, Action onStop)
    {
        _window = window;
        _onRec = onRec;
        _onStop = onStop;
    }

    public bool TryRegister()
    {
        Unregister();
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        if (_source is null)
            return false;

        _source.AddHook(Hook);
        var recOk = RegisterHotKey(_hwnd, RecId, ModControl | ModAlt, VkR);
        var stopOk = RegisterHotKey(_hwnd, StopId, ModControl | ModAlt, VkS);
        _registered = recOk && stopOk;
        if (!_registered)
            Unregister();
        return _registered;
    }

    public void Unregister()
    {
        if (_source is not null)
        {
            _source.RemoveHook(Hook);
            _source = null;
        }

        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, RecId);
            UnregisterHotKey(_hwnd, StopId);
            _hwnd = IntPtr.Zero;
        }

        _registered = false;
    }

    public void Dispose() => Unregister();

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
            return IntPtr.Zero;

        var id = wParam.ToInt32();
        if (id == RecId)
        {
            _onRec();
            handled = true;
        }
        else if (id == StopId)
        {
            _onStop();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
