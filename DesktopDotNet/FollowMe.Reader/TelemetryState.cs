namespace FollowMe.Reader;

public sealed class TelemetryAggregate
{
    public CoreStatusFrame? CoreFrame { get; private set; }
    public DateTimeOffset? CoreUpdatedAtUtc { get; private set; }

    public PlayerVitalsStatsPagePayload? VitalsPage { get; private set; }
    public DateTimeOffset? VitalsUpdatedAtUtc { get; private set; }

    public PlayerMainStatsPagePayload? MainPage { get; private set; }
    public DateTimeOffset? MainUpdatedAtUtc { get; private set; }

    public PlayerOffenseStatsPagePayload? OffensePage { get; private set; }
    public DateTimeOffset? OffenseUpdatedAtUtc { get; private set; }

    public PlayerDefenseStatsPagePayload? DefensePage { get; private set; }
    public DateTimeOffset? DefenseUpdatedAtUtc { get; private set; }

    public PlayerResistanceStatsPagePayload? ResistancePage { get; private set; }
    public DateTimeOffset? ResistanceUpdatedAtUtc { get; private set; }

    public PlayerPositionFrame? PositionFrame { get; private set; }
    public DateTimeOffset? PositionUpdatedAtUtc { get; private set; }

    public MultiBoxStateFrame? MultiBoxFrame { get; private set; }
    public DateTimeOffset? MultiBoxUpdatedAtUtc { get; private set; }

    private readonly Dictionary<PlayerStatsPageSchema, byte> _pageSequences = new();

    public byte? LastSequence => CoreFrame?.Header.Sequence
        ?? (VitalsPage is not null ? (byte?)PlayerStatsLastSequence(PlayerStatsPageSchema.Vitals) : null)
        ?? (MainPage is not null ? (byte?)PlayerStatsLastSequence(PlayerStatsPageSchema.Main) : null)
        ?? (OffensePage is not null ? (byte?)PlayerStatsLastSequence(PlayerStatsPageSchema.Offense) : null)
        ?? (DefensePage is not null ? (byte?)PlayerStatsLastSequence(PlayerStatsPageSchema.Defense) : null)
        ?? (ResistancePage is not null ? (byte?)PlayerStatsLastSequence(PlayerStatsPageSchema.Resistances) : null);

    public void Apply(TelemetryFrame frame, DateTimeOffset? observedAtUtc = null)
    {
        var timestamp = observedAtUtc ?? DateTimeOffset.UtcNow;

        switch (frame)
        {
            case CoreStatusFrame core:
                CoreFrame = core;
                CoreUpdatedAtUtc = timestamp;
                break;

            case PlayerPositionFrame position:
                PositionFrame = position;
                PositionUpdatedAtUtc = timestamp;
                break;

            case MultiBoxStateFrame multiBox:
                MultiBoxFrame = multiBox;
                MultiBoxUpdatedAtUtc = timestamp;
                break;

            case PlayerStatsPageFrame stats:
                _pageSequences[stats.Payload.Schema] = stats.Header.Sequence;
                switch (stats.Payload)
                {
                    case PlayerVitalsStatsPagePayload vitals:
                        VitalsPage = vitals;
                        VitalsUpdatedAtUtc = timestamp;
                        break;

                    case PlayerMainStatsPagePayload main:
                        MainPage = main;
                        MainUpdatedAtUtc = timestamp;
                        break;

                    case PlayerOffenseStatsPagePayload offense:
                        OffensePage = offense;
                        OffenseUpdatedAtUtc = timestamp;
                        break;

                    case PlayerDefenseStatsPagePayload defense:
                        DefensePage = defense;
                        DefenseUpdatedAtUtc = timestamp;
                        break;

                    case PlayerResistanceStatsPagePayload resistance:
                        ResistancePage = resistance;
                        ResistanceUpdatedAtUtc = timestamp;
                        break;
                }

                break;
        }
    }

