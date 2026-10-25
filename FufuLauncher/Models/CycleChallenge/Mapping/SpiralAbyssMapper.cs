/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text.Json;
using FufuLauncher.Models.CycleChallenge.Dto;

namespace FufuLauncher.Models.CycleChallenge.Mapping;

public static class SpiralAbyssMapper
{
    public static SpiralAbyssDisplay Map(SpiralAbyssDto? dto, CycleChallengeTimeContext? timeContext = null)
    {
        var context = timeContext ?? CycleChallengeTimeContext.Default;
        if (dto is null)
        {
            return SpiralAbyssDisplay.Empty(timeContext: context);
        }


        var floors = new List<SpiralAbyssFloor>();
        foreach (var floorDto in dto.Floors ?? new List<SpiralAbyssFloorDto>())
        {
            if (floorDto is null)
            {
                continue;
            }

            floors.Add(MapFloor(floorDto, context));
        }

        floors.Sort(static (left, right) => left.Index.CompareTo(right.Index));


        var totalStar = Math.Max(dto.TotalStar, 0);
        var (maxFloorValue, maxFloorText) = ResolveMaxFloor(dto.MaxFloor, floors);


        var strongestHit = 0L;
        foreach (var rank in dto.DamageRank ?? new List<SpiralAbyssRankDto>())
        {
            if (rank is null)
            {
                continue;
            }

            strongestHit = Math.Max(rank.Value, 0L);
            break;
        }

        return new SpiralAbyssDisplay(
            dto.Uid?.Trim() ?? string.Empty,
            dto.ScheduleId?.Trim() ?? string.Empty,
            ParseTime(dto.StartTime),
            ParseTime(dto.EndTime),
            totalStar,
            SpiralAbyssConstants.MaxTotalStar,
            maxFloorValue,
            maxFloorText,
            Math.Max(dto.TotalBattleTimes, 0),
            Math.Max(dto.TotalWinTimes, 0),
            strongestHit,
            dto.IsUnlock,
            dto.IsJustSkippedFloor,
            string.IsNullOrWhiteSpace(dto.SkippedFloor) ? null : dto.SkippedFloor.Trim(),
            floors,
            MapRank(SpiralAbyssRankKind.Reveal, dto.RevealRank),
            MapRank(SpiralAbyssRankKind.Defeat, dto.DefeatRank),
            MapRank(SpiralAbyssRankKind.Damage, dto.DamageRank),
            MapRank(SpiralAbyssRankKind.TakeDamage, dto.TakeDamageRank),
            MapRank(SpiralAbyssRankKind.NormalSkill, dto.NormalSkillRank),
            MapRank(SpiralAbyssRankKind.EnergySkill, dto.EnergySkillRank),
            context);
    }

    private static SpiralAbyssFloor MapFloor(SpiralAbyssFloorDto dto, CycleChallengeTimeContext context)
    {
        var levels = new List<SpiralAbyssLevel>();
        foreach (var levelDto in dto.Levels ?? new List<SpiralAbyssLevelDto>())
        {
            if (levelDto is null)
            {
                continue;
            }

            levels.Add(MapLevel(levelDto, context));
        }

        levels.Sort(static (left, right) => left.Index.CompareTo(right.Index));


        var leyLineCommon = NormalizeTextList(dto.LeyLineDisorder);
        var leyLineUpper = NormalizeTextList(dto.LeyLineDisorderUpper);
        var leyLineLower = NormalizeTextList(dto.LeyLineDisorderLower);

        return new SpiralAbyssFloor(
            dto.Index,
            dto.IsUnlock,
            Math.Max(dto.Star, 0),
            Math.Max(dto.MaxStar, 0),
            leyLineCommon,
            leyLineUpper,
            leyLineLower,
            levels);
    }

    private static SpiralAbyssLevel MapLevel(SpiralAbyssLevelDto dto, CycleChallengeTimeContext context)
    {
        var battles = new List<SpiralAbyssBattle>();
        foreach (var battleDto in dto.Battles ?? new List<SpiralAbyssBattleDto>())
        {
            if (battleDto is null)
            {
                continue;
            }

            battles.Add(MapBattle(battleDto, context));
        }


        battles.Sort(static (left, right) => left.Index.CompareTo(right.Index));

        return new SpiralAbyssLevel(
            dto.Index,
            Math.Max(dto.Star, 0),
            Math.Max(dto.MaxStar, 0),
            battles);
    }

