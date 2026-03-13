using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VolumeAssistant.Service.CambridgeAudio;

/// <summary>
/// Listens for Windows media key presses (Play/Pause, Next Track, Previous Track)
/// using a low-level keyboard hook (WH_KEYBOARD_LL).
/// Runs a dedicated message-pump thread so that the hook receives events
/// even in a Windows Service process that has no UI message loop.
/// </summary>
internal sealed class MediaKeyListener : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const uint WM_QUIT = 0x0012;

    // Virtual key codes for media keys
    private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
    private const uint VK_MEDIA_PREV_TRACK = 0xB1;
    private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
    // Scroll Lock
    private const uint VK_SCROLL = 0x91;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpmsg);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern short GetAsyncKeyState(int vKey);

    private Thread? _thread;
    private volatile uint _threadId;
    private IntPtr _hookHandle = IntPtr.Zero;
    // Keep a strong reference to the delegate to prevent GC collection while the hook is active.
    private LowLevelKeyboardProc? _hookProc;
    private bool _disposed;

    /// <summary>Raised when the Play/Pause media key is pressed.</summary>
    public event EventHandler? PlayPausePressed;

    /// <summary>Raised when the Next Track media key is pressed.</summary>
    public event EventHandler? NextTrackPressed;

    /// <summary>Raised when the Previous Track media key is pressed.</summary>
    public event EventHandler? PreviousTrackPressed;

    /// <summary>Raised when Shift+ScrollLock is pressed to request source switching.</summary>
    public event EventHandler? SourceSwitchRequested;

    /// <summary>
    /// Starts the media key listener on a background thread.
    /// On non-Windows platforms this is a no-op.
    /// </summary>
    public void Start()
    {
        if (!OperatingSystem.IsWindows() || _thread != null)
            return;

        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "MediaKeyHookThread" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    [SupportedOSPlatform("windows")]
    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _hookProc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, IntPtr.Zero, 0);

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    [SupportedOSPlatform("windows")]
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var kbInfo = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            switch (kbInfo.vkCode)
            {
                case VK_MEDIA_PLAY_PAUSE:
                    PlayPausePressed?.Invoke(this, EventArgs.Empty);
                    break;
                case VK_MEDIA_NEXT_TRACK:
                    NextTrackPressed?.Invoke(this, EventArgs.Empty);
                    break;
                case VK_MEDIA_PREV_TRACK:
                    PreviousTrackPressed?.Invoke(this, EventArgs.Empty);
                    break;
                case VK_SCROLL:
                    try
                    {
                        // Check if either Shift key is currently down; GetAsyncKeyState returns
                        // a short where the high-order bit is set when the key is down.
                        const int VK_SHIFT = 0x10;
                        short state = GetAsyncKeyState(VK_SHIFT);
                        bool shiftDown = (state & 0x8000) != 0;
                        if (shiftDown)
                        {
                            SourceSwitchRequested?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    catch
                    {
                        // Don't let an error here crash the hook thread; ignore.
                    }
                    break;
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (OperatingSystem.IsWindows() && _threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }
    }
}
