namespace FollowMe.Reader;

public readonly record struct Bgr24Color(byte B, byte G, byte R)
{
    public static readonly Bgr24Color Black = new(16, 16, 16);
    public static readonly Bgr24Color White = new(245, 245, 245);

    public double DistanceSquared(Bgr24Color other)
    {
        var deltaB = B - other.B;
        var deltaG = G - other.G;
        var deltaR = R - other.R;
        return (deltaB * deltaB) + (deltaG * deltaG) + (deltaR * deltaR);
    }
}

public readonly record struct FrameSignalStats(double MinLuma, double MaxLuma, double AverageLuma)
{
    public double LumaRange => MaxLuma - MinLuma;
}

public sealed record class Bgr24Frame
{
    public Bgr24Frame(int width, int height, byte[] pixels, string sourceKind = "memory")
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (pixels.Length != width * height * 3)
        {
            throw new ArgumentException("Pixel buffer length did not match width*height*3.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
        SourceKind = sourceKind;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public string SourceKind { get; init; }

    public string CaptureRouteReason { get; init; } = string.Empty;

    public static Bgr24Frame CreateSolid(int width, int height, Bgr24Color color, string sourceKind = "synthetic")
    {
        var pixels = new byte[width * height * 3];
        for (var index = 0; index < pixels.Length; index += 3)
        {
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
        }

        return new Bgr24Frame(width, height, pixels, sourceKind);
    }

    public Bgr24Color GetColor(int x, int y)
    {
        var clampedX = Math.Clamp(x, 0, Width - 1);
        var clampedY = Math.Clamp(y, 0, Height - 1);
        var offset = ((clampedY * Width) + clampedX) * 3;
        return new Bgr24Color(Pixels[offset], Pixels[offset + 1], Pixels[offset + 2]);
    }

    public void SetColor(int x, int y, Bgr24Color color)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        var offset = ((y * Width) + x) * 3;
        Pixels[offset] = color.B;
        Pixels[offset + 1] = color.G;
        Pixels[offset + 2] = color.R;
    }

    public Bgr24Color SampleAverage(double centerX, double centerY, int radius)
    {
        var x = (int)Math.Round(centerX);
        var y = (int)Math.Round(centerY);
        var minX = Math.Max(0, x - radius);
        var maxX = Math.Min(Width - 1, x + radius);
        var minY = Math.Max(0, y - radius);
        var maxY = Math.Min(Height - 1, y + radius);

        long sumB = 0;
        long sumG = 0;
        long sumR = 0;
        long count = 0;

        for (var sampleY = minY; sampleY <= maxY; sampleY++)
        {
            for (var sampleX = minX; sampleX <= maxX; sampleX++)
            {
                var color = GetColor(sampleX, sampleY);
                sumB += color.B;
                sumG += color.G;
                sumR += color.R;
                count++;
            }
        }

        if (count == 0)
        {
            return GetColor(x, y);
        }

        return new Bgr24Color(
            (byte)(sumB / count),
            (byte)(sumG / count),
            (byte)(sumR / count));
    }

    public void FillRect(int x, int y, int width, int height, Bgr24Color color)
    {
        var minX = Math.Max(0, x);
        var minY = Math.Max(0, y);
        var maxX = Math.Min(Width, x + width);
        var maxY = Math.Min(Height, y + height);

        for (var row = minY; row < maxY; row++)
        {
            for (var col = minX; col < maxX; col++)
            {
                SetColor(col, row, color);
            }
        }
    }

    public void Paste(Bgr24Frame source, int left, int top)
    {
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                SetColor(left + x, top + y, source.GetColor(x, y));
            }
        }
    }

    public Bgr24Frame Crop(int x, int y, int width, int height, string sourceKind = "crop")
    {
        var cropWidth = Math.Clamp(width, 1, Width - x);
        var cropHeight = Math.Clamp(height, 1, Height - y);
        var pixels = new byte[cropWidth * cropHeight * 3];
        for (var row = 0; row < cropHeight; row++)
        {
            var sourceOffset = (((y + row) * Width) + x) * 3;
            var destOffset = row * cropWidth * 3;
            Buffer.BlockCopy(Pixels, sourceOffset, pixels, destOffset, cropWidth * 3);
        }

        return new Bgr24Frame(cropWidth, cropHeight, pixels, sourceKind);
    }

    public Bgr24Frame Copy(string sourceKind = "clone")
    {
        return new Bgr24Frame(Width, Height, (byte[])Pixels.Clone(), sourceKind);
    }

    public Bgr24Frame ScaleNearest(double scale, string sourceKind = "scale")
    {
        var newWidth = Math.Max(1, (int)Math.Round(Width * scale));
        var newHeight = Math.Max(1, (int)Math.Round(Height * scale));
        var result = CreateSolid(newWidth, newHeight, Bgr24Color.Black, sourceKind);

        for (var y = 0; y < newHeight; y++)
        {
            var sourceY = Math.Clamp((int)Math.Floor(y / scale), 0, Height - 1);
            for (var x = 0; x < newWidth; x++)
            {
                var sourceX = Math.Clamp((int)Math.Floor(x / scale), 0, Width - 1);
                result.SetColor(x, y, GetColor(sourceX, sourceY));
            }
        }

        return result;
    }

    public Bgr24Frame ApplyGain(double redGain, double greenGain, double blueGain, string sourceKind = "gain")
    {
        var clone = Copy(sourceKind);
        for (var offset = 0; offset < clone.Pixels.Length; offset += 3)
        {
            clone.Pixels[offset] = ClampToByte(clone.Pixels[offset] * blueGain);
            clone.Pixels[offset + 1] = ClampToByte(clone.Pixels[offset + 1] * greenGain);
            clone.Pixels[offset + 2] = ClampToByte(clone.Pixels[offset + 2] * redGain);
        }

        return clone;
    }

    public Bgr24Frame ApplyGamma(double gamma, string sourceKind = "gamma")
    {
        var clone = Copy(sourceKind);
        for (var offset = 0; offset < clone.Pixels.Length; offset += 3)
        {
            clone.Pixels[offset] = ApplyGammaChannel(clone.Pixels[offset], gamma);
            clone.Pixels[offset + 1] = ApplyGammaChannel(clone.Pixels[offset + 1], gamma);
            clone.Pixels[offset + 2] = ApplyGammaChannel(clone.Pixels[offset + 2], gamma);
        }

        return clone;
    }

    public Bgr24Frame ApplyBoxBlur(int radius, string sourceKind = "blur")
    {
        if (radius <= 0)
        {
            return Copy(sourceKind);
        }

        var clone = CreateSolid(Width, Height, Bgr24Color.Black, sourceKind);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                clone.SetColor(x, y, SampleAverage(x, y, radius));
            }
        }

        return clone;
    }

    public byte[] ToPaddedBottomUpRows()
    {
        var paddedStride = ((Width * 3) + 3) & ~3;
        var result = new byte[paddedStride * Height];
        var rowStride = Width * 3;
        for (var row = 0; row < Height; row++)
        {
            var sourceOffset = (Height - row - 1) * rowStride;
            var destOffset = row * paddedStride;
            Buffer.BlockCopy(Pixels, sourceOffset, result, destOffset, rowStride);
        }

        return result;
    }

    public FrameSignalStats MeasureSignal()
    {
        if (Pixels.Length == 0)
        {
            return new FrameSignalStats(0, 0, 0);
        }

        var minLuma = 255.0;
        var maxLuma = 0.0;
        double totalLuma = 0.0;
        var count = Pixels.Length / 3;

        for (var offset = 0; offset < Pixels.Length; offset += 3)
        {
            var luma = (Pixels[offset + 2] * 0.299) + (Pixels[offset + 1] * 0.587) + (Pixels[offset] * 0.114);
            minLuma = Math.Min(minLuma, luma);
            maxLuma = Math.Max(maxLuma, luma);
            totalLuma += luma;
        }

        return new FrameSignalStats(minLuma, maxLuma, totalLuma / count);
    }

    public static Bgr24Frame FromPaddedBottomUpRows(int width, int height, byte[] rows, string sourceKind)
    {
        var paddedStride = ((width * 3) + 3) & ~3;
        var pixels = new byte[width * height * 3];
        var rowStride = width * 3;
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = row * paddedStride;
            var destOffset = (height - row - 1) * rowStride;
            Buffer.BlockCopy(rows, sourceOffset, pixels, destOffset, rowStride);
        }

        return new Bgr24Frame(width, height, pixels, sourceKind);
    }

    private static byte ClampToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    private static byte ApplyGammaChannel(byte value, double gamma)
    {
        if (gamma <= 0)
        {
            return value;
        }

        var normalized = value / 255.0;
        var corrected = Math.Pow(normalized, gamma);
        return ClampToByte(corrected * 255.0);
    }
}

