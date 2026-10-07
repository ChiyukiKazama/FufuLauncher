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

    public string CountText => PullCount.ToString();

    public SolidColorBrush RarityBackground => Rank switch
    {
        5 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 198, 160, 96)),
        4 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 149, 118, 193)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 168, 209))
    };
}

public sealed class GachaHistoryPoolDisplayItem
{
    public string Key { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Period { get; init; } = string.Empty;

    public string TotalPulls { get; init; } = string.Empty;

    public string BannerImageUrl { get; init; } = "ms-appx:///Assets/StoreLogo.png";

    public IReadOnlyList<GachaHistoryItemDisplayItem> FeaturedItems { get; init; } = [];

    public IReadOnlyList<GachaHistoryItemDisplayItem> FiveStarItems { get; init; } = [];

    public IReadOnlyList<GachaHistoryItemDisplayItem> FourStarItems { get; init; } = [];

    public IReadOnlyList<GachaHistoryItemDisplayItem> ThreeStarItems { get; init; } = [];

    public bool HasFiveStarItems => FiveStarItems.Count > 0;

    public bool HasFourStarItems => FourStarItems.Count > 0;

    public bool HasThreeStarItems => ThreeStarItems.Count > 0;

    public bool HasObtainedItems => HasFiveStarItems || HasFourStarItems || HasThreeStarItems;
}
