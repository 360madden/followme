namespace FollowMe.Reader;

public sealed record PaletteEntry(byte Symbol, string Name, Bgr24Color Color);

public sealed record StripProfile(
    string Id,
    byte NumericId,
    int WindowWidth,
    int WindowHeight,
    int BandWidth,
    int BandHeight,
    int CaptureHeight,
    int SegmentCount,
    int SegmentWidth,
    int SegmentHeight,
    int PayloadStartSegment,
    int PayloadSymbolCount,
    PaletteEntry[] Palette,
    byte[] LeftControl,
    byte[] RightControl)
{
    public int PayloadStartIndex => PayloadStartSegment - 1;

    public Bgr24Color GetPaletteColor(byte symbol)
    {
        foreach (var entry in Palette)
        {
            if (entry.Symbol == symbol)
            {
                return entry.Color;
            }
        }

        return Palette[0].Color;
    }

    public byte GetExpectedControlSymbol(int segmentIndex)
    {
        if (segmentIndex < LeftControl.Length)
        {
            return LeftControl[segmentIndex];
        }

        var rightIndex = segmentIndex - (SegmentCount - RightControl.Length);
        if (rightIndex >= 0 && rightIndex < RightControl.Length)
        {
            return RightControl[rightIndex];
        }

        throw new ArgumentOutOfRangeException(nameof(segmentIndex));
    }
}

public static class StripProfiles
{
    public static readonly PaletteEntry[] Alphabet =
    {
        new(0, "black", new Bgr24Color(16, 16, 16)),
        new(1, "white", new Bgr24Color(245, 245, 245)),
        new(2, "red", new Bgr24Color(48, 59, 255)),
        new(3, "green", new Bgr24Color(89, 199, 52)),
        new(4, "blue", new Bgr24Color(255, 132, 10)),
        new(5, "yellow", new Bgr24Color(10, 214, 255)),
        new(6, "magenta", new Bgr24Color(242, 90, 191)),
        new(7, "cyan", new Bgr24Color(255, 210, 100))
    };

    public static readonly StripProfile P360C = new(
        "P360C",
        1,
        640,
        360,
        640,
        24,
        48,
        80,
        8,
        24,
        9,
        64,
        Alphabet,
        new byte[] { 0, 1, 0, 1, 2, 3, 4, 5 },
        new byte[] { 5, 4, 3, 2, 1, 0, 1, 0 });

    public static StripProfile Default => P360C;
}
