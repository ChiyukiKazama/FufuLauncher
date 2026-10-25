/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Services.CycleChallenge;

public static class CycleChallengeIcons
{
    public const string IconFolderUri = "ms-appx:///Assets/CycleChallenge/Icons/";


    private const string StygianMedalPrefix = "UI_LeyLineChallenge_Medal_";


    private const string TheaterHeraldryPrefix = "UI_RoleCombat_Medal_S_";


    public static string GetLocal(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return string.Empty;
        }

        string name = iconName.Trim();


        if (name.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ms-appdata://", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }


        int slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0 && slash < name.Length - 1)
        {
            name = name[(slash + 1)..];
        }

        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Length == 0 ? string.Empty : IconFolderUri + name + ".png";
    }


    public static string DifficultyMedal(int difficulty, string? iconName = null)
    {
        string fromApi = GetLocal(iconName);
        if (fromApi.Length > 0)
        {
            return fromApi;
        }

        int index = Math.Clamp(difficulty, 0, 7);
        return GetLocal(StygianMedalPrefix + index);
    }


    public static string TheaterHeraldry(int heraldry)
    {
        int index = Math.Clamp(heraldry, 0, 5);
        return GetLocal(TheaterHeraldryPrefix + index);
    }


    public static string Tarot(bool gotMedal)
    {
        return GetLocal(gotMedal
            ? "UI_Icon_RoleCombat_Reward_Tarot_04"
            : "UI_Icon_RoleCombat_Reward_Tarot_05");
    }


    public static string AbyssStar()
    {
        return GetLocal("UI_Icon_Tower_Star");
    }


    public static string TheaterStar(bool got)
    {
        return GetLocal(got ? "UI_Icon_Tower_Star1" : "UI_Icon_Tower_Star2");
    }
}