public static class BmpIO
{
    public static Bgr24Frame Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0x4D42)
        {
            throw new InvalidDataException("Not a BMP file.");
        }

        _ = reader.ReadUInt32();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        var pixelOffset = reader.ReadUInt32();
        var dibHeaderSize = reader.ReadUInt32();
        if (dibHeaderSize < 40)
        {
            throw new InvalidDataException("Unsupported BMP header.");
        }

        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var planes = reader.ReadUInt16();
        var bitsPerPixel = reader.ReadUInt16();
        var compression = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        if (planes != 1 || bitsPerPixel != 24 || compression != 0)
        {
            throw new InvalidDataException("Only uncompressed 24-bit BMP files are supported.");
        }

        stream.Position = pixelOffset;
        var paddedStride = ((width * 3) + 3) & ~3;
        var rows = reader.ReadBytes(paddedStride * Math.Abs(height));
        return Bgr24Frame.FromPaddedBottomUpRows(width, Math.Abs(height), rows, "bmp");
    }

    public static void Save(string path, Bgr24Frame frame)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var paddedRows = frame.ToPaddedBottomUpRows();
        const int pixelOffset = 54;
        var totalSize = pixelOffset + paddedRows.Length;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4D42);
        writer.Write(totalSize);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(pixelOffset);
        writer.Write(40);
        writer.Write(frame.Width);
        writer.Write(frame.Height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(paddedRows.Length);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        writer.Write(paddedRows);
    }
}
