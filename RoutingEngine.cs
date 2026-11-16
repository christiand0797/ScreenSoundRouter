using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ScreenSoundRouter;

public record RoutedAppStatus(
    uint ProcessId,
    string ProcessName,
    string WindowTitle,
    string MonitorGdi,
    string MonitorFriendlyName,
    string AudioEndpointId,
    string AudioEndpointName,
    bool IsActiveAudio);

public class RoutingEngine
{
    public RouterConfig Config { get; private set; }
    public List<MonitorInfo> Monitors { get; private set; } = new();
    public List<AudioDeviceInfo> AudioDevices { get; private set; } = new();
    public List<RoutedAppStatus> CurrentStatuses { get; private set; } = new();

    private readonly Dictionary<uint, string> _lastAssignedEndpointByPid = new();

    public event Action<string>? OnLogMessage;
    public event Action? OnStatusUpdated;

    public RoutingEngine()
    {
        RefreshHardware();
        Config = RouterConfig.LoadOrCreateDefault(Monitors, AudioDevices);
    }

    public void RefreshHardware()
    {
        Monitors = MonitorDetector.GetMonitors();
        AudioDevices = AudioRouter.GetRenderDevices();
        if (Config != null)
        {
            // Ensure any newly connected monitor gets auto-matched if possible
            bool changed = false;
            foreach (var mon in Monitors)
            {
                if (!Config.MonitorToAudioEndpoint.TryGetValue(mon.GdiDeviceName, out string? cur) ||
                    string.IsNullOrWhiteSpace(cur))
                {
                    var matched = AudioDevices.FirstOrDefault(d =>
                        (!string.IsNullOrWhiteSpace(mon.FriendlyName) &&
                         d.FriendlyName.Contains(mon.FriendlyName, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(d.Description) &&
                         mon.FriendlyName.Contains(d.Description, StringComparison.OrdinalIgnoreCase)));
                    if (matched != null)
                    {
                        Config.MonitorToAudioEndpoint[mon.GdiDeviceName] = matched.Id;
                        changed = true;
                    }
                }
            }
            if (changed)
                Config.Save();
        }
    }

    public void Tick(bool forceReapply = false)
    {
        try
        {
            var sessions = AudioRouter.GetAudioSessions();
            var windowMap = MonitorDetector.GetProcessMonitorMap();

            var monitorByGdi = Monitors.ToDictionary(m => m.GdiDeviceName, StringComparer.OrdinalIgnoreCase);
            var audioById = AudioDevices.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

            var newStatuses = new List<RoutedAppStatus>();
            var activePids = new HashSet<uint>();

            foreach (var session in sessions)
            {
                activePids.Add(session.ProcessId);

                if (Config.IgnoredProcesses.Any(p => p.Equals(session.ProcessName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var winInfo = MonitorDetector.ResolveMonitorForProcess(session.ProcessId, session.ProcessName, windowMap);
                if (winInfo == null)
                    continue;

                string monitorGdi = winInfo.MonitorGdiName;
                string monitorName = monitorByGdi.TryGetValue(monitorGdi, out var mInfo)
                    ? $"{mInfo.FriendlyName} ({mInfo.GdiDeviceName.Replace(@"\\.\DISPLAY", "Display ")})"
                    : monitorGdi;

                if (!Config.MonitorToAudioEndpoint.TryGetValue(monitorGdi, out string? targetEndpointId) ||
                    string.IsNullOrWhiteSpace(targetEndpointId))
                {
                    continue;
                }

                string targetAudioName = audioById.TryGetValue(targetEndpointId, out var aInfo)
                    ? aInfo.FriendlyName
                    : targetEndpointId;

                if (Config.AutoRoutingEnabled)
                {
                    string? persistedEndpoint = AudioRouter.GetProcessAudioEndpoint(session.ProcessId);
                    bool needsSwitch = forceReapply ||
                        !string.Equals(persistedEndpoint, targetEndpointId, StringComparison.OrdinalIgnoreCase) ||
                        (!_lastAssignedEndpointByPid.TryGetValue(session.ProcessId, out string? lastId) ||
                         !string.Equals(lastId, targetEndpointId, StringComparison.OrdinalIgnoreCase));

                    if (needsSwitch)
                    {
                        if (AudioRouter.SetProcessAudioEndpoint(session.ProcessId, targetEndpointId))
                        {
                            _lastAssignedEndpointByPid[session.ProcessId] = targetEndpointId;
                            Log($"Routed '{session.ProcessName}' (PID {session.ProcessId}) -> {targetAudioName} [{monitorName}]");
                        }
                    }
                }

                newStatuses.Add(new RoutedAppStatus(
                    session.ProcessId,
                    session.ProcessName,
                    string.IsNullOrWhiteSpace(winInfo.WindowTitle) ? session.ProcessName : winInfo.WindowTitle,
                    monitorGdi,
                    monitorName,
                    targetEndpointId,
                    targetAudioName,
                    session.State == 1));
            }

            // Clean up exited PIDs from cache
            var stalePids = _lastAssignedEndpointByPid.Keys.Where(k => !activePids.Contains(k)).ToList();
            foreach (var stale in stalePids)
                _lastAssignedEndpointByPid.Remove(stale);

            CurrentStatuses = newStatuses.OrderByDescending(s => s.IsActiveAudio).ThenBy(s => s.ProcessName).ToList();
            OnStatusUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RoutingEngine Tick error: {ex.Message}");
        }
    }

    public void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        OnLogMessage?.Invoke(line);
    }
}
