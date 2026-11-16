using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ScreenSoundRouter;

public record MonitorInfo(string GdiDeviceName, string FriendlyName, Rectangle Bounds, bool IsPrimary);
public record WindowMonitorInfo(IntPtr WindowHandle, uint ProcessId, string WindowTitle, string MonitorGdiName);

public static class MonitorDetector
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public int Area => Math.Max(0, Width) * Math.Max(0, Height);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    // DisplayConfig structs for querying EDID monitor friendly names
    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    public static List<MonitorInfo> GetMonitors()
    {
        var friendlyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(2, out uint numPaths, out uint numModes) == 0)
            {
                var paths = new DISPLAYCONFIG_PATH_INFO[numPaths];
                var modes = new DISPLAYCONFIG_MODE_INFO[numModes];
                if (QueryDisplayConfig(2, ref numPaths, paths, ref numModes, modes, IntPtr.Zero) == 0)
                {
                    for (int i = 0; i < numPaths; i++)
                    {
                        var srcName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                        srcName.header.type = 1;
                        srcName.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                        srcName.header.adapterId = paths[i].sourceInfo.adapterId;
                        srcName.header.id = paths[i].sourceInfo.id;
                        DisplayConfigGetDeviceInfo(ref srcName);

                        var targetName = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                        targetName.header.type = 2;
                        targetName.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                        targetName.header.adapterId = paths[i].targetInfo.adapterId;
                        targetName.header.id = paths[i].targetInfo.id;
                        DisplayConfigGetDeviceInfo(ref targetName);

                        if (!string.IsNullOrWhiteSpace(srcName.viewGdiDeviceName) &&
                            !string.IsNullOrWhiteSpace(targetName.monitorFriendlyDeviceName))
                        {
                            friendlyMap[srcName.viewGdiDeviceName] = targetName.monitorFriendlyDeviceName;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"QueryDisplayConfig error: {ex.Message}");
        }

        var monitors = new List<MonitorInfo>();
        foreach (var screen in Screen.AllScreens)
        {
            string gdi = screen.DeviceName;
            if (!friendlyMap.TryGetValue(gdi, out string? friendly) || string.IsNullOrWhiteSpace(friendly))
            {
                friendly = gdi.Replace(@"\\.\DISPLAY", "Display ");
            }
            monitors.Add(new MonitorInfo(gdi, friendly, screen.Bounds, screen.Primary));
        }

        return monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
    }

    private static Dictionary<uint, uint> GetParentProcessMap()
    {
        var map = new Dictionary<uint, uint>();
        try
        {
            IntPtr hSnapshot = CreateToolhelp32Snapshot(0x00000002, 0); // TH32CS_SNAPPROCESS
            if (hSnapshot != IntPtr.Zero && hSnapshot != new IntPtr(-1))
            {
                try
                {
                    PROCESSENTRY32 pe = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                    if (Process32First(hSnapshot, ref pe))
                    {
                        do
                        {
                            map[pe.th32ProcessID] = pe.th32ParentProcessID;
                        } while (Process32Next(hSnapshot, ref pe));
                    }
                }
                finally
                {
                    CloseHandle(hSnapshot);
                }
            }
        }
        catch { }
        return map;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    /// <summary>
    /// Maps visible application windows to the monitor they are currently on.
    /// Also resolves child audio processes (like Chrome/Edge/Steam/CEF audio utility processes) to their parent window's monitor.
    /// </summary>
    public static Dictionary<uint, WindowMonitorInfo> GetProcessMonitorMap()
    {
        var pidBestWindow = new Dictionary<uint, (IntPtr Hwnd, int Area, string Title, string MonitorGdi)>();

        EnumWindowsProc callback = (hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                return true;

            // Check DWMWA_CLOAKED (14) to ignore invisible background UWP/shell windows
            if (DwmGetWindowAttribute(hWnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            // Ignore tool windows unless they are sizeable app windows
            const int GWL_EXSTYLE = -20;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                return true;

            if (!GetWindowRect(hWnd, out RECT rect))
                return true;

            int area = rect.Area;
            if (area < 1000) // Ignore tiny 1x1 helper windows
                return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0)
                return true;

            IntPtr hMon = MonitorFromWindow(hWnd, 2); // MONITOR_DEFAULTTONEAREST = 2
            if (hMon == IntPtr.Zero)
                return true;

            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(hMon, ref mi))
                return true;

            int len = GetWindowTextLength(hWnd);
            string title = "";
            if (len > 0)
            {
                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                title = sb.ToString();
            }

            if (!pidBestWindow.TryGetValue(pid, out var existing) || area > existing.Area)
            {
                pidBestWindow[pid] = (hWnd, area, title, mi.szDevice);
            }

            return true;
        };

        // Use OpenDesktop("Default") first so it works even if launched from a background/sandboxed desktop
        IntPtr hDesk = OpenDesktop("Default", 0, false, 0x0001 | 0x0040);
        if (hDesk != IntPtr.Zero)
        {
            try
            {
                EnumDesktopWindows(hDesk, callback, IntPtr.Zero);
            }
            finally
            {
                CloseDesktop(hDesk);
            }
        }
        else
        {
            EnumWindows(callback, IntPtr.Zero);
        }

        var result = new Dictionary<uint, WindowMonitorInfo>();
        foreach (var kv in pidBestWindow)
        {
            result[kv.Key] = new WindowMonitorInfo(kv.Value.Hwnd, kv.Key, kv.Value.Title, kv.Value.MonitorGdi);
        }

        return result;
    }

    /// <summary>
    /// Finds the monitor for a given process ID, walking up the parent process tree or matching process name if the audio session runs in a child process.
    /// </summary>
    public static WindowMonitorInfo? ResolveMonitorForProcess(
        uint pid,
        string processName,
        Dictionary<uint, WindowMonitorInfo> windowMap,
        Dictionary<uint, uint>? parentMap = null)
    {
        if (windowMap.TryGetValue(pid, out var direct))
            return direct;

        parentMap ??= GetParentProcessMap();

        // Walk up the parent process chain (up to 5 levels) for multi-process apps (Chrome, Edge, Firefox, Electron, Steam games)
        uint current = pid;
        for (int depth = 0; depth < 5; depth++)
        {
            if (!parentMap.TryGetValue(current, out uint parentPid) || parentPid == 0 || parentPid == current)
                break;

            if (windowMap.TryGetValue(parentPid, out var parentWin))
                return parentWin;

            current = parentPid;
        }

        // Fallback: if another process with the exact same executable name has a visible window, use its monitor
        if (!string.IsNullOrWhiteSpace(processName))
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (windowMap.TryGetValue((uint)p.Id, out var sameNameWin))
                        return sameNameWin;
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        return null;
    }
}
