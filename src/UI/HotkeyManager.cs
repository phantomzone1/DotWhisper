using System.Runtime.InteropServices;

namespace DotWhisper.UI;

public sealed partial class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 1;

    private readonly Action _onPressed;
    private readonly HotkeyWindow _window;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotkeyManager(string keyName, Action onPressed)
    {
        _onPressed = onPressed;
        _window = new HotkeyWindow(this);

        if (!Enum.TryParse<Keys>(keyName, ignoreCase: true, out var key))
            throw new InvalidOperationException($"Unknown hotkey: {keyName}");

        if (!RegisterHotKey(_window.Handle, HOTKEY_ID, 0, (uint)key))
        {
            var error = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException($"Failed to register hotkey {keyName}. Error code: {error}");
        }
    }

    public void Dispose()
    {
        UnregisterHotKey(_window.Handle, HOTKEY_ID);
        _window.Dispose();
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly HotkeyManager _owner;

        public HotkeyWindow(HotkeyManager owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam == HOTKEY_ID)
                _owner._onPressed();

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }
}
