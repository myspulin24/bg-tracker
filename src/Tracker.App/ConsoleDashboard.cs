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
        var mode = state.BattlegroundsSignalSeen
            ? state.IsDuos ? "Battlegrounds Duos" : "Battlegrounds sólo"
            : "zatím nepotvrzen";
        builder.AppendLine($"Režim:  {mode}");
        builder.AppendLine($"Kolo:   {state.Round?.ToString() ?? "—"} (tah {state.Turn?.ToString() ?? "—"})");
        builder.AppendLine($"Fáze:   {state.Phase}");
        builder.AppendLine($"Zlato:  {Gold(state)}");
        builder.AppendLine($"Upgrade tavernu: {state.TavernUpgradeCost?.ToString() ?? "—"}");
        builder.AppendLine($"Další soupeř: {OpponentLabel(state)}");
        builder.AppendLine($"Typy v nabídce: {Races(state)}");
        builder.AppendLine($"Výsledek: {TranslateResult(state.Result)}{FinalPlace(state)}");
        builder.AppendLine();

        AppendLobby(builder, state);
        AppendBoard(builder, state.IsTeammateFighting ? "DESKA SPOLUHRÁČE" : "MOJE DESKA", state.PlayerBoard);
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

        var places = Places(state, lobby);
        for (var index = 0; index < lobby.Count; index++)
        {
            var participant = lobby[index];
            var marker = participant.IsEliminated
                ? '☠'
                : participant.IsLocal ? '>'
                : participant.IsTeammate ? '+'
                : participant.PlayerId == state.NextOpponentPlayerId ||
                  (state.IsDuos && participant.PlayerId == state.NextOpponentTeammatePlayerId) ? '*' : ' ';
            var place = places[index].PadLeft(2);
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

    /// <summary>
    /// Čísla míst pro řádky tabulky. V Duos se místo rozdává týmu, takže se píše jen k prvnímu
    /// z dvojice; u druhého by tvrdilo, že je o příčku horší.
    /// </summary>
    private static string[] Places(TrackerState state, IReadOnlyList<LobbyParticipant> lobby)
    {
        var places = new string[lobby.Count];
        if (!state.IsDuos)
        {
            for (var index = 0; index < lobby.Count; index++)
            {
                places[index] = (index + 1).ToString();
            }

            return places;
        }

        var row = 0;
        var teams = state.Teams;
        for (var team = 0; team < teams.Count; team++)
        {
            for (var member = 0; member < teams[team].Count && row < places.Length; member++)
            {
                places[row++] = member == 0 ? (team + 1).ToString() : string.Empty;
            }
        }

        return places;
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
            var extras = string.Join(" · ", new[] { minion.Keywords, minion.TeammateHint }.Where(part => part.Length > 0));
            var keywords = extras.Length > 0 ? $"  [{extras}]" : string.Empty;
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
            // V Duos se bojuje proti celé dvojici.
            if (state.IsDuos && (combat.OpponentTeammateBattleTag ?? combat.OpponentTeammateHeroName) is { } mate)
            {
                opponent = $"{opponent} + {mate}";
            }

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

    /// <summary>
    /// V Duos se bojuje proti celé dvojici: první nastupuje hrdina z <c>NEXT_OPPONENT_PLAYER_ID</c>,
    /// druhý se přidá, až padne některá z desek. Kdo začíná za náš tým, říká tag na entitě hráče.
    /// </summary>
    private static string OpponentLabel(TrackerState state)
    {
        var first = SlotLabel(state.NextOpponent, state.NextOpponentPlayerId);
        if (!state.IsDuos)
        {
            return first;
        }

        var second = state.NextOpponentTeammatePlayerId is null
            ? string.Empty
            : $" + {SlotLabel(state.NextOpponentTeammate, state.NextOpponentTeammatePlayerId)}";
        var starter = state.LocalFightsFirst switch
        {
            true => " · první bojuji já",
            false => " · první bojuje spoluhráč",
            null => string.Empty
        };
        return $"{first}{second}{starter}";
    }

    private static string SlotLabel(LobbyParticipant? participant, int? playerId) => participant is not null
        ? $"{participant.HeroName ?? "—"} ({participant.BattleTag ?? "Skrytý hráč"})"
        : playerId is { } slot
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
