namespace FollowMe.Reader;

public enum FrameType : byte
{
    CoreStatus = 1,
    PlayerStatsPage = 2,
    PlayerPosition = 3,
    MultiBoxState = 4
}

public enum ResourceKind : byte
{
    None = 0,
    Mana = 1,
    Energy = 2,
    Power = 3,
    Charge = 4,
    Planar = 5
}

public enum PlayerStatsPageSchema : byte
{
    Vitals = 1,
    Main = 2,
    Offense = 3,
    Defense = 4,
    Resistances = 5
}

public sealed record TelemetryFrameHeader(
    byte ProtocolVersion,
    byte ProfileId,
    FrameType FrameType,
    byte SchemaId,
    byte Sequence,
    byte ReservedFlags,
    ushort HeaderCrc16);

public readonly record struct CoreStatusSnapshot(
    byte PlayerStateFlags,
    byte PlayerHealthPctQ8,
    byte PlayerResourceKind,
    byte PlayerResourcePctQ8,
    byte TargetStateFlags,
    byte TargetHealthPctQ8,
    byte TargetResourceKind,
    byte TargetResourcePctQ8,
    byte PlayerLevel,
    byte TargetLevel,
    byte PlayerCallingRolePacked,
    byte TargetCallingRelationPacked)
{
    public static CoreStatusSnapshot CreateSynthetic()
    {
        return new CoreStatusSnapshot(
            0b0000_0111,
            198,
            (byte)ResourceKind.Mana,
            144,
            0b0000_1111,
            91,
            (byte)ResourceKind.None,
            0,
            70,
            72,
            0x31,
            0x42);
    }
}

public readonly record struct PlayerVitalsSnapshot(
    uint HealthCurrent,
    uint HealthMax,
    ushort ResourceCurrent,
    ushort ResourceMax)
{
    public static PlayerVitalsSnapshot CreateSynthetic()
    {
        return new PlayerVitalsSnapshot(3260, 3260, 100, 100);
    }
}

public readonly record struct PlayerMainStatsSnapshot(
    ushort Armor,
    ushort Strength,
    ushort Dexterity,
    ushort Intelligence,
    ushort Wisdom,
    ushort Endurance)
{
    public static PlayerMainStatsSnapshot CreateSynthetic()
    {
        return new PlayerMainStatsSnapshot(660, 62, 102, 16, 19, 98);
    }
}

public readonly record struct PlayerOffenseStatsSnapshot(
    ushort AttackPower,
    ushort PhysicalCrit,
    ushort Hit,
    ushort SpellPower,
    ushort SpellCrit,
    ushort CritPower)
{
    public static PlayerOffenseStatsSnapshot CreateSynthetic()
    {
        return new PlayerOffenseStatsSnapshot(103, 112, 1, 17, 17, 11);
    }
}

public readonly record struct PlayerDefenseStatsSnapshot(
    ushort Dodge,
    ushort Block,
    ushort Reserved1,
    ushort Reserved2,
    ushort Reserved3,
    ushort Reserved4)
{
    public static PlayerDefenseStatsSnapshot CreateSynthetic()
    {
        return new PlayerDefenseStatsSnapshot(102, 0, 0, 0, 0, 0);
    }
}

public readonly record struct PlayerResistanceStatsSnapshot(
    ushort LifeResist,
    ushort DeathResist,
    ushort FireResist,
    ushort WaterResist,
    ushort EarthResist,
    ushort AirResist)
{
    public static PlayerResistanceStatsSnapshot CreateSynthetic()
    {
        return new PlayerResistanceStatsSnapshot(36, 56, 36, 36, 36, 36);
    }
}

// ── MultiBox snapshots ────────────────────────────────────────────────────────

