/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Helpers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FufuLauncher.Views.Controls.CycleChallenge;

public sealed partial class AvatarCard : UserControl
{
    public static readonly DependencyProperty AvatarIdProperty = DependencyProperty.Register(
        nameof(AvatarId),
        typeof(int),
        typeof(AvatarCard),
        new PropertyMetadata(0, OnViewPropertyChanged));


    public static readonly DependencyProperty IconUrlProperty = DependencyProperty.Register(
        nameof(IconUrl),
        typeof(string),
        typeof(AvatarCard),
        new PropertyMetadata(string.Empty, OnViewPropertyChanged));


    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level),
        typeof(int),
        typeof(AvatarCard),
        new PropertyMetadata(0, OnViewPropertyChanged));


    public static readonly DependencyProperty RarityProperty = DependencyProperty.Register(
        nameof(Rarity),
        typeof(int),
        typeof(AvatarCard),
        new PropertyMetadata(0, OnViewPropertyChanged));


    public static readonly DependencyProperty CharacterNameProperty = DependencyProperty.Register(
        nameof(CharacterName),
        typeof(string),
        typeof(AvatarCard),
        new PropertyMetadata(string.Empty, OnViewPropertyChanged));


    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption),
        typeof(string),
        typeof(AvatarCard),
        new PropertyMetadata(string.Empty, OnViewPropertyChanged));


    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth),
        typeof(double),
        typeof(AvatarCard),
        new PropertyMetadata(66d, OnViewPropertyChanged));


    private const double ClipCornerRadius = 9d;

    private static readonly SolidColorBrush RarityFiveStarBrush = CreateBrush(0xFF, 0xD8, 0x6B);
    private static readonly SolidColorBrush RarityFourStarBrush = CreateBrush(0xC9, 0xA8, 0xFF);
    private static readonly SolidColorBrush RarityDefaultStarBrush = CreateBrush(0x9D, 0xD8, 0xF0);

    private static readonly object AvatarNamesLock = new();
    private static Dictionary<int, string>? _avatarNames;

    private string _currentIconUrl = string.Empty;

    public AvatarCard()
    {
        InitializeComponent();
        PortraitHost.SizeChanged += OnPortraitHostSizeChanged;
        Loaded += (_, _) => UpdatePortraitClip();

        Refresh();
    }


    public int AvatarId
    {
        get => (int)GetValue(AvatarIdProperty);
        set => SetValue(AvatarIdProperty, value);
    }


    public string? IconUrl
    {
        get => (string?)GetValue(IconUrlProperty);
        set => SetValue(IconUrlProperty, value);
    }


    public int Level
    {
        get => (int)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }


    public int Rarity
    {
        get => (int)GetValue(RarityProperty);
        set => SetValue(RarityProperty, value);
    }


    public string? CharacterName
    {
        get => (string?)GetValue(CharacterNameProperty);
        set => SetValue(CharacterNameProperty, value);
    }


    public string? Caption
    {
        get => (string?)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }


    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    private static void OnViewPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AvatarCard card)
        {
            card.Refresh();
        }
    }

    private void Refresh()
    {
        double width = NormalizeWidth(CardWidth);
        double portrait = Math.Max(24, width - 2);

        Root.Width = width;
        PortraitHost.Width = portrait;
        PortraitHost.Height = portrait;

        RarityBackground.Rarity = Rarity;

        double nameSize = Math.Clamp(width * 0.165, 9.5, 12);
        double captionSize = Math.Clamp(width * 0.135, 8.5, 10);
        NameText.FontSize = nameSize;
        NameText.LineHeight = Math.Round(nameSize * 1.32);
        CaptionText.FontSize = captionSize;
        CaptionText.LineHeight = Math.Round(captionSize * 1.42);

        UpdatePortrait();
        UpdateRarityBadge();
        UpdateLevelBadge();
        UpdateTexts();
        UpdatePortraitClip();
    }

    private void UpdatePortrait()
    {
        string url = IconUrl?.Trim() ?? string.Empty;
        bool hasIcon = url.Length > 0;

        AvatarImage.Visibility = hasIcon ? Visibility.Visible : Visibility.Collapsed;
        FallbackText.Visibility = hasIcon ? Visibility.Collapsed : Visibility.Visible;

        if (string.Equals(_currentIconUrl, url, StringComparison.Ordinal))
        {
            return;
        }

        _currentIconUrl = url;
        AvatarImage.Source = url;
    }

    private void UpdateRarityBadge()
    {
        bool show = Rarity > 0;

        RarityBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RarityText.Text = show ? Rarity.ToString(CultureInfo.InvariantCulture) : string.Empty;
        StarGlyph.Foreground = Rarity switch
        {
            5 => RarityFiveStarBrush,
            4 => RarityFourStarBrush,
            _ => RarityDefaultStarBrush,
        };
    }

    private void UpdateLevelBadge()
    {
        bool show = Level > 0;

        LevelBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LevelText.Text = show ? Level.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private void UpdateTexts()
    {
        string name = CharacterName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            name = ResolveAvatarName(AvatarId) ?? string.Empty;
        }

        NameText.Text = name;

        string caption = Caption?.Trim() ?? string.Empty;
        CaptionText.Text = caption;
        CaptionText.Visibility = caption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        InfoPanel.Visibility = name.Length > 0 || caption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateToolTip(name);
    }

    private void UpdateToolTip(string name)
    {
        string levelLabel = "PlayerInfo_Level".GetLocalized();
        string rarityLabel = "GachaAnalysis_Rarity".GetLocalized();

        var parts = new List<string>(3);
        if (name.Length > 0)
        {
            parts.Add(name);
        }

        if (Level > 0)
        {
            parts.Add($"{levelLabel}{Level.ToString(CultureInfo.InvariantCulture)}");
        }

        if (Rarity > 0)
        {
            parts.Add($"{rarityLabel} {Rarity.ToString(CultureInfo.InvariantCulture)}★");
        }

        if (parts.Count == 0)
        {
            ToolTipService.SetToolTip(Root, null);
            AutomationProperties.SetName(this, string.Empty);
            return;
        }

        string text = string.Join(" · ", parts);
        ToolTipService.SetToolTip(Root, text);
        AutomationProperties.SetName(this, text);
    }

    private static string? ResolveAvatarName(int avatarId)
    {
        if (avatarId <= 0)
        {
            return null;
        }

        if (_avatarNames is null)
        {
            lock (AvatarNamesLock)
            {
                if (_avatarNames is null)
                {
                    var names = new Dictionary<int, string>();
                    try
                    {
                        MetadataRepository repository = App.GetService<MetadataRepository>();
                        foreach (var item in repository.GetAllMetadata())
                        {
                            if (!string.IsNullOrWhiteSpace(item.ItemId)
                                && int.TryParse(item.ItemId, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int id)
                                && id > 0
                                && !string.IsNullOrWhiteSpace(item.Name))
                            {
                                names.TryAdd(id, item.Name);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Info("AvatarCard",
                            $"[AvatarCard] Failed to resolve metadata character name: {ex.Message}");
                    }

                    if (names.Count > 0)
                    {
                        _avatarNames = names;
                    }
                }
            }
        }

        return _avatarNames is not null
               && _avatarNames.TryGetValue(avatarId, out string? resolvedName)
            ? resolvedName
            : null;
    }

    private void UpdatePortraitClip()
    {
        try
        {
            double width = PortraitHost.ActualWidth;
            double height = PortraitHost.ActualHeight;
            if (width <= 1 || height <= 1)
            {
                return;
            }

            Visual visual = ElementCompositionPreview.GetElementVisual(PortraitHost);
            Compositor compositor = visual.Compositor;

            CompositionRoundedRectangleGeometry geometry = compositor.CreateRoundedRectangleGeometry();
            geometry.Size = new Vector2((float)width, (float)(height + ClipCornerRadius));
            geometry.CornerRadius = new Vector2((float)ClipCornerRadius, (float)ClipCornerRadius);
            visual.Clip = compositor.CreateGeometricClip(geometry);
        }
        catch (Exception ex)
        {
            DebugLog.Info("AvatarCard", $"[AvatarCard] Failed to apply rounded clip: {ex.Message}");
        }
    }

    private void OnPortraitHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePortraitClip();
    }

    private static double NormalizeWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            return 66;
        }

        return width;
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        return new SolidColorBrush(Color.FromArgb(0xFF, r, g, b));
    }
}