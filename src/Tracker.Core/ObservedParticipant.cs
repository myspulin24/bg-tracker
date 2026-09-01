namespace Tracker.Core;

public sealed class ObservedParticipant(string entity)
{
    public string Entity { get; internal set; } = entity;
    public int? TavernTier { get; internal set; }
    public int? Health { get; internal set; }
    public int? Armor { get; internal set; }
    public int? Damage { get; internal set; }
    public string? PlayState { get; internal set; }

    public int? EffectiveHealth => Health is null ? null : Health - (Damage ?? 0);
}
