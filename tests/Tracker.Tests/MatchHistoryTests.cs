using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Historie zápasů: hrdina, umístění a režim se berou z logu, MMR doplňuje uživatel a změna se
/// počítá proti předchozímu zápasu téhož režimu. Historie přežívá restart a nezávisí na
/// retenci archivu.
/// </summary>
public sealed class MatchHistoryTests
{
    private const string GameState = "D 18:24 GameState.DebugPrintPower() - ";

    private static readonly DateTimeOffset Now = new(2026, 9, 5, 17, 30, 0, TimeSpan.FromHours(2));

    private static string[] SoloGame(int place) =>
    [
        GameState + "CREATE_GAME",
        GameState + "    Player EntityID=20 PlayerID=7 GameAccountId=[hi=144115198130930503 lo=53370550]",
        GameState + "    Player EntityID=21 PlayerID=15 GameAccountId=[hi=0 lo=0]",
        "D 18:24 GameState.DebugPrintGame() - PlayerID=7, PlayerName=Hráč#21600",
        "D 18:24 GameState.DebugPrintGame() - PlayerID=15, PlayerName=Protihráč",
        GameState + "FULL_ENTITY - Creating ID=116 CardID=TB_BaconShop_HERO_92",
        GameState + "    tag=CONTROLLER value=7",
        GameState + "    tag=CARDTYPE value=HERO",
        GameState + "    tag=PLAYER_ID value=7",
        GameState + "    tag=HEALTH value=30",
        GameState + "    tag=ZONE value=PLAY",
        GameState + "FULL_ENTITY - Updating [entityName=Y'Shaarj id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] CardID=TB_BaconShop_HERO_92",
        GameState + "TAG_CHANGE Entity=Hráč#21600 tag=HERO_ENTITY value=116",
        GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=9",
        GameState + $"TAG_CHANGE Entity=[entityName=Y'Shaarj id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] tag=PLAYER_LEADERBOARD_PLACE value={place}",
        GameState + "TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_GAMEOVER"
    ];

    private static MatchHistory Replay(IEnumerable<string> lines)
    {
        var parser = new PowerLogParser();
        var tracker = new GameStateTracker();
        var history = new MatchHistory();
        foreach (var line in lines)
        {
            MatchRecorder.Handle(parser, tracker, null, line, Now, history);
        }

        return history;
    }

    [Fact]
    public void RecordsAFinishedGameFromTheTrackerState()
    {
        var history = Replay(SoloGame(place: 3));

        var record = Assert.Single(history.Records);
        Assert.Equal("Y'Shaarj", record.HeroName);
        Assert.Equal("TB_BaconShop_HERO_92", record.HeroCardId);
        Assert.Equal(3, record.Place);
        Assert.Equal(8, record.PlaceCount);
        Assert.Equal(5, record.Rounds);
        Assert.False(record.IsDuos);
        Assert.Null(record.Mmr);
        Assert.Equal(Now, record.EndedAt);
        Assert.Equal("match-20260905-173000-000", record.Id);
    }

    [Fact]
    public void SkipsAGameThatNeverReachedTheFirstTurn()
    {
        // Odchod z lobby při výběru hrdiny: CREATE_GAME a hned konec, bez jediného kola.
        var history = Replay(SoloGame(place: 3).Where(line => !line.Contains("tag=TURN", StringComparison.Ordinal)));

        Assert.Empty(history.Records);
    }

    [Fact]
    public void ComputesMmrChangesPerModeAndKnowsTheCurrentMmr()
    {
        var history = new MatchHistory();
        history.Add(Record("a", Now.AddHours(-3), duos: false, mmr: 6400));
        history.Add(Record("b", Now.AddHours(-2), duos: true, mmr: 5000));
        history.Add(Record("c", Now.AddHours(-1), duos: false, mmr: 6459));
        history.Add(Record("d", Now, duos: false, mmr: null));

        // Změna se počítá jen v rámci režimu: Duos zápas mezi dvěma sólo zápasy nic nerozbije.
        Assert.Equal(59, history.ChangeFor(history.Records[2]));
        Assert.Null(history.ChangeFor(history.Records[0]));
        Assert.Null(history.ChangeFor(history.Records[3]));
        Assert.Equal(6459, history.CurrentMmr(duos: false));
        Assert.Equal(5000, history.CurrentMmr(duos: true));

        // Doplnění MMR k poslednímu zápasu dá i jeho změnu proti předchozímu.
        Assert.True(history.SetMmr("d", 6378));
        Assert.Equal(-81, history.ChangeFor(history.Records[3]));
        Assert.Equal(6378, history.CurrentMmr(duos: false));
        Assert.False(history.SetMmr("d", 6378));
    }

    [Fact]
    public void ListsTheLatestGamesOfOneModeNewestFirst()
    {
        var history = new MatchHistory();
        for (var index = 0; index < 8; index++)
        {
            history.Add(Record($"s{index}", Now.AddMinutes(index), duos: index % 2 == 1, mmr: null));
        }

        var latest = history.Latest(duos: false, count: 3);
        Assert.Equal(["s6", "s4", "s2"], latest.Select(record => record.Id));
        Assert.All(history.Latest(duos: true, count: 10), record => Assert.True(record.IsDuos));
    }

    [Fact]
    public void DedupesByIdAndSurvivesTheStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bgtracker-history-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "history.json");
        try
        {
            var history = new MatchHistory();
            var changes = 0;
            history.Changed += (_, _) => changes++;
            Assert.True(history.Add(Record("m1", Now, duos: true, mmr: 5100)));
            Assert.False(history.Add(Record("m1", Now.AddMinutes(5), duos: true, mmr: 1)));
            Assert.Equal(1, changes);

            MatchHistoryStore.Save(history, path);
            var loaded = MatchHistoryStore.Load(path);

            var record = Assert.Single(loaded.Records);
            Assert.Equal("m1", record.Id);
            Assert.True(record.IsDuos);
            Assert.Equal(5100, record.Mmr);
            Assert.Equal("Cookie the Cook", record.TeammateHeroName);
            Assert.Equal(Now, record.EndedAt);
            Assert.False(File.Exists(path + ".tmp"));

            File.WriteAllText(path, "[ { broken");
            Assert.Empty(MatchHistoryStore.Load(path).Records);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void TakesTheIdFromTheArchiveFileName()
    {
        Assert.Equal("match-20260905-171234-123",
            MatchHistory.IdFor(@"C:\data\matches\match-20260905-171234-123.power.log", Now));
        Assert.Equal("match-20260905-171234-123-1",
            MatchHistory.IdFor(@"C:\data\matches\match-20260905-171234-123-1.power.log.br", Now));
        Assert.Equal("match-20260905-173000-000", MatchHistory.IdFor(null, Now));
    }

    private static MatchRecord Record(string id, DateTimeOffset endedAt, bool duos, int? mmr) => new()
    {
        Id = id,
        EndedAt = endedAt,
        IsDuos = duos,
        HeroName = "A. F. Kay",
        TeammateHeroName = duos ? "Cookie the Cook" : null,
        Place = 2,
        PlaceCount = duos ? 4 : 8,
        Rounds = 12,
        Mmr = mmr
    };
}
