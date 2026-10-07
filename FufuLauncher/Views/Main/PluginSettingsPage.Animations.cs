using System.Collections.Specialized;
using System.Diagnostics;
using System.Numerics;
using FufuLauncher.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage
{
    private const string SettingTranslationProperty = "Translation";
    private const int SettingEntranceStaggerMilliseconds = 24;
    private const int SettingEntranceMaximumDelayMilliseconds = 168;
    private const int SettingEntrancePresentationWindowMilliseconds = 250;
    private const float SettingEntranceVerticalOffset = 18;
    private UISettings? _settingAnimationPreferences;
    private readonly HashSet<PluginSettingItem> _presentedSettings = new();
    private readonly Dictionary<UIElement, (PluginSettingItem Item, Visual Visual)> _settingTransitionVisuals = new();
    private ScalarKeyFrameAnimation? _settingEntranceFade;
    private Vector3KeyFrameAnimation? _settingEntranceSlide;
    private bool _settingEntrancePresentationActive;
    private bool _settingEntranceAnimationsEnabled;
    private long _lastSettingInsertionAt;
    private long _nextSettingEntranceAt;

    private void OnSettingItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResetSettingEntranceAnimations(_settingEntrancePresentationActive);
        }
        else if (e.Action == NotifyCollectionChangedAction.Add)
        {
            _lastSettingInsertionAt = Environment.TickCount64;
        }
    }

    private void OnSettingContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Phase != 0)
        {
            return;
        }

        var container = args.ItemContainer;
        if (!args.InRecycleQueue && _settingTransitionVisuals.TryGetValue(container, out var current) &&
            ReferenceEquals(current.Item, args.Item))
        {
            return;
        }

        if (_settingTransitionVisuals.Remove(container, out var previous))
        {
            RestoreSettingTransitionVisual(container, previous.Visual);
        }

        if (args.InRecycleQueue || args.Item is not PluginSettingItem item)
        {
            return;
        }
        if (_settingPinAnimationCancellation != null)
        {
            _presentedSettings.Add(item);
            HoldSettingPinContainer(container, item);
            return;
        }
        if (!_presentedSettings.Add(item)) return;

        var now = Environment.TickCount64;
        if (!_settingEntrancePresentationActive || !_settingEntranceAnimationsEnabled ||
            !ViewModel.IsConfigurationReady ||
            now - _lastSettingInsertionAt > SettingEntrancePresentationWindowMilliseconds)
        {
            return;
        }

        Visual? visual = null;
        try
        {
            visual = GetSettingTransitionVisual(container, item);
            PrepareSettingEntranceAnimations(visual.Compositor);
            var delayMilliseconds = Math.Clamp(_nextSettingEntranceAt - now, 0, SettingEntranceMaximumDelayMilliseconds);
            _nextSettingEntranceAt = now + delayMilliseconds + SettingEntranceStaggerMilliseconds;
            var delay = TimeSpan.FromMilliseconds(delayMilliseconds);
            _settingEntranceFade!.DelayTime = delay;
            _settingEntranceSlide!.DelayTime = delay;
            visual.Properties.InsertVector3(SettingTranslationProperty, new Vector3(0, SettingEntranceVerticalOffset, 0));
            visual.Opacity = 0;
            visual.StartAnimation(nameof(Visual.Opacity), _settingEntranceFade);
            visual.StartAnimation(SettingTranslationProperty, _settingEntranceSlide);
        }
        catch (Exception ex)
        {
            _settingEntranceAnimationsEnabled = false;
            _settingTransitionVisuals.Remove(container);
            if (visual != null)
            {
                RestoreSettingTransitionVisual(container, visual);
            }
            Debug.WriteLine($"[PluginSettings] Setting entrance animation failed: {ex}");
        }
    }

    private void PrepareSettingEntranceAnimations(Compositor compositor)
    {
        if (_settingEntranceFade != null && _settingEntranceSlide != null &&
            _settingEntranceFade.Compositor.Equals(compositor))
        {
            return;
        }

        _settingEntranceFade?.Dispose();
        _settingEntranceSlide?.Dispose();
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
        _settingEntranceFade = compositor.CreateScalarKeyFrameAnimation();
        _settingEntranceFade.InsertKeyFrame(0, 0);
        _settingEntranceFade.InsertKeyFrame(1, 1, easing);
        _settingEntranceFade.Duration = TimeSpan.FromMilliseconds(280);
        _settingEntranceFade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        _settingEntranceFade.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        _settingEntranceSlide = compositor.CreateVector3KeyFrameAnimation();
        _settingEntranceSlide.InsertKeyFrame(0, new Vector3(0, SettingEntranceVerticalOffset, 0));
        _settingEntranceSlide.InsertKeyFrame(1, Vector3.Zero, easing);
        _settingEntranceSlide.Duration = TimeSpan.FromMilliseconds(360);
        _settingEntranceSlide.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        _settingEntranceSlide.StopBehavior = AnimationStopBehavior.SetToFinalValue;
    }

    private void ResetSettingEntranceAnimations(bool presentationActive)
    {
        CancelSettingPinAnimations();
        _settingEntrancePresentationActive = presentationActive;
        RestoreSettingTransitionVisuals();
        _presentedSettings.Clear();
        _lastSettingInsertionAt = 0;
        _nextSettingEntranceAt = 0;
        _settingEntranceAnimationsEnabled = false;
        if (presentationActive)
        {
            try
            {
                _settingAnimationPreferences ??= new UISettings();
                _settingEntranceAnimationsEnabled = _settingAnimationPreferences.AnimationsEnabled;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginSettings] Animation preference read failed: {ex}");
            }
        }
        if (!presentationActive)
        {
            _settingEntranceFade?.Dispose();
            _settingEntranceSlide?.Dispose();
            _settingEntranceFade = null;
            _settingEntranceSlide = null;
        }
    }

    private Visual GetSettingTransitionVisual(UIElement container, PluginSettingItem item)
    {
        if (_settingTransitionVisuals.TryGetValue(container, out var current))
        {
            if (ReferenceEquals(current.Item, item)) return current.Visual;

            RestoreSettingTransitionVisual(container, current.Visual);
        }
        ElementCompositionPreview.SetIsTranslationEnabled(container, true);
        var visual = ElementCompositionPreview.GetElementVisual(container);
        visual.Properties.InsertVector3(SettingTranslationProperty, Vector3.Zero);
        _settingTransitionVisuals[container] = (item, visual);
        return visual;
    }

    private void RestoreSettingTransitionVisuals()
    {
        foreach (var entrance in _settingTransitionVisuals)
        {
            RestoreSettingTransitionVisual(entrance.Key, entrance.Value.Visual);
        }
        _settingTransitionVisuals.Clear();
    }

    private static void RestoreSettingTransitionVisual(UIElement container, Visual visual)
    {
        try
        {
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.StopAnimation(SettingTranslationProperty);
            visual.Opacity = (float)container.Opacity;
            visual.Properties.InsertVector3(SettingTranslationProperty, Vector3.Zero);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSettings] Setting transition reset failed: {ex}");
        }
    }
}
