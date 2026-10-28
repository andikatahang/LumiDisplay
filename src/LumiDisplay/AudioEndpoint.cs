using System;
using System.Runtime.InteropServices;

namespace LumiDisplay;

/// <summary>
/// Minimal Core Audio (MMDevice API) access to Windows' default playback device.
///
/// - <see cref="DefaultOutputIsDisplay"/>: for "Forward to system audio" — when sound plays through
///   headphones or laptop speakers, the volume keys are left to Windows.
/// - Volume / mute (IAudioEndpointVolume): the volume keys change the Windows volume, so the
///   taskbar slider and per-app mixer always match what you hear. The monitor's own hardware
///   speaker level (DDC/CI) is a separate stage, set in Monitors &amp; Test.
/// </summary>
internal static class AudioEndpoint
{
    /// <summary>Windows volume of the default playback device, 0–100, or null if unavailable.</summary>
    public static double? GetVolumePercent() =>
        // Rounded so float noise (0.7f → 69.99999) can't push a grid step to the wrong mark
        WithEndpointVolume(v => v.GetMasterVolumeLevelScalar(out float level) == 0 ? Math.Round(level * 100.0, 2) : (double?)null);

    public static bool SetVolumePercent(double percent) =>
        WithEndpointVolume(v => v.SetMasterVolumeLevelScalar((float)Math.Clamp(percent / 100.0, 0, 1), IntPtr.Zero) == 0);

    public static bool? GetMute() =>
        WithEndpointVolume(v => v.GetMute(out bool muted) == 0 ? muted : (bool?)null);

    public static bool SetMute(bool muted) =>
        WithEndpointVolume(v => v.SetMute(muted, IntPtr.Zero) == 0);

    private static T? WithEndpointVolume<T>(Func<IAudioEndpointVolume, T> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? endpoint = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device) != 0 || device == null)
                return default;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out endpoint) != 0 || endpoint is not IAudioEndpointVolume volume)
                return default;
            return action(volume);
        }
        catch
        {
            return default;
        }
        finally
        {
            if (endpoint != null) Marshal.ReleaseComObject(endpoint);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    // EndpointFormFactor.DigitalAudioDisplayDevice
    private const uint FormFactorDisplay = 9;

    private static readonly Guid FormFactorFmtId = new("1da5d803-d492-4edd-8c23-e0c0ffee7f0e");

    /// <summary>
    /// True when the default render endpoint is a display device. Returns true when the
    /// query fails, so LumiDisplay keeps handling the keys as it did before.
    /// </summary>
    public static bool DefaultOutputIsDisplay()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IPropertyStore? store = null;
        var value = new PROPVARIANT();
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device) != 0 || device == null)
                return true;
            if (device.OpenPropertyStore(STGM_READ, out store) != 0 || store == null)
                return true;
            // PKEY_AudioEndpoint_FormFactor; a local copy, so the marshaller passes a stack address
            var key = new PROPERTYKEY { fmtid = FormFactorFmtId, pid = 0 };
            if (store.GetValue(ref key, out value) != 0 || value.vt != VT_UI4)
                return true;
            return value.uintVal == FormFactorDisplay;
        }
        catch
        {
            return true;
        }
        finally
        {
            PropVariantClear(ref value);
            if (store != null) Marshal.ReleaseComObject(store);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    // --- COM interop ---

    private const int STGM_READ = 0;
    private const ushort VT_UI4 = 19;
    private const int CLSCTX_ALL = 0x17;

    private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint uintVal;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
    }

    // Methods in vtable order; only the ones LumiDisplay calls have real signatures that matter
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, IntPtr eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, IntPtr eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(IntPtr eventContext);
        [PreserveSig] int VolumeStepDown(IntPtr eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }
}
