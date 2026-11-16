using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenSoundRouter;

public record AudioDeviceInfo(string Id, string FriendlyName, string Description);
public record AudioSessionInfo(uint ProcessId, string ProcessName, string EndpointId, int State);

public static class AudioRouter
{
    private const string DEVINTERFACE_AUDIO_RENDER = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string MMDEVAPI_TOKEN = @"\\?\SWD#MMDEVAPI#";
    private const string AudioPolicyConfigClassId = "Windows.Media.Internal.AudioPolicyConfig";

    // Windows 10 21H2+ / Windows 11 IID
    private static readonly Guid IID_IAudioPolicyConfig_21H2 = new("ab3d4648-e242-459f-b02f-541c70306324");
    // Older Windows 10 IID
    private static readonly Guid IID_IAudioPolicyConfig_Downlevel = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, uint length, out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetPersistedDefaultAudioEndpointDelegate(IntPtr thisPtr, uint processId, int flow, int role, IntPtr deviceId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPersistedDefaultAudioEndpointDelegate(IntPtr thisPtr, uint processId, int flow, int role, out IntPtr deviceId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ClearAllPersistedApplicationDefaultEndpointsDelegate(IntPtr thisPtr);

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out uint pcDevices);
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out uint pdwState);
    }

    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    }

    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(IntPtr AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);
        int GetSimpleAudioVolume(IntPtr AudioSessionGuid, uint StreamFlags, out IntPtr AudioVolume);
        int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
    }

    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int SessionCount);
        int GetSession(int SessionCount, out IAudioSessionControl Session);
    }

    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl { }

    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        int GetState(out int pRetVal);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        int GetGroupingParam(out Guid pRetVal);
        int SetGroupingParam(ref Guid Override, ref Guid EventContext);
        int RegisterAudioSessionNotification(IntPtr NewNotifications);
        int UnregisterAudioSessionNotification(IntPtr NewNotifications);
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        int GetProcessId(out uint pRetVal);
        int IsSystemSoundsSession();
        int SetDuckingPreference(bool optOut);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pwszVal;
        public IntPtr pwszVal2;
    }

    public static List<AudioDeviceInfo> GetRenderDevices()
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.EnumAudioEndpoints(0, 1, out var devices); // eRender = 0, DEVICE_STATE_ACTIVE = 1
            devices.GetCount(out uint count);

            var pkeyFriendlyName = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
            var pkeyDeviceDesc = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 2 };

            for (uint i = 0; i < count; i++)
            {
                devices.Item(i, out var dev);
                dev.GetId(out string devId);
                dev.OpenPropertyStore(0, out var props);

                string friendlyName = devId;
                string desc = "";

                if (props != null)
                {
                    props.GetValue(ref pkeyFriendlyName, out var pvName);
                    props.GetValue(ref pkeyDeviceDesc, out var pvDesc);

                    if (pvName.vt == 31 && pvName.pwszVal != IntPtr.Zero)
                        friendlyName = Marshal.PtrToStringUni(pvName.pwszVal) ?? devId;
                    if (pvDesc.vt == 31 && pvDesc.pwszVal != IntPtr.Zero)
                        desc = Marshal.PtrToStringUni(pvDesc.pwszVal) ?? "";
                }

                result.Add(new AudioDeviceInfo(devId, friendlyName, desc));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetRenderDevices error: {ex.Message}");
        }
        return result;
    }

    public static List<AudioSessionInfo> GetAudioSessions()
    {
        var sessions = new List<AudioSessionInfo>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.EnumAudioEndpoints(0, 1, out var devices);
            devices.GetCount(out uint count);
            Guid iidSessionMgr2 = typeof(IAudioSessionManager2).GUID;

            var seenPids = new HashSet<uint>();

            for (uint i = 0; i < count; i++)
            {
                devices.Item(i, out var dev);
                dev.GetId(out string devId);
                dev.Activate(ref iidSessionMgr2, 23, IntPtr.Zero, out object objMgr);
                if (objMgr is not IAudioSessionManager2 mgr) continue;

                mgr.GetSessionEnumerator(out var sessionEnum);
                sessionEnum.GetCount(out int sCount);

                for (int s = 0; s < sCount; s++)
                {
                    sessionEnum.GetSession(s, out var ctrl);
                    if (ctrl is not IAudioSessionControl2 ctrl2) continue;

                    ctrl2.GetProcessId(out uint pid);
                    if (pid == 0) continue; // Ignore system sounds

                    ctrl2.GetState(out int state);
                    // AudioSessionStateActive = 1, AudioSessionStateInactive = 0, AudioSessionStateExpired = 2
                    if (state == 2) continue;

                    string procName = "";
                    try
                    {
                        using var p = Process.GetProcessById((int)pid);
                        procName = p.ProcessName;
                    }
                    catch
                    {
                        continue; // Process exited
                    }

                    // Prefer active session entry if a process has an inactive leftover on a previous endpoint
                    if (state == 1 || !seenPids.Contains(pid))
                    {
                        sessions.RemoveAll(x => x.ProcessId == pid);
                        sessions.Add(new AudioSessionInfo(pid, procName, devId, state));
                        seenPids.Add(pid);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetAudioSessions error: {ex.Message}");
        }
        return sessions;
    }

    private static bool TryGetFactory(out IntPtr factory, out int setSlot, out int getSlot, out int clearSlot)
    {
        RoInitialize(1);
        WindowsCreateString(AudioPolicyConfigClassId, (uint)AudioPolicyConfigClassId.Length, out IntPtr hClassId);
        try
        {
            Guid iid21H2 = IID_IAudioPolicyConfig_21H2;
            if (RoGetActivationFactory(hClassId, ref iid21H2, out factory) == 0 && factory != IntPtr.Zero)
            {
                setSlot = 25;
                getSlot = 26;
                clearSlot = 27;
                return true;
            }

            Guid iidDownlevel = IID_IAudioPolicyConfig_Downlevel;
            if (RoGetActivationFactory(hClassId, ref iidDownlevel, out factory) == 0 && factory != IntPtr.Zero)
            {
                setSlot = 23;
                getSlot = 24;
                clearSlot = 25;
                return true;
            }
        }
        finally
        {
            if (hClassId != IntPtr.Zero)
                WindowsDeleteString(hClassId);
        }

        factory = IntPtr.Zero;
        setSlot = getSlot = clearSlot = 0;
        return false;
    }

    public static string? GetProcessAudioEndpoint(uint processId)
    {
        if (!TryGetFactory(out IntPtr factory, out _, out int getSlot, out _))
            return null;

        try
        {
            IntPtr vtable = Marshal.ReadIntPtr(factory);
            IntPtr fnGetPtr = Marshal.ReadIntPtr(vtable, getSlot * IntPtr.Size);
            var getFn = Marshal.GetDelegateForFunctionPointer<GetPersistedDefaultAudioEndpointDelegate>(fnGetPtr);

            if (getFn(factory, processId, 0, 1, out IntPtr outDevHString) == 0 && outDevHString != IntPtr.Zero)
            {
                try
                {
                    IntPtr rawBuf = WindowsGetStringRawBuffer(outDevHString, out uint len);
                    if (rawBuf != IntPtr.Zero && len > 0)
                    {
                        string unpacked = Marshal.PtrToStringUni(rawBuf, (int)len) ?? "";
                        if (unpacked.StartsWith(MMDEVAPI_TOKEN, StringComparison.OrdinalIgnoreCase))
                            unpacked = unpacked.Substring(MMDEVAPI_TOKEN.Length);
                        if (unpacked.EndsWith(DEVINTERFACE_AUDIO_RENDER, StringComparison.OrdinalIgnoreCase))
                            unpacked = unpacked.Substring(0, unpacked.Length - DEVINTERFACE_AUDIO_RENDER.Length);
                        return unpacked;
                    }
                }
                finally
                {
                    WindowsDeleteString(outDevHString);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetProcessAudioEndpoint error: {ex.Message}");
        }
        finally
        {
            Marshal.Release(factory);
        }
        return null;
    }

    public static bool SetProcessAudioEndpoint(uint processId, string? endpointId)
    {
        if (!TryGetFactory(out IntPtr factory, out int setSlot, out _, out _))
            return false;

        IntPtr hDevId = IntPtr.Zero;
        try
        {
            if (!string.IsNullOrWhiteSpace(endpointId))
            {
                string fullId = $"{MMDEVAPI_TOKEN}{endpointId}{DEVINTERFACE_AUDIO_RENDER}";
                WindowsCreateString(fullId, (uint)fullId.Length, out hDevId);
            }

            IntPtr vtable = Marshal.ReadIntPtr(factory);
            IntPtr fnSetPtr = Marshal.ReadIntPtr(vtable, setSlot * IntPtr.Size);
            var setFn = Marshal.GetDelegateForFunctionPointer<SetPersistedDefaultAudioEndpointDelegate>(fnSetPtr);

            // Set for eConsole (0), eMultimedia (1), and eCommunications (2)
            int hr0 = setFn(factory, processId, 0, 0, hDevId);
            int hr1 = setFn(factory, processId, 0, 1, hDevId);
            int hr2 = setFn(factory, processId, 0, 2, hDevId);

            return hr0 == 0 && hr1 == 0 && hr2 == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetProcessAudioEndpoint error: {ex.Message}");
            return false;
        }
        finally
        {
            if (hDevId != IntPtr.Zero)
                WindowsDeleteString(hDevId);
            Marshal.Release(factory);
        }
    }

    public static bool ClearAllPersistedAudioEndpoints()
    {
        if (!TryGetFactory(out IntPtr factory, out _, out _, out int clearSlot))
            return false;

        try
        {
            IntPtr vtable = Marshal.ReadIntPtr(factory);
            IntPtr fnClearPtr = Marshal.ReadIntPtr(vtable, clearSlot * IntPtr.Size);
            var clearFn = Marshal.GetDelegateForFunctionPointer<ClearAllPersistedApplicationDefaultEndpointsDelegate>(fnClearPtr);
            return clearFn(factory) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.Release(factory);
        }
    }
}
