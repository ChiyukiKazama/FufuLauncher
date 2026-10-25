/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models.CycleChallenge.Dto;
using FufuLauncher.Services.CycleChallenge;

namespace FufuLauncher.Models.CycleChallenge.Mapping;

public static class RoleCombatMapper
{
    private static readonly TimeSpan MaxRoundDuration = TimeSpan.FromMinutes(30);


    public static RoleCombatDisplay Parse(string? payloadJson, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return RoleCombatDisplay.CreateState(TheaterDisplayState.NotParticipated);
        }

        var envelope = CycleChallengePayloadReader.Read(payloadJson);
        if (envelope.Entry is not { } entryElement)
        {
            return RoleCombatDisplay.CreateState(TheaterDisplayState.NotParticipated);
        }

        var entry = entryElement.Deserialize<RoleCombatEntryDto>(CycleChallengePayloadReader.SerializerOptions);
        return Map(entry, envelope.IsUnlock, region);
    }


    public static RoleCombatDisplay Map(RoleCombatEntryDto? entry, bool? isUnlock, string? region = null)
    {
        var context = CycleChallengeTime.CreateContext(region);

        if (entry is null)
        {
            return RoleCombatDisplay.CreateState(isUnlock == false
                ? TheaterDisplayState.NotUnlocked
                : TheaterDisplayState.NotParticipated);
        }

        var hasData = entry.HasData ?? false;
        var hasDetailData = entry.HasDetailData ?? entry.Detail is not null;

        if (isUnlock == false || (!hasData && !hasDetailData))
        {
            return RoleCombatDisplay.CreateState(isUnlock == false
                ? TheaterDisplayState.NotUnlocked
                : TheaterDisplayState.NotParticipated);
        }

        if (!hasDetailData)
        {
            return BuildSummaryOnly(entry, context);
        }

        var detail = entry.Detail;
        var stat = entry.Stat ?? detail?.DetailStat;
        var schedule = entry.Schedule;

        var rounds = MapRounds(detail, stat, context);
        var backupAvatars = MapAvatars(detail?.BackupAvatars);
        var battleStats = MapBattleStats(detail?.FightStatistics);
        var heraldry = stat?.Heraldry ?? entry.Heraldry ?? 0;

        return new RoleCombatDisplay
        {
            State = TheaterDisplayState.Content,
            PeriodText = FormatPeriod(schedule, entry, context),
            DifficultyName = TheaterDifficulty.GetDisplayName(stat?.DifficultyId ?? entry.DifficultyId ?? 0),
            HeraldryIconPath = CycleChallengeIcons.TheaterHeraldry(heraldry),
            MaxRoundText = FormatMaxRound(stat, entry),
            MedalIcons = MapMedalIcons(rounds),
            CoinText = FormatNumber(stat?.CoinNum ?? 0),
            AvatarBonusText = FormatNumber(stat?.AvatarBonusNum ?? 0),
            RentText = FormatNumber(stat?.RentCount ?? 0),
            Rounds = rounds,
            BackupAvatars = backupAvatars,
            BattleStats = battleStats,
        };
    }


    private static RoleCombatDisplay BuildSummaryOnly(RoleCombatEntryDto entry, CycleChallengeTimeContext context)
    {
        var stat = entry.Stat ?? entry.Detail?.DetailStat;
        var schedule = entry.Schedule;
        var heraldry = stat?.Heraldry ?? entry.Heraldry ?? 0;

        return new RoleCombatDisplay
        {
            State = TheaterDisplayState.Content,
            PeriodText = FormatPeriod(schedule, entry, context),
            DifficultyName = TheaterDifficulty.GetDisplayName(stat?.DifficultyId ?? entry.DifficultyId ?? 0),
            HeraldryIconPath = CycleChallengeIcons.TheaterHeraldry(heraldry),
            MaxRoundText = FormatMaxRound(stat, entry),
            CoinText = FormatNumber(stat?.CoinNum ?? 0),
            AvatarBonusText = FormatNumber(stat?.AvatarBonusNum ?? 0),
            RentText = FormatNumber(stat?.RentCount ?? 0),
        };
    }

    private static List<TheaterRound> MapRounds(
        RoleCombatDetailDto? detail, RoleCombatStatDto? stat, CycleChallengeTimeContext context)
    {
        var roundsData = detail?.RoundsData;
        if (roundsData is null || roundsData.Count == 0)
        {
            return [];
        }

        var medalRoundList = stat?.GetMedalRoundList;
        var result = new List<TheaterRound>(roundsData.Count);


        long? lastFinishUnix = null;

        for (var index = 0; index < roundsData.Count; index++)
        {
            var round = roundsData[index];
            if (round is null)
            {
                continue;
            }

            var isGetMedal = round.IsGetMedal
                             ?? (medalRoundList is not null && index < medalRoundList.Count &&
                                 medalRoundList[index] != 0);
            var isTarot = round.IsTarot ?? false;
            var roundId = round.RoundId ?? 0;

            var finishUnix = ParseUnixSeconds(round.FinishTime);
            var durationText = string.Empty;
            if (lastFinishUnix is { } start && finishUnix is { } end && end >= start)
            {
                var delta = end - start;
                if (delta > 0 && delta <= (long)MaxRoundDuration.TotalSeconds)
                {
                    durationText = string.Format("Theater_UsedTime".GetLocalized(), FormatDuration(delta));
                }
            }

            if (finishUnix is not null)
            {
                lastFinishUnix = finishUnix;
            }

            var tarotIcon = isTarot
                ? CycleChallengeIcons.Tarot(isGetMedal)
                : string.Empty;

            result.Add(new TheaterRound(
                roundId,
                isGetMedal,
                isTarot,
                FormatRoundTitle(round, isTarot),
                CycleChallengeIcons.TheaterStar(isGetMedal),
                FormatFinishTime(round, context),
                durationText,
                tarotIcon,
                MapAvatars(round.Avatars),
                MapEnemies(round.Enemies),
                MapBuffs(round.ChoiceCards),
                round.SplendourBuff?.Summary?.Description ?? string.Empty,
                MapSplendourBuffs(round.SplendourBuff)));
        }

        return result;
    }


    private static string FormatMaxRound(RoleCombatStatDto? stat, RoleCombatEntryDto entry)
    {
        var maxRound = stat?.MaxRoundId ?? entry.MaxRoundId;
        var tarotFinished = stat?.TarotFinishedCount ?? 0;
        return tarotFinished > 0
            ? string.Format("Theater_MaxRoundWithTarot".GetLocalized(), maxRound, tarotFinished)
            : string.Format("Theater_RoundTitle".GetLocalized(), maxRound);
    }


    private static List<TheaterMedalIcon> MapMedalIcons(List<TheaterRound> rounds)
    {
        if (rounds.Count == 0)
        {
            return [];
        }

        var result = new List<TheaterMedalIcon>(rounds.Count);
        foreach (var round in rounds)
        {
            var icon = round.IsTarot
                ? CycleChallengeIcons.Tarot(round.IsGetMedal)
                : CycleChallengeIcons.TheaterStar(round.IsGetMedal);
            result.Add(new TheaterMedalIcon(icon));
        }

        return result;
    }

    private static List<TheaterAvatar> MapAvatars(List<RoleCombatAvatarDto>? avatars)
    {
        if (avatars is null || avatars.Count == 0)
        {
            return [];
        }

        var result = new List<TheaterAvatar>(avatars.Count);
        foreach (var avatar in avatars)
        {
            if (avatar is null)
            {
                continue;
            }

            result.Add(new TheaterAvatar(
                avatar.AvatarId ?? 0,
                avatar.Image,
                avatar.Level ?? 0,
                avatar.Rarity ?? 0,
                avatar.Name,
                avatar.AvatarType ?? 1));
        }

        return result;
    }

    private static List<TheaterEnemy> MapEnemies(List<RoleCombatEnemyDto>? enemies)
    {
        if (enemies is null || enemies.Count == 0)
        {
            return [];
        }

        var result = new List<TheaterEnemy>(enemies.Count);
        foreach (var enemy in enemies)
        {
            if (enemy is null)
            {
                continue;
            }


            if (string.IsNullOrWhiteSpace(enemy.Name) && string.IsNullOrWhiteSpace(enemy.Icon))
            {
                continue;
            }

            result.Add(new TheaterEnemy(enemy.Name, enemy.Icon, enemy.Level ?? 0));
        }

        return result;
    }


    private static List<TheaterBuff> MapBuffs(List<RoleCombatBuffDto>? buffs)
    {
        if (buffs is null || buffs.Count == 0)
        {
            return [];
        }

        var result = new List<TheaterBuff>(buffs.Count);
        foreach (var buff in buffs)
        {
            if (buff is null)
            {
                continue;
            }

            var name = NormalizeRichText(buff.Name);
            var description = NormalizeRichText(buff.Description);
            if (name.Length == 0 && description.Length == 0 && string.IsNullOrWhiteSpace(buff.Icon))
            {
                continue;
            }

            result.Add(new TheaterBuff(buff.Icon, name, description, buff.IsEnhanced ?? false));
        }

        return result;
    }


    private static List<TheaterSplendourBuff> MapSplendourBuffs(RoleCombatSplendourBuffDto? splendour)
    {
        if (splendour?.Buffs is null || splendour.Buffs.Count == 0)
        {
            return [];
        }

        var result = new List<TheaterSplendourBuff>(splendour.Buffs.Count);
        foreach (var item in splendour.Buffs)
        {
            if (item is null)
            {
                continue;
            }

            var effects = new List<string>();
            if (item.LevelEffects is { Count: > 0 })
            {
                foreach (var effect in item.LevelEffects)
                {
                    if (effect is null)
                    {
                        continue;
                    }

                    var text = NormalizeRichText(effect.Description);
                    if (text.Length > 0)
                    {
                        effects.Add(text);
                    }
                }
            }

            var name = NormalizeRichText(item.Name);
            if (name.Length == 0 && string.IsNullOrWhiteSpace(item.Icon))
            {
                continue;
            }

            result.Add(new TheaterSplendourBuff(item.Icon, name, item.Level ?? 0, effects));
        }

        return result;
    }


    private static string NormalizeRichText(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace("\\n", "\n", StringComparison.Ordinal);

    private static TheaterBattleStats? MapBattleStats(RoleCombatFightStatisticsDto? fight)
    {
        if (fight is null)
        {
            return null;
        }

        var totalUseTime = fight.TotalUseTime ?? 0;
        var isShow = fight.IsShowBattleStats ?? false;


        if (!isShow && totalUseTime <= 0)
        {
            return null;
        }

        var shortest = new List<TheaterAvatarDamage>();
        if (fight.ShortestAvatarList is { Count: > 0 })
        {
            foreach (var item in fight.ShortestAvatarList)
            {
                if (item is not null && (item.AvatarId ?? 0) > 0)
                {
                    shortest.Add(new TheaterAvatarDamage(item.AvatarId ?? 0, item.AvatarIcon, item.Rarity ?? 0,
                        ParseValue(item.Value)));
                }
            }
        }

        return new TheaterBattleStats(
            totalUseTime > 0 ? FormatDuration(totalUseTime) : string.Empty,
            MapAvatarDamage(fight.MaxDefeatAvatar),
            MapAvatarDamage(fight.MaxDamageAvatar),
            MapAvatarDamage(fight.MaxTakeDamageAvatar),
            shortest);
    }

    private static TheaterAvatarDamage? MapAvatarDamage(RoleCombatAvatarStatisticsDto? avatar)
    {
        if (avatar is null || (avatar.AvatarId ?? 0) <= 0)
        {
            return null;
        }

        return new TheaterAvatarDamage(avatar.AvatarId ?? 0, avatar.AvatarIcon, avatar.Rarity ?? 0,
            ParseValue(avatar.Value));
    }


    private static long ParseValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }


    private static long? ParseUnixSeconds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
               parsed > 0
            ? parsed
            : null;
    }


    private static string FormatRoundTitle(RoleCombatRoundDto round, bool isTarot)
    {
        if (!isTarot)
        {
            return string.Format("Theater_RoundTitle".GetLocalized(), round.RoundId ?? 0);
        }

        var serial = round.TarotSerialNumber ?? 0;
        return serial > 0
            ? string.Format("Theater_TarotTitle".GetLocalized(), ToRoman(serial))
            : "Theater_TarotTitleShort".GetLocalized();
    }


    private static string FormatFinishTime(RoleCombatRoundDto round, CycleChallengeTimeContext context)
    {
        if (round.FinishDateTime is { } dateTime
            && (dateTime.Year ?? 0) > 0
            && (dateTime.Month ?? 0) > 0
            && (dateTime.Day ?? 0) > 0)
        {
            var wallClock = new DateTime(
                dateTime.Year ?? 1,
                dateTime.Month ?? 1,
                dateTime.Day ?? 1,
                Math.Clamp(dateTime.Hour ?? 0, 0, 23),
                Math.Clamp(dateTime.Minute ?? 0, 0, 59),
                Math.Clamp(dateTime.Second ?? 0, 0, 59),
                DateTimeKind.Unspecified);
            return string.Format(
                "Theater_FinishAt".GetLocalized(),
                CycleChallengeTime.FormatWallClock(wallClock, "MM-dd HH:mm", context));
        }

        var unix = ParseUnixSeconds(round.FinishTime);
        if (unix is null)
        {
            return string.Empty;
        }

        return string.Format(
            "Theater_FinishAt".GetLocalized(),
            CycleChallengeTime.FormatUnix(unix.Value, "MM-dd HH:mm", context));
    }


    private static string FormatPeriod(
        RoleCombatScheduleDto? schedule, RoleCombatEntryDto entry, CycleChallengeTimeContext context)
    {
        var start = FormatUnixTime(schedule?.StartTime ?? entry.StartTime, context);
        var end = FormatUnixTime(schedule?.EndTime ?? entry.EndTime, context);
        if (start.Length == 0)
        {
            start = FormatDateTime(schedule?.StartDateTime, context);
        }

        if (end.Length == 0)
        {
            end = FormatDateTime(schedule?.EndDateTime, context);
        }

        if (start.Length > 0 && end.Length > 0)
        {
            return $"{start} - {end}";
        }

        return start.Length > 0 ? start : end;
    }

    private static string FormatDateTime(RoleCombatDateTimeDto? dateTime, CycleChallengeTimeContext context)
    {
        if (dateTime is null || (dateTime.Year ?? 0) <= 0 || (dateTime.Month ?? 0) <= 0 || (dateTime.Day ?? 0) <= 0)
        {
            return string.Empty;
        }

        var wallClock = new DateTime(
            dateTime.Year ?? 1,
            dateTime.Month ?? 1,
            dateTime.Day ?? 1,
            0,
            0,
            0,
            DateTimeKind.Unspecified);
        return CycleChallengeTime.FormatWallClock(wallClock, "yyyy.MM.dd", context);
    }

    private static string FormatUnixTime(string? raw, CycleChallengeTimeContext context)
    {
        var unix = ParseUnixSeconds(raw);
        if (unix is null)
        {
            return string.Empty;
        }

        return CycleChallengeTime.FormatUnix(unix.Value, "yyyy.MM.dd", context);
    }


    internal static string FormatDuration(long seconds)
    {
        if (seconds <= 0)
        {
            return string.Empty;
        }

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture,
                $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes:D2}:{span.Seconds:D2}");
    }

    private static string FormatNumber(int? value) =>
        value is null ? "--" : (value.Value > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : "0");


    private static string ToRoman(int value)
    {
        string[] romans =
        [
            "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X",
            "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX",
        ];

        return value >= 1 && value <= romans.Length
            ? romans[value - 1]
            : value.ToString(CultureInfo.InvariantCulture);
    }
}