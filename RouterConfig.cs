using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace ScreenSoundRouter;

public class RouterConfig
{
    public bool AutoRoutingEnabled { get; set; } = true;
    public int PollIntervalMs { get; set; } = 600;
    public Dictionary<string, string> MonitorToAudioEndpoint { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> IgnoredProcesses { get; set; } = new();

    private static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenSoundRouter");

    public static string ConfigFilePath => Path.Combine(ConfigDir, "config.json");

    public static RouterConfig LoadOrCreateDefault(List<MonitorInfo> monitors, List<AudioDeviceInfo> audioDevices)
    {
        RouterConfig? config = null;
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                config = JsonSerializer.Deserialize<RouterConfig>(json);
            }
        }
        catch { }

        config ??= new RouterConfig();
        config.MonitorToAudioEndpoint = new Dictionary<string, string>(
            config.MonitorToAudioEndpoint ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        bool modified = false;

        // Auto-match any unconfigured monitor whose EDID friendly name matches an audio device's name
        foreach (var mon in monitors)
        {
            if (!config.MonitorToAudioEndpoint.TryGetValue(mon.GdiDeviceName, out string? currentId) ||
                string.IsNullOrWhiteSpace(currentId) ||
                !audioDevices.Any(d => d.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase)))
            {
                var matchedAudio = audioDevices.FirstOrDefault(d =>
                    (!string.IsNullOrWhiteSpace(mon.FriendlyName) &&
                     d.FriendlyName.Contains(mon.FriendlyName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(d.Description) &&
                     mon.FriendlyName.Contains(d.Description, StringComparison.OrdinalIgnoreCase)));

                if (matchedAudio != null)
                {
                    config.MonitorToAudioEndpoint[mon.GdiDeviceName] = matchedAudio.Id;
                    modified = true;
                }
            }
        }

        if (modified || !File.Exists(ConfigFilePath))
        {
            config.Save();
        }

        return config;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch { }
    }

    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRunName = "ScreenSoundRouter";

    public static bool IsStartWithWindowsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppRunName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartWithWindows(bool enable, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return;

            if (enable)
            {
                key.SetValue(AppRunName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(AppRunName, false);
            }
        }
        catch { }
    }
}
