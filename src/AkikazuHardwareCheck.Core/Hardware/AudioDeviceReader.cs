using System.Runtime.InteropServices;

namespace AkikazuHardwareCheck.Core.Hardware;

public sealed record AudioDevice(string Name, bool IsBluetooth);

public sealed record AudioCandidate(string Name, string? Bus, uint? FormFactor);

/// <summary>
/// Asks Windows Core Audio for the current playback and recording devices. Listing every endpoint would show ports
/// nothing is plugged into, such as a motherboard's digital output, which says nothing about whether sound can play.
/// A monitor's DisplayPort or HDMI audio does count: whether the monitor really has speakers cannot be determined, so the
/// speaker test settles it by ear.
/// </summary>
public static class AudioDeviceReader
{
    private const int DATA_FLOW_RENDER = 0;
    private const int DATA_FLOW_CAPTURE = 1;
    private const int ROLE_MULTIMEDIA = 1;
    private const uint DEVICE_STATE_ACTIVE = 1;
    private const ushort VT_LPWSTR = 31;
    private const ushort VT_UI4 = 19;

    // EndpointFormFactor values from mmdeviceapi.h.
    private const uint FORM_SPEAKERS = 1;
    private const uint FORM_LINE_LEVEL = 2;
    private const uint FORM_HEADPHONES = 3;
    private const uint FORM_MICROPHONE = 4;
    private const uint FORM_HEADSET = 5;
    private const uint FORM_HANDSET = 6;
    private const uint FORM_DISPLAY = 9;
    private const uint FORM_UNKNOWN = 10;

    private static readonly Guid EndpointPropertyCategory = new("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E");
    private const uint FORM_FACTOR_ID = 0;

    private static readonly Guid DevicePropertyCategory = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    private const uint FRIENDLY_NAME_ID = 14;
    private const uint ENUMERATOR_NAME_ID = 24;

    // What Windows calls an endpoint in the languages drivers commonly install under.
    private static readonly string[] GenericEndpointKinds =
    [
        "Speaker", "Headphone", "Headset", "Microphone", "Mic", "Line In", "扬声器", "耳机", "麦克风", "揚聲器", "喇叭", "耳機", "麥克風",
        "スピーカー", "ヘッドホン", "ヘッドフォン", "ヘッドセット", "マイク", "스피커", "헤드폰", "헤드셋", "마이크", "Lautsprecher", "Kopfhörer", "Mikrofon", "Haut-parleur", "Casque",
    ];

