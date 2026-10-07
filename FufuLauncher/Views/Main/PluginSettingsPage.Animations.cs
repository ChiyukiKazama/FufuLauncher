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
    private const string SettingEntranceTranslation = "Translation";
    private const int SettingEntranceStaggerMilliseconds = 24;
    private const int SettingEntranceMaximumDelayMilliseconds = 168;
    private const int SettingEntrancePresentationWindowMilliseconds = 250;
    private const float SettingEntranceVerticalOffset = 18;
    private UISettings? _settingAnimationPreferences;
    private readonly HashSet<PluginSettingItem> _presentedSettings = new();
    private readonly Dictionary<UIElement, (PluginSettingItem Item, Visual Visual)> _settingEntranceVisuals = new();
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
        if (!args.InRecycleQueue && _settingEntranceVisuals.TryGetValue(container, out var current) &&
            ReferenceEquals(current.Item, args.Item))
        {
            return;
        }

        if (_settingEntranceVisuals.Remove(container, out var previous))
        {
            RestoreSettingEntranceVisual(container, previous.Visual);
        }

        if (args.InRecycleQueue || args.Item is not PluginSettingItem item || !_presentedSettings.Add(item))
        {
            return;
        }

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
            ElementCompositionPreview.SetIsTranslationEnabled(container, true);
            visual = ElementCompositionPreview.GetElementVisual(container);
            PrepareSettingEntranceAnimations(visual.Compositor);
            var delayMilliseconds = Math.Clamp(_nextSettingEntranceAt - now, 0, SettingEntranceMaximumDelayMilliseconds);
            _nextSettingEntranceAt = now + delayMilliseconds + SettingEntranceStaggerMilliseconds;
            var delay = TimeSpan.FromMilliseconds(delayMilliseconds);
            _settingEntranceFade!.DelayTime = delay;
            _settingEntranceSlide!.DelayTime = delay;
            visual.Properties.InsertVector3(SettingEntranceTranslation, new Vector3(0, SettingEntranceVerticalOffset, 0));
            visual.Opacity = 0;
            _settingEntranceVisuals[container] = (item, visual);
            visual.StartAnimation(nameof(Visual.Opacity), _settingEntranceFade);
            visual.StartAnimation(SettingEntranceTranslation, _settingEntranceSlide);
        }
        catch (Exception ex)
        {
            _settingEntranceAnimationsEnabled = false;
            _settingEntranceVisuals.Remove(container);
            if (visual != null)
            {
                RestoreSettingEntranceVisual(container, visual);
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
        _settingEntrancePresentationActive = presentationActive;
        foreach (var entrance in _settingEntranceVisuals)
        {
            RestoreSettingEntranceVisual(entrance.Key, entrance.Value.Visual);
        }
        _settingEntranceVisuals.Clear();
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

    private static void RestoreSettingEntranceVisual(UIElement container, Visual visual)
    {
        try
        {
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.StopAnimation(SettingEntranceTranslation);
            visual.Opacity = (float)container.Opacity;
            visual.Properties.InsertVector3(SettingEntranceTranslation, Vector3.Zero);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSettings] Setting entrance animation reset failed: {ex}");
        }
    }
}
