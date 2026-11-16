# ScreenSoundRouter 🔊🖥️

**Automatically route each application or game's audio output to the speakers of whichever monitor its window is currently on.**

Perfect for dual-monitor / multi-monitor setups where:
- Your child or friend is playing a game on **Monitor 2** (with sound coming out of Monitor 2's speakers), while
- You watch a video, browse, or play a different game on **Monitor 1** (with your sound coming out of Monitor 1's speakers or headphones).
- Whenever you drag a game, browser, or media player window from one screen to another, **ScreenSoundRouter** detects the move in real time and switches that app's Windows audio output device automatically!

---

## ✨ Features

- **Zero-Config Auto-Matching:** Queries Windows DisplayConfig (EDID) monitor names and automatically matches each display to its corresponding HDMI / DisplayPort / NVIDIA / AMD audio output device on first launch.
- **Real-Time Per-App Audio Routing:** Polls active WASAPI audio sessions and visible windows (`MonitorFromWindow`), routing each process's audio stream dynamically using Windows' per-application audio policy engine (`IAudioPolicyConfig`).
- **Multi-Process Aware:** Automatically resolves child audio utility processes (used by Chrome, Edge, Firefox, Electron, and Steam games) back to their parent application window so browser tabs and games route seamlessly.
- **System Tray GUI + Dark Mode Dashboard:**
  - View and customize which audio output device belongs to each monitor (`\\.\DISPLAY1`, `\\.\DISPLAY2`, etc.).
  - Live table showing every audio-playing application, its PID, window title, current monitor, and routed speaker output.
  - Real-time activity log and one-click **"Reset All Apps to Windows Default"** button.
  - Optional **"Start Automatically with Windows (Tray)"** toggle.
- **Command-Line Interface (CLI):** Supports `--status`, `--sync-once`, and `--minimized` flags for scripting or headless use.

---

## 🚀 Quick Start

### Option 1: Download Prebuilt Release
1. Download `ScreenSoundRouter.exe` from the [**Releases**](../../releases) page.
2. Run `ScreenSoundRouter.exe`.
3. Verify your **Monitor → Audio Output Device** dropdown mappings in the main window (they are auto-matched by monitor name on first run).
4. Drag any game or browser window between your monitors—its audio will follow the window automatically!

### Option 2: Build from Source
Requires the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows 10 (1803+) or Windows 11.

```powershell
git clone https://github.com/christiand0797/ScreenSoundRouter.git
cd ScreenSoundRouter
dotnet build -c Release
.\bin\Release\net8.0-windows\ScreenSoundRouter.exe
```

To build a standalone single-file executable:
```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

---

## 🖥️ CLI Usage

```text
ScreenSoundRouter.exe              Launch GUI and background system tray router
ScreenSoundRouter.exe --minimized  Launch directly to the system tray (silent startup)
ScreenSoundRouter.exe --status     Print detected monitors, audio devices, and active sessions
ScreenSoundRouter.exe --sync-once  Perform a single routing pass and exit
```

### Example `--status` Output
```text
=== Detected Monitors ===
  \\.\DISPLAY1 | M492i-B2 (1536x864) -> M492i-B2 (NVIDIA High Definition Audio)
  \\.\DISPLAY2 | D24f-J09 (1920x1080) -> D24f-J09 (NVIDIA High Definition Audio)

=== Active Audio Sessions & Screen Routing ===
  PID 13356  | mc3                  | D24f-J09 (Display 2)         -> D24f-J09 (NVIDIA High Definition Audio)
```

---

## 💡 Tips for Playing a Game on One Monitor While Using Another

1. **Use Borderless Windowed Mode:** Set your game's display mode to **Borderless Windowed** (or Windowed) so it doesn't minimize when you click onto your other monitor. Move windows quickly between screens using `Win + Shift + Left/Right Arrow`.
2. **Enable Background Audio in the Game:** Some games mute their audio when their window is unfocused. Check the game's Audio Settings for **"Mute Audio in Background"** / **"Play Audio When in Background"** so your son's game keeps playing audio when you click on your browser on the other monitor.
3. **Controller Input on Second Monitor:** If your son is playing with a controller on Monitor 2 while you use mouse & keyboard on Monitor 1, most controller-friendly games (or tools like *ProtoInput* / *Nucleus Co-Op* / *Special K*) let the game continue receiving gamepad input in the background.

---

## ⚙️ Configuration

Settings are automatically saved to:
```text
%APPDATA%\ScreenSoundRouter\config.json
```

Example `config.json`:
```json
{
  "AutoRoutingEnabled": true,
  "PollIntervalMs": 600,
  "MonitorToAudioEndpoint": {
    "\\\\.\\DISPLAY1": "{0.0.0.00000000}.{515254af-db07-46dc-96e4-2543f2bf670b}",
    "\\\\.\\DISPLAY2": "{0.0.0.00000000}.{211c1f35-bad3-4bf7-bc5f-3e8fcb2e1c04}"
  },
  "IgnoredProcesses": []
}
```

---

## 📄 License

MIT License — see [LICENSE](LICENSE) for details.