    // Buses real audio hardware sits on. Virtual cables and capture software register under ROOT or software buses instead.
    private static readonly HashSet<string> PhysicalBuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "HDAUDIO", "INTELAUDIO", "USB", "PCI", "ACPI", "BTHENUM", "BTHHFENUM", "BTHLEDEVICE",
    };

    /// <summary>
    /// Windows names an endpoint "Speakers (Realtek Audio)" in whatever language the driver was installed under, so a Chinese
    /// system can say スピーカー. The row label already says what it is, so only the name in brackets is kept.
    /// </summary>
    public static string CleanEndpointName(string name)
    {
        var open = name.IndexOf('(', StringComparison.Ordinal);
        if (open <= 0 || !name.EndsWith(')'))
        {
            return name;
        }

        var kind = name[..open].Trim();
        return GenericEndpointKinds.Any(generic => kind.StartsWith(generic, StringComparison.OrdinalIgnoreCase))
            ? name[(open + 1)..^1].Trim()
            : name;
    }

    public static AudioDevice? ReadSpeaker() => Read(DATA_FLOW_RENDER);

    public static AudioDevice? ReadMicrophone() => Read(DATA_FLOW_CAPTURE);

    public static bool IsPhysicalBus(string? bus) => bus is not null && PhysicalBuses.Contains(bus);

    public static bool IsBluetoothBus(string? bus) => bus?.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>A real output: plugged-in speakers or headphones, a monitor, or a USB or Bluetooth device. A bare digital port is not.</summary>
    public static bool IsRealOutput(AudioCandidate device) =>
        IsPhysicalBus(device.Bus)
        && (device.FormFactor is FORM_SPEAKERS or FORM_LINE_LEVEL or FORM_HEADPHONES or FORM_HEADSET or FORM_HANDSET or FORM_DISPLAY || IsUnknownExternal(device));

    /// <summary>A real input: a microphone or headset. Features such as stereo mix report an unknown type on the sound card itself.</summary>
    public static bool IsRealInput(AudioCandidate device) =>
        IsPhysicalBus(device.Bus)
        && (device.FormFactor is FORM_MICROPHONE or FORM_HEADSET or FORM_HANDSET || IsUnknownExternal(device));

    /// <summary>The default device when it qualifies, otherwise the first active one that does, otherwise none.</summary>
    public static AudioCandidate? Choose(AudioCandidate? defaultDevice, IEnumerable<AudioCandidate> active, Func<AudioCandidate, bool> qualifies) =>
        defaultDevice is not null && qualifies(defaultDevice) ? defaultDevice : active.FirstOrDefault(qualifies);

    private static bool IsUnknownExternal(AudioCandidate device) =>
        device.FormFactor == FORM_UNKNOWN && (IsBluetoothBus(device.Bus) || string.Equals(device.Bus, "USB", StringComparison.OrdinalIgnoreCase));

    private static AudioDevice? Read(int dataFlow)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            Func<AudioCandidate, bool> qualifies = dataFlow == DATA_FLOW_RENDER ? IsRealOutput : IsRealInput;
            var chosen = Choose(ReadDefault(enumerator, dataFlow), ReadActive(enumerator, dataFlow), qualifies);
            return chosen is null
                ? null
                : new AudioDevice(PeripheralCollector.CleanName(CleanEndpointName(chosen.Name)) ?? chosen.Name, IsBluetoothBus(chosen.Bus));
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static AudioCandidate? ReadDefault(IMMDeviceEnumerator enumerator, int dataFlow)
    {
        // A failure here only means no default device is set, which is a normal answer on a PC without audio.
        if (enumerator.GetDefaultAudioEndpoint(dataFlow, ROLE_MULTIMEDIA, out var device) != 0)
        {
            return null;
        }

        return Describe(device);
    }

    private static List<AudioCandidate> ReadActive(IMMDeviceEnumerator enumerator, int dataFlow)
    {
        var devices = new List<AudioCandidate>();
        if (enumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_ACTIVE, out var collection) != 0)
        {
            return devices;
        }

        try
        {
            collection.GetCount(out var count);
            for (uint index = 0; index < count; index++)
            {
                collection.Item(index, out var device);
                if (Describe(device) is { } description)
                {
                    devices.Add(description);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }

        return devices;
    }

    private static AudioCandidate? Describe(IMMDevice device)
    {
        try
        {
            if (device.OpenPropertyStore(0, out var store) != 0)
            {
                return null;
            }

            try
            {
                var name = ReadString(store, FRIENDLY_NAME_ID);
                return name is null ? null : new AudioCandidate(name, ReadString(store, ENUMERATOR_NAME_ID), ReadFormFactor(store));
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private static string? ReadString(IPropertyStore store, uint propertyId)
    {
        var key = new PropertyKey { Category = DevicePropertyCategory, Id = propertyId };
        if (store.GetValue(ref key, out var value) != 0)
        {
            return null;
        }

        try
        {
            return value.Type == VT_LPWSTR ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    private static uint? ReadFormFactor(IPropertyStore store)
    {
        var key = new PropertyKey { Category = EndpointPropertyCategory, Id = FORM_FACTOR_ID };
        if (store.GetValue(ref key, out var value) != 0)
        {
            return null;
        }

        try
        {
            return value.Type == VT_UI4 ? (uint)value.Pointer.ToInt64() : null;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropertyVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Category;
        public uint Id;
    }

    // Only the string member is read. The layout matches the 24-byte 64-bit PROPVARIANT.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public IntPtr Pointer;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    // Unused methods hold vtable slots in declaration order and are never called.
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, uint stateMask, [MarshalAs(UnmanagedType.Interface)] out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate();

        [PreserveSig]
        int OpenPropertyStore(int access, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount();

        void GetAt();

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropertyVariant value);
    }
}
