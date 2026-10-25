/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models.CycleChallenge;

public enum StygianDisplayState
{
    Content = 0,


    NotUnlocked,


    NotParticipated,


    Error,
}

public static class StygianDifficulty
{
    public static string GetDisplayName(int difficulty) => difficulty switch
    {
        1 => "Stygian_Difficulty_1".GetLocalized(),
        2 => "Stygian_Difficulty_2".GetLocalized(),
        3 => "Stygian_Difficulty_3".GetLocalized(),
        4 => "Stygian_Difficulty_4".GetLocalized(),
        5 => "Stygian_Difficulty_5".GetLocalized(),
        6 => "Stygian_Difficulty_6".GetLocalized(),
        _ => "Stygian_Difficulty_Unknown".GetLocalized(),
    };
}

public sealed record StygianAvatar(int AvatarId, string? IconUrl, int Level, int Rarity, string? Name, int Rank)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "?" : Name!;


    public bool HasRank => Rank > 0;


    public string RankText => Rank > 0 ? Rank.ToString(CultureInfo.InvariantCulture) : string.Empty;
}

public sealed record StygianBestAvatar(int AvatarId, string? IconUrl, string ValueText, string TypeText, int Type)
{
    public bool HasTypeText => TypeText.Length > 0;


    public bool HasValue => ValueText.Length > 0;
}

public sealed record StygianMonster(string? Name, string? IconUrl, int Level, IReadOnlyList<string> Descriptions)
{
    public bool IsPresent => !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(IconUrl);


    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "?" : Name!;


    public string LevelText => Level > 0 ? $"Lv.{Level}" : string.Empty;


    public bool HasLevel => Level > 0;


    public bool HasDescriptions => Descriptions.Count > 0;


    public static StygianMonster Empty
    {
        get;
    } = new(null, null, 0, []);
}

public sealed record StygianChallenge(
    string Name,
    int Second,
    string TimeText,
    IReadOnlyList<StygianAvatar> Team,
    IReadOnlyList<StygianBestAvatar> BestAvatars,
    StygianMonster Monster)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "?" : Name!;


    public bool HasTeam => Team.Count > 0;


    public bool HasBestAvatars => BestAvatars.Count > 0;
}

public sealed record StygianMode(
    bool HasData,
    string DifficultyName,
    string DifficultyIconPath,
    string BestTimeText,
    string ExtremeValueText,
    IReadOnlyList<StygianChallenge> Challenges)
{
    public bool HasDifficulty => DifficultyName.Length > 0 &&
                                 DifficultyName != "Stygian_Difficulty_Unknown".GetLocalized();


    public bool HasDifficultyIcon => DifficultyIconPath.Length > 0;


    public bool HasBestTime => BestTimeText.Length > 0;


    public bool HasExtremeValue => ExtremeValueText.Length > 0;


    public bool HasChallenges => Challenges.Count > 0;


    public string EmptyText => HasData ? string.Empty : "Stygian_NoCoopData".GetLocalized();


    public static StygianMode CreateEmpty(bool hasData = false) =>
        new(hasData, string.Empty, string.Empty, string.Empty, string.Empty, []);
}

public sealed record StygianBling(int AvatarId, string? IconUrl, int Rarity, string? Name, bool IsPlus)
{
    public double IconOpacity => IsPlus ? 1d : 0.45d;


    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "?" : Name!;
}

public sealed record HardChallengeDisplay
{
    public StygianDisplayState State
    {
        get;
        init;
    } = StygianDisplayState.NotParticipated;


    public string ErrorMessage
    {
        get;
        init;
    } = string.Empty;


    public string ScheduleName
    {
        get;
        init;
    } = string.Empty;


    public string PeriodText
    {
        get;
        init;
    } = string.Empty;


    public StygianMode Solo
    {
        get;
        init;
    } = StygianMode.CreateEmpty();


    public StygianMode Coop
    {
        get;
        init;
    } = StygianMode.CreateEmpty();


    public IReadOnlyList<StygianBling> Blings
    {
        get;
        init;
    } = [];


    public bool ShowContent => State == StygianDisplayState.Content;


    public bool HasState => State != StygianDisplayState.Content;


    public string StateTitle => State switch
    {
        StygianDisplayState.NotUnlocked => "CycleChallenge_NotUnlocked".GetLocalized(),
        StygianDisplayState.NotParticipated => "CycleChallenge_NotParticipated".GetLocalized(),
        StygianDisplayState.Error => "CycleChallenge_LoadFailed".GetLocalized(),
        _ => string.Empty,
    };


    public string StateDetail => State == StygianDisplayState.Error ? ErrorMessage : string.Empty;


    public bool HasStateDetail => StateDetail.Length > 0;


    public bool HasPeriod => PeriodText.Length > 0;


    public bool HasScheduleName => ScheduleName.Length > 0;


    public bool HasCoopData => Coop.HasData;


    public bool HasBlings => Blings.Count > 0;


    public static HardChallengeDisplay CreateState(StygianDisplayState state, string? detail = null) => new()
    {
        State = state,
        ErrorMessage = detail ?? string.Empty,
    };


    public static HardChallengeDisplay CreateError(string message) =>
        CreateState(StygianDisplayState.Error, message);
}