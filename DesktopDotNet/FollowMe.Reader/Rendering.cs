namespace FollowMe.Reader;

public static class ColorStripRenderer
{
    public static byte[] ComposeAllSymbols(StripProfile profile, ReadOnlySpan<byte> payloadSymbols)
    {
        if (payloadSymbols.Length != profile.PayloadSymbolCount)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadSymbols));
        }

        var symbols = new byte[profile.SegmentCount];
        Array.Copy(profile.LeftControl, 0, symbols, 0, profile.LeftControl.Length);
        for (var index = 0; index < payloadSymbols.Length; index++)
        {
            symbols[profile.PayloadStartIndex + index] = payloadSymbols[index];
        }

        Array.Copy(profile.RightControl, 0, symbols, profile.SegmentCount - profile.RightControl.Length, profile.RightControl.Length);
        return symbols;
    }

    public static Bgr24Frame Render(StripProfile profile, ReadOnlySpan<byte> transportBytes)
    {
        var payloadSymbols = FrameProtocol.EncodeBytesToPayloadSymbols(transportBytes);
        return RenderPayloadSymbols(profile, payloadSymbols);
    }

    public static Bgr24Frame RenderPayloadSymbols(StripProfile profile, ReadOnlySpan<byte> payloadSymbols)
    {
        var allSymbols = ComposeAllSymbols(profile, payloadSymbols);
        var image = Bgr24Frame.CreateSolid(profile.BandWidth, profile.BandHeight, profile.GetPaletteColor(0), "synthetic-strip");
        for (var segmentIndex = 0; segmentIndex < allSymbols.Length; segmentIndex++)
        {
            var color = profile.GetPaletteColor(allSymbols[segmentIndex]);
            image.FillRect(
                segmentIndex * profile.SegmentWidth,
                0,
                profile.SegmentWidth,
                profile.SegmentHeight,
                color);
        }

        return image;
    }
}