    private static SpiralAbyssBattle MapBattle(SpiralAbyssBattleDto dto, CycleChallengeTimeContext context)
    {
        var avatars = new List<SpiralAbyssAvatar>();
        foreach (var avatarDto in dto.Avatars ?? new List<SpiralAbyssAvatarDto>())
        {
            if (avatarDto is null)
            {
                continue;
            }

            avatars.Add(new SpiralAbyssAvatar(
                avatarDto.Id,
                NormalizeIconUrl(avatarDto.Icon),
                Math.Max(avatarDto.Level, 0),
                Math.Max(avatarDto.Rarity, 0)));
        }

        return new SpiralAbyssBattle(
            dto.Index,
            ParseDateTimeElement(dto.SettleDateTime, context),
            ParseTime(dto.Timestamp),
            avatars,
            context);
    }

    private static SpiralAbyssRankSection MapRank(SpiralAbyssRankKind kind, List<SpiralAbyssRankDto>? rankDtos)
    {
        var rows = new List<SpiralAbyssRankRow>();
        foreach (var rankDto in rankDtos ?? new List<SpiralAbyssRankDto>())
        {
            if (rankDto is null)
            {
                continue;
            }

            rows.Add(new SpiralAbyssRankRow(
                rankDto.AvatarId,
                NormalizeIconUrl(rankDto.AvatarIcon),
                Math.Max(rankDto.Rarity, 0),
                rankDto.Value));
        }

        return new SpiralAbyssRankSection(kind, rows);
    }


    private static (int Value, string Text) ResolveMaxFloor(string? raw, IReadOnlyList<SpiralAbyssFloor> floors)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var text = raw.Trim();
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            {
                var value = Math.Max(numeric, 0);
                return (value, value > 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            }

            var head = text.Split('-', '－')[0];
            if (int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var floorNo))
            {
                return (Math.Max(floorNo, 0), text);
            }

            return (0, text);
        }

        var deepest = 0;
        foreach (var floor in floors)
        {
            if (floor.Index > deepest)
            {
                deepest = floor.Index;
            }
        }

        return (deepest, deepest > 0 ? deepest.ToString(CultureInfo.InvariantCulture) : string.Empty);
    }


    private static DateTimeOffset? ParseTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            if (unix <= 0)
            {
                return null;
            }

            return unix > 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                : DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var unixDouble) &&
            unixDouble > 0)
        {
            return unixDouble > 100_000_000_000d
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)unixDouble)
                : DateTimeOffset.FromUnixTimeSeconds((long)unixDouble);
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dateTime))
        {
            return new DateTimeOffset(dateTime, TimeSpan.Zero);
        }

        return null;
    }


    private static DateTime? ParseDateTimeElement(JsonElement? element, CycleChallengeTimeContext context)
    {
        if (element is not { } value)
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var year = ReadInt(value, "year");
                var month = ReadInt(value, "month");
                var day = ReadInt(value, "day");
                if (year is not > 0 || month is not > 0 || day is not > 0)
                {
                    return null;
                }

                var hour = ReadInt(value, "hour") ?? 0;
                var minute = ReadInt(value, "minute") ?? 0;
                var second = ReadInt(value, "second") ?? 0;
                try
                {
                    return new DateTime(
                        year.Value,
                        month.Value,
                        day.Value,
                        Math.Clamp(hour, 0, 23),
                        Math.Clamp(minute, 0, 59),
                        Math.Clamp(second, 0, 59),
                        DateTimeKind.Unspecified);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }

            case JsonValueKind.String:
            {
                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    break;
                }


                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var wall)
                    && wall.Kind == DateTimeKind.Unspecified)
                {
                    return wall;
                }


                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                        out var parsed))
                {
                    return parsed.ToOffset(context.SourceOffset).DateTime;
                }

                break;
            }

            case JsonValueKind.Number:
                if (value.TryGetInt64(out var unix) && unix > 0)
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unix).ToOffset(context.SourceOffset).DateTime;
                }

                break;
        }

        return null;
    }


    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            var pascalName = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
            if (!element.TryGetProperty(pascalName, out property))
            {
                return null;
            }
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }


    private static List<string> NormalizeTextList(List<string>? texts)
    {
        var result = new List<string>();
        foreach (var text in texts ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text.Trim());
            }
        }

        return result;
    }


    private static string? NormalizeIconUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        if (text.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + text;
        }

        return text;
    }
}