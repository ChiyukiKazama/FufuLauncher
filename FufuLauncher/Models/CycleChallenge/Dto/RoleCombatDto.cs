/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using System.Text.Json.Serialization;

namespace FufuLauncher.Models.CycleChallenge.Dto;

public sealed class RoleCombatResponseDto
{
    [JsonPropertyName("data")]
    public List<RoleCombatEntryDto>? Data
    {
        get;
        set;
    }


    [JsonPropertyName("is_unlock")]
    public bool? IsUnlock
    {
        get;
        set;
    }


    [JsonPropertyName("links")]
    public RoleCombatLinksDto? Links
    {
        get;
        set;
    }
}

public sealed class RoleCombatEntryDto
{
    [JsonPropertyName("uid")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Uid
    {
        get;
        set;
    }


    [JsonPropertyName("schedule_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ScheduleId
    {
        get;
        set;
    }


    [JsonPropertyName("start_time")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? StartTime
    {
        get;
        set;
    }


    [JsonPropertyName("end_time")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? EndTime
    {
        get;
        set;
    }


    [JsonPropertyName("difficulty_id")]
    public int? DifficultyId
    {
        get;
        set;
    }


    [JsonPropertyName("max_round_id")]
    public int? MaxRoundId
    {
        get;
        set;
    }


    [JsonPropertyName("heraldry")]
    public int? Heraldry
    {
        get;
        set;
    }


    [JsonPropertyName("medal_num")]
    public int? MedalNum
    {
        get;
        set;
    }


    [JsonPropertyName("detail")]
    public RoleCombatDetailDto? Detail
    {
        get;
        set;
    }


    [JsonPropertyName("stat")]
    public RoleCombatStatDto? Stat
    {
        get;
        set;
    }


    [JsonPropertyName("schedule")]
    public RoleCombatScheduleDto? Schedule
    {
        get;
        set;
    }


    [JsonPropertyName("has_data")]
    public bool? HasData
    {
        get;
        set;
    }


    [JsonPropertyName("has_detail_data")]
    public bool? HasDetailData
    {
        get;
        set;
    }
}

public sealed class RoleCombatStatDto
{
    [JsonPropertyName("difficulty_id")]
    public int? DifficultyId
    {
        get;
        set;
    }


    [JsonPropertyName("max_round_id")]
    public int? MaxRoundId
    {
        get;
        set;
    }


    [JsonPropertyName("heraldry")]
    public int? Heraldry
    {
        get;
        set;
    }


    [JsonPropertyName("get_medal_round_list")]
    public List<int>? GetMedalRoundList
    {
        get;
        set;
    }


    [JsonPropertyName("medal_num")]
    public int? MedalNum
    {
        get;
        set;
    }


    [JsonPropertyName("coin_num")]
    public int? CoinNum
    {
        get;
        set;
    }


    [JsonPropertyName("avatar_bonus_num")]
    public int? AvatarBonusNum
    {
        get;
        set;
    }


    [JsonPropertyName("rent_cnt")]
    public int? RentCount
    {
        get;
        set;
    }


    [JsonPropertyName("tarot_finished_cnt")]
    public int? TarotFinishedCount
    {
        get;
        set;
    }
}

public sealed class RoleCombatDetailDto
{
    [JsonPropertyName("rounds_data")]
    public List<RoleCombatRoundDto>? RoundsData
    {
        get;
        set;
    }


    [JsonPropertyName("detail_stat")]
    public RoleCombatStatDto? DetailStat
    {
        get;
        set;
    }


    [JsonPropertyName("lineup_link")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? LineupLink
    {
        get;
        set;
    }


    [JsonPropertyName("backup_avatars")]
    public List<RoleCombatAvatarDto>? BackupAvatars
    {
        get;
        set;
    }


    [JsonPropertyName("fight_statisic")]
    public RoleCombatFightStatisticsDto? FightStatistics
    {
        get;
        set;
    }
}

public sealed class RoleCombatRoundDto
{
    [JsonPropertyName("avatars")]
    public List<RoleCombatAvatarDto>? Avatars
    {
        get;
        set;
    }


    [JsonPropertyName("choice_cards")]
    public List<RoleCombatBuffDto>? ChoiceCards
    {
        get;
        set;
    }


    [JsonPropertyName("buffs")]
    public List<RoleCombatBuffDto>? Buffs
    {
        get;
        set;
    }


    [JsonPropertyName("is_get_medal")]
    public bool? IsGetMedal
    {
        get;
        set;
    }


    [JsonPropertyName("round_id")]
    public int? RoundId
    {
        get;
        set;
    }


    [JsonPropertyName("finish_time")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? FinishTime
    {
        get;
        set;
    }


    [JsonPropertyName("finish_date_time")]
    public RoleCombatDateTimeDto? FinishDateTime
    {
        get;
        set;
    }


    [JsonPropertyName("enemies")]
    public List<RoleCombatEnemyDto>? Enemies
    {
        get;
        set;
    }


    [JsonPropertyName("splendour_buff")]
    public RoleCombatSplendourBuffDto? SplendourBuff
    {
        get;
        set;
    }


    [JsonPropertyName("is_tarot")]
    public bool? IsTarot
    {
        get;
        set;
    }


    [JsonPropertyName("tarot_serial_no")]
    public int? TarotSerialNumber
    {
        get;
        set;
    }
}

public sealed class RoleCombatAvatarDto
{
    [JsonPropertyName("avatar_id")]
    public int? AvatarId
    {
        get;
        set;
    }


    [JsonPropertyName("avatar_type")]
    public int? AvatarType
    {
        get;
        set;
    }


    [JsonPropertyName("name")]
    public string? Name
    {
        get;
        set;
    }


    [JsonPropertyName("element")]
    public string? Element
    {
        get;
        set;
    }


    [JsonPropertyName("image")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Image
    {
        get;
        set;
    }


    [JsonPropertyName("level")]
    public int? Level
    {
        get;
        set;
    }


    [JsonPropertyName("rarity")]
    public int? Rarity
    {
        get;
        set;
    }
}

public sealed class RoleCombatEnemyDto
{
    [JsonPropertyName("name")]
    public string? Name
    {
        get;
        set;
    }


    [JsonPropertyName("icon")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Icon
    {
        get;
        set;
    }


    [JsonPropertyName("level")]
    public int? Level
    {
        get;
        set;
    }
}

public sealed class RoleCombatBuffDto
{
    [JsonPropertyName("id")]
    public int? Id
    {
        get;
        set;
    }


    [JsonPropertyName("icon")]
    public string? Icon
    {
        get;
        set;
    }


    [JsonPropertyName("name")]
    public string? Name
    {
        get;
        set;
    }


    [JsonPropertyName("desc")]
    public string? Description
    {
        get;
        set;
    }


    [JsonPropertyName("is_enhanced")]
    public bool? IsEnhanced
    {
        get;
        set;
    }
}

public sealed class RoleCombatSplendourBuffDto
{
    [JsonPropertyName("summary")]
    public RoleCombatSplendourBuffSummaryDto? Summary
    {
        get;
        set;
    }


    [JsonPropertyName("buffs")]
    public List<RoleCombatSplendourBuffItemDto>? Buffs
    {
        get;
        set;
    }
}

public sealed class RoleCombatSplendourBuffSummaryDto
{
    [JsonPropertyName("total_level")]
    public int? TotalLevel
    {
        get;
        set;
    }


    [JsonPropertyName("desc")]
    public string? Description
    {
        get;
        set;
    }
}

public sealed class RoleCombatSplendourBuffItemDto
{
    [JsonPropertyName("icon")]
    public string? Icon
    {
        get;
        set;
    }


    [JsonPropertyName("name")]
    public string? Name
    {
        get;
        set;
    }


    [JsonPropertyName("level")]
    public int? Level
    {
        get;
        set;
    }


    [JsonPropertyName("level_effect")]
    public List<RoleCombatBuffDto>? LevelEffects
    {
        get;
        set;
    }
}

public sealed class RoleCombatFightStatisticsDto
{
    [JsonPropertyName("max_defeat_avatar")]
    public RoleCombatAvatarStatisticsDto? MaxDefeatAvatar
    {
        get;
        set;
    }


    [JsonPropertyName("max_damage_avatar")]
    public RoleCombatAvatarStatisticsDto? MaxDamageAvatar
    {
        get;
        set;
    }


    [JsonPropertyName("max_take_damage_avatar")]
    public RoleCombatAvatarStatisticsDto? MaxTakeDamageAvatar
    {
        get;
        set;
    }


    [JsonPropertyName("total_coin_consumed")]
    public RoleCombatAvatarStatisticsDto? TotalCoinConsumed
    {
        get;
        set;
    }


    [JsonPropertyName("shortest_avatar_list")]
    public List<RoleCombatAvatarStatisticsDto>? ShortestAvatarList
    {
        get;
        set;
    }


    [JsonPropertyName("total_use_time")]
    public int? TotalUseTime
    {
        get;
        set;
    }


    [JsonPropertyName("is_show_battle_stats")]
    public bool? IsShowBattleStats
    {
        get;
        set;
    }
}

public sealed class RoleCombatAvatarStatisticsDto
{
    [JsonPropertyName("avatar_id")]
    public int? AvatarId
    {
        get;
        set;
    }


    [JsonPropertyName("avatar_icon")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? AvatarIcon
    {
        get;
        set;
    }


    [JsonPropertyName("value")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Value
    {
        get;
        set;
    }


    [JsonPropertyName("rarity")]
    public int? Rarity
    {
        get;
        set;
    }
}

public sealed class RoleCombatScheduleDto
{
    [JsonPropertyName("start_time")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? StartTime
    {
        get;
        set;
    }


    [JsonPropertyName("end_time")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? EndTime
    {
        get;
        set;
    }


    [JsonPropertyName("schedule_type")]
    public int? ScheduleType
    {
        get;
        set;
    }


    [JsonPropertyName("schedule_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ScheduleId
    {
        get;
        set;
    }


    [JsonPropertyName("start_date_time")]
    public RoleCombatDateTimeDto? StartDateTime
    {
        get;
        set;
    }


    [JsonPropertyName("end_date_time")]
    public RoleCombatDateTimeDto? EndDateTime
    {
        get;
        set;
    }
}

public sealed class RoleCombatDateTimeDto
{
    [JsonPropertyName("year")]
    public int? Year
    {
        get;
        set;
    }


    [JsonPropertyName("month")]
    public int? Month
    {
        get;
        set;
    }


    [JsonPropertyName("day")]
    public int? Day
    {
        get;
        set;
    }


    [JsonPropertyName("hour")]
    public int? Hour
    {
        get;
        set;
    }


    [JsonPropertyName("minute")]
    public int? Minute
    {
        get;
        set;
    }


    [JsonPropertyName("second")]
    public int? Second
    {
        get;
        set;
    }
}

public sealed class RoleCombatLinksDto
{
    [JsonPropertyName("lineup_link")]
    public string? LineupLink
    {
        get;
        set;
    }


    [JsonPropertyName("lineup_link_pc")]
    public string? LineupLinkPc
    {
        get;
        set;
    }


    [JsonPropertyName("strategy_link")]
    public string? StrategyLink
    {
        get;
        set;
    }


    [JsonPropertyName("lineup_publish_link")]
    public string? LineupPublishLink
    {
        get;
        set;
    }


    [JsonPropertyName("lineup_publish_link_pc")]
    public string? LineupPublishLinkPc
    {
        get;
        set;
    }
}

public sealed record CycleChallengePayloadEnvelope(JsonElement? Entry, bool? IsUnlock);

public static class CycleChallengePayloadReader
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };


    public static CycleChallengePayloadEnvelope Read(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new CycleChallengePayloadEnvelope(null, null);
        }

        using var document = JsonDocument.Parse(payloadJson, DocumentOptions);
        var root = document.RootElement;


        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0)
            {
                return new CycleChallengePayloadEnvelope(null, null);
            }

            root = root[0];
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new CycleChallengePayloadEnvelope(null, null);
        }

        bool? isUnlock = TryReadBool(root, "is_unlock");


        var current = root;
        for (var depth = 0; depth < 3; depth++)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return new CycleChallengePayloadEnvelope(null, isUnlock);
            }

            isUnlock ??= TryReadBool(current, "is_unlock");

            if (!current.TryGetProperty("data", out var data))
            {
                return new CycleChallengePayloadEnvelope(current.Clone(), isUnlock);
            }

            switch (data.ValueKind)
            {
                case JsonValueKind.Array:
                    if (data.GetArrayLength() == 0)
                    {
                        return new CycleChallengePayloadEnvelope(null, isUnlock);
                    }

                    var first = data[0];
                    return first.ValueKind == JsonValueKind.Object
                        ? new CycleChallengePayloadEnvelope(first.Clone(), isUnlock)
                        : new CycleChallengePayloadEnvelope(null, isUnlock);

                case JsonValueKind.Object:
                    current = data;
                    break;

                default:
                    return new CycleChallengePayloadEnvelope(null, isUnlock);
            }
        }

        return new CycleChallengePayloadEnvelope(current.ValueKind == JsonValueKind.Object ? current.Clone() : null,
            isUnlock);
    }

    private static bool? TryReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }
}