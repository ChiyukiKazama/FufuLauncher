/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Models;

public sealed class GachaHistoryItemDisplayItem
{
    public string Name { get; init; } = string.Empty;

    public string ImageUrl { get; init; } = "ms-appx:///Assets/StoreLogo.png";

    public int Rank { get; init; }

    public int PullCount { get; init; }

    public string Badge => PullCount > 0 ? $"×{PullCount}" : "UP";

    public SolidColorBrush RarityBackground => Rank switch
    {
        5 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 198, 160, 96)),
        4 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 149, 118, 193)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 168, 209))
    };
}

public sealed class GachaHistoryPoolDisplayItem
{
    public string Title { get; init; } = string.Empty;

    public string Period { get; init; } = string.Empty;

    public string TotalPulls { get; init; } = string.Empty;

    public IReadOnlyList<GachaHistoryItemDisplayItem> FeaturedItems { get; init; } = [];
}

public sealed class GachaHistoryPeriodDisplayItem
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DateRange { get; init; } = string.Empty;

    public string TotalPulls { get; init; } = string.Empty;

    public IReadOnlyList<GachaHistoryPoolDisplayItem> Pools { get; init; } = [];
}
