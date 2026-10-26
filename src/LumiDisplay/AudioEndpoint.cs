using System;
using System.Runtime.InteropServices;

namespace LumiDisplay;

/// <summary>
/// Minimal Core Audio (MMDevice API) query: is Windows' default playback device a display
/// (HDMI / DisplayPort monitor audio)?
///
/// Used for "Forward to system audio": when sound plays through headphones or laptop
/// speakers, the volume keys belong to Windows; when it plays through a monitor, LumiDisplay
/// drives the monitor's own speaker volume over DDC/CI.
/// </summary>
internal static class AudioEndpoint
{
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
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }
}
