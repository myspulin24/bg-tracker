using System.Collections.ObjectModel;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Tracker.Core;

namespace Tracker.Desktop;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string gameStatus = "Čekání";
    private string turn = "—";
    private string phase = "čekání";
    private string gold = "—";
    private string place = "—";
    private string tavernUpgrade = "Upgrade: —";
    private string nextOpponent = "Další soupeř: —";
    private string opposingBoardTitle = "NABÍDKA BOBA";
    private string playerBoardTitle = "MOJE DESKA";
    private string boardOwnerTitle = "MOJE DESKA";
    private bool areBoardsCollapsed;
    private string availableRaces = "—";
    private string buffs = string.Empty;
    private bool hasBuffs;
    private string result = "—";
    private string diagnostics = $"{TrackerVersion.Display} • {TrackerVersion.Copyright} • 0 řádků / 0 událostí";
    private string pauseButtonText = "Pozastavit";
    private string modeLabel = "DEMO";
    private string gameMode = "SÓLO";
    private string sourceDescription = "syntetická data";
    private bool isPauseEnabled = true;
    private string updateStatus = string.Empty;
    private bool hasUpdate;
    private bool isUpdateReady;
    private bool isLoading;
    private double loadProgress;
    private string loadStatus = string.Empty;
    private string sourceTooltip = string.Empty;

    /// <summary>Bonus, který v této hře ještě nenastal.</summary>
    private const string EmptyBonus = "+0/+0";

    /// <summary>Glyfy z fontu Segoe MDL2 Assets: pauza a přehrát.</summary>
    private const string PauseGlyph = "\uE769";
    private const string PlayGlyph = "\uE768";

    private string spellBuff = EmptyBonus;
    private bool hasSpellBuff;
    private string bloodGemBuff = EmptyBonus;
    private bool hasBloodGemBuff;
    private string elementalBuff = EmptyBonus;
    private bool hasElementalBuff;
    private string pirateBuff = EmptyBonus;
    private bool hasPirateBuff;
    private string undeadBuff = "+0";
    private bool hasUndeadBuff;
    private string lesserTrinket = "—";
    private bool hasLesserTrinket;
    private string greaterTrinket = "—";
    private bool hasGreaterTrinket;
    private string goldDetail = "—";
    private string goldSpent = "—";
    private string refreshCost = "—";
    private string freeRefreshes = string.Empty;
    private bool hasFreeRefreshes;
    private string upgradeCost = "—";
    private string extraGold = string.Empty;
    private bool hasExtraGold;
    private string tempGold = string.Empty;
    private bool hasTempGold;
    private bool hasCardCounters;
    private CardInfo? lesserTrinketCard;
    private CardInfo? greaterTrinketCard;
    private bool isSidePanelVisible = true;
    private bool hasInlineBuffs;
    private string mediaTitle = string.Empty;
    private string mediaSubtitle = string.Empty;
    private ImageSource? mediaArt;
    private bool hasMedia;
    private string mediaPlayGlyph = PlayGlyph;
    private string mediaPlayTooltip = "Přehrát hudbu";
    private bool canMediaPlayPause;
    private bool canMediaNext;
    private bool canMediaPrevious;
    private readonly Dictionary<int, MinionViewModel[]> boardCache = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ParticipantViewModel> Participants { get; } = [];
    public ObservableCollection<MinionViewModel> PlayerBoard { get; } = [];
    public ObservableCollection<MinionViewModel> OpposingBoard { get; } = [];
    public ObservableCollection<string> Events { get; } = [];

    public string GameStatus { get => gameStatus; private set => Set(ref gameStatus, value); }
    public string Turn { get => turn; private set => Set(ref turn, value); }
    public string Phase { get => phase; private set => Set(ref phase, value); }
    public string Gold { get => gold; private set => Set(ref gold, value); }
    public string Place { get => place; private set => Set(ref place, value); }
    public string TavernUpgrade { get => tavernUpgrade; private set => Set(ref tavernUpgrade, value); }
    public string NextOpponent { get => nextOpponent; private set => Set(ref nextOpponent, value); }
    public string OpposingBoardTitle { get => opposingBoardTitle; private set => Set(ref opposingBoardTitle, value); }

    /// <summary>
    /// Nadpis vlastní desky. V Duos může na téže straně desky během souboje stát spoluhráč
    /// a pak se to musí říct, jinak by uživatel svou desku hledal v cizí sestavě.
    /// </summary>
    public string PlayerBoardTitle { get => playerBoardTitle; private set => Set(ref playerBoardTitle, value); }

    /// <summary>Sekce s deskami je sbalená; nadpis pak říká jen to, že je skrytá.</summary>
    public bool AreBoardsCollapsed
    {
        get => areBoardsCollapsed;
        set
        {
            Set(ref areBoardsCollapsed, value);
            PlayerBoardTitle = value ? "DESKY (skryto)" : boardOwnerTitle;
        }
    }
    public string AvailableRaces { get => availableRaces; private set => Set(ref availableRaces, value); }

    /// <summary>Bonusy platné pro celou hru; prázdné, dokud žádný nenastane.</summary>
    public string Buffs { get => buffs; private set => Set(ref buffs, value); }

    public bool HasBuffs { get => hasBuffs; private set => Set(ref hasBuffs, value); }
    public string Result { get => result; private set => Set(ref result, value); }
    public string Diagnostics { get => diagnostics; private set => Set(ref diagnostics, value); }
    public string PauseButtonText { get => pauseButtonText; set => Set(ref pauseButtonText, value); }
    public string ModeLabel { get => modeLabel; set => Set(ref modeLabel, value); }

    /// <summary>Herní režim lobby: <c>SÓLO</c>, nebo <c>DUOS</c>.</summary>
    public string GameMode { get => gameMode; private set => Set(ref gameMode, value); }

    /// <summary>Verze v hlavičce, ať je na první pohled vidět, co běží.</summary>
    public string Version => TrackerVersion.Display;

    /// <summary>Ladicí build se v hlavičce odliší barvou.</summary>
    public bool IsDevelopmentBuild => TrackerVersion.IsDevelopmentBuild;

    public string SourceDescription { get => sourceDescription; set => Set(ref sourceDescription, value); }
    public bool IsPauseEnabled { get => isPauseEnabled; set => Set(ref isPauseEnabled, value); }

    /// <summary>Text pruhu s aktualizací; pruh se zobrazuje jen podle <see cref="HasUpdate"/>.</summary>
    public string UpdateStatus { get => updateStatus; set => Set(ref updateStatus, value); }
    public bool HasUpdate { get => hasUpdate; set => Set(ref hasUpdate, value); }

    /// <summary>Stažená verze čeká na výměnu, takže má smysl nabídnout restart.</summary>
    public bool IsUpdateReady { get => isUpdateReady; set => Set(ref isUpdateReady, value); }

    /// <summary>Načítá se zápas ze souboru; po tu dobu se ukazuje pruh s postupem.</summary>
    public bool IsLoading { get => isLoading; set => Set(ref isLoading, value); }

    /// <summary>Postup načítání v procentech.</summary>
    public double LoadProgress { get => loadProgress; set => Set(ref loadProgress, value); }

    public string LoadStatus { get => loadStatus; set => Set(ref loadStatus, value); }

    /// <summary>Plná cesta ke zdroji; v hlavičce se vejde jen zkrácený popis.</summary>
    public string SourceTooltip { get => sourceTooltip; set => Set(ref sourceTooltip, value); }

    /// <summary>Název skladby, u videa v prohlížeči jeho titulek.</summary>
    public string MediaTitle { get => mediaTitle; private set => Set(ref mediaTitle, value); }

    /// <summary>Interpret a přehrávač pod názvem skladby.</summary>
    public string MediaSubtitle { get => mediaSubtitle; private set => Set(ref mediaSubtitle, value); }

    /// <summary>Obal skladby; když ho přehrávač nedodá, zůstane v rámečku ikona noty.</summary>
    public ImageSource? MediaArt { get => mediaArt; private set => Set(ref mediaArt, value); }

    /// <summary>Hraje něco? Proužek s hudbou se jinak vůbec nezobrazuje.</summary>
    public bool HasMedia { get => hasMedia; private set => Set(ref hasMedia, value); }

    /// <summary>Glyf tlačítka přehrávání: pauza, když hraje, jinak přehrát.</summary>
    public string MediaPlayGlyph { get => mediaPlayGlyph; private set => Set(ref mediaPlayGlyph, value); }

    public string MediaPlayTooltip { get => mediaPlayTooltip; private set => Set(ref mediaPlayTooltip, value); }

    /// <summary>
    /// Tlačítka se řídí tím, co přehrávač hlásí jako podporované. Prohlížeč s jedním videem
    /// například nezná další ani předchozí skladbu.
    /// </summary>
    public bool CanMediaPlayPause { get => canMediaPlayPause; private set => Set(ref canMediaPlayPause, value); }

    public bool CanMediaNext { get => canMediaNext; private set => Set(ref canMediaNext, value); }

    public bool CanMediaPrevious { get => canMediaPrevious; private set => Set(ref canMediaPrevious, value); }

    /// <summary>
    /// Bonusy pro celou hru rozpadlé po řádcích do bočního panelu. Řádek se ukazuje vždy;
    /// dokud bonus nenastane, je zeslabený, aby panel při prvním kouzlu neposkočil.
    /// </summary>
    public string SpellBuff { get => spellBuff; private set => Set(ref spellBuff, value); }

    public bool HasSpellBuff { get => hasSpellBuff; private set => Set(ref hasSpellBuff, value); }
    public string BloodGemBuff { get => bloodGemBuff; private set => Set(ref bloodGemBuff, value); }
    public bool HasBloodGemBuff { get => hasBloodGemBuff; private set => Set(ref hasBloodGemBuff, value); }
    public string ElementalBuff { get => elementalBuff; private set => Set(ref elementalBuff, value); }
    public bool HasElementalBuff { get => hasElementalBuff; private set => Set(ref hasElementalBuff, value); }
    public string PirateBuff { get => pirateBuff; private set => Set(ref pirateBuff, value); }
    public bool HasPirateBuff { get => hasPirateBuff; private set => Set(ref hasPirateBuff, value); }

    /// <summary>Útok undeadů. Život se u nich takhle nebuffuje, proto je hodnota jen jedna.</summary>
    public string UndeadBuff { get => undeadBuff; private set => Set(ref undeadBuff, value); }

    public bool HasUndeadBuff { get => hasUndeadBuff; private set => Set(ref hasUndeadBuff, value); }

    /// <summary>
    /// Karty na desce, které si samy zvyšují hodnotu. Nejsou to bonusy pro celou hru: hra je
    /// drží na kartě, ne na entitě hráče (viz <see cref="CardCounter" />).
    /// </summary>
    public ObservableCollection<CardCounter> CardCounters { get; } = [];

    public bool HasCardCounters { get => hasCardCounters; private set => Set(ref hasCardCounters, value); }

    /// <summary>Karta malého trinketu; z ní se v tooltipu bere popis efektu.</summary>
    public CardInfo? LesserTrinketCard { get => lesserTrinketCard; private set => Set(ref lesserTrinketCard, value); }

    public CardInfo? GreaterTrinketCard { get => greaterTrinketCard; private set => Set(ref greaterTrinketCard, value); }

    /// <summary>Jméno vybraného trinketu, nebo odpočet do výběru.</summary>
    public string LesserTrinket { get => lesserTrinket; private set => Set(ref lesserTrinket, value); }

    /// <summary>Je malý trinket už vybraný? Prázdný slot se vypisuje zeslabeně.</summary>
    public bool HasLesserTrinket { get => hasLesserTrinket; private set => Set(ref hasLesserTrinket, value); }

    public string GreaterTrinket { get => greaterTrinket; private set => Set(ref greaterTrinket, value); }
    public bool HasGreaterTrinket { get => hasGreaterTrinket; private set => Set(ref hasGreaterTrinket, value); }

    /// <summary>Zlato k utracení a strop kola, tedy <c>4/7</c>.</summary>
    public string GoldDetail { get => goldDetail; private set => Set(ref goldDetail, value); }

    /// <summary>Kolik zlata už v tomhle kole padlo.</summary>
    public string GoldSpent { get => goldSpent; private set => Set(ref goldSpent, value); }

    /// <summary>
    /// Zlato nad strop kola, které přinesly efekty. Vysvětluje, proč je k utracení víc, než
    /// kolik kolo dává.
    /// </summary>
    public string TempGold { get => tempGold; private set => Set(ref tempGold, value); }

    public bool HasTempGold { get => hasTempGold; private set => Set(ref hasTempGold, value); }

    /// <summary>Cena rerollu; nula se vypisuje jako „zdarma“.</summary>
    public string RefreshCost { get => refreshCost; private set => Set(ref refreshCost, value); }

    /// <summary>Kolik rerollů zbývá zdarma, například <c>2×</c>.</summary>
    public string FreeRefreshes { get => freeRefreshes; private set => Set(ref freeRefreshes, value); }

    /// <summary>Má hráč volné rerolly? Řádek se jinak neukazuje.</summary>
    public bool HasFreeRefreshes { get => hasFreeRefreshes; private set => Set(ref hasFreeRefreshes, value); }

    public string UpgradeCost { get => upgradeCost; private set => Set(ref upgradeCost, value); }

    /// <summary>Zlato navíc na příští kolo, například <c>+2</c>.</summary>
    public string ExtraGold { get => extraGold; private set => Set(ref extraGold, value); }

    /// <summary>Dostane hráč příští kolo zlato navíc? Řádek se jinak neukazuje.</summary>
    public bool HasExtraGold { get => hasExtraGold; private set => Set(ref hasExtraGold, value); }

    /// <summary>
    /// Je vidět boční panel? Když ano, řádek s bonusy v kartě lobby se schová, aby tatáž
    /// informace nestála na dvou místech.
    /// </summary>
    public bool IsSidePanelVisible
    {
        get => isSidePanelVisible;
        set
        {
            Set(ref isSidePanelVisible, value);
            HasInlineBuffs = hasBuffs && !value;
        }
    }

    /// <summary>Řádek s bonusy uvnitř karty lobby; jen když je boční panel schovaný.</summary>
    public bool HasInlineBuffs { get => hasInlineBuffs; private set => Set(ref hasInlineBuffs, value); }

    /// <summary>Přenese do rozhraní, co hlásí systémové rozhraní pro média.</summary>
    public void ApplyMedia(NowPlaying playing, ImageSource? art)
    {
        MediaTitle = playing.Title;
        MediaSubtitle = playing.Subtitle;
        MediaArt = art;
        HasMedia = playing.HasTrack;
        MediaPlayGlyph = playing.IsPlaying ? PauseGlyph : PlayGlyph;
        MediaPlayTooltip = playing.IsPlaying ? "Pozastavit hudbu" : "Přehrát hudbu";
        CanMediaPlayPause = playing.CanPlayPause;
        CanMediaNext = playing.CanSkipNext;
        CanMediaPrevious = playing.CanSkipPrevious;
    }

    public void Update(TrackerState state)
    {
        var standings = state.Standings;

        GameStatus = state.IsGameActive ? "Hra probíhá" : state.GamesSeen == 0 ? "Čekání" : "Hra skončila";
        Turn = state.Round?.ToString() ?? "—";
        Phase = FirstUpper(state.Phase);
        Gold = state.AvailableGold is { } available ? $"{available}/{state.Gold ?? available}" : "—";
        Place = state.LocalPlace is { } localPlace ? $"{localPlace}/{state.PlaceCount}" : "—";
        TavernUpgrade = $"Upgrade tavernu: {Value(state.TavernUpgradeCost)}";
        NextOpponent = OpponentLine(state);
        AvailableRaces = state.AvailableRaces.Count == 0
            ? "Typy v nabídce: —"
            : "Typy: " + string.Join(" · ", state.AvailableRaces.Select(MinionRace.Display));
        Buffs = BuffSummary(state.Buffs);
        HasBuffs = Buffs.Length > 0;
        ApplySidePanel(state);
        boardOwnerTitle = state.IsTeammateFighting
            ? $"DESKA SPOLUHRÁČE · {state.Teammate?.HeroName ?? "souboj"}"
            : "MOJE DESKA";
        PlayerBoardTitle = AreBoardsCollapsed ? "DESKY (skryto)" : boardOwnerTitle;
        OpposingBoardTitle = OpposingTitle(state);
        GameMode = state.IsDuos ? "DUOS" : "SÓLO";
        Result = state.FinalPlace is { } finalPlace
            ? $"{finalPlace}. místo"
            : TranslateResult(state.Result);
        Diagnostics =
            $"{TrackerVersion.Display} • {TrackerVersion.Copyright} • {state.ParsedLines} řádků / {state.RecognizedEvents} událostí";

        Sync(Participants, [.. Rank(state, standings).Select(row => new ParticipantViewModel(
            row.Place,
            row.Participant.HeroName ?? "Čekám na hrdinu",
            row.Participant.BattleTag ?? "Skrytý hráč",
            row.Participant.IsEliminated ? "†" : Value(row.Participant.EffectiveHealth),
            Value(row.Participant.Armor),
            Value(row.Participant.TavernTier),
            Value(row.Participant.Triples),
            TranslateStatus(row.Participant.PlayState),
            row.Participant.IsLocal,
            row.Participant.IsTeammate,
            row.Participant.PlayerId == state.NextOpponentPlayerId ||
                (state.IsDuos && row.Participant.PlayerId == state.NextOpponentTeammatePlayerId),
            row.Participant.IsEliminated,
            row.IsTeamStart,
            StableBoard(row.Participant, state),
            BoardCaption(row.Participant, state)))]);

        Sync(PlayerBoard, [.. state.PlayerBoard.Select(MinionViewModel.From)]);
        Sync(OpposingBoard, [.. (state.IsCombatPhase ? state.OpponentBoard : state.Shop).Select(MinionViewModel.From)]);
        Sync(Events, [.. state.RecentEvents.Reverse()]);
    }

    /// <summary>
    /// Aktualizuje kolekci na místě. Kompletní <c>Clear</c> a znovunaplnění při každém ticku by
    /// resetovalo posun v panelu událostí a způsobilo problikávání seznamů.
    /// </summary>
    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (index >= target.Count)
            {
                target.Add(source[index]);
            }
            else if (!EqualityComparer<T>.Default.Equals(target[index], source[index]))
            {
                target[index] = source[index];
            }
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    /// <summary>Jeden řádek tabulky lobby i s tím, kolikáté místo a začátek kterého týmu značí.</summary>
    private readonly record struct Row(LobbyParticipant Participant, string Place, bool IsTeamStart);

    /// <summary>
    /// Očísluje řádky tabulky. V Duos se místo rozdává týmu, ne hráči, takže se číslo píše
    /// jen k prvnímu z dvojice; u druhého by tvrdilo, že je o příčku horší.
    /// </summary>
    private static IReadOnlyList<Row> Rank(TrackerState state, IReadOnlyList<LobbyParticipant> standings)
    {
        if (!state.IsDuos)
        {
            return [.. standings.Select((participant, index) => new Row(participant, $"{index + 1}", false))];
        }

        var rows = new List<Row>(standings.Count);
        var teams = state.Teams;
        for (var index = 0; index < teams.Count; index++)
        {
            for (var member = 0; member < teams[index].Count; member++)
            {
                rows.Add(new Row(teams[index][member], member == 0 ? $"{index + 1}" : string.Empty, member == 0));
            }
        }

        return rows;
    }

    /// <summary>
    /// U lokálního hráče je zajímavá živá deska, u soupeřů poslední, kterou log ukázal.
    /// </summary>
    private static IReadOnlyList<BoardMinion> BoardOf(LobbyParticipant participant, TrackerState state) =>
        IsMe(participant, state) && state.PlayerBoard.Count > 0 ? state.PlayerBoard : participant.LastBoard;

    /// <summary>
    /// Živá deska patří jedinému slotu. V Duos sdílí spoluhráč tutéž stranu desky jako lokální
    /// hráč, takže sám příznak <c>IsLocal</c> by mu mohl podstrčit cizí sestavu.
    /// </summary>
    private static bool IsMe(LobbyParticipant participant, TrackerState state) =>
        state.LocalPlayerSlot is { } slot ? participant.PlayerId == slot : participant.IsLocal;

    private static string BoardCaption(LobbyParticipant participant, TrackerState state)
    {
        if (IsMe(participant, state) && state.PlayerBoard.Count > 0)
        {
            return "Aktuální deska";
        }

        if (participant.LastBoard.Count > 0)
        {
            return $"Deska z kola {participant.LastBoardRound?.ToString() ?? "—"}";
        }

        // Proti spoluhráči se nenastupuje, ale jeho deska se v logu objeví, jakmile sám bojuje:
        // hra ji postaví na tutéž stranu desky jako tu vlastní.
        return participant.IsTeammate
            ? "Desku spoluhráče uvidíte po jeho prvním souboji"
            : "Desku uvidíte, až proti tomuto hráči nastoupíte";
    }

    /// <summary>
    /// Vrací stejnou instanci, dokud se obsah desky nezmění. Nová instance při každém ticku by
    /// nahradila celý řádek lobby a zavřela právě otevřené podokno s deskou.
    /// </summary>
    private IReadOnlyList<MinionViewModel> StableBoard(LobbyParticipant participant, TrackerState state)
    {
        var next = BoardOf(participant, state).Select(MinionViewModel.From).ToArray();
        if (boardCache.TryGetValue(participant.PlayerId, out var previous) && previous.SequenceEqual(next))
        {
            return previous;
        }

        boardCache[participant.PlayerId] = next;
        return next;
    }

    /// <summary>
    /// Řádek s dalším soupeřem. V Duos se bojuje proti celé dvojici: první nastupuje hrdina
    /// z <c>NEXT_OPPONENT_PLAYER_ID</c>, druhý se přidá, až padne některá z desek. Kdo začíná
    /// za náš tým, říká tag na entitě hráče; podle toho uživatel ví, proti komu nastoupí sám.
    /// </summary>
    private static string OpponentLine(TrackerState state)
    {
        if (!state.IsDuos)
        {
            return $"Další soupeř: {SlotLabel(state.NextOpponent, state.NextOpponentPlayerId)}";
        }

        var first = HeroLabel(state.NextOpponent, state.NextOpponentPlayerId);
        var second = state.NextOpponentTeammatePlayerId is null
            ? string.Empty
            : $" + {HeroLabel(state.NextOpponentTeammate, state.NextOpponentTeammatePlayerId)}";
        var starter = state.LocalFightsFirst switch
        {
            true => " · první bojuji já",
            false => " · první bojuje spoluhráč",
            null => string.Empty
        };
        return $"Další soupeři: {first}{second}{starter}";
    }

    /// <summary>
    /// Nadpis protější desky. V Duos se během souboje na soupeřově straně vystřídají oba
    /// hrdinové dvojice, proto se píše, který z nich tam právě stojí.
    /// </summary>
    private static string OpposingTitle(TrackerState state)
    {
        if (!state.IsCombatPhase)
        {
            return "NABÍDKA BOBA";
        }

        return state.IsDuos && state.CombatOpponent?.HeroName is { } hero
            ? $"DESKA SOUPEŘE · {hero}"
            : "DESKA SOUPEŘE";
    }

    private static string SlotLabel(LobbyParticipant? participant, int? playerId) => participant is not null
        ? $"{participant.HeroName ?? "—"} · {participant.BattleTag ?? "Skrytý hráč"}"
        : playerId is { } slot
            ? $"hráč #{slot}"
            : "—";

    /// <summary>Jen hrdina; v Duos jsou v řádku dva a s BattleTagy by se nevešel.</summary>
    private static string HeroLabel(LobbyParticipant? participant, int? playerId) =>
        participant?.HeroName ?? (playerId is { } slot ? $"hráč #{slot}" : "—");

    private static string TranslateResult(string? result) => result switch
    {
        "WON" => "Výhra",
        "LOST" => "Prohra",
        "TIED" => "Remíza",
        _ => "—"
    };

    private static string TranslateStatus(string? status) => status switch
    {
        "WON" => "výhra",
        "LOST" => "prohra",
        "TIED" => "remíza",
        null => "—",
        _ => status.ToLowerInvariant()
    };

    private static string Value(int? value) => value?.ToString() ?? "—";

    /// <summary>
    /// Řádek s bonusy pro celou hru. Vypíše jen to, co v této hře skutečně nastalo, aby
    /// v běžném zápase neubíral místo prázdnými nulami.
    /// </summary>
    private static string BuffSummary(GlobalBuffs buffs)
    {
        if (buffs.IsEmpty)
        {
            return string.Empty;
        }

        var parts = new List<string>(4);
        if (buffs.HasSpell)
        {
            parts.Add($"kouzla {Bonus(buffs.SpellAttack, buffs.SpellHealth)}");
        }

        if (buffs.HasBloodGem)
        {
            parts.Add($"blood gemy {Bonus(buffs.BloodGemAttack, buffs.BloodGemHealth)}");
        }

        if (buffs.HasElemental)
        {
            parts.Add($"elementálové {Bonus(buffs.ElementalAttack, buffs.ElementalHealth)}");
        }

        if (buffs.HasPirate)
        {
            parts.Add($"piráti {Bonus(buffs.PirateAttack, buffs.PirateHealth)}");
        }

        if (buffs.HasUndead)
        {
            parts.Add($"undead +{buffs.UndeadAttack} útoku");
        }

        return "Bonusy: " + string.Join(" · ", parts);
    }

    private static string Bonus(int attack, int health) => $"+{attack}/+{health}";

    /// <summary>
    /// Boční panel: bonusy pro celou hru po řádcích, oba sloty na trinkety s odpočtem
    /// a ekonomika krčmy. Hodnoty, které log nezná, zůstávají pomlčkou, ať je poznat rozdíl
    /// mezi nulou a neznámem.
    /// </summary>
    private void ApplySidePanel(TrackerState state)
    {
        var buffs = state.Buffs;
        SpellBuff = Bonus(buffs.SpellAttack, buffs.SpellHealth);
        HasSpellBuff = buffs.HasSpell;
        BloodGemBuff = Bonus(buffs.BloodGemAttack, buffs.BloodGemHealth);
        HasBloodGemBuff = buffs.HasBloodGem;
        ElementalBuff = Bonus(buffs.ElementalAttack, buffs.ElementalHealth);
        HasElementalBuff = buffs.HasElemental;
        PirateBuff = Bonus(buffs.PirateAttack, buffs.PirateHealth);
        HasPirateBuff = buffs.HasPirate;
        UndeadBuff = $"+{buffs.UndeadAttack}";
        HasUndeadBuff = buffs.HasUndead;

        LesserTrinket = TrinketLabel(state.LesserTrinket);
        HasLesserTrinket = state.LesserTrinket is { IsFilled: true };
        LesserTrinketCard = CardCache.Shared.Get(state.LesserTrinket?.CardId);
        GreaterTrinket = TrinketLabel(state.GreaterTrinket);
        HasGreaterTrinket = state.GreaterTrinket is { IsFilled: true };
        GreaterTrinketCard = CardCache.Shared.Get(state.GreaterTrinket?.CardId);

        Sync(CardCounters, Counters(state));
        HasCardCounters = CardCounters.Count > 0;

        GoldDetail = state.AvailableGold is { } available
            ? $"{available}/{state.Gold ?? available}"
            : "—";
        GoldSpent = Value(state.GoldSpent);

        // Oba bonusy se ukazují pořád, i když jsou nulové: hráč potřebuje vědět nejen že bonus
        // má, ale i že žádný nemá. Bonus tohohle kola přišel jako dočasné zlato z karet
        // zahraných minule, bonus na příští kolo přibývá z karet zahraných teď.
        HasTempGold = state.TempGold is > 0;
        TempGold = $"+{state.TempGold ?? 0}";
        HasExtraGold = state.ExtraGoldNextTurn is > 0;
        ExtraGold = $"+{state.ExtraGoldNextTurn ?? 0}";
        RefreshCost = state.RefreshCost switch
        {
            0 => "zdarma",
            { } cost => cost.ToString(CultureInfo.InvariantCulture),
            null => "—",
        };
        HasFreeRefreshes = state.FreeRefreshes is > 0;
        FreeRefreshes = state.FreeRefreshes is { } free ? $"{free}×" : string.Empty;
        UpgradeCost = UpgradeLabel(state);
        HasInlineBuffs = HasBuffs && !IsSidePanelVisible;
    }

    /// <summary>
    /// Karty na desce, které si samy zvyšují hodnotu. Poznají se podle textu: „improve this“ je
    /// formulace, kterou hra u těchto karet používá, a jen u nich se aktuální hodnota
    /// z počítadla liší od čísel v textu. Ostatní karty počítadla používají k vnitřní evidenci,
    /// takže by z nich v panelu byla jen nesrozumitelná čísla.
    /// </summary>
    private static IReadOnlyList<CardCounter> Counters(TrackerState state) =>
    [
        .. state.PlayerBoard
            .Where(minion => minion.ScriptDataNum1 is > 0 && Improves(minion.CardId))
            .Select(minion => new CardCounter(
                minion.Name,
                Counter(minion),
                CardCache.Shared.Get(minion.CardId)))
    ];

    /// <summary>
    /// Čte se z už načtené tabulky popisů, ne z <see cref="CardInfo" />: ten se doplňuje
    /// asynchronně, takže v tomtéž okamžiku, kdy karta na desce přibude, je jeho popis ještě
    /// prázdný a sekce by se objevila až o jeden přepočet později.
    /// </summary>
    private static bool Improves(string? cardId) =>
        cardId is not null &&
        CardCache.Shared.Texts.Loaded.TryGetValue(cardId, out var text) &&
        text.Contains("improve this", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Hodnota, kterou karta právě dává. Z obou počítadel se bere to vyšší, protože jedno drží
    /// nasčítaný přírůstek a druhé už výsledek: naměřeno na Spark Snapperovi, kde při
    /// <c>NUM_1</c> = 26 a <c>NUM_2</c> = 28 dostal minion na desce +28/+28. Vypisovat obojí
    /// jako <c>26/28</c> by tvrdilo, že je satelit nesouměrný, což není.
    /// </summary>
    private static string Counter(BoardMinion minion) =>
        Math.Max(minion.ScriptDataNum1 ?? 0, minion.ScriptDataNum2 ?? 0)
            .ToString(CultureInfo.InvariantCulture);

    private static string TrinketLabel(Trinket? trinket) => trinket switch
    {
        { IsFilled: true, Name: { } name } => name,
        // Nula neznamená „za nula kol“, ale že nabídka je na stole a čeká na výběr.
        { TurnsLeft: <= 0 } => "čeká výběr",
        { TurnsLeft: { } turns } => StatFormat.TurnsLeft(turns),
        _ => "—",
    };

    /// <summary>
    /// Cena upgradu tavernu. Na posledním tieru už ji hra neposílá, takže se místo pomlčky,
    /// která znamená „nevím“, vypíše <c>max</c>.
    /// </summary>
    private static string UpgradeLabel(TrackerState state)
    {
        if (state.TavernUpgradeCost is { } cost)
        {
            return cost.ToString(CultureInfo.InvariantCulture);
        }

        var tier = state.LocalParticipant?.TavernTier;
        return tier is { } current && state.MaxTavernTier is { } max && current >= max ? "max" : "—";
    }

    private static string FirstUpper(string value) => string.IsNullOrEmpty(value)
        ? value
        : char.ToUpperInvariant(value[0]) + value[1..];

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
