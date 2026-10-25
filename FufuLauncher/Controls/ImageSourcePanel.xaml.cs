/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using FufuLauncher.Helpers;
using FufuLauncher.Services.Background;
using FufuLauncher.ViewModels;
using FufuLauncher.Views;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Controls;

public sealed partial class ImageSourcePanel : UserControl
{
    private const double TabStep = 40;
    private const double PreviewFrameWidth = 360;
    private const double DefaultWindowWidth = 1360;
    private const double DefaultWindowHeight = 768;
    private const double PreviewSwipeThreshold = 42;
    private const double PreviewAnimationMs = 360;
    private const double PreviewInitialFadeMs = 480;
    private const double PreviewInitialZoom = 1.08;
    private const float PreviewClipCornerRadius = 12f;

    private MainWindow? _mainWindow;
    private bool _initialized;
    private ImageSource? _displayedPreviewSource;
    private ImageSource? _displayedOriginalSource;
    private ImageSource? _pendingPreviewSource;
    private bool _isPreviewTransitioning;
    private bool _isPreviewPointerPressed;
    private bool _isPreviewInViewport;
    private bool _entryFadePending;
    private int _previewDirection = 1;
    private int _previewTransitionId;
    private Storyboard? _initialFadeStoryboard;
    private Storyboard? _previewTransitionStoryboard;
    private DispatcherTimer? _entryFadeTimer;
    private DispatcherTimer? _previewTransitionTimer;
    private DispatcherTimer? _previewSyncTimer;
    private DispatcherTimer? _uiHeartbeatTimer;
    private long _lastUiHeartbeat;
    private long _previewSourceSetTick;
    private bool _uiFrameHookAttached;
    private bool _previewFirstFrameSeen;
    private Windows.Foundation.Point _previewPointerPressedPoint;

    public ImageSourceViewModel ViewModel
    {
        get;
    }

    public ImageSourcePanel()
    {
        ViewModel = App.GetService<ImageSourceViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        PreviewCurrentBrush.ImageOpened += (_, _) =>
            DebugLog.Info("ImageSourcePanel", "Image.Opened", $"current {Describe(PreviewCurrentBrush.ImageSource)}");
        PreviewCurrentBrush.ImageFailed += (_, _) =>
        {
            DebugLog.Info("ImageSourcePanel", "Image.Failed", "current");
            SetPreviewPlaceholder(PreviewCurrentImage, true);
        };
        PreviewIncomingBrush.ImageOpened += (_, _) =>
            DebugLog.Info("ImageSourcePanel", "Image.Opened", $"incoming {Describe(PreviewIncomingBrush.ImageSource)}");
        PreviewIncomingBrush.ImageFailed += (_, _) =>
        {
            DebugLog.Info("ImageSourcePanel", "Image.Failed", "incoming");
            SetPreviewPlaceholder(PreviewIncomingImage, true);
        };
    }

