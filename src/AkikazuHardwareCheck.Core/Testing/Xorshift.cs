namespace AkikazuHardwareCheck.Core.Testing;

internal static class Xorshift
{
    public static uint Next(uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}