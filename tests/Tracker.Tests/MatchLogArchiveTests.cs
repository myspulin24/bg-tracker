using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

public sealed class MatchLogArchiveTests
{
    [Fact]
    public void RestoresAnActiveMatchAndFinishesItWithoutCreatingAnotherFile()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"tracker-archive-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(testDirectory, "data");
        var sourcePath = Path.Combine(testDirectory, "Power.log");

        try
        {
            Directory.CreateDirectory(testDirectory);
            File.WriteAllText(sourcePath, new string('x', 100));

            using (var archive = MatchLogArchive.Open(dataDirectory, sourcePath))
            {
                archive.StartMatch(new DateTimeOffset(2026, 9, 1, 18, 30, 0, TimeSpan.Zero));
                archive.Append("CREATE_GAME");
                archive.Append("TAG_CHANGE Entity=GameEntity tag=TURN value=3");
                archive.SaveCheckpoint(42);
            }

            using (var restored = MatchLogArchive.Open(dataDirectory, sourcePath))
            {
                Assert.True(restored.HasActiveMatch);
                Assert.Equal(42, restored.ResumePosition);
                Assert.Equal(
                    ["CREATE_GAME", "TAG_CHANGE Entity=GameEntity tag=TURN value=3"],
                    restored.ReadActiveLines());

                restored.Append("TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_GAMEOVER");
                restored.CompleteMatch();
                restored.SaveCheckpoint(84);
            }

            using var completed = MatchLogArchive.Open(dataDirectory, sourcePath);
            Assert.False(completed.HasActiveMatch);
            Assert.Equal(84, completed.ResumePosition);
            Assert.Single(Directory.GetFiles(completed.MatchesDirectory, "*.power.log"));
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void StartsANewMatchFileEvenWhenThePreviousGameNeverReportedGameOver()
    {
        const string prefix = "D 18:24 GameState.DebugPrintPower() - ";
        var testDirectory = Path.Combine(Path.GetTempPath(), $"tracker-rotate-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(testDirectory, "data");
        var sourcePath = Path.Combine(testDirectory, "Power.log");

        try
        {
            Directory.CreateDirectory(testDirectory);
            File.WriteAllText(sourcePath, new string('x', 100));

            var parser = new PowerLogParser();
            var tracker = new GameStateTracker();
            using var archive = MatchLogArchive.Open(dataDirectory, sourcePath);

            string[] lines =
            [
                prefix + "CREATE_GAME",
                prefix + "TAG_CHANGE Entity=GameEntity tag=TURN value=5",
                // Hra spadla bez FINAL_GAMEOVER a rovnou začíná další zápas.
                prefix + "CREATE_GAME",
                prefix + "TAG_CHANGE Entity=GameEntity tag=TURN value=1",
                prefix + "TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_GAMEOVER"
            ];

            var startedAt = new DateTimeOffset(2026, 9, 1, 18, 30, 0, TimeSpan.Zero);
            foreach (var line in lines)
            {
                MatchRecorder.Handle(parser, tracker, archive, line, startedAt);
                startedAt = startedAt.AddSeconds(1);
            }

            var files = Directory.GetFiles(archive.MatchesDirectory, "*.power.log").Order().ToArray();
            Assert.Equal(2, files.Length);
            Assert.Equal(2, tracker.State.GamesSeen);
            Assert.False(archive.HasActiveMatch);

            // Každý zápas začíná vlastním CREATE_GAME a druhý nepřebral řádky prvního.
            Assert.Equal([lines[0], lines[1]], File.ReadAllLines(files[0]));
            Assert.Equal([lines[2], lines[3], lines[4]], File.ReadAllLines(files[1]));
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }
}
