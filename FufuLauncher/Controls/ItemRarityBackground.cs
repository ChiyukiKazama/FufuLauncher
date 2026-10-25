/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace FufuLauncher.Controls;

public sealed class ItemRarityBackground : ContentControl
{
    public static readonly DependencyProperty RarityProperty = DependencyProperty.Register(
        nameof(Rarity), typeof(int), typeof(ItemRarityBackground),
        new PropertyMetadata(3, OnRarityChanged));

    public static readonly DependencyProperty BackgroundImageSourceProperty = DependencyProperty.Register(
        nameof(BackgroundImageSource), typeof(ImageSource), typeof(ItemRarityBackground),
        new PropertyMetadata(null));

    public static readonly DependencyProperty FallbackBrushProperty = DependencyProperty.Register(
        nameof(FallbackBrush), typeof(Brush), typeof(ItemRarityBackground),
        new PropertyMetadata(null));

    public ItemRarityBackground()
    {
        DefaultStyleKey = typeof(ItemRarityBackground);
        UpdateBackground();
    }


    public int Rarity
    {
        get => (int)GetValue(RarityProperty);
        set => SetValue(RarityProperty, value);
    }

    public ImageSource BackgroundImageSource
    {
        get => (ImageSource)GetValue(BackgroundImageSourceProperty);
        private set => SetValue(BackgroundImageSourceProperty, value);
    }

    public Brush FallbackBrush
    {
        get => (Brush)GetValue(FallbackBrushProperty);
        private set => SetValue(FallbackBrushProperty, value);
    }

    private static void OnRarityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ItemRarityBackground)sender).UpdateBackground();

    private void UpdateBackground()
    {
        var rarity = Rarity is >= 1 and <= 5 ? Rarity : 3;
        var relativePath = $"Assets/ItemRarity/ItemBackground{rarity}.png";
        var localPath = Path.Combine(AppContext.BaseDirectory, relativePath);


        var uri = File.Exists(localPath)
            ? new Uri(localPath)
            : new Uri($"ms-appx:///{relativePath}");
        BackgroundImageSource = new BitmapImage(uri);

        var (top, bottom) = rarity switch
        {
            1 => (Color.FromArgb(255, 88, 94, 104), Color.FromArgb(255, 132, 141, 151)),
            2 => (Color.FromArgb(255, 65, 100, 83), Color.FromArgb(255, 102, 156, 119)),
            5 => (Color.FromArgb(255, 148, 104, 64), Color.FromArgb(255, 225, 155, 85)),
            4 => (Color.FromArgb(255, 110, 83, 145), Color.FromArgb(255, 164, 127, 203)),
            _ => (Color.FromArgb(255, 63, 103, 124), Color.FromArgb(255, 80, 164, 199))
        };
        FallbackBrush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = top, Offset = 0 },
                new GradientStop { Color = bottom, Offset = 1 }
            }
        };
    }
}