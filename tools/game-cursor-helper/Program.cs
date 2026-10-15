using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    internal const string TargetProcessName = "DarkSoulsRemastered";
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    internal const int GWL_EXSTYLE = -20;
    internal const long WS_EX_TOOLWINDOW = 0x00000080L;
    internal const long WS_EX_NOACTIVATE = 0x08000000L;
    internal const uint GW_OWNER = 4;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
        if (!OperatingSystem.IsWindows()) return 2;
        var shortcutText = args.Length > 0 ? args[0] : "F10";
        var ownerPid = args.Length > 1 && int.TryParse(args[1], out var parsedPid) ? parsedPid : 0;
        if (!CursorShortcut.TryParse(shortcutText, out var initialShortcut)) return 2;
        using var shortcut = new CursorShortcutHook(ownerPid);
        shortcut.SetShortcut(initialShortcut);
        try
        {
            shortcut.Start();
            await MonitorGameLifetimeAsync(shortcut, ownerPid);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int SelfTest()
    {
        if (!OperatingSystem.IsWindows()
            || !CursorShortcut.TryParse("F10", out var defaultShortcut)
            || !CursorShortcut.TryParse("Ctrl+Shift+F8", out var combinedShortcut)
            || CursorShortcut.TryParse("Ctrl", out _)
            || CursorShortcut.TryParse(string.Empty, out _)
            || !CursorShortcutHook.ShouldTrigger(defaultShortcut, 0x79, WM_KEYDOWN, true, false, false, false)
            || CursorShortcutHook.ShouldTrigger(defaultShortcut, 0x79, WM_KEYDOWN, false, false, false, false)
            || CursorShortcutHook.ShouldTrigger(defaultShortcut, 0x79, WM_KEYDOWN, true, true, false, false)
            || !CursorShortcutHook.ShouldTrigger(combinedShortcut, 0x77, WM_KEYDOWN, true, true, false, true)) return 1;
        try
        {
            using var hook = new CursorShortcutHook(0);
            hook.Start();
            return 0;
        }
        catch { return 1; }
    }

    private static async Task MonitorGameLifetimeAsync(CursorShortcutHook shortcut, int ownerPid)
    {
        using var owner = TryGetProcess(ownerPid);
        var gameWasSeen = false;
        var ownerClosedAt = owner is null || owner.HasExited ? DateTime.UtcNow : (DateTime?)null;
        while (true)
        {
            shortcut.TrackForeground(GetForegroundWindow());
            if (GameProcessIsRunning()) gameWasSeen = true;
            else if (gameWasSeen) return;

            if (!gameWasSeen)
            {
                if (owner is not null && owner.HasExited) ownerClosedAt ??= DateTime.UtcNow;
                if (ownerClosedAt is not null && DateTime.UtcNow - ownerClosedAt.Value > TimeSpan.FromMinutes(5)) return;
            }
            await Task.Delay(150);
        }
    }

    private static Process? TryGetProcess(int processId)
    {
        if (processId <= 0) return null;
        try { return Process.GetProcessById(processId); }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static bool GameProcessIsRunning()
    {
        var processes = Process.GetProcessesByName(TargetProcessName);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    internal static bool IsDsrWindow(IntPtr window, bool requireVisible)
    {
        if (window == IntPtr.Zero || !IsWindow(window) || (requireVisible && !IsWindowVisible(window))) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return !process.HasExited && string.Equals(process.ProcessName, TargetProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

internal readonly record struct CursorShortcut(uint VirtualKey, bool Control, bool Alt, bool Shift)
{
    public static bool TryParse(string? text, out CursorShortcut shortcut)
    {
        shortcut = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 4 || parts.Any(string.IsNullOrWhiteSpace)) return false;

        var control = false;
        var alt = false;
        var shift = false;
        foreach (var part in parts[..^1])
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                if (control) return false;
                control = true;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                if (alt) return false;
                alt = true;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                if (shift) return false;
                shift = true;
            }
            else return false;
        }

        if (!TryGetVirtualKey(parts[^1], out var virtualKey)) return false;
        shortcut = new CursorShortcut(virtualKey, control, alt, shift);
        return true;
    }

    private static bool TryGetVirtualKey(string key, out uint virtualKey)
    {
        virtualKey = 0;
        if (key.Length == 1)
        {
            var character = char.ToUpperInvariant(key[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = character;
                return true;
            }
        }
        if (key.Length >= 2 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }
        virtualKey = key.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "tab" => 0x09,
            "insert" => 0x2D,
            "delete" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "enter" => 0x0D,
            "backspace" => 0x08,
            _ => 0,
        };
        return virtualKey != 0;
    }
}

internal sealed class CursorShortcutHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_QUIT = 0x0012;
    private const int WM_TIMER = 0x0113;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x00000010;
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_MENU = 0x12;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const uint PM_NOREMOVE = 0x0000;
    private const uint HandoffDelayMilliseconds = 65;
    private const uint RetryDelayMilliseconds = 30;
    private const int MaxHandoffAttempts = 3;
    private const uint TimerIdentifier = 0xD510;

    private readonly object _settingsLock = new();
    private readonly object _windowHistoryLock = new();
    private readonly LinkedList<IntPtr> _recentWindows = new();
    private readonly int _ownerPid;
    private readonly uint _helperPid = (uint)Environment.ProcessId;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly LowLevelKeyboardProc _keyboardProc;
    private CursorShortcut _shortcut = new(0x79, false, false, false); // F10
    private Thread? _thread;
    private Exception? _startupError;
    private uint _threadId;
    private IntPtr _keyboardHook;
    private UIntPtr _timerId;
    private IntPtr _pendingGameWindow;
    private IntPtr _pendingTargetWindow;
    private uint _suppressedKey;
    private int _handoffAttempts;
    private bool _disposed;

    public CursorShortcutHook(int ownerPid)
    {
        _ownerPid = ownerPid;
        _keyboardProc = KeyboardHookProc;
    }

    public bool SetShortcut(CursorShortcut shortcut)
    {
        lock (_settingsLock) _shortcut = shortcut;
        return true;
    }

    public void TrackForeground(IntPtr window)
    {
        if (!IsSensibleWindow(window) || Program.IsDsrWindow(window, requireVisible: true)) return;
        lock (_windowHistoryLock)
        {
            _recentWindows.Remove(window);
            _recentWindows.AddFirst(window);
            while (_recentWindows.Count > 24) _recentWindows.RemoveLast();
        }
    }

    internal static bool ShouldTrigger(CursorShortcut shortcut, uint virtualKey, int message, bool dsrForeground, bool control, bool alt, bool shift)
    {
        return dsrForeground && virtualKey == shortcut.VirtualKey
            && (message == Program.WM_KEYDOWN || message == WM_SYSKEYDOWN)
            && control == shortcut.Control && alt == shortcut.Alt && shift == shortcut.Shift;
    }

    public void Start()
    {
        _thread = new Thread(HookThreadMain) { IsBackground = true, Name = "DS1TR cursor shortcut" };
        _thread.Start();
        _started.Wait();
        if (_startupError is not null)
        {
            _thread.Join();
            throw new InvalidOperationException("Could not initialize the cursor shortcut hook.", _startupError);
        }
    }

    private void HookThreadMain()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
            if (_keyboardHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the cursor shortcut hook.");
            _started.Set();

            int result;
            while ((result = GetMessage(out var message, IntPtr.Zero, 0, 0)) > 0)
            {
                if (message.Id == WM_TIMER && message.WParam == _timerId)
                {
                    HandleHandoffTimer();
                    continue;
                }
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
            if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "The cursor shortcut message loop failed.");
        }
        catch (Exception error)
        {
            _startupError = error;
            _started.Set();
        }
        finally
        {
            if (_timerId != UIntPtr.Zero) _ = KillTimer(IntPtr.Zero, _timerId);
            if (_keyboardHook != IntPtr.Zero) _ = UnhookWindowsHookEx(_keyboardHook);
        }
    }

    private IntPtr KeyboardHookProc(int code, IntPtr messagePointer, IntPtr dataPointer)
    {
        if (code < 0) return CallNextHookEx(_keyboardHook, code, messagePointer, dataPointer);
        var message = unchecked((int)messagePointer.ToInt64());
        var key = Marshal.PtrToStructure<KeyboardData>(dataPointer);
        if ((key.Flags & LLKHF_INJECTED) != 0) return CallNextHookEx(_keyboardHook, code, messagePointer, dataPointer);

        if (_suppressedKey != 0 && key.VirtualKey == _suppressedKey)
        {
            if (message is Program.WM_KEYUP or WM_SYSKEYUP) _suppressedKey = 0;
            if (message is Program.WM_KEYDOWN or Program.WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP) return new IntPtr(1);
        }

        if (message is Program.WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            CursorShortcut shortcut;
            lock (_settingsLock) shortcut = _shortcut;
            var foreground = Program.GetForegroundWindow();
            var matches = ShouldTrigger(shortcut, key.VirtualKey, message, Program.IsDsrWindow(foreground, requireVisible: true),
                IsKeyDown(VK_CONTROL, VK_LCONTROL, VK_RCONTROL), IsKeyDown(VK_MENU, VK_LMENU, VK_RMENU), IsKeyDown(VK_SHIFT, VK_LSHIFT, VK_RSHIFT));
            if (matches && _suppressedKey == 0 && _pendingGameWindow == IntPtr.Zero)
            {
                _pendingGameWindow = foreground;
                _handoffAttempts = 0;
                if (ScheduleTimer(HandoffDelayMilliseconds))
                {
                    _suppressedKey = key.VirtualKey;
                    return new IntPtr(1);
                }
                _pendingGameWindow = IntPtr.Zero;
            }
        }
        return CallNextHookEx(_keyboardHook, code, messagePointer, dataPointer);
    }

    private void HandleHandoffTimer()
    {
        _ = KillTimer(IntPtr.Zero, _timerId);
        _timerId = UIntPtr.Zero;
        var gameWindow = _pendingGameWindow;
        if (gameWindow == IntPtr.Zero) return;
        var foreground = Program.GetForegroundWindow();
        if (foreground != gameWindow || !Program.IsDsrWindow(gameWindow, requireVisible: true))
        {
            if (foreground != gameWindow) _ = ClipCursor(IntPtr.Zero);
            _pendingGameWindow = IntPtr.Zero;
            _pendingTargetWindow = IntPtr.Zero;
            return;
        }
        if (IsKeyDown(VK_CONTROL, VK_LCONTROL, VK_RCONTROL) || IsKeyDown(VK_MENU, VK_LMENU, VK_RMENU) || IsKeyDown(VK_SHIFT, VK_LSHIFT, VK_RSHIFT))
        {
            _ = ScheduleTimer(RetryDelayMilliseconds);
            return;
        }

        _ = ClipCursor(IntPtr.Zero);
        if (_pendingTargetWindow == IntPtr.Zero) _pendingTargetWindow = ChooseTargetWindow(gameWindow);
        if (_pendingTargetWindow == IntPtr.Zero)
        {
            _pendingGameWindow = IntPtr.Zero;
            return;
        }

        var activated = ActivateTargetWindow(_pendingTargetWindow, gameWindow);
        _handoffAttempts++;
        if (activated || Program.GetForegroundWindow() != gameWindow || _handoffAttempts >= MaxHandoffAttempts)
        {
            _ = ClipCursor(IntPtr.Zero);
            _pendingGameWindow = IntPtr.Zero;
            _pendingTargetWindow = IntPtr.Zero;
        }
        else
            _ = ScheduleTimer(RetryDelayMilliseconds);
    }

    private bool ScheduleTimer(uint milliseconds)
    {
        if (_timerId != UIntPtr.Zero) _ = KillTimer(IntPtr.Zero, _timerId);
        _timerId = SetTimer(IntPtr.Zero, (UIntPtr)TimerIdentifier, milliseconds, IntPtr.Zero);
        if (_timerId != UIntPtr.Zero) return true;
        Console.Error.WriteLine("Could not schedule cursor shortcut handoff.");
        return false;
    }

    private static bool IsKeyDown(int generic, int left, int right)
    {
        return KeyIsDown(generic) || KeyIsDown(left) || KeyIsDown(right);
    }

    private static bool KeyIsDown(int key) => GetAsyncKeyState(key) < 0;

    private IntPtr ChooseTargetWindow(IntPtr gameWindow)
    {
        IntPtr[] recent;
        lock (_windowHistoryLock) recent = _recentWindows.ToArray();
        foreach (var window in recent)
        {
            if (window != gameWindow && IsSensibleWindow(window)) return window;
        }

        var selected = IntPtr.Zero;
        var callback = new EnumWindowsProc((window, _) =>
        {
            if (window == gameWindow || !IsSensibleWindow(window)) return true;
            selected = window;
            return false;
        });
        _ = EnumWindows(callback, IntPtr.Zero);
        return selected;
    }

    private bool IsSensibleWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window) || !IsWindowVisible(window) || IsIconic(window) || GetWindow(window, Program.GW_OWNER) != IntPtr.Zero)
            return false;

        var extendedStyle = GetWindowLongPtr(window, Program.GWL_EXSTYLE).ToInt64();
        if ((extendedStyle & (Program.WS_EX_TOOLWINDOW | Program.WS_EX_NOACTIVATE)) != 0) return false;

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == _helperPid || (_ownerPid > 0 && processId == (uint)_ownerPid)) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (process.HasExited || string.Equals(process.ProcessName, Program.TargetProcessName, StringComparison.OrdinalIgnoreCase)) return false;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }

        var className = new StringBuilder(128);
        _ = GetClassName(window, className, className.Capacity);
        return className.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    private bool ActivateTargetWindow(IntPtr target, IntPtr gameWindow)
    {
        if (!IsSensibleWindow(target)) return false;
        _ = ClipCursor(IntPtr.Zero);
        var foregroundThread = GetWindowThreadProcessId(gameWindow, out _);
        var targetThread = GetWindowThreadProcessId(target, out _);
        var attachedToHelper = foregroundThread != 0 && _threadId != 0 && AttachThreadInput(foregroundThread, _threadId, true);
        var attachedToTarget = foregroundThread != 0 && targetThread != 0 && AttachThreadInput(foregroundThread, targetThread, true);
        try
        {
            _ = BringWindowToTop(target);
            var activated = SetForegroundWindow(target);
            if (attachedToTarget)
            {
                _ = SetActiveWindow(target);
                _ = SetFocus(target);
            }
            _ = ClipCursor(IntPtr.Zero);
            return activated || Program.GetForegroundWindow() == target;
        }
        finally
        {
            if (attachedToTarget) _ = AttachThreadInput(foregroundThread, targetThread, false);
            if (attachedToHelper) _ = AttachThreadInput(foregroundThread, _threadId, false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0) _ = PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join();
        _started.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData { public uint VirtualKey; public uint ScanCode; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X; public int Y; public uint Private; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern UIntPtr SetTimer(IntPtr window, UIntPtr id, uint milliseconds, IntPtr callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr window, UIntPtr id);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetActiveWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint firstThread, uint secondThread, bool attach);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint minimum, uint maximum);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClipCursor(IntPtr rectangle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
