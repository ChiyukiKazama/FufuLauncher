/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using FufuLauncher.Services.CycleChallenge;

namespace FufuLauncher.Models.CycleChallenge;

public static class SpiralAbyssConstants
{
    public const int MaxTotalStar = 36;


    public const int MaxStarPerFloor = 9;
}

public enum SpiralAbyssRankKind
{
    Reveal = 0,


    Defeat = 1,


    Damage = 2,


    TakeDamage = 3,


    NormalSkill = 4,


    EnergySkill = 5,
}

public sealed record SpiralAbyssAvatar(int AvatarId, string? IconUrl, int Level, int Rarity);

public sealed record SpiralAbyssBattle(
    int Index,
    DateTime? SettleTime,
    DateTimeOffset? Timestamp,
    IReadOnlyList<SpiralAbyssAvatar> Avatars,
    CycleChallengeTimeContext TimeContext)
{
    public bool IsUpperHalf => Index == 1;


    public bool IsLowerHalf => Index == 2;


    public string TimeText => SettleTime is { } settleTime
        ? CycleChallengeTime.FormatWallClock(settleTime, "MM-dd HH:mm", TimeContext)
        : Timestamp is { } timestamp
            ? CycleChallengeTime.Format(timestamp, "MM-dd HH:mm", TimeContext)
            : "--";


    public bool HasAvatars => Avatars.Count > 0;
}

public sealed record SpiralAbyssLevel(int Index, int Star, int MaxStar, IReadOnlyList<SpiralAbyssBattle> Battles)
{
    public string StarText => MaxStar > 0
        ? $"{Star.ToString(CultureInfo.InvariantCulture)}/{MaxStar.ToString(CultureInfo.InvariantCulture)}"
        : Star.ToString(CultureInfo.InvariantCulture);


    public string IndexText => Index.ToString(CultureInfo.InvariantCulture);


    public SpiralAbyssBattle? UpperBattle => FindBattle(1);


    public SpiralAbyssBattle? LowerBattle => FindBattle(2);


    public IReadOnlyList<SpiralAbyssAvatar> UpperAvatars => UpperBattle?.Avatars ?? Array.Empty<SpiralAbyssAvatar>();


    public IReadOnlyList<SpiralAbyssAvatar> LowerAvatars => LowerBattle?.Avatars ?? Array.Empty<SpiralAbyssAvatar>();


    public string UpperTimeText => UpperBattle?.TimeText ?? "--";


    public string LowerTimeText => LowerBattle?.TimeText ?? "--";


    public bool HasUpperBattle => UpperBattle is not null;


    public bool HasLowerBattle => LowerBattle is not null;

    private SpiralAbyssBattle? FindBattle(int battleIndex)
    {
        foreach (var battle in Battles)
        {
            if (battle.Index == battleIndex)
            {
                return battle;
            }
        }

        return null;
    }
}

public sealed record SpiralAbyssFloor(
    int Index,
    bool IsUnlocked,
    int Star,
    int MaxStar,
    IReadOnlyList<string> LeyLineDisorder,
    IReadOnlyList<string> LeyLineDisorderUpper,
    IReadOnlyList<string> LeyLineDisorderLower,
    IReadOnlyList<SpiralAbyssLevel> Levels)
{
    public string IndexText => Index.ToString(CultureInfo.InvariantCulture);


    public string StarText => MaxStar > 0
        ? $"{Star.ToString(CultureInfo.InvariantCulture)}/{MaxStar.ToString(CultureInfo.InvariantCulture)}"
        : Star.ToString(CultureInfo.InvariantCulture);


    public string AbyssStar => CycleChallengeIcons.AbyssStar();


    public bool HasCommonLeyLine => LeyLineDisorder.Count > 0;


    public bool HasUpperLeyLine => LeyLineDisorderUpper.Count > 0;


    public bool HasLowerLeyLine => LeyLineDisorderLower.Count > 0;


    public bool ShowSplitLeyLine => HasUpperLeyLine || HasLowerLeyLine;


    public bool ShowCommonLeyLine => !ShowSplitLeyLine && HasCommonLeyLine;


    public bool HasLeyLineDisorder => ShowSplitLeyLine || ShowCommonLeyLine;


    public string LeyLineText => string.Join(Environment.NewLine, LeyLineDisorder);


    public string UpperLeyLineText => string.Join(Environment.NewLine, LeyLineDisorderUpper);


    public string LowerLeyLineText => string.Join(Environment.NewLine, LeyLineDisorderLower);


    public bool HasLevels => Levels.Count > 0;


    public bool IsLocked => !IsUnlocked;
}

