/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using FufuLauncher.Services.GameAnnouncement;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views
{
    public sealed partial class GameAnnouncementBanner : UserControl
    {
        private const float CornerRadiusValue = 8f;

        public static readonly DependencyProperty BannerUrlProperty = DependencyProperty.Register(
            nameof(BannerUrl),
            typeof(string),
            typeof(GameAnnouncementBanner),
            new PropertyMetadata(null, OnBannerUrlChanged));

        private int _loadVersion;
        private CancellationTokenSource? _loadCancellation;

        public string? BannerUrl
        {
            get => (string?)GetValue(BannerUrlProperty);
            set => SetValue(BannerUrlProperty, value);
        }

        public GameAnnouncementBanner()
        {
            InitializeComponent();

            Loaded += GameAnnouncementBanner_Loaded;
            SizeChanged += GameAnnouncementBanner_SizeChanged;
            Unloaded += GameAnnouncementBanner_Unloaded;
        }

        public void AnimateZoom(double target)
        {
            Storyboard storyboard = new();
            DoubleAnimation scaleX = CreateScaleAnimation(target);
            Storyboard.SetTarget(scaleX, ZoomTransform);
            Storyboard.SetTargetProperty(scaleX, "ScaleX");
            storyboard.Children.Add(scaleX);

            DoubleAnimation scaleY = CreateScaleAnimation(target);
            Storyboard.SetTarget(scaleY, ZoomTransform);
            Storyboard.SetTargetProperty(scaleY, "ScaleY");
            storyboard.Children.Add(scaleY);

            storyboard.Begin();
        }

        private void GameAnnouncementBanner_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateRoundedClip();
        }

        private void GameAnnouncementBanner_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateRoundedClip();
        }

        private void GameAnnouncementBanner_Unloaded(object sender, RoutedEventArgs e)
        {
            _loadCancellation?.Cancel();
        }

        private void UpdateRoundedClip()
        {
            try
            {
                Visual visual = ElementCompositionPreview.GetElementVisual(RootGrid);
                Compositor compositor = visual.Compositor;

                CompositionRoundedRectangleGeometry geometry = compositor.CreateRoundedRectangleGeometry();
                geometry.CornerRadius = new Vector2(CornerRadiusValue, CornerRadiusValue);
                geometry.Size = new Vector2((float)ActualWidth, (float)ActualHeight);

                visual.Clip = compositor.CreateGeometricClip(geometry);
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameAnnouncementBanner",
                    $"[GameAnnouncementBanner] Failed to apply rounded clip: {ex.Message}");
            }
        }

        private static void OnBannerUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((GameAnnouncementBanner)d).LoadBannerAsync((string?)e.NewValue);
        }

        private async void LoadBannerAsync(string? url)
        {
            int version = ++_loadVersion;
            _loadCancellation?.Cancel();
            CancellationTokenSource cts = new();
            _loadCancellation = cts;
            CancellationToken token = cts.Token;

            if (string.IsNullOrWhiteSpace(url))
            {
                ShowPlaceholder();
                return;
            }

            try
            {
                IGameAnnouncementImageService imageService = App.GetService<IGameAnnouncementImageService>();
                byte[]? bytes = await imageService.GetImageBytesAsync(url, token);

                if (version != _loadVersion || token.IsCancellationRequested || bytes is null)
                {
                    if (version == _loadVersion)
                    {
                        ShowPlaceholder();
                    }

                    return;
                }

                using var stream = new MemoryStream(bytes).AsRandomAccessStream();
                BitmapImage bitmap = new()
                {
                    DecodePixelType = DecodePixelType.Logical,
                    DecodePixelWidth = 640
                };
                await bitmap.SetSourceAsync(stream);

                if (version != _loadVersion)
                {
                    return;
                }

                InnerImage.Source = bitmap;
                InnerImage.Visibility = Visibility.Visible;
                PlaceholderIcon.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameAnnouncementBanner", $"[GameAnnouncementBanner] Image load failed: {ex.Message}");
                if (version == _loadVersion)
                {
                    ShowPlaceholder();
                }
            }
        }

        private void ShowPlaceholder()
        {
            InnerImage.Visibility = Visibility.Collapsed;
            PlaceholderIcon.Visibility = Visibility.Visible;
        }

        private static DoubleAnimation CreateScaleAnimation(double target)
        {
            return new DoubleAnimation
            {
                To = target,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
        }
    }
}