/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json.Serialization;

namespace FufuLauncher.Models.CycleChallenge.Dto;

public sealed class HardChallengeResponseDto
{
    [JsonPropertyName("data")]
    public List<HardChallengeEntryDto>? Data
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
    public HardChallengeLinksDto? Links
    {
        get;
        set;
    }
}

public sealed class HardChallengeEntryDto
{
    [JsonPropertyName("schedule")]
    public HardChallengeScheduleDto? Schedule
    {
        get;
        set;
    }


    [JsonPropertyName("single")]
    public HardChallengeModeDto? Single
    {
        get;
        set;
    }


    [JsonPropertyName("mp")]
    public HardChallengeModeDto? MultiPlayer
    {
        get;
        set;
    }


    [JsonPropertyName("blings")]
    public List<HardChallengeBlingAvatarDto>? Blings
    {
        get;
        set;
    }
}

public sealed class HardChallengeScheduleDto
{
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


    [JsonPropertyName("start_date_time")]
    public HardChallengeDateTimeDto? StartDateTime
    {
        get;
        set;
    }


    [JsonPropertyName("end_date_time")]
    public HardChallengeDateTimeDto? EndDateTime
    {
        get;
        set;
    }


    [JsonPropertyName("is_valid")]
    public bool? IsValid
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
}

public sealed class HardChallengeModeDto
{
    [JsonPropertyName("best")]
    public HardChallengeBestDto? Best
    {
        get;
        set;
    }


    [JsonPropertyName("challenge")]
    public List<HardChallengeChallengeDto>? Challenges
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
}

public sealed class HardChallengeBestDto
{
    [JsonPropertyName("difficulty")]
    public int? Difficulty
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


    [JsonPropertyName("icon")]
    public string? Icon
    {
        get;
        set;
    }
}

public sealed class HardChallengeChallengeDto
{
    [JsonPropertyName("name")]
    public string? Name
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


    [JsonPropertyName("teams")]
    public List<HardChallengeAvatarDto>? Teams
    {
        get;
        set;
    }


    [JsonPropertyName("best_avatar")]
    public List<HardChallengeBestAvatarDto>? BestAvatars
    {
        get;
        set;
    }


    [JsonPropertyName("monster")]
    public HardChallengeMonsterDto? Monster
    {
        get;
        set;
    }
}

public sealed class HardChallengeAvatarDto
{
    [JsonPropertyName("avatar_id")]
    public int? AvatarId
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


    [JsonPropertyName("rank")]
    public int? Rank
    {
        get;
        set;
    }
}

public sealed class HardChallengeBestAvatarDto
{
    [JsonPropertyName("avatar_id")]
    public int? AvatarId
    {
        get;
        set;
    }


    [JsonPropertyName("side_icon")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? SideIcon
    {
        get;
        set;
    }


    [JsonPropertyName("dps")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Dps
    {
        get;
        set;
    }


    [JsonPropertyName("type")]
    public int? Type
    {
        get;
        set;
    }
}

public sealed class HardChallengeMonsterDto
{
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


    [JsonPropertyName("icon")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Icon
    {
        get;
        set;
    }


    [JsonPropertyName("desc")]
    public List<string>? Descriptions
    {
        get;
        set;
    }


    [JsonPropertyName("tags")]
    public List<HardChallengeMonsterTagDto>? Tags
    {
        get;
        set;
    }


    [JsonPropertyName("monster_id")]
    public int? MonsterId
    {
        get;
        set;
    }
}

public sealed class HardChallengeMonsterTagDto
{
    [JsonPropertyName("type")]
    public int? Type
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

public sealed class HardChallengeBlingAvatarDto
{
    [JsonPropertyName("avatar_id")]
    public int? AvatarId
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


    [JsonPropertyName("rarity")]
    public int? Rarity
    {
        get;
        set;
    }


    [JsonPropertyName("is_plus")]
    public bool? IsPlus
    {
        get;
        set;
    }
}

public sealed class HardChallengeLinksDto
{
    [JsonPropertyName("lineup_link")]
    public string? LineupLink
    {
        get;
        set;
    }


    [JsonPropertyName("play_link")]
    public string? PlayLink
    {
        get;
        set;
    }
}

public sealed class HardChallengeDateTimeDto
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