public sealed record SpiralAbyssRankRow(int AvatarId, string? IconUrl, int Rarity, long Value)
{
    public string ValueText => Value > 0 ? Value.ToString("N0", CultureInfo.InvariantCulture) : "--";
}

public sealed record SpiralAbyssRankSection(SpiralAbyssRankKind Kind, IReadOnlyList<SpiralAbyssRankRow> Rows)
{
    public bool HasRows => Rows.Count > 0;
}

public sealed record SpiralAbyssDisplay(
    string Uid,
    string ScheduleId,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    int TotalStar,
    int MaxTotalStar,
    int MaxFloor,
    string MaxFloorText,
    int TotalBattleTimes,
    int TotalWinTimes,
    long StrongestHitValue,
    bool IsUnlocked,
    bool IsJustSkippedFloor,
    string? SkippedFloor,
    IReadOnlyList<SpiralAbyssFloor> Floors,
    SpiralAbyssRankSection RevealRank,
    SpiralAbyssRankSection DefeatRank,
    SpiralAbyssRankSection DamageRank,
    SpiralAbyssRankSection TakeDamageRank,
    SpiralAbyssRankSection NormalSkillRank,
    SpiralAbyssRankSection EnergySkillRank,
    CycleChallengeTimeContext TimeContext)
{
    public string StarText =>
        $"{TotalStar.ToString(CultureInfo.InvariantCulture)}/{MaxTotalStar.ToString(CultureInfo.InvariantCulture)}";


    public string MaxFloorDisplay => string.IsNullOrWhiteSpace(MaxFloorText) ? "--" : MaxFloorText;


    public string BattleTimesText => TotalBattleTimes.ToString(CultureInfo.InvariantCulture);


    public string WinTimesText => TotalWinTimes.ToString(CultureInfo.InvariantCulture);


    public string StrongestHitText => StrongestHitValue > 0
        ? StrongestHitValue.ToString("N0", CultureInfo.InvariantCulture)
        : "--";


    public bool HasStrongestHit => StrongestHitValue > 0;


    public string PeriodText
    {
        get
        {
            if (StartTime is not { } start || EndTime is not { } end)
            {
                return string.Empty;
            }

            return
                $"{CycleChallengeTime.Format(start, "yyyy-MM-dd", TimeContext)} ~ {CycleChallengeTime.Format(end, "yyyy-MM-dd", TimeContext)}";
        }
    }


    public bool HasFloors => Floors.Count > 0;


    public bool ShowSkippedFloorHint => IsJustSkippedFloor || !string.IsNullOrWhiteSpace(SkippedFloor);


    public string SkippedFloorText => string.IsNullOrWhiteSpace(SkippedFloor) ? "--" : SkippedFloor;


    public bool HasAnyRank => RevealRank.HasRows || DefeatRank.HasRows || DamageRank.HasRows
                              || TakeDamageRank.HasRows || NormalSkillRank.HasRows || EnergySkillRank.HasRows;


    public static SpiralAbyssDisplay Empty(
        string? uid = null,
        string? scheduleId = null,
        CycleChallengeTimeContext? timeContext = null)
    {
        return new SpiralAbyssDisplay(
            uid ?? string.Empty,
            scheduleId ?? string.Empty,
            null,
            null,
            0,
            SpiralAbyssConstants.MaxTotalStar,
            0,
            string.Empty,
            0,
            0,
            0,
            true,
            false,
            null,
            [],
            new SpiralAbyssRankSection(SpiralAbyssRankKind.Reveal, []),
            new SpiralAbyssRankSection(SpiralAbyssRankKind.Defeat, []),
            new SpiralAbyssRankSection(SpiralAbyssRankKind.Damage, []),
            new SpiralAbyssRankSection(SpiralAbyssRankKind.TakeDamage, []),
            new SpiralAbyssRankSection(SpiralAbyssRankKind.NormalSkill, []),
            new SpiralAbyssRankSection(SpiralAbyssRankKind.EnergySkill, []),
            timeContext ?? CycleChallengeTimeContext.Default);
    }
}