namespace Tracker.Desktop;

public sealed record ParticipantViewModel(
    string Place,
    string HeroName,
    string BattleTag,
    string Health,
    string Armor,
    string Tier,
    string Triples,
    string Status,
    bool IsLocal,
    bool IsNextOpponent,
    bool IsEliminated,
    IReadOnlyList<MinionViewModel> Board,
    string BoardCaption)
{
    public bool HasBoard => Board.Count > 0;
}