public readonly record struct PlayerPositionSnapshot(float X, float Y, float Z)
{
    public static PlayerPositionSnapshot Zero => new(0f, 0f, 0f);

    public float DistanceTo(PlayerPositionSnapshot other)
    {
        var dx = X - other.X;
        var dz = Z - other.Z;   // horizontal plane distance; Y is vertical
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public float BearingTo(PlayerPositionSnapshot other)
    {
        var dx = other.X - X;
        var dz = other.Z - Z;
        var radians = MathF.Atan2(dx, dz);
        var degrees = radians * (180f / MathF.PI);
        return (degrees + 360f) % 360f;
    }
}

public sealed record MultiBoxStateSnapshot(byte Flags, string TargetName)
{
    public bool InCombat => (Flags & 0x01) != 0;
    public bool HasTarget => (Flags & 0x02) != 0;
    public bool TargetHostile => (Flags & 0x04) != 0;

    public static MultiBoxStateSnapshot Empty => new(0, string.Empty);
}

// ── MultiBox frames ───────────────────────────────────────────────────────────

public sealed record PlayerPositionFrame(
    TelemetryFrameHeader Header,
    PlayerPositionSnapshot Payload,
    uint PayloadCrc32C,
    byte[] TransportBytes)
    : TelemetryFrame(Header, PayloadCrc32C, TransportBytes);

public sealed record MultiBoxStateFrame(
    TelemetryFrameHeader Header,
    MultiBoxStateSnapshot Payload,
    uint PayloadCrc32C,
    byte[] TransportBytes)
    : TelemetryFrame(Header, PayloadCrc32C, TransportBytes);

// ─────────────────────────────────────────────────────────────────────────────

public abstract record TelemetryFrame(
    TelemetryFrameHeader Header,
    uint PayloadCrc32C,
    byte[] TransportBytes);

public sealed record CoreStatusFrame(
    TelemetryFrameHeader Header,
    CoreStatusSnapshot Payload,
    uint PayloadCrc32C,
    byte[] TransportBytes)
    : TelemetryFrame(Header, PayloadCrc32C, TransportBytes);

public abstract record PlayerStatsPagePayload(PlayerStatsPageSchema Schema);

public sealed record PlayerVitalsStatsPagePayload(PlayerVitalsSnapshot Snapshot)
    : PlayerStatsPagePayload(PlayerStatsPageSchema.Vitals);

public sealed record PlayerMainStatsPagePayload(PlayerMainStatsSnapshot Snapshot)
    : PlayerStatsPagePayload(PlayerStatsPageSchema.Main);

public sealed record PlayerOffenseStatsPagePayload(PlayerOffenseStatsSnapshot Snapshot)
    : PlayerStatsPagePayload(PlayerStatsPageSchema.Offense);

public sealed record PlayerDefenseStatsPagePayload(PlayerDefenseStatsSnapshot Snapshot)
    : PlayerStatsPagePayload(PlayerStatsPageSchema.Defense);

public sealed record PlayerResistanceStatsPagePayload(PlayerResistanceStatsSnapshot Snapshot)
    : PlayerStatsPagePayload(PlayerStatsPageSchema.Resistances);

public sealed record PlayerStatsPageFrame(
    TelemetryFrameHeader Header,
    PlayerStatsPagePayload Payload,
    uint PayloadCrc32C,
    byte[] TransportBytes)
    : TelemetryFrame(Header, PayloadCrc32C, TransportBytes);

public sealed record TransportParseResult(
    bool IsAccepted,
    string Reason,
    bool MagicValid,
    bool ProtocolProfileValid,
    bool FrameSchemaValid,
    bool HeaderCrcValid,
    bool PayloadCrcValid,
    byte[] TransportBytes,
    TelemetryFrame? Frame);

public static class TransportConstants
{
    public const byte MagicC = (byte)'C';
    public const byte MagicL = (byte)'L';
    public const byte ProtocolVersion = 1;
    public const byte CoreFrameType = 1;
    public const byte CoreSchemaId = 1;
    public const byte PlayerStatsFrameType = 2;
    public const int TransportBytes = 24;
    public const int HeaderBytes = 8;
    public const int PayloadBytes = 12;
    public const int PayloadCrcBytes = 4;
    public const int PayloadSymbols = 64;
    public const byte PlayerPositionSchemaId = 1;
    public const byte MultiBoxStateSchemaId = 1;
    public const int TargetNameMaxBytes = 10;
}

public static class FrameProtocol
{
    public static byte[] BuildCoreFrameBytes(byte profileId, byte sequence, CoreStatusSnapshot snapshot)
    {
        Span<byte> payload = stackalloc byte[TransportConstants.PayloadBytes];
        payload[0] = snapshot.PlayerStateFlags;
        payload[1] = snapshot.PlayerHealthPctQ8;
        payload[2] = snapshot.PlayerResourceKind;
        payload[3] = snapshot.PlayerResourcePctQ8;
        payload[4] = snapshot.TargetStateFlags;
        payload[5] = snapshot.TargetHealthPctQ8;
        payload[6] = snapshot.TargetResourceKind;
        payload[7] = snapshot.TargetResourcePctQ8;
        payload[8] = snapshot.PlayerLevel;
        payload[9] = snapshot.TargetLevel;
        payload[10] = snapshot.PlayerCallingRolePacked;
        payload[11] = snapshot.TargetCallingRelationPacked;
        return BuildFrameBytes(profileId, sequence, FrameType.CoreStatus, TransportConstants.CoreSchemaId, payload);
    }

    public static byte[] BuildPlayerVitalsFrameBytes(byte profileId, byte sequence, PlayerVitalsSnapshot snapshot)
    {
        Span<byte> payload = stackalloc byte[TransportConstants.PayloadBytes];
        WriteUInt32BigEndian(payload, 0, snapshot.HealthCurrent);
        WriteUInt32BigEndian(payload, 4, snapshot.HealthMax);
        WriteUInt16BigEndian(payload, 8, snapshot.ResourceCurrent);
        WriteUInt16BigEndian(payload, 10, snapshot.ResourceMax);
        return BuildFrameBytes(profileId, sequence, FrameType.PlayerStatsPage, (byte)PlayerStatsPageSchema.Vitals, payload);
    }

    public static byte[] BuildPlayerMainStatsFrameBytes(byte profileId, byte sequence, PlayerMainStatsSnapshot snapshot)
    {
        Span<byte> payload = stackalloc byte[TransportConstants.PayloadBytes];
        WriteSixUInt16(payload, snapshot.Armor, snapshot.Strength, snapshot.Dexterity, snapshot.Intelligence, snapshot.Wisdom, snapshot.Endurance);
        return BuildFrameBytes(profileId, sequence, FrameType.PlayerStatsPage, (byte)PlayerStatsPageSchema.Main, payload);
    }

    public static byte[] BuildPlayerOffenseStatsFrameBytes(byte profileId, byte sequence, PlayerOffenseStatsSnapshot snapshot)
    {
        Span<byte> payload = stackalloc byte[TransportConstants.PayloadBytes];
        WriteSixUInt16(payload, snapshot.AttackPower, snapshot.PhysicalCrit, snapshot.Hit, snapshot.SpellPower, snapshot.SpellCrit, snapshot.CritPower);
        return BuildFrameBytes(profileId, sequence, FrameType.PlayerStatsPage, (byte)PlayerStatsPageSchema.Offense, payload);
    }

    public static byte[] BuildPlayerDefenseStatsFrameBytes(byte profileId, byte sequence, PlayerDefenseStatsSnapshot snapshot)
    {
        Span<byte> payload = stackalloc byte[TransportConstants.PayloadBytes];
        WriteSixUInt16(payload, snapshot.Dodge, snapshot.Block, snapshot.Reserved1, snapshot.Reserved2, snapshot.Reserved3, snapshot.Reserved4);
        return BuildFrameBytes(profileId, sequence, FrameType.PlayerStatsPage, (byte)PlayerStatsPageSchema.Defense, payload);
    }

    public static byte[] BuildPlayerPositionFrameBytes(byte profileId, byte sequence, PlayerPositionSnapshot snapshot)
    {
        Span<byte> payload = stackalloc byte[TransportConstants.PayloadBytes];
        WriteInt32BigEndian(payload, 0, FloatToFixed(snapshot.X));
        WriteInt32BigEndian(payload, 4, FloatToFixed(snapshot.Y));
        WriteInt32BigEndian(payload, 8, FloatToFixed(snapshot.Z));
        return BuildFrameBytes(profileId, sequence, FrameType.PlayerPosition, TransportConstants.PlayerPositionSchemaId, payload);
    }

    public static byte[] BuildMultiBoxStateFrameBytes(byte profileId, byte sequence, MultiBoxStateSnapshot snapshot)
    {
        Span<byte> payload = stackalloc byte[TransportConstants.PayloadBytes];
        payload[0] = snapshot.Flags;
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(
            snapshot.TargetName ?? string.Empty);
        var nameLen = Math.Min(nameBytes.Length, TransportConstants.TargetNameMaxBytes);
        payload[1] = (byte)nameLen;
        for (var i = 0; i < nameLen; i++)
        {
            payload[2 + i] = nameBytes[i];
        }
        return BuildFrameBytes(profileId, sequence, FrameType.MultiBoxState, TransportConstants.MultiBoxStateSchemaId, payload);
    }

    public static bool TryParsePlayerPositionFrame(ReadOnlySpan<byte> bytes, out PlayerPositionFrame? frame, out string reason)
    {
        var result = AnalyzeFrameBytes(bytes);
        frame = result.Frame as PlayerPositionFrame;
        reason = result.Reason;
        return result.IsAccepted && frame is not null;
    }

    public static bool TryParseMultiBoxStateFrame(ReadOnlySpan<byte> bytes, out MultiBoxStateFrame? frame, out string reason)
    {
        var result = AnalyzeFrameBytes(bytes);
        frame = result.Frame as MultiBoxStateFrame;
        reason = result.Reason;
        return result.IsAccepted && frame is not null;
    }

    public static byte[] BuildPlayerResistanceStatsFrameBytes(byte profileId, byte sequence, PlayerResistanceStatsSnapshot snapshot)
    {
        Span<byte> payload = stackalloc byte[TransportConstants.PayloadBytes];
        WriteSixUInt16(payload, snapshot.LifeResist, snapshot.DeathResist, snapshot.FireResist, snapshot.WaterResist, snapshot.EarthResist, snapshot.AirResist);
        return BuildFrameBytes(profileId, sequence, FrameType.PlayerStatsPage, (byte)PlayerStatsPageSchema.Resistances, payload);
    }

    public static byte[] EncodeBytesToPayloadSymbols(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != TransportConstants.TransportBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        var symbols = new byte[TransportConstants.PayloadSymbols];
        for (var symbolIndex = 0; symbolIndex < TransportConstants.PayloadSymbols; symbolIndex++)
        {
            byte symbol = 0;
            for (var bit = 0; bit < 3; bit++)
            {
                var streamBit = (symbolIndex * 3) + bit;
                var byteIndex = streamBit / 8;
                var bitIndex = 7 - (streamBit % 8);
                var bitValue = (bytes[byteIndex] >> bitIndex) & 0x01;
                symbol = (byte)((symbol << 1) | bitValue);
            }

            symbols[symbolIndex] = symbol;
        }

        return symbols;
    }

    public static byte[] DecodePayloadSymbolsToBytes(ReadOnlySpan<byte> symbols)
    {
        if (symbols.Length != TransportConstants.PayloadSymbols)
        {
            throw new ArgumentOutOfRangeException(nameof(symbols));
        }

        var bytes = new byte[TransportConstants.TransportBytes];
        for (var symbolIndex = 0; symbolIndex < symbols.Length; symbolIndex++)
        {
            var symbol = symbols[symbolIndex];
            if (symbol > 7)
            {
                throw new InvalidDataException($"Symbol {symbolIndex} was out of range: {symbol}.");
            }

            for (var bit = 0; bit < 3; bit++)
            {
                var streamBit = (symbolIndex * 3) + bit;
                var byteIndex = streamBit / 8;
                var bitIndex = 7 - (streamBit % 8);
                var bitValue = (symbol >> (2 - bit)) & 0x01;
                bytes[byteIndex] |= (byte)(bitValue << bitIndex);
            }
        }

        return bytes;
    }

    public static bool TryParseCoreFrameBytes(ReadOnlySpan<byte> bytes, out CoreStatusFrame? frame, out string reason)
    {
        var result = AnalyzeFrameBytes(bytes);
        frame = result.Frame as CoreStatusFrame;
        reason = result.Reason;
        return result.IsAccepted && frame is not null;
    }

    public static TransportParseResult AnalyzeCoreFrameBytes(ReadOnlySpan<byte> bytes)
    {
        var result = AnalyzeFrameBytes(bytes);
        if (!result.IsAccepted)
        {
            return result;
        }

        if (result.Frame is CoreStatusFrame)
        {
            return result;
        }

        return result with
        {
            IsAccepted = false,
            Reason = "Decoded frame was not a core-status frame.",
            FrameSchemaValid = false,
            Frame = null
        };
    }

    public static TransportParseResult AnalyzeFrameBytes(ReadOnlySpan<byte> bytes)
    {
        TelemetryFrame? frame = null;
        var transportBytes = bytes.ToArray();

        if (bytes.Length != TransportConstants.TransportBytes)
        {
            return new TransportParseResult(
                false,
                $"Expected {TransportConstants.TransportBytes} transport bytes, got {bytes.Length}.",
                false,
                false,
                false,
                false,
                false,
                transportBytes,
                null);
        }

        var magicValid = bytes[0] == TransportConstants.MagicC && bytes[1] == TransportConstants.MagicL;
        if (!magicValid)
        {
            return new TransportParseResult(false, "Invalid magic/version.", false, false, false, false, false, transportBytes, null);
        }

        var protocolVersion = (byte)(bytes[2] >> 4);
        var profileId = (byte)(bytes[2] & 0x0F);
        var rawFrameType = (byte)(bytes[3] >> 4);
        var schemaId = (byte)(bytes[3] & 0x0F);
        var protocolProfileValid = protocolVersion == TransportConstants.ProtocolVersion && profileId == StripProfiles.Default.NumericId;
        if (!protocolProfileValid)
        {
            return new TransportParseResult(false, "Invalid magic/version.", true, false, false, false, false, transportBytes, null);
        }

        var frameType = Enum.IsDefined(typeof(FrameType), rawFrameType) ? (FrameType)(rawFrameType) : 0;
        var frameSchemaValid = IsSupportedFrameSchema(frameType, schemaId);
        if (!frameSchemaValid)
        {
            return new TransportParseResult(false, "Invalid frame type/schema.", true, true, false, false, false, transportBytes, null);
        }

        var expectedHeaderCrc = ComputeCrc16(bytes[..6]);
        var actualHeaderCrc = (ushort)((bytes[6] << 8) | bytes[7]);
        var headerCrcValid = expectedHeaderCrc == actualHeaderCrc;
        if (!headerCrcValid)
        {
            return new TransportParseResult(false, "Header CRC failure.", true, true, true, false, false, transportBytes, null);
        }

        var payload = bytes.Slice(8, TransportConstants.PayloadBytes);
        var expectedPayloadCrc = ComputeCrc32C(payload);
        var actualPayloadCrc = (uint)((bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
        var payloadCrcValid = expectedPayloadCrc == actualPayloadCrc;
        if (!payloadCrcValid)
        {
            return new TransportParseResult(false, "Payload CRC failure.", true, true, true, true, false, transportBytes, null);
        }

        var header = new TelemetryFrameHeader(
            protocolVersion,
            profileId,
            frameType,
            schemaId,
            bytes[4],
            bytes[5],
            actualHeaderCrc);

        frame = frameType switch
        {
            FrameType.CoreStatus => new CoreStatusFrame(
                header,
                new CoreStatusSnapshot(
                    payload[0],
                    payload[1],
                    payload[2],
                    payload[3],
                    payload[4],
                    payload[5],
                    payload[6],
                    payload[7],
                    payload[8],
                    payload[9],
                    payload[10],
                    payload[11]),
                actualPayloadCrc,
                bytes.ToArray()),
            FrameType.PlayerStatsPage => new PlayerStatsPageFrame(
                header,
                ParsePlayerStatsPayload((PlayerStatsPageSchema)schemaId, payload),
                actualPayloadCrc,
                bytes.ToArray()),
            FrameType.PlayerPosition => new PlayerPositionFrame(
                header,
                new PlayerPositionSnapshot(
                    FixedToFloat(ReadInt32BigEndian(payload, 0)),
                    FixedToFloat(ReadInt32BigEndian(payload, 4)),
                    FixedToFloat(ReadInt32BigEndian(payload, 8))),
                actualPayloadCrc,
                bytes.ToArray()),
            FrameType.MultiBoxState => ParseMultiBoxStateFrame(header, payload, actualPayloadCrc, bytes),
            _ => null
        };

        if (frame is null)
        {
            return new TransportParseResult(false, "Invalid frame type/schema.", true, true, false, true, true, transportBytes, null);
        }

        return new TransportParseResult(true, "Accepted", true, true, true, true, true, transportBytes, frame);
    }

    public static ushort ComputeCrc16(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
            }
        }

        return crc;
    }

    public static uint ComputeCrc32C(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFF_FFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0x82F63B78u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static byte[] BuildFrameBytes(byte profileId, byte sequence, FrameType frameType, byte schemaId, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != TransportConstants.PayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        var bytes = new byte[TransportConstants.TransportBytes];
        bytes[0] = TransportConstants.MagicC;
        bytes[1] = TransportConstants.MagicL;
        bytes[2] = (byte)((TransportConstants.ProtocolVersion << 4) | (profileId & 0x0F));
        bytes[3] = (byte)(((byte)frameType << 4) | (schemaId & 0x0F));
        bytes[4] = sequence;
        bytes[5] = 0;

        var headerCrc = ComputeCrc16(bytes.AsSpan(0, 6));
        bytes[6] = (byte)(headerCrc >> 8);
        bytes[7] = (byte)(headerCrc & 0xFF);

        payload.CopyTo(bytes.AsSpan(8, TransportConstants.PayloadBytes));

        var payloadCrc = ComputeCrc32C(bytes.AsSpan(8, TransportConstants.PayloadBytes));
        bytes[20] = (byte)(payloadCrc >> 24);
        bytes[21] = (byte)(payloadCrc >> 16);
        bytes[22] = (byte)(payloadCrc >> 8);
        bytes[23] = (byte)(payloadCrc & 0xFF);
        return bytes;
    }

    private static bool IsSupportedFrameSchema(FrameType frameType, byte schemaId)
    {
        return frameType switch
        {
            FrameType.CoreStatus => schemaId == TransportConstants.CoreSchemaId,
            FrameType.PlayerStatsPage => Enum.IsDefined(typeof(PlayerStatsPageSchema), schemaId),
            FrameType.PlayerPosition => schemaId == TransportConstants.PlayerPositionSchemaId,
            FrameType.MultiBoxState => schemaId == TransportConstants.MultiBoxStateSchemaId,
            _ => false
        };
    }

    private static MultiBoxStateFrame ParseMultiBoxStateFrame(
        TelemetryFrameHeader header,
        ReadOnlySpan<byte> payload,
        uint crc,
        ReadOnlySpan<byte> fullBytes)
    {
        var flags = payload[0];
        var nameLen = Math.Min((int)payload[1], TransportConstants.TargetNameMaxBytes);
        var nameBytes = new byte[nameLen];
        for (var i = 0; i < nameLen; i++)
        {
            nameBytes[i] = payload[2 + i];
        }
        var targetName = System.Text.Encoding.ASCII.GetString(nameBytes);
        return new MultiBoxStateFrame(header, new MultiBoxStateSnapshot(flags, targetName), crc, fullBytes.ToArray());
    }

    // Fixed-point encoding: float world coord → int32 (×100), stored big-endian
    // Range: ±21,474,836 world units (ample for any MMO zone)
    private static int FloatToFixed(float value) =>
        (int)Math.Round(value * 100.0, MidpointRounding.AwayFromZero);

    private static float FixedToFloat(int value) => value / 100.0f;

    private static void WriteInt32BigEndian(Span<byte> payload, int offset, int value)
    {
        var u = unchecked((uint)value);
        payload[offset]     = (byte)(u >> 24);
        payload[offset + 1] = (byte)((u >> 16) & 0xFF);
        payload[offset + 2] = (byte)((u >> 8) & 0xFF);
        payload[offset + 3] = (byte)(u & 0xFF);
    }

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> payload, int offset)
    {
        var u = ((uint)payload[offset] << 24)
              | ((uint)payload[offset + 1] << 16)
              | ((uint)payload[offset + 2] << 8)
              | payload[offset + 3];
        return unchecked((int)u);
    }

    private static PlayerStatsPagePayload ParsePlayerStatsPayload(PlayerStatsPageSchema schema, ReadOnlySpan<byte> payload)
    {
        return schema switch
        {
            PlayerStatsPageSchema.Vitals => new PlayerVitalsStatsPagePayload(
                new PlayerVitalsSnapshot(
                    ReadUInt32BigEndian(payload, 0),
                    ReadUInt32BigEndian(payload, 4),
                    ReadUInt16BigEndian(payload, 8),
                    ReadUInt16BigEndian(payload, 10))),
            PlayerStatsPageSchema.Main => new PlayerMainStatsPagePayload(
                new PlayerMainStatsSnapshot(
                    ReadUInt16BigEndian(payload, 0),
                    ReadUInt16BigEndian(payload, 2),
                    ReadUInt16BigEndian(payload, 4),
                    ReadUInt16BigEndian(payload, 6),
                    ReadUInt16BigEndian(payload, 8),
                    ReadUInt16BigEndian(payload, 10))),
            PlayerStatsPageSchema.Offense => new PlayerOffenseStatsPagePayload(
                new PlayerOffenseStatsSnapshot(
                    ReadUInt16BigEndian(payload, 0),
                    ReadUInt16BigEndian(payload, 2),
                    ReadUInt16BigEndian(payload, 4),
                    ReadUInt16BigEndian(payload, 6),
                    ReadUInt16BigEndian(payload, 8),
                    ReadUInt16BigEndian(payload, 10))),
            PlayerStatsPageSchema.Defense => new PlayerDefenseStatsPagePayload(
                new PlayerDefenseStatsSnapshot(
                    ReadUInt16BigEndian(payload, 0),
                    ReadUInt16BigEndian(payload, 2),
                    ReadUInt16BigEndian(payload, 4),
                    ReadUInt16BigEndian(payload, 6),
                    ReadUInt16BigEndian(payload, 8),
                    ReadUInt16BigEndian(payload, 10))),
            PlayerStatsPageSchema.Resistances => new PlayerResistanceStatsPagePayload(
                new PlayerResistanceStatsSnapshot(
                    ReadUInt16BigEndian(payload, 0),
                    ReadUInt16BigEndian(payload, 2),
                    ReadUInt16BigEndian(payload, 4),
                    ReadUInt16BigEndian(payload, 6),
                    ReadUInt16BigEndian(payload, 8),
                    ReadUInt16BigEndian(payload, 10))),
            _ => throw new InvalidDataException($"Unsupported player stats page schema: {schema}.")
        };
    }

    private static void WriteUInt16BigEndian(Span<byte> payload, int offset, ushort value)
    {
        payload[offset] = (byte)(value >> 8);
        payload[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteUInt32BigEndian(Span<byte> payload, int offset, uint value)
    {
        payload[offset] = (byte)(value >> 24);
        payload[offset + 1] = (byte)((value >> 16) & 0xFF);
        payload[offset + 2] = (byte)((value >> 8) & 0xFF);
        payload[offset + 3] = (byte)(value & 0xFF);
    }

    private static void WriteSixUInt16(Span<byte> payload, ushort v1, ushort v2, ushort v3, ushort v4, ushort v5, ushort v6)
    {
        WriteUInt16BigEndian(payload, 0, v1);
        WriteUInt16BigEndian(payload, 2, v2);
        WriteUInt16BigEndian(payload, 4, v3);
        WriteUInt16BigEndian(payload, 6, v4);
        WriteUInt16BigEndian(payload, 8, v5);
        WriteUInt16BigEndian(payload, 10, v6);
    }

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> payload, int offset)
    {
        return (ushort)((payload[offset] << 8) | payload[offset + 1]);
    }

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> payload, int offset)
    {
        return ((uint)payload[offset] << 24)
             | ((uint)payload[offset + 1] << 16)
             | ((uint)payload[offset + 2] << 8)
             | payload[offset + 3];
    }
}
