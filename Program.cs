using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ScreenSoundRouter;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--status", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("--sync-once", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            bool attached = Console.IsOutputRedirected || AttachConsole(ATTACH_PARENT_PROCESS);
            try
            {
                return RunCli(args);
            }
            finally
            {
                Console.Out.Flush();
                if (attached && !Console.IsOutputRedirected)
                    FreeConsole();
            }
        }

        using var mutex = new Mutex(true, "Global\\ScreenSoundRouter_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            return 0;
        }

        ApplicationConfiguration.Initialize();
        var engine = new RoutingEngine();
        bool startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                                            a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        Application.Run(new MainForm(engine, startMinimized));
        return 0;
    }

    private static int RunCli(string[] args)
    {
        var engine = new RoutingEngine();

        if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("ScreenSoundRouter - Automatic Per-Monitor Audio Router for Windows");
            Console.WriteLine("Usage:");
            Console.WriteLine("  ScreenSoundRouter.exe              Launch GUI and background tray router");
            Console.WriteLine("  ScreenSoundRouter.exe --minimized  Launch directly to the system tray");
            Console.WriteLine("  ScreenSoundRouter.exe --status     Print detected monitors, audio devices, and active sessions");
            Console.WriteLine("  ScreenSoundRouter.exe --sync-once  Run one routing pass and exit");
            return 0;
        }

        engine.OnLogMessage += Console.WriteLine;
        engine.Tick(forceReapply: true);

        Console.WriteLine("=== Detected Monitors ===");
        foreach (var mon in engine.Monitors)
        {
            engine.Config.MonitorToAudioEndpoint.TryGetValue(mon.GdiDeviceName, out string? epId);
            var epName = engine.AudioDevices.FirstOrDefault(d => d.Id.Equals(epId, StringComparison.OrdinalIgnoreCase))?.FriendlyName ?? "(Unmapped)";
            Console.WriteLine($"  {mon.GdiDeviceName} | {mon.FriendlyName} ({mon.Bounds.Width}x{mon.Bounds.Height}) -> {epName}");
        }

        Console.WriteLine("\n=== Active Audio Sessions & Screen Routing ===");
        foreach (var st in engine.CurrentStatuses)
        {
            Console.WriteLine($"  PID {st.ProcessId,-6} | {st.ProcessName,-20} | {st.MonitorFriendlyName,-28} -> {st.AudioEndpointName}");
        }

        return 0;
    }
}
