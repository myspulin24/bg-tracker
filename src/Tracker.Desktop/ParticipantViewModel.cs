using System.Windows;

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
    bool IsTeammate,
    bool IsNextOpponent,
    bool IsEliminated,
    bool IsTeamStart,
    IReadOnlyList<MinionViewModel> Board,
    string BoardCaption)
{
    public bool HasBoard => Board.Count > 0;

    /// <summary>Mezera nad prvním hrdinou týmu; v Duos je jinak z tabulky dvojice nepoznat.</summary>
    public Thickness RowMargin => IsTeamStart ? new Thickness(0, 4, 0, 2) : new Thickness(0, 0, 0, 2);
}
