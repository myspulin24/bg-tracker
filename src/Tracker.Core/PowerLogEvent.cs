namespace Tracker.Core;

public enum PowerLogEventKind
{
    Unknown,
    GameCreated,
    TagChanged,
    EntityCreated,
    EntityShown,
    PlayerDeclared,
    PlayerNamed,

    /// <summary>
    /// Entity descriptor nalezený na jinak neznámém řádku, například v <c>DebugPrintOptions</c>,
    /// <c>META_DATA</c> nebo <c>BLOCK_START</c>. Je to jediný zdroj jmen pro karty v nabídce Boba.
    /// </summary>
    EntityObserved
}

public sealed record PowerLogEvent(
    PowerLogEventKind Kind,
    string RawLine,
    string? Entity = null,
    string? Tag = null,
    string? Value = null,
    int? EntityId = null,
    int? PlayerId = null,
    bool IsLocalPlayer = false,
    string? CardId = null,
    int? ControllerId = null,
    string? Zone = null,
    int? ZonePosition = null,
    bool IsDeferred = false);
