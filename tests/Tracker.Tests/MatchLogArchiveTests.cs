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
            // Dokončený zápas se zabalil, prostý text po něm nezůstal.
            Assert.Single(Directory.GetFiles(completed.MatchesDirectory, "*.power.log.br"));
            Assert.Empty(Directory.GetFiles(completed.MatchesDirectory, "*.power.log"));
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

            var files = Directory.GetFiles(archive.MatchesDirectory, "*.power.log.br").Order().ToArray();
            Assert.Equal(2, files.Length);
            Assert.Equal(2, tracker.State.GamesSeen);
            Assert.False(archive.HasActiveMatch);

            // Každý zápas začíná vlastním CREATE_GAME a druhý nepřebral řádky prvního.
            Assert.Equal([lines[0], lines[1]], MatchLogArchive.ReadMatch(files[0]));
            Assert.Equal([lines[2], lines[3], lines[4]], MatchLogArchive.ReadMatch(files[1]));
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
    public void PacksTheFinishedMatchAndKeepsItReadable()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"tracker-pack-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(testDirectory, "data");
        var sourcePath = Path.Combine(testDirectory, "Power.log");

        try
        {
            Directory.CreateDirectory(testDirectory);
            File.WriteAllText(sourcePath, "x");

            // Log se hodně opakuje, takže na komprimaci reaguje stejně jako ten skutečný.
            string[] lines = [.. Enumerable.Range(0, 5000)
                .Select(index => $"D 18:24 GameState.DebugPrintPower() -     TAG_CHANGE Entity=GameEntity tag=TURN value={index}")];

            string packed;
            using (var archive = MatchLogArchive.Open(dataDirectory, sourcePath))
            {
                archive.StartMatch(DateTimeOffset.UnixEpoch);
                foreach (var line in lines)
                {
                    archive.Append(line);
                }

                var plain = archive.ActiveMatchPath!;
                archive.CompleteMatch();
                packed = Directory.GetFiles(archive.MatchesDirectory, "*.power.log.br").Single();
                Assert.False(File.Exists(plain));
            }

            // Obsah přežije zabalení beze změny a soubor je řádově menší.
            Assert.Equal(lines, MatchLogArchive.ReadMatch(packed));
            var original = lines.Sum(line => line.Length + Environment.NewLine.Length);
            Assert.True(new FileInfo(packed).Length * 10 < original,
                $"zabalený {new FileInfo(packed).Length} B není desetkrát menší než {original} B");
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
    public void KeepsAsManyMatchesAsTheCallerAsksFor()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"tracker-retention-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(testDirectory, "data");
        var matchesDirectory = Path.Combine(dataDirectory, "matches");
        var sourcePath = Path.Combine(testDirectory, "Power.log");

        try
        {
            Directory.CreateDirectory(matchesDirectory);
            File.WriteAllText(sourcePath, "x");
            for (var index = 0; index < 50; index++)
            {
                File.WriteAllText(Path.Combine(matchesDirectory, $"match-20260901-{index:D6}-000.power.log.br"), "x");
            }

            using var archive = MatchLogArchive.Open(dataDirectory, sourcePath, retainedMatches: 40);

            Assert.Equal(40, archive.RetainedMatches);
            Assert.Equal(40, Directory.GetFiles(matchesDirectory, "*.power.log.br").Length);
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
    public void PacksLeftoverPlainLogsAndKeepsOnlyTheNewestMatches()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"tracker-prune-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(testDirectory, "data");
        var matchesDirectory = Path.Combine(dataDirectory, "matches");
        var sourcePath = Path.Combine(testDirectory, "Power.log");

        try
        {
            Directory.CreateDirectory(matchesDirectory);
            File.WriteAllText(sourcePath, "x");

            // Zápasy z verze před kompresí; jejich jména nesou čas, takže se řadí abecedně.
            for (var index = 0; index < MatchLogArchive.DefaultRetainedMatches + 5; index++)
            {
                File.WriteAllText(
                    Path.Combine(matchesDirectory, $"match-2026090{index / 10}-{index:D6}-000.power.log"),
                    $"zápas {index}");
            }

            using var archive = MatchLogArchive.Open(dataDirectory, sourcePath);

            Assert.Empty(Directory.GetFiles(matchesDirectory, "*.power.log"));
            var kept = Directory.GetFiles(matchesDirectory, "*.power.log.br").Order().ToArray();
            Assert.Equal(MatchLogArchive.DefaultRetainedMatches, kept.Length);

            // Smazalo se pět nejstarších, nejnovější zůstal a jde přečíst.
            Assert.Equal(
                [$"zápas {MatchLogArchive.DefaultRetainedMatches + 4}"],
                MatchLogArchive.ReadMatch(kept[^1]));

            // Retence je nastavitelná: snížení ořeže složku hned.
            archive.RetainedMatches = 2;
            Assert.Equal(2, Directory.GetFiles(matchesDirectory, "*.power.log.br").Length);
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
