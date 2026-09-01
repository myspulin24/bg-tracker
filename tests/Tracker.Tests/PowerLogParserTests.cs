using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

public sealed class PowerLogParserTests
{
    private readonly PowerLogParser parser = new();

    [Fact]
    public void ParsesTagChangeWithEntityDescriptor()
    {
        const string line = "D 12:00 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Alice id=2 zone=PLAY] tag=PLAYER_TECH_LEVEL value=4";

        var result = parser.Parse(line);

        Assert.Equal(PowerLogEventKind.TagChanged, result.Kind);
        Assert.Equal("Alice (#2)", result.Entity);
        Assert.Equal("PLAYER_TECH_LEVEL", result.Tag);
        Assert.Equal("4", result.Value);
    }

    [Fact]
    public void ReducesPilotGameState()
    {
        var tracker = new GameStateTracker();
        var lines = new[]
        {
            "D 12:00 Debug - CREATE_GAME",
            "D 12:00 Debug - TAG_CHANGE Entity=GameEntity tag=GAMEMODE value=BACON",
            "D 12:00 Debug - TAG_CHANGE Entity=GameEntity tag=TURN value=7",
            "D 12:00 Debug - TAG_CHANGE Entity=GameEntity tag=STEP value=MAIN_ACTION",
            "D 12:00 Debug - TAG_CHANGE Entity=[entityName=Alice id=2 zone=PLAY] tag=HEALTH value=30",
            "D 12:00 Debug - TAG_CHANGE Entity=[entityName=Alice id=2 zone=PLAY] tag=DAMAGE value=6",
            "D 12:00 Debug - TAG_CHANGE Entity=[entityName=Alice id=2 zone=PLAY] tag=PLAYER_TECH_LEVEL value=4"
        };

        foreach (var line in lines)
        {
            tracker.Apply(parser.Parse(line));
        }

        var player = Assert.Single(tracker.State.Participants);
        Assert.True(tracker.State.IsGameActive);
        Assert.True(tracker.State.BattlegroundsSignalSeen);
        Assert.Equal(7, tracker.State.Turn);
        Assert.Equal("nákup", tracker.State.Phase);
        Assert.Equal(24, player.EffectiveHealth);
        Assert.Equal(4, player.TavernTier);
    }

    [Fact]
    public void ResolvesRevealedEntityAndKeepsLocalPlayersResult()
    {
        var tracker = new GameStateTracker();
        var lines = new[]
        {
            "D 12:00 Debug - CREATE_GAME",
            "D 12:00 Debug - Player EntityID=2 PlayerID=1 GameAccountId=[hi=12 lo=34]",
            "D 12:00 Debug - Player EntityID=3 PlayerID=2 GameAccountId=[hi=0 lo=0]",
            "D 12:00 Debug - PlayerID=1, PlayerName=Local#1234",
            "D 12:00 Debug - PlayerID=2, PlayerName=Opponent",
            "D 12:00 Debug - FULL_ENTITY - Creating ID=4 CardID=LOCAL_HERO",
            "D 12:00 Debug -     tag=CONTROLLER value=1",
            "D 12:00 Debug -     tag=CARDTYPE value=HERO",
            "D 12:00 Debug -     tag=PLAYER_ID value=1",
            "D 12:00 Debug - FULL_ENTITY - Updating [entityName=The Lich King id=4 zone=PLAY zonePos=0 cardId=LOCAL_HERO player=1] CardID=LOCAL_HERO",
            "D 12:00 Debug - TAG_CHANGE Entity=Local#1234 tag=HERO_ENTITY value=4",
            "D 12:00 Debug - TAG_CHANGE Entity=66 tag=PLAYER_TECH_LEVEL value=4",
            "D 12:00 Debug - FULL_ENTITY - Updating [entityName=Sindragosa id=66 zone=PLAY zonePos=0 cardId=HERO player=2] CardID=HERO",
            "D 12:00 Debug - TAG_CHANGE Entity=[entityName=Sindragosa id=66 zone=PLAY zonePos=0 cardId=HERO player=2] tag=PLAYER_ID value=2",
            "D 12:00 Debug - TAG_CHANGE Entity=Opponent tag=HERO_ENTITY value=66",
            "D 12:00 Debug - TAG_CHANGE Entity=Local#1234 tag=PLAYSTATE value=LOST",
            "D 12:00 Debug - TAG_CHANGE Entity=Sindragosa tag=PLAYSTATE value=WON"
        };

        foreach (var line in lines)
        {
            tracker.Apply(parser.Parse(line));
        }

        Assert.Equal("LOST", tracker.State.Result);
        Assert.Contains(tracker.State.Participants, participant =>
            participant.Entity == "Sindragosa (#66)" && participant.TavernTier == 4);
        Assert.Contains(tracker.State.LobbyParticipants, participant =>
            participant.PlayerId == 1 && participant.IsLocal && participant.BattleTag == "Local#1234" &&
            participant.HeroName == "The Lich King");
        Assert.Contains(tracker.State.LobbyParticipants, participant =>
            participant.PlayerId == 2 && participant.HeroName == "Sindragosa");
    }
}
