using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using AkikazuHardwareCheck.Core.Testing;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// Plays a test sound and records and plays back the microphone. Only a person can tell whether sound really came out,
/// so these exist for staff to listen. The recording stays in memory and is discarded.
/// </summary>
internal static class AudioCheck
{
    private const int MCI_SUCCESS = 0;
    private const int ERROR_TEXT_LENGTH = 256;
    private const string ALIAS = "hardwarecheck";

    private static readonly TimeSpan RecordTime = TimeSpan.FromSeconds(3);

    public static void PlayTone()
    {
        using var stream = new MemoryStream(TestTone.CreateWave());
        using var player = new SoundPlayer(stream);
        player.PlaySync();
    }

    public static void PlayChannel(bool left, bool right)
    {
        using var stream = new MemoryStream(TestTone.CreateChannelWave(left, right));
        using var player = new SoundPlayer(stream);
        player.PlaySync();
    }

    public static void RecordAndPlayBack(Action onPlaybackStart)
    {
        Send($"open new type waveaudio alias {ALIAS}");
        try
        {
            Send($"record {ALIAS}");
            Thread.Sleep(RecordTime);
            Send($"stop {ALIAS}");
            onPlaybackStart();
            Send($"play {ALIAS} from 0 wait");
        }
        finally
        {
            _ = mciSendStringW($"close {ALIAS}", null, 0, IntPtr.Zero);
        }
    }

    private static void Send(string command)
    {
        var error = mciSendStringW(command, null, 0, IntPtr.Zero);
        if (error == MCI_SUCCESS)
        {
            return;
        }

        var text = new StringBuilder(ERROR_TEXT_LENGTH);
        _ = mciGetErrorStringW(error, text, ERROR_TEXT_LENGTH);
        throw new InvalidOperationException(text.ToString());
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendStringW(string command, StringBuilder? result, int resultLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorStringW(int error, StringBuilder text, int length);
}
