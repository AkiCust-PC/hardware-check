using System.Buffers.Binary;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace AkikazuHardwareCheck.Core.Hardware;

public sealed record CpuIdInfo(
    string Vendor,
    string Signature,
    int? BaseClockMhz,
    int? MaxClockMhz,
    int? BusClockMhz,
    IReadOnlyList<string> Features);

/// <summary>
/// CPUID gives the processor identity and instruction-set flags that WMI does not expose. It is x86/x64 only;
/// ARM64 systems simply have no CPUID data.
/// </summary>
public static class CpuIdReader
{
    public static CpuIdInfo? Read()
    {
        if (!X86Base.IsSupported)
        {
            return null;
        }

        var leaf0 = X86Base.CpuId(0, 0);
        var leaf1 = X86Base.CpuId(1, 0);
        var features = ReadFeatures(leaf0.Eax, leaf1);
        var signature = Signature(leaf1.Eax);
        var (baseClock, maxClock, busClock) = leaf0.Eax >= 0x16
            ? ReadClocks(X86Base.CpuId(0x16, 0))
            : (null, null, null);
        return new CpuIdInfo(Vendor(leaf0), signature, baseClock, maxClock, busClock, features);
    }

    private static string Vendor((int Eax, int Ebx, int Ecx, int Edx) leaf)
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), leaf.Ebx);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), leaf.Edx);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), leaf.Ecx);
        return Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
    }

    private static string Signature(int eax)
    {
        var stepping = eax & 0xF;
        var family = (eax >> 8) & 0xF;
        var model = (eax >> 4) & 0xF;
        if (family == 0xF)
        {
            family += (eax >> 20) & 0xFF;
        }

        if (family is 0x6 or 0xF)
        {
            model += ((eax >> 16) & 0xF) << 4;
        }

        return $"Family {family} Model {model} Stepping {stepping}";
    }

    private static (int? BaseClock, int? MaxClock, int? BusClock) ReadClocks((int Eax, int Ebx, int Ecx, int Edx) leaf) =>
        (Positive(leaf.Eax), Positive(leaf.Ebx), Positive(leaf.Ecx));

    private static int? Positive(int value) => value > 0 ? value : null;

    private static List<string> ReadFeatures(int maxBasicLeaf, (int Eax, int Ebx, int Ecx, int Edx) leaf1)
    {
        var features = new List<string>();
        Add(leaf1.Edx, 25, "SSE");
        Add(leaf1.Edx, 26, "SSE2");
        Add(leaf1.Ecx, 0, "SSE3");
        Add(leaf1.Ecx, 9, "SSSE3");
        Add(leaf1.Ecx, 12, "FMA");
        Add(leaf1.Ecx, 19, "SSE4.1");
        Add(leaf1.Ecx, 20, "SSE4.2");
        Add(leaf1.Ecx, 23, "POPCNT");
        Add(leaf1.Ecx, 25, "AES");
        Add(leaf1.Ecx, 27, "OSXSAVE");
        Add(leaf1.Ecx, 28, "AVX");
        Add(leaf1.Ecx, 29, "F16C");
        Add(leaf1.Ecx, 30, "RDRAND");

        if (maxBasicLeaf >= 7)
        {
            var leaf7 = X86Base.CpuId(7, 0);
            Add(leaf7.Ebx, 3, "BMI1");
            Add(leaf7.Ebx, 5, "AVX2");
            Add(leaf7.Ebx, 8, "BMI2");
            Add(leaf7.Ebx, 16, "AVX-512F");
            Add(leaf7.Ebx, 17, "AVX-512DQ");
            Add(leaf7.Ebx, 18, "RDSEED");
            Add(leaf7.Ebx, 19, "ADX");
            Add(leaf7.Ebx, 29, "SHA");
            Add(leaf7.Ebx, 30, "AVX-512BW");
            Add(leaf7.Ebx, 31, "AVX-512VL");
        }

        return features;

        void Add(int value, int bit, string name)
        {
            if ((value & (1 << bit)) != 0)
            {
                features.Add(name);
            }
        }
    }
}