    public TelemetryHudSnapshot CreateSnapshot(DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.UtcNow;
        return new TelemetryHudSnapshot(
            CoreFrame?.Payload.PlayerLevel,
            GetResourceLabel((ResourceKind)(CoreFrame?.Payload.PlayerResourceKind ?? 0)),
            VitalsPage?.Snapshot.HealthCurrent,
            VitalsPage?.Snapshot.HealthMax,
            VitalsPage?.Snapshot.ResourceCurrent,
            VitalsPage?.Snapshot.ResourceMax,
            MainPage?.Snapshot.Armor,
            MainPage?.Snapshot.Strength,
            MainPage?.Snapshot.Dexterity,
            MainPage?.Snapshot.Intelligence,
            MainPage?.Snapshot.Wisdom,
            MainPage?.Snapshot.Endurance,
            OffensePage?.Snapshot.AttackPower,
            OffensePage?.Snapshot.PhysicalCrit,
            OffensePage?.Snapshot.Hit,
            OffensePage?.Snapshot.SpellPower,
            OffensePage?.Snapshot.SpellCrit,
            OffensePage?.Snapshot.CritPower,
            DefensePage?.Snapshot.Dodge,
            DefensePage?.Snapshot.Block,
            ResistancePage?.Snapshot.LifeResist,
            ResistancePage?.Snapshot.DeathResist,
            ResistancePage?.Snapshot.FireResist,
            ResistancePage?.Snapshot.WaterResist,
            ResistancePage?.Snapshot.EarthResist,
            ResistancePage?.Snapshot.AirResist,
            ComputeAgeSeconds(CoreUpdatedAtUtc, current),
            ComputeAgeSeconds(VitalsUpdatedAtUtc, current),
            ComputeAgeSeconds(MainUpdatedAtUtc, current),
            ComputeAgeSeconds(OffenseUpdatedAtUtc, current),
            ComputeAgeSeconds(DefenseUpdatedAtUtc, current),
            ComputeAgeSeconds(ResistanceUpdatedAtUtc, current));
    }

    public static string GetResourceLabel(ResourceKind kind)
    {
        return kind switch
        {
            ResourceKind.Mana => "Mana",
            ResourceKind.Energy => "Energy",
            ResourceKind.Power => "Power",
            ResourceKind.Charge => "Charge",
            ResourceKind.Planar => "Planar",
            _ => "None"
        };
    }

    private double? ComputeAgeSeconds(DateTimeOffset? timestamp, DateTimeOffset now)
    {
        return timestamp is null ? null : Math.Max(0, (now - timestamp.Value).TotalSeconds);
    }

    private byte PlayerStatsLastSequence(PlayerStatsPageSchema schema)
    {
        return _pageSequences.TryGetValue(schema, out var sequence) ? sequence : (byte)0;
    }
}

public sealed record TelemetryHudSnapshot(
    byte? PlayerLevel,
    string ResourceLabel,
    uint? HealthCurrent,
    uint? HealthMax,
    ushort? ResourceCurrent,
    ushort? ResourceMax,
    ushort? Armor,
    ushort? Strength,
    ushort? Dexterity,
    ushort? Intelligence,
    ushort? Wisdom,
    ushort? Endurance,
    ushort? AttackPower,
    ushort? PhysicalCrit,
    ushort? Hit,
    ushort? SpellPower,
    ushort? SpellCrit,
    ushort? CritPower,
    ushort? Dodge,
    ushort? Block,
    ushort? LifeResist,
    ushort? DeathResist,
    ushort? FireResist,
    ushort? WaterResist,
    ushort? EarthResist,
    ushort? AirResist,
    double? CoreAgeSeconds,
    double? VitalsAgeSeconds,
    double? MainAgeSeconds,
    double? OffenseAgeSeconds,
    double? DefenseAgeSeconds,
    double? ResistanceAgeSeconds);
