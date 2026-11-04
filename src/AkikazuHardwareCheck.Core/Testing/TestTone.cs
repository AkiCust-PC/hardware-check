using System.Text;

namespace AkikazuHardwareCheck.Core.Testing;

/// <summary>Builds a short stereo test sound in memory: the left channel, a pause, then the right channel.</summary>
public static class TestTone
{
    private const int SAMPLE_RATE = 44100;
    private const int CHANNELS = 2;
    private const int BITS_PER_SAMPLE = 16;
    private const int BYTES_PER_FRAME = CHANNELS * BITS_PER_SAMPLE / 8;
    private const int HEADER_BYTES = 44;
    private const double TONE_HERTZ = 880;
    private const double TONE_SECONDS = 0.8;
    private const double PAUSE_SECONDS = 0.3;
    private const double FADE_SECONDS = 0.02;

    // Well below full scale, so the first sound from an unknown speaker is not startling.
    private const double VOLUME = 0.3;

    public static byte[] CreateWave()
    {
        var toneFrames = (int)(SAMPLE_RATE * TONE_SECONDS);
        var pauseFrames = (int)(SAMPLE_RATE * PAUSE_SECONDS);
        var frames = (toneFrames * 2) + pauseFrames;
        var dataBytes = frames * BYTES_PER_FRAME;

        using var stream = new MemoryStream(HEADER_BYTES + dataBytes);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(HEADER_BYTES - 8 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)CHANNELS);
        writer.Write(SAMPLE_RATE);
        writer.Write(SAMPLE_RATE * BYTES_PER_FRAME);
        writer.Write((short)BYTES_PER_FRAME);
        writer.Write((short)BITS_PER_SAMPLE);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);

        for (var frame = 0; frame < toneFrames; frame++)
        {
            writer.Write(Sample(frame, toneFrames));
            writer.Write((short)0);
        }

        for (var frame = 0; frame < pauseFrames * CHANNELS; frame++)
        {
            writer.Write((short)0);
        }

        for (var frame = 0; frame < toneFrames; frame++)
        {
            writer.Write((short)0);
            writer.Write(Sample(frame, toneFrames));
        }

        return stream.ToArray();
    }

    private static short Sample(int frame, int total)
    {
        var fadeFrames = SAMPLE_RATE * FADE_SECONDS;
        var envelope = Math.Min(1, Math.Min(frame, total - frame) / fadeFrames);
        var wave = Math.Sin(2 * Math.PI * TONE_HERTZ * frame / SAMPLE_RATE);
        return (short)(wave * envelope * VOLUME * short.MaxValue);
    }
}
