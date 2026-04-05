using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace DotWhisper.UI;

public static partial class ClipboardHelper
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    public static void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            // Clipboard locked by another process
        }
    }

    public static void AutoPaste(string text, ILogger log)
    {
        try
        {
            Clipboard.SetText(text);

            // Small delay to ensure clipboard is ready
            Thread.Sleep(50);

            // Attach to the foreground window's thread so SendInput targets it
            var foreground = GetForegroundWindow();
            var foregroundThread = GetWindowThreadProcessId(foreground, out _);
            var currentThread = (uint)Environment.CurrentManagedThreadId;

            bool attached = false;
            if (foregroundThread != currentThread)
            {
                attached = AttachThreadInput(currentThread, foregroundThread, true);
            }

            try
            {
                SendCtrlV();
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
        catch (ExternalException ex)
        {
            log.LogWarning(ex, "Clipboard access failed, skipping AutoPaste");
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new INPUT[]
        {
            KeyDown(0x11),  // VK_CONTROL
            KeyDown(0x56),  // VK_V
            KeyUp(0x56),
            KeyUp(0x11)
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyDown(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk } }
    };

    private static INPUT KeyUp(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
