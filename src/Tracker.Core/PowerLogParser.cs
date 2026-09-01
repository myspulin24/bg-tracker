using System.Text.RegularExpressions;

namespace Tracker.Core;

public sealed partial class PowerLogParser
{
    private int? pendingEntityId;

    public PowerLogEvent Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        // GameState.* je autoritativní stav hry. PowerTaskList.* je animační fronta, kterou hra
        // vypisuje se zpožděním, takže její descriptory nesou zastaralé zóny a pozice. Jména entit
        // v nich naopak bývají už doplněná, proto se řádky nezahazují, jen se označí.
        var deferred = line.Contains("PowerTaskList.", StringComparison.Ordinal) ||
                       line.Contains("PowerProcessor.", StringComparison.Ordinal);

        var parsed = ParseCore(line);
        return deferred ? parsed with { IsDeferred = true } : parsed;
    }

    private PowerLogEvent ParseCore(string line)
    {
        if (line.Contains("CREATE_GAME", StringComparison.Ordinal))
        {
            pendingEntityId = null;
            return new(PowerLogEventKind.GameCreated, line);
        }

        var playerDeclaration = PlayerDeclarationRegex().Match(line);
        if (playerDeclaration.Success)
        {
            pendingEntityId = int.Parse(playerDeclaration.Groups["entityId"].Value);
            var high = playerDeclaration.Groups["high"].Value;
            var low = playerDeclaration.Groups["low"].Value;
            return new(
                PowerLogEventKind.PlayerDeclared,
                line,
                EntityId: int.Parse(playerDeclaration.Groups["entityId"].Value),
                PlayerId: int.Parse(playerDeclaration.Groups["playerId"].Value),
                IsLocalPlayer: high != "0" || low != "0");
        }

        var playerName = PlayerNameRegex().Match(line);
        if (playerName.Success)
        {
            pendingEntityId = null;
            return new(
                PowerLogEventKind.PlayerNamed,
                line,
                playerName.Groups["name"].Value.Trim(),
                PlayerId: int.Parse(playerName.Groups["playerId"].Value));
        }

        var tagChange = TagChangeRegex().Match(line);
        if (tagChange.Success)
        {
            pendingEntityId = null;
            var rawEntity = tagChange.Groups["entity"].Value;
            return new(
                PowerLogEventKind.TagChanged,
                line,
                CleanEntity(rawEntity),
                tagChange.Groups["tag"].Value,
                tagChange.Groups["value"].Value.Trim(),
                ExtractEntityId(rawEntity),
                ControllerId: ExtractDescriptorInt(rawEntity, "player"),
                Zone: ExtractDescriptorValue(rawEntity, "zone"),
                ZonePosition: ExtractDescriptorInt(rawEntity, "zonePos"));
        }

        var updatedEntity = UpdatedEntityRegex().Match(line);
        if (updatedEntity.Success)
        {
            var rawEntity = updatedEntity.Groups["entity"].Value;
            pendingEntityId = ExtractEntityId(rawEntity);
            return new(
                PowerLogEventKind.EntityCreated,
                line,
                CleanEntity(rawEntity),
                "CARD_ID",
                updatedEntity.Groups["cardId"].Value,
                ExtractEntityId(rawEntity),
                CardId: updatedEntity.Groups["cardId"].Value,
                ControllerId: ExtractDescriptorInt(rawEntity, "player"),
                Zone: ExtractDescriptorValue(rawEntity, "zone"),
                ZonePosition: ExtractDescriptorInt(rawEntity, "zonePos"));
        }

        var fullEntity = FullEntityRegex().Match(line);
        if (fullEntity.Success)
        {
            pendingEntityId = int.Parse(fullEntity.Groups["id"].Value);
            return new(
                PowerLogEventKind.EntityCreated,
                line,
                fullEntity.Groups["id"].Value,
                "CARD_ID",
                fullEntity.Groups["cardId"].Value,
                pendingEntityId,
                CardId: fullEntity.Groups["cardId"].Value);
        }

        var showEntity = ShowEntityRegex().Match(line);
        if (showEntity.Success)
        {
            var rawEntity = showEntity.Groups["entity"].Value;
            pendingEntityId = ExtractEntityId(rawEntity);
            return new(
                PowerLogEventKind.EntityShown,
                line,
                CleanEntity(rawEntity),
                "CARD_ID",
                showEntity.Groups["cardId"].Value,
                ExtractEntityId(rawEntity),
                CardId: showEntity.Groups["cardId"].Value,
                ControllerId: ExtractDescriptorInt(rawEntity, "player"),
                Zone: ExtractDescriptorValue(rawEntity, "zone"),
                ZonePosition: ExtractDescriptorInt(rawEntity, "zonePos"));
        }

        var entityTag = EntityTagRegex().Match(line);
        if (entityTag.Success && pendingEntityId is { } taggedEntityId)
        {
            return new(
                PowerLogEventKind.TagChanged,
                line,
                taggedEntityId.ToString(),
                entityTag.Groups["tag"].Value,
                entityTag.Groups["value"].Value.Trim(),
                taggedEntityId);
        }

        pendingEntityId = null;

        // Poslední záchrana: řádky jako DebugPrintOptions, META_DATA nebo BLOCK_START nesou plný
        // entity descriptor. U karet v nabídce Boba je to jediné místo, kde se objeví jejich jméno.
        if (line.Contains("entityName=", StringComparison.Ordinal))
        {
            var descriptor = DescriptorRegex().Match(line);
            if (descriptor.Success && ExtractEntityId(descriptor.Value) is { } observedEntityId)
            {
                return new(
                    PowerLogEventKind.EntityObserved,
                    line,
                    CleanEntity(descriptor.Value),
                    EntityId: observedEntityId,
                    CardId: ExtractDescriptorValue(descriptor.Value, "cardId"),
                    ControllerId: ExtractDescriptorInt(descriptor.Value, "player"),
                    Zone: ExtractDescriptorValue(descriptor.Value, "zone"),
                    ZonePosition: ExtractDescriptorInt(descriptor.Value, "zonePos"));
            }
        }

        return new(PowerLogEventKind.Unknown, line);
    }

    internal static string CleanEntity(string raw)
    {
        var entity = raw.Trim();
        var descriptor = EntityNameRegex().Match(entity);
        if (descriptor.Success)
        {
            var name = descriptor.Groups["name"].Value.Trim();
            var id = descriptor.Groups["id"].Value;
            return string.IsNullOrWhiteSpace(name) ? $"entity #{id}" : $"{name} (#{id})";
        }

        return entity.Trim('[', ']');
    }

    private static int? ExtractEntityId(string raw)
    {
        var trimmed = raw.Trim().Trim('[', ']');
        if (int.TryParse(trimmed, out var numericId))
        {
            return numericId;
        }

        var descriptor = EntityNameRegex().Match(raw);
        return descriptor.Success && int.TryParse(descriptor.Groups["id"].Value, out var descriptorId)
            ? descriptorId
            : null;
    }

    private static int? ExtractDescriptorInt(string raw, string key) =>
        int.TryParse(ExtractDescriptorValue(raw, key), out var value) ? value : null;

    private static string? ExtractDescriptorValue(string raw, string key)
    {
        var match = Regex.Match(raw, $@"(?:^|\s){Regex.Escape(key)}=(?<value>[^\s\]]*)", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    [GeneratedRegex(@"TAG_CHANGE\s+Entity=(?<entity>.+?)\s+tag=(?<tag>\S+)\s+value=(?<value>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TagChangeRegex();

    [GeneratedRegex(@"FULL_ENTITY\s+-\s+Creating ID=(?<id>\d+)\s+CardID=(?<cardId>\S*)", RegexOptions.CultureInvariant)]
    private static partial Regex FullEntityRegex();

    [GeneratedRegex(@"FULL_ENTITY\s+-\s+Updating\s+(?<entity>\[.+?\])\s+CardID=(?<cardId>\S*)", RegexOptions.CultureInvariant)]
    private static partial Regex UpdatedEntityRegex();

    [GeneratedRegex(@"SHOW_ENTITY\s+-\s+Updating Entity=(?<entity>.+?)\s+CardID=(?<cardId>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ShowEntityRegex();

    [GeneratedRegex(@"(?:entityName|name)=(?<name>.*?)\s+id=(?<id>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex EntityNameRegex();

    [GeneratedRegex(@"\[entityName=[^\]]*\bid=\d+[^\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex DescriptorRegex();

    [GeneratedRegex(@"Player\s+EntityID=(?<entityId>\d+)\s+PlayerID=(?<playerId>\d+)\s+GameAccountId=\[hi=(?<high>\d+)\s+lo=(?<low>\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex PlayerDeclarationRegex();

    [GeneratedRegex(@"PlayerID=(?<playerId>\d+),\s+PlayerName=(?<name>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PlayerNameRegex();

    [GeneratedRegex(@"\s-\s+tag=(?<tag>\S+)\s+value=(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityTagRegex();
}
