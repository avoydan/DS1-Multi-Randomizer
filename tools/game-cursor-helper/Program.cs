using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    private const uint CursorShowing = 0x00000001;
    private const int PollMilliseconds = 40;
    private const string TargetProcessName = "DarkSoulsRemastered";

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
        {
            if (!OperatingSystem.IsWindows()) return 1;
            return LoadLibrary("user32.dll") != IntPtr.Zero ? 0 : 1;
        }
        if (!OperatingSystem.IsWindows()) return 2;

        using var stop = new CancellationTokenSource();
        var input = ReadStopCommand(stop);
        try
        {
            await WatchAsync(stop.Token);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
        finally
        {
            stop.Cancel();
            try { await input; } catch { /* stdin can close while the watcher is stopping */ }
        }
    }

    private static async Task ReadStopCommand(CancellationTokenSource stop)
    {
        try
        {
            while (await Console.In.ReadLineAsync(stop.Token) is { } line)
            {
                if (string.Equals(line.Trim(), "stop", StringComparison.OrdinalIgnoreCase))
                {
                    stop.Cancel();
                    return;
                }
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { return; }
        stop.Cancel(); // EOF means the owning launcher exited; do not leave an orphan watcher.
    }

    private static async Task WatchAsync(CancellationToken token)
    {
        var helperReleasedClip = false;
        var wasGameForeground = false;
        var lastGameWindow = IntPtr.Zero;
        while (!token.IsCancellationRequested)
        {
            var foreground = GetForegroundWindow();
            var foregroundIsGame = IsDsrWindow(foreground, requireVisible: true);

            if (foregroundIsGame)
            {
                wasGameForeground = true;
                lastGameWindow = foreground;
                if (TryGetCursorVisible(out var visible) && visible)
                {
                    // Recheck immediately before changing the global clip rectangle.
                    if (GetForegroundWindow() == foreground && IsDsrWindow(foreground, requireVisible: true)
                        && TryGetCursorVisible(out var stillVisible) && stillVisible)
                    {
                        _ = ClipCursor(IntPtr.Zero);
                        helperReleasedClip = true;
                    }
                }
            }
            else if (wasGameForeground && !foregroundIsGame)
            {
                // ClipCursor is global. Release once on a verified DSR -> other-window transition, even if the
                // cursor was hidden during gameplay. The foreground recheck narrows the unavoidable global race.
                if (lastGameWindow != IntPtr.Zero && GetForegroundWindow() == foreground
                    && IsDsrWindow(lastGameWindow, requireVisible: false) && !IsDsrWindow(foreground, requireVisible: false))
                    _ = ClipCursor(IntPtr.Zero);
                helperReleasedClip = false;
                wasGameForeground = false;
                lastGameWindow = IntPtr.Zero;
            }

            try { await Task.Delay(PollMilliseconds, token); }
            catch (OperationCanceledException) { break; }
        }
        var finalForeground = GetForegroundWindow();
        if (wasGameForeground && lastGameWindow != IntPtr.Zero && GetForegroundWindow() == finalForeground
            && IsDsrWindow(lastGameWindow, requireVisible: false) && !IsDsrWindow(finalForeground, requireVisible: false))
        {
            _ = ClipCursor(IntPtr.Zero);
        }
        else if (helperReleasedClip && IsDsrWindow(finalForeground, requireVisible: true))
        {
            if (GetForegroundWindow() == finalForeground) _ = ClipCursor(IntPtr.Zero);
        }
    }

    private static bool IsDsrWindow(IntPtr window, bool requireVisible)
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
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool TryGetCursorVisible(out bool visible)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        visible = false;
        if (!GetCursorInfo(ref info)) return false;
        visible = (info.Flags & CursorShowing) != 0;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Cursor;
        public Point Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClipCursor(IntPtr rectangle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string name);
}