    private void Panel_Loaded(object sender, RoutedEventArgs e)
    {
        DebugLog.Info("ImageSourcePanel", "Panel.Loaded",
            $"hasWindow={App.MainWindow is MainWindow} current={Describe(_mainWindow?.CurrentGlobalBackground?.ImageSource)}");

        if (_mainWindow != null)
        {
            _mainWindow.GlobalBackgroundApplied -= OnGlobalBackgroundApplied;
            _mainWindow.GlobalBackgroundImageAvailable -= OnGlobalBackgroundImageAvailable;
            _mainWindow.SizeChanged -= OnMainWindowSizeChanged;
        }

        if (App.MainWindow is MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _mainWindow.GlobalBackgroundApplied += OnGlobalBackgroundApplied;
            _mainWindow.GlobalBackgroundImageAvailable += OnGlobalBackgroundImageAvailable;
            _mainWindow.SizeChanged += OnMainWindowSizeChanged;
        }

        UpdatePreviewFrameSize();

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ShowPreview(_mainWindow?.CurrentGlobalBackground, forceEntrance: true);
        DebugLog.Info("ImageSourcePanel", "Panel.Loaded.AfterShowPreview",
            $"displayed={Describe(_displayedPreviewSource)}");
        UpdateTabSelection(ViewModel.SelectedTab, false);

        _previewSyncTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _previewSyncTimer.Tick -= OnPreviewSyncTimerTick;
        _previewSyncTimer.Tick += OnPreviewSyncTimerTick;
        _previewSyncTimer.Start();

        _uiHeartbeatTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiHeartbeatTimer.Tick -= OnUiHeartbeatTick;
        _uiHeartbeatTimer.Tick += OnUiHeartbeatTick;
        _lastUiHeartbeat = Environment.TickCount64;
        _uiHeartbeatTimer.Start();

        if (!_initialized)
        {
            _initialized = true;
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await InitializePanelAsync();
                }
                catch (Exception ex)
                {
                    DebugLog.Info("ImageSourcePanel", $"[ImageSource] Initialization failed: {ex.Message}");
                }
            });
        }
    }

    private void OnPreviewSyncTimerTick(object? sender, object e)
    {
        if (ViewModel.SelectedTab == ImageSourceTab.Custom && !ViewModel.HasCustomBackground) return;
        if (ViewModel.SelectedTab == ImageSourceTab.Slideshow && !ViewModel.HasSlideshowFolder) return;

        var current = _mainWindow?.CurrentGlobalBackground;
        if (current?.ImageSource != null && !ReferenceEquals(current.ImageSource, _displayedOriginalSource))
            ShowPreview(current);
    }

    private async Task InitializePanelAsync()
    {
        await ViewModel.InitializeAsync();
        if (!IsLoaded) return;

        UpdateTabSelection(ViewModel.SelectedTab, false);

        var emptyCustom = ViewModel.SelectedTab == ImageSourceTab.Custom && !ViewModel.HasCustomBackground;
        var emptySlideshow = ViewModel.SelectedTab == ImageSourceTab.Slideshow && !ViewModel.HasSlideshowFolder;

        if (emptyCustom || emptySlideshow)
            ShowFallback();
        else if (_displayedPreviewSource == null)
            ShowPreview(_mainWindow?.CurrentGlobalBackground);
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs e)
    {
        DebugLog.Info("ImageSourcePanel", "Panel.Unloaded");
        _isPreviewInViewport = false;
        _previewSyncTimer?.Stop();
        _uiHeartbeatTimer?.Stop();
        _entryFadeTimer?.Stop();
        if (_mainWindow != null)
            _mainWindow.GlobalBackgroundImageAvailable -= OnGlobalBackgroundImageAvailable;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_mainWindow != null)
        {
            _mainWindow.GlobalBackgroundApplied -= OnGlobalBackgroundApplied;
            _mainWindow.SizeChanged -= OnMainWindowSizeChanged;
            _mainWindow = null;
        }
    }

    private void OnUiHeartbeatTick(object? sender, object e)
    {
        var now = Environment.TickCount64;
        if (_lastUiHeartbeat > 0 && now - _lastUiHeartbeat > 800)
            DebugLog.Info("ImageSourcePanel", "UI.HeartbeatGap", $"{now - _lastUiHeartbeat}ms");
        _lastUiHeartbeat = now;
    }

    private void OnMainWindowSizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        UpdatePreviewFrameSize(args.Size.Width, args.Size.Height);
    }

    private void UpdatePreviewFrameSize()
    {
        UpdatePreviewFrameSize(_mainWindow?.Width ?? 0, _mainWindow?.Height ?? 0);
    }

    private void UpdatePreviewFrameSize(double windowWidth, double windowHeight)
    {
        if (!double.IsFinite(windowWidth) || !double.IsFinite(windowHeight) ||
            windowWidth <= 0 || windowHeight <= 0)
        {
            windowWidth = DefaultWindowWidth;
            windowHeight = DefaultWindowHeight;
        }

        PreviewHost.Width = PreviewFrameWidth;
        PreviewHost.Height = Math.Round(PreviewFrameWidth * windowHeight / windowWidth);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageSourceViewModel.IsPreviewFallback) &&
            ViewModel.IsPreviewFallback)
        {
            ShowFallback();
        }
        else if (e.PropertyName == nameof(ImageSourceViewModel.SelectedTab))
        {
            UpdateTabSelection(ViewModel.SelectedTab, false);
        }
    }

    private async void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<ImageSourceTab>(tag, true, out var tab))
        {
            return;
        }

        DebugLog.Info("ImageSourcePanel", "Panel.TabClick", tab.ToString());
        UpdateTabSelection(tab, true);
        await ViewModel.SelectTabAsync(tab);
    }

    private void OnOpenOfficialBackgroundWindowClick(object sender, RoutedEventArgs e)
    {
        OfficialBackgroundWindow.ShowOrActivate();
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        StepPreview(-1);
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        StepPreview(1);
    }

    private void StepPreview(int direction)
    {
        _previewDirection = direction;
        var command = direction < 0 ? ViewModel.PreviousCommand : ViewModel.NextCommand;
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private void OnGlobalBackgroundImageAvailable(ImageSource source)
    {
        DebugLog.Info("ImageSourcePanel", "GlobalImageAvailable", Describe(source));
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowImage(source));
            return;
        }

        ShowImage(source);
    }

    private void OnGlobalBackgroundApplied(BackgroundRenderResult result)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ApplyBackgroundResult(result));
            return;
        }

        ApplyBackgroundResult(result);
    }

    private void ApplyBackgroundResult(BackgroundRenderResult result)
    {
        ViewModel.ApplyBackgroundResult(result);

        if (ViewModel.SelectedTab == ImageSourceTab.Custom && !ViewModel.HasCustomBackground)
        {
            ShowFallback();
            return;
        }

        if (ViewModel.SelectedTab == ImageSourceTab.Slideshow && !ViewModel.HasSlideshowFolder)
        {
            ShowFallback();
            return;
        }

        ShowPreview(result);
    }

    private void ShowPreview(BackgroundRenderResult? result, bool forceEntrance = false)
    {
        if (result == null || result.IsFallback || result.ImageSource == null)
        {
            ShowFallback();
            return;
        }

        DebugLog.Info("ImageSourcePanel", "ApplyResult",
            $"source={Describe(result.ImageSource)} fallback={result.IsFallback}");
        ShowImage(result.ImageSource, forceEntrance);
    }

    private void ShowFallback()
    {
        DebugLog.Info("ImageSourcePanel", "ShowFallback");
        _displayedPreviewSource = null;
        _displayedOriginalSource = null;
        _pendingPreviewSource = null;
        _entryFadePending = false;
        _entryFadeTimer?.Stop();
        _entryFadeTimer = null;
        _isPreviewTransitioning = false;
        _previewTransitionId++;
        _previewTransitionTimer?.Stop();
        _previewTransitionTimer = null;
        try
        {
            _initialFadeStoryboard?.Stop();
            _previewTransitionStoryboard?.Stop();
        }
        catch
        {
        }

        _initialFadeStoryboard = null;
        _previewTransitionStoryboard = null;
        PreviewCurrentBrush.ImageSource = null;
        PreviewIncomingBrush.ImageSource = null;
        ResetPreviewLayers();
        PreviewCurrentLayer.Opacity = 1;
        PreviewIncomingLayer.Opacity = 0;
        PreviewCurrentPlaceholder.Visibility = Visibility.Visible;
        PreviewIncomingPlaceholder.Visibility = Visibility.Visible;
    }

    private void ShowImage(ImageSource source, bool forceEntrance = false)
    {
        DebugLog.Info("ImageSourcePanel", "ShowImage.Enter",
            $"source={Describe(source)} force={forceEntrance} transitioning={_isPreviewTransitioning} viewport={_isPreviewInViewport}");

        if (!forceEntrance &&
            ReferenceEquals(_displayedOriginalSource, source) &&
            PreviewCurrentBrush.ImageSource != null)
        {
            return;
        }

        if (_isPreviewTransitioning)
        {
            _pendingPreviewSource = source;
            return;
        }

        var previewSource = CreatePreviewSource(source);
        _displayedOriginalSource = source;
        DebugLog.Info("ImageSourcePanel", "ShowImage.PreviewReady", $"preview={Describe(previewSource)}");

        if (_displayedPreviewSource == null || PreviewCurrentBrush.ImageSource == null ||
            forceEntrance || !_isPreviewInViewport)
        {
            ShowInitialPreview(previewSource);
            return;
        }

        StartPreviewTransition(previewSource, _previewDirection);
    }

    private static ImageSource CreatePreviewSource(ImageSource source)
    {
        var started = Environment.TickCount64;
        DebugLog.Info("ImageSourcePanel", "PreviewThumb.Start", $"source={Describe(source)}");
        try
        {
            var result = BackgroundImageLoader.CreatePreviewThumbnail(source, 640);
            DebugLog.Info("ImageSourcePanel", "PreviewThumb.Done",
                $"{Describe(result)} in {Environment.TickCount64 - started}ms");
            return result;
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourcePanel", "PreviewThumb.Failed",
                $"{Environment.TickCount64 - started}ms {ex.Message}");
            return source;
        }
    }

    private static string Describe(ImageSource? source)
    {
        if (source is WriteableBitmap bitmap)
            return $"{bitmap.PixelWidth}x{bitmap.PixelHeight}";
        return source?.GetType().Name ?? "null";
    }

    private void ShowInitialPreview(ImageSource source)
    {
        DebugLog.Info("ImageSourcePanel", "ShowInitialPreview", Describe(source));
        _displayedPreviewSource = source;
        _pendingPreviewSource = null;
        ResetPreviewLayers();
        PreviewCurrentLayer.Opacity = 0.6;
        PreviewCurrentScale.ScaleX = PreviewInitialZoom;
        PreviewCurrentScale.ScaleY = PreviewInitialZoom;
        SetPreviewImage(PreviewCurrentImage, source);
        DebugLog.Info("ImageSourcePanel", "ShowInitialPreview.SourceAssigned",
            Describe(PreviewCurrentBrush.ImageSource));

        _entryFadePending = true;

        _entryFadeTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TryStartEntryFade();
        };
        _entryFadeTimer = timer;
        timer.Start();
    }

    private void TryStartEntryFade()
    {
        if (!_entryFadePending) return;

        _entryFadePending = false;
        _entryFadeTimer?.Stop();
        FadeInInitialPreview();
    }

    private void FadeInInitialPreview()
    {
        PreviewIncomingLayer.Opacity = 0;
        PreviewCurrentLayer.Opacity = 0.6;
        PreviewCurrentScale.ScaleX = PreviewInitialZoom;
        PreviewCurrentScale.ScaleY = PreviewInitialZoom;

        DebugLog.Info("ImageSourcePanel", "Fade.Start",
            $"opacity={PreviewCurrentLayer.Opacity} scale={PreviewCurrentScale.ScaleX}");
        var storyboard = new Storyboard();
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(PreviewInitialFadeMs));

        storyboard.Children.Add(CreateDoubleAnimation(PreviewCurrentLayer, "Opacity", 1, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(PreviewCurrentScale, "ScaleX", 1, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(PreviewCurrentScale, "ScaleY", 1, duration, easing));

        try
        {
            _initialFadeStoryboard?.Stop();
        }
        catch
        {
        }

        _initialFadeStoryboard = storyboard;
        storyboard.Completed += (_, _) => DebugLog.Info("ImageSourcePanel", "Fade.Completed");
        storyboard.Begin();
        DebugLog.Info("ImageSourcePanel", "Fade.BeginReturned");
    }

    private void PreviewHost_EffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var wasVisible = _isPreviewInViewport;
        _isPreviewInViewport = args.EffectiveViewport.Width > 0 && args.EffectiveViewport.Height > 0;
        if (wasVisible != _isPreviewInViewport)
            DebugLog.Info("ImageSourcePanel", "Viewport.Changed", $"visible={_isPreviewInViewport}");
        if (_isPreviewInViewport && _entryFadePending)
            TryStartEntryFade();
    }

    private bool IsPreviewVisibleInWindow()
    {
        try
        {
            if (XamlRoot == null || PreviewHost.ActualWidth <= 0 || PreviewHost.ActualHeight <= 0)
                return false;

            var bounds = PreviewHost.TransformToVisual(null).TransformBounds(
                new Windows.Foundation.Rect(0, 0, PreviewHost.ActualWidth, PreviewHost.ActualHeight));
            var rootSize = XamlRoot.Size;

            return bounds.Bottom > 0 && bounds.Top < rootSize.Height &&
                   bounds.Right > 0 && bounds.Left < rootSize.Width;
        }
        catch
        {
            return false;
        }
    }

    private void StartPreviewTransition(ImageSource source, int direction)
    {
        try
        {
            _initialFadeStoryboard?.Stop();
        }
        catch
        {
        }

        _initialFadeStoryboard = null;
        _entryFadePending = false;
        _entryFadeTimer?.Stop();

        var width = Math.Max(PreviewViewport.ActualWidth, 1);
        var offset = width * 0.18 * direction;

        SetPreviewImage(PreviewIncomingImage, source);

        PreviewIncomingTranslate.X = offset;
        PreviewIncomingLayer.Opacity = 0;
        PreviewIncomingScale.ScaleX = 1.015;
        PreviewIncomingScale.ScaleY = 1.015;
        PreviewCurrentTranslate.X = 0;
        PreviewCurrentLayer.Opacity = 1;
        PreviewCurrentScale.ScaleX = 1;
        PreviewCurrentScale.ScaleY = 1;

        var storyboard = new Storyboard();
        var easing = new SineEase { EasingMode = EasingMode.EaseInOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(PreviewAnimationMs));

        storyboard.Children.Add(CreateDoubleAnimation(PreviewCurrentTranslate, "X", -offset, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(PreviewCurrentLayer, "Opacity", 0, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(PreviewCurrentScale, "ScaleX", 0.985, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(PreviewCurrentScale, "ScaleY", 0.985, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(PreviewIncomingTranslate, "X", 0, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(PreviewIncomingLayer, "Opacity", 1, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(PreviewIncomingScale, "ScaleX", 1, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(PreviewIncomingScale, "ScaleY", 1, duration, easing));

        DebugLog.Info("ImageSourcePanel", "Transition.Start", $"source={Describe(source)} direction={direction}");
        _isPreviewTransitioning = true;
        var transitionId = ++_previewTransitionId;
        storyboard.Completed += (_, _) => CompletePreviewTransition(source, transitionId);

        try
        {
            _previewTransitionStoryboard?.Stop();
        }
        catch
        {
        }

        _previewTransitionStoryboard = storyboard;
        storyboard.Begin();

        _previewTransitionTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewAnimationMs + 200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CompletePreviewTransition(source, transitionId);
        };
        _previewTransitionTimer = timer;
        timer.Start();
    }

    private void CompletePreviewTransition(ImageSource source, int transitionId)
    {
        if (!_isPreviewTransitioning || transitionId != _previewTransitionId) return;

        _previewTransitionTimer?.Stop();
        _previewTransitionTimer = null;
        try
        {
            _previewTransitionStoryboard?.Stop();
        }
        catch
        {
        }

        _previewTransitionStoryboard = null;

        DebugLog.Info("ImageSourcePanel", "Transition.Complete",
            $"source={Describe(source)} firstFrame={_previewFirstFrameSeen} currentLoaded={PreviewCurrentImage.IsLoaded} currentSize={PreviewCurrentImage.ActualWidth}x{PreviewCurrentImage.ActualHeight} currentSource={Describe(PreviewCurrentBrush.ImageSource)}");
        SwapPreviewLayers(source);
        MakeCurrentPreviewVisible();
        DebugLog.Info("ImageSourcePanel", "Transition.AfterSwap",
            $"firstFrame={_previewFirstFrameSeen} currentSource={Describe(PreviewCurrentBrush.ImageSource)} currentLoaded={PreviewCurrentImage.IsLoaded} layerOpacity={PreviewCurrentLayer.Opacity} layerVisibility={PreviewCurrentLayer.Visibility} imageOpacity={PreviewCurrentImage.Opacity} imageSize={PreviewCurrentImage.ActualWidth}x{PreviewCurrentImage.ActualHeight} hostSize={PreviewHost.ActualWidth}x{PreviewHost.ActualHeight} placeholder={PreviewCurrentPlaceholder.Visibility} scale={PreviewCurrentScale.ScaleX} translate={PreviewCurrentTranslate.X}");
        _isPreviewTransitioning = false;

        if (_pendingPreviewSource != null && !ReferenceEquals(_pendingPreviewSource, _displayedOriginalSource))
        {
            var pending = _pendingPreviewSource!;
            _pendingPreviewSource = null;
            ShowImage(pending);
        }
        else
        {
            _pendingPreviewSource = null;
        }
    }

    private void SwapPreviewLayers(ImageSource displayedSource)
    {
        PreviewCurrentBrush.ImageSource = PreviewIncomingBrush.ImageSource;
        PreviewCurrentPlaceholder.Visibility = PreviewIncomingPlaceholder.Visibility;
        _displayedPreviewSource = displayedSource;
        ResetPreviewLayers();
    }

    private void ResetPreviewLayers()
    {
        PreviewCurrentTranslate.X = 0;
        PreviewIncomingTranslate.X = 0;
        PreviewCurrentLayer.Opacity = 1;
        PreviewIncomingLayer.Opacity = 0;
        PreviewCurrentScale.ScaleX = 1;
        PreviewCurrentScale.ScaleY = 1;
        PreviewIncomingScale.ScaleX = 1;
        PreviewIncomingScale.ScaleY = 1;
    }

    private void MakeCurrentPreviewVisible()
    {
        PreviewHost.Visibility = Visibility.Visible;
        PreviewCurrentLayer.Visibility = Visibility.Visible;
        PreviewCurrentLayer.Opacity = 1;
        PreviewCurrentImage.Visibility = Visibility.Visible;
        PreviewCurrentImage.Opacity = 1;
        PreviewCurrentScale.ScaleX = 1;
        PreviewCurrentScale.ScaleY = 1;
        PreviewCurrentTranslate.X = 0;
        PreviewCurrentPlaceholder.Visibility = Visibility.Collapsed;
        PreviewIncomingLayer.Opacity = 0;
        PreviewIncomingPlaceholder.Visibility = Visibility.Collapsed;
        ForcePreviewRender("MakeCurrentPreviewVisible");
        DebugLog.Info("ImageSourcePanel", "Preview.MakeVisible",
            $"layerOpacity={PreviewCurrentLayer.Opacity} imageOpacity={PreviewCurrentImage.Opacity} placeholder={PreviewCurrentPlaceholder.Visibility} host={PreviewHost.ActualWidth}x{PreviewHost.ActualHeight}");
    }

    private void SetPreviewImage(Border imageControl, ImageSource? source)
    {
        var brush = ReferenceEquals(imageControl, PreviewCurrentImage) ? PreviewCurrentBrush : PreviewIncomingBrush;
        DebugLog.Info("ImageSourcePanel", "Image.SourceSet.Start", $"{imageControl.Name} {Describe(source)}");
        brush.ImageSource = source;
        SetPreviewPlaceholder(imageControl, source == null);

        if (source == null)
        {
            DebugLog.Info("ImageSourcePanel", "Image.SourceSet.Done", $"{imageControl.Name} null");
            return;
        }

        HookFirstFrame(imageControl.Name);
        ForcePreviewRender(imageControl.Name);
        DebugLog.Info("ImageSourcePanel", "Image.SourceSet.Done", $"{imageControl.Name} {Describe(source)}");
    }

    private void ForcePreviewRender(string reason)
    {
        try
        {
            PreviewCurrentImage.InvalidateMeasure();
            PreviewIncomingImage.InvalidateMeasure();
            PreviewHost.InvalidateMeasure();
            PreviewHost.InvalidateArrange();
            PreviewHost.UpdateLayout();
            DebugLog.Info("ImageSourcePanel", "Preview.ForceRender", reason);
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourcePanel", "Preview.ForceRender.Failed", $"{reason} {ex.Message}");
        }
    }

    private void HookFirstFrame(string imageName)
    {
        if (_uiFrameHookAttached) return;
        _uiFrameHookAttached = true;
        _previewSourceSetTick = Environment.TickCount64;
        CompositionTarget.Rendering += OnFirstFrameRendering;

        void OnFirstFrameRendering(object? sender, object e)
        {
            CompositionTarget.Rendering -= OnFirstFrameRendering;
            _uiFrameHookAttached = false;
            _previewFirstFrameSeen = true;
            DebugLog.Info("ImageSourcePanel", "Preview.FirstFrame",
                $"{imageName} after {Environment.TickCount64 - _previewSourceSetTick}ms loaded={PreviewCurrentImage.IsLoaded} size={PreviewCurrentImage.ActualWidth}x{PreviewCurrentImage.ActualHeight}");
        }
    }

    private void SetPreviewPlaceholder(Border imageControl, bool isVisible)
    {
        var placeholder = ReferenceEquals(imageControl, PreviewCurrentImage)
            ? PreviewCurrentPlaceholder
            : PreviewIncomingPlaceholder;
        placeholder.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PreviewHost_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateArrowsOpacity(1.0);
    }

    private void PreviewHost_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateArrowsOpacity(0.0);
    }

    private void PreviewViewport_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isPreviewPointerPressed = true;
        _previewPointerPressedPoint = e.GetCurrentPoint(PreviewViewport).Position;
        PreviewViewport.CapturePointer(e.Pointer);
    }

    private void PreviewViewport_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPreviewPointerPressed) return;

        var releasedPoint = e.GetCurrentPoint(PreviewViewport).Position;
        var deltaX = releasedPoint.X - _previewPointerPressedPoint.X;
        _isPreviewPointerPressed = false;
        PreviewViewport.ReleasePointerCapture(e.Pointer);

        if (Math.Abs(deltaX) < PreviewSwipeThreshold) return;

        StepPreview(deltaX < 0 ? 1 : -1);
    }

    private void PreviewViewport_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isPreviewPointerPressed = false;
    }

    private void AnimateArrowsOpacity(double toOpacity)
    {
        if (PreviousImageButton == null || NextImageButton == null) return;

        var duration = new Duration(TimeSpan.FromMilliseconds(200));
        var storyboard = new Storyboard();

        var prevAnim = new DoubleAnimation { To = toOpacity, Duration = duration, EnableDependentAnimation = true };
        Storyboard.SetTarget(prevAnim, PreviousImageButton);
        Storyboard.SetTargetProperty(prevAnim, "Opacity");
        storyboard.Children.Add(prevAnim);

        var nextAnim = new DoubleAnimation { To = toOpacity, Duration = duration, EnableDependentAnimation = true };
        Storyboard.SetTarget(nextAnim, NextImageButton);
        Storyboard.SetTargetProperty(nextAnim, "Opacity");
        storyboard.Children.Add(nextAnim);

        PreviousImageButton.IsHitTestVisible = toOpacity > 0;
        NextImageButton.IsHitTestVisible = toOpacity > 0;

        storyboard.Begin();
    }

    private void UpdateTabSelection(ImageSourceTab tab, bool animate)
    {
        var index = Math.Clamp((int)tab, 0, TabButtonPanel.Children.Count - 1);
        var targetY = index * TabStep;

        if (animate)
        {
            var animation = new DoubleAnimation
            {
                From = TabSelectionTransform.Y,
                To = targetY,
                Duration = new Duration(TimeSpan.FromMilliseconds(240)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true
            };

            Storyboard.SetTarget(animation, TabSelectionTransform);
            Storyboard.SetTargetProperty(animation, "Y");

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
        else
        {
            TabSelectionTransform.Y = targetY;
        }

        var selectedIndex = (int)tab;
        for (var i = 0; i < TabButtonPanel.Children.Count; i++)
        {
            if (TabButtonPanel.Children[i] is Button button && button.Content is FrameworkElement content)
                content.Opacity = i == selectedIndex ? 1.0 : 0.55;
        }
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_entryFadePending)
            TryStartEntryFade();
    }

    private static DoubleAnimation CreateDoubleAnimation(DependencyObject target, string property, double to,
        Duration duration, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}