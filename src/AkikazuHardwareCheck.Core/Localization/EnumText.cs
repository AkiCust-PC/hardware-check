using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Localization;

/// <summary>Maps enum values to language keys. Tests check every value exists in every language.</summary>
public static class EnumText
{
    public static string Key<T>(string prefix, T value)
        where T : struct, Enum
    {
        var name = value.ToString();
        return $"{prefix}.{char.ToLowerInvariant(name[0])}{name[1..]}";
    }

    /// <summary>"No information" is a fact rather than an inference, so only real estimates carry the label.</summary>
    public static string CoolingEstimate(Translator t, CoolingOutcome value)
    {
        var text = t[Key("cooling", value)];
        return value == CoolingOutcome.NoData ? text : t["report.coolingInferred", text];
    }

    public static string Phase(Translator t, TestPhase value) => t[Key("phase", value)];

    public static string Status(Translator t, TestStatus value) => t[Key("status", value)];

    public static string Overall(Translator t, OverallResult value) => t[Key("overall", value)];

    public static string Anomaly(Translator t, AnomalyCode value) => t[Key("anomaly", value)];
}