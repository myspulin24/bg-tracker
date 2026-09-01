using System.Text;
using Tracker.Core;

namespace Tracker.App;

internal static class ConsoleDashboard
{
    private const char Escape = (char)0x1b;

    public static void Render(TrackerState state, string source, bool clear = true)
    {
        var output = Build(state, source);
        if (clear && !Console.IsOutputRedirected)
        {
            Console.Write($"{Escape}[2J{Escape}[H");
        }

        Console.Write(output);
    }

    internal static string Build(TrackerState state, string source)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"HEARTHSTONE BATTLEGROUNDS TRACKER {TrackerVersion.Display}   {TrackerVersion.Copyright}");
        builder.AppendLine(new string('=', 72));
        builder.AppendLine($"Zdroj:  {source}");
        builder.AppendLine($"Stav:   {(state.IsGameActive ? "hra probíhá" : "čekání / hra skončila")}");
        builder.AppendLine($"Režim:  {(state.BattlegroundsSignalSeen ? "Battlegrounds rozpoznán" : "zatím nepotvrzen")}");
        builder.AppendLine($"Kolo:   {state.Round?.ToString() ?? "—"} (tah {state.Turn?.ToString() ?? "—"})");
        builder.AppendLine($"Fáze:   {state.Phase}");
        builder.AppendLine($"Zlato:  {Gold(state)}");
        builder.AppendLine($"Upgrade tavernu: {state.TavernUpgradeCost?.ToString() ?? "—"}");
        builder.AppendLine($"Další soupeř: {OpponentLabel(state)}");
        builder.AppendLine($"Typy v nabídce: {Races(state)}");
        builder.AppendLine($"Výsledek: {TranslateResult(state.Result)}{FinalPlace(state)}");
        builder.AppendLine();

        AppendLobby(builder, state);
        AppendBoard(builder, "MOJE DESKA", state.PlayerBoard);
        AppendBoard(builder, state.IsCombatPhase ? "DESKA SOUPEŘE" : "NABÍDKA BOBA",
            state.IsCombatPhase ? state.OpponentBoard : state.Shop);
        AppendBoard(builder, "RUKA", state.Hand);
        AppendKnownBoards(builder, state);
        AppendCombats(builder, state);

        builder.AppendLine("POSLEDNÍ UDÁLOSTI");
        if (state.RecentEvents.Count == 0)
        {
            builder.AppendLine("(čekám na rozpoznatelnou událost)");
        }
        else
        {
            foreach (var recentEvent in state.RecentEvents)
            {
                builder.AppendLine($"• {recentEvent}");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"Diagnostika: {state.ParsedLines} řádků, {state.RecognizedEvents} rozpoznaných událostí, {GameCount(state.GamesSeen)}");
        builder.AppendLine("Ukončení: Ctrl+C");
        return builder.ToString();
    }

    private static void AppendLobby(StringBuilder builder, TrackerState state)
    {
        builder.AppendLine("LOBBY");
        builder.AppendLine("  #  Hrdina                     BattleTag             HP  ARM  TIER  TRIPLE");

        var lobby = state.Standings;
        if (lobby.Count == 0)
        {
            builder.AppendLine("  (zatím žádní hráči)");
        }

        for (var index = 0; index < lobby.Count; index++)
        {
            var participant = lobby[index];
            var marker = participant.IsEliminated
                ? '☠'
                : participant.IsLocal ? '>' : participant.PlayerId == state.NextOpponentPlayerId ? '*' : ' ';
            var place = (index + 1).ToString().PadLeft(2);
            var hero = Truncate(participant.HeroName ?? "—", 26).PadRight(26);
            var tag = Truncate(participant.BattleTag ?? "Skrytý hráč", 21).PadRight(21);
            var health = (participant.IsEliminated ? "†" : participant.EffectiveHealth?.ToString() ?? "—").PadLeft(3);
            var armor = (participant.Armor?.ToString() ?? "—").PadLeft(4);
            var tier = (participant.TavernTier?.ToString() ?? "—").PadLeft(5);
            var triples = (participant.Triples?.ToString() ?? "—").PadLeft(2);
            builder.AppendLine($"{marker} {place} {hero} {tag} {health} {armor} {tier}  {triples}");
        }

        builder.AppendLine();
    }

    private static void AppendBoard(StringBuilder builder, string title, IReadOnlyList<BoardMinion> minions)
    {
        if (minions.Count == 0)
        {
            return;
        }

        builder.AppendLine($"{title} ({minions.Count})");
        foreach (var minion in minions)
        {
            var gold = minion.IsGolden ? "★ " : "  ";
            var tier = minion.TechLevel is { } level ? $"T{level}" : "  ";
            var keywords = minion.Keywords.Length > 0 ? $"  [{minion.Keywords}]" : string.Empty;
            builder.AppendLine($"  {minion.ZonePosition}. {gold}{Truncate(minion.Name, 30).PadRight(30)} {minion.Stats,-8} {tier}{keywords}");
        }

        builder.AppendLine();
    }

    /// <summary>
    /// Cizí desku log ukáže jen během souboje proti ní, takže jde vždy o poslední viděný stav.
    /// </summary>
    private static void AppendKnownBoards(StringBuilder builder, TrackerState state)
    {
        var known = state.Standings.Where(participant => participant.LastBoard.Count > 0).ToArray();
        if (known.Length == 0)
        {
            return;
        }

        builder.AppendLine("POSLEDNÍ ZNÁMÉ DESKY SOUPEŘŮ");
        foreach (var participant in known)
        {
            var label = Truncate(participant.BattleTag ?? participant.HeroName ?? "—", 18).PadRight(18);
            var round = $"kolo {participant.LastBoardRound?.ToString() ?? "—"}".PadRight(8);
            var minions = string.Join(", ", participant.LastBoard.Select(
                minion => $"{(minion.IsGolden ? "★" : string.Empty)}{minion.Name} {minion.Stats}"));
            builder.AppendLine($"  {label} {round} {Truncate(minions, 90)}");
        }

        builder.AppendLine();
    }

    private static void AppendCombats(StringBuilder builder, TrackerState state)
    {
        if (state.CombatHistory.Count == 0)
        {
            return;
        }

        builder.AppendLine("SOUBOJE");
        foreach (var combat in state.CombatHistory.TakeLast(8))
        {
            var round = combat.Round?.ToString() ?? "—";
            var opponent = combat.OpponentBattleTag ?? combat.OpponentHeroName ??
                           (combat.OpponentPlayerId is { } slot ? $"hráč #{slot}" : "—");
            var damage = combat.DamageTaken is > 0 ? $" −{combat.DamageTaken} HP" : string.Empty;
            builder.AppendLine($"  Kolo {round,-3} {Truncate(opponent, 24).PadRight(24)} {TranslateResult(combat.Outcome)}{damage}");
        }

        builder.AppendLine();
    }

    private static string Races(TrackerState state) => state.AvailableRaces.Count == 0
        ? "—"
        : string.Join(" · ", state.AvailableRaces.Select(MinionRace.Display));

    private static string Gold(TrackerState state) => state.AvailableGold is { } available
        ? $"{available}/{state.Gold ?? available}"
        : "—";

    private static string OpponentLabel(TrackerState state) => state.NextOpponent is { } opponent
        ? $"{opponent.HeroName ?? "—"} ({opponent.BattleTag ?? "Skrytý hráč"})"
        : state.NextOpponentPlayerId is { } slot
            ? $"hráč #{slot}"
            : "—";

    private static string FinalPlace(TrackerState state) =>
        state.FinalPlace is { } place ? $" — {place}. místo" : string.Empty;

    private static string TranslateResult(string? result) => result switch
    {
        "WON" => "výhra",
        "LOST" => "prohra",
        "TIED" => "remíza",
        _ => "—"
    };

    private static string GameCount(int count) =>
        $"{count} {(count == 1 ? "hra" : count is >= 2 and <= 4 ? "hry" : "her")}";

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
}
