using System.Diagnostics;
using System.Numerics;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage
{
    private const string HeaderCollapsedPreferenceKey = "OfficialPluginSettingsHeaderCollapsed";
    private bool _configurationChromeActive;
    private bool _settingsHeaderCollapsed;
    private bool _settingsHeaderPreferenceTouched;
    private int _configurationChromeSession;
    private Task<bool>? _settingsHeaderPreference;
    private Task _settingsHeaderPreferenceSave = Task.CompletedTask;
    private CancellationTokenSource? _settingsHeaderTransition;
    private CancellationTokenSource? _configurationLoadingTransition;

    private void InitializeConfigurationChrome()
    {
        StopConfigurationChrome();
        _configurationChromeActive = true;
        ApplySectionVisibility(SettingsHeaderDetails, SettingsBodyViewport, !_settingsHeaderCollapsed);
        ApplySectionVisibility(ConfigurationLoadingHost, SettingsViewport, ViewModel.IsLoadingConfiguration);
        ConfigurationLoadingProgress.IsIndeterminate = ViewModel.IsLoadingConfiguration;
        UpdateSettingsHeaderMenu();
        _ = RestoreSettingsHeaderPreferenceAsync(_configurationChromeSession);
    }

    private void StopConfigurationChrome()
    {
        _configurationChromeActive = false;
        _configurationChromeSession++;
        CancelChromeTransition(ref _settingsHeaderTransition);
        CancelChromeTransition(ref _configurationLoadingTransition);
        ApplySectionVisibility(SettingsHeaderDetails, SettingsBodyViewport, !_settingsHeaderCollapsed);
        ApplySectionVisibility(ConfigurationLoadingHost, SettingsViewport, false);
        ConfigurationLoadingProgress.IsIndeterminate = false;
        SettingsHeaderVisibilityButton.IsEnabled = true;
    }

    private void UpdateConfigurationLoadingPresentation()
    {
        if (!_configurationChromeActive) return;

        CancelChromeTransition(ref _configurationLoadingTransition);
        RestoreChromeVisual(SettingsViewport, true);
        RestoreChromeVisual(ConfigurationLoadingHost, false);
        ConfigurationLoadingProgress.IsIndeterminate = ViewModel.IsLoadingConfiguration;
        if (ViewModel.IsLoadingConfiguration)
        {
            ConfigurationLoadingHost.Visibility = Visibility.Visible;
            ConfigurationLoadingProgress.Value = 0;
            return;
        }
        ConfigurationLoadingProgress.Value = ViewModel.IsConfigurationReady ? ConfigurationLoadingProgress.Maximum : 0;
        if (ConfigurationLoadingHost.Visibility == Visibility.Collapsed) return;
        if (!_settingEntranceAnimationsEnabled)
        {
            ApplySectionVisibility(ConfigurationLoadingHost, SettingsViewport, false);
            return;
        }
        var cancellation = new CancellationTokenSource();
        _configurationLoadingTransition = cancellation;
        _ = FinishConfigurationLoadingPresentationAsync(cancellation);
    }

    private async Task FinishConfigurationLoadingPresentationAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            await AnimateSectionVisibilityAsync(ConfigurationLoadingHost, SettingsViewport, SettingsBodyViewport,
                false, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSettings] Loading indicator transition failed: {ex}");
        }
        finally
        {
            if (ReferenceEquals(_configurationLoadingTransition, cancellation))
            {
                _configurationLoadingTransition = null;
                ApplySectionVisibility(ConfigurationLoadingHost, SettingsViewport, ViewModel.IsLoadingConfiguration);
            }
            cancellation.Dispose();
        }
    }

    private void OnToggleSettingsHeaderClick(object sender, RoutedEventArgs e)
    {
        if (!_configurationChromeActive || _settingsHeaderTransition != null) return;

        _settingsHeaderPreferenceTouched = true;
        _settingsHeaderCollapsed = !_settingsHeaderCollapsed;
        UpdateSettingsHeaderMenu();
        SettingsFunctionsButton.Flyout?.Hide();
        _settingsHeaderPreferenceSave = SaveSettingsHeaderPreferenceAsync(_settingsHeaderPreferenceSave,
            _settingsHeaderCollapsed);
        if (!_settingEntranceAnimationsEnabled)
        {
            ApplySectionVisibility(SettingsHeaderDetails, SettingsBodyViewport, !_settingsHeaderCollapsed);
            return;
        }
        var cancellation = new CancellationTokenSource();
        _settingsHeaderTransition = cancellation;
        SettingsHeaderVisibilityButton.IsEnabled = false;
        _ = AnimateSettingsHeaderAsync(cancellation, !_settingsHeaderCollapsed);
    }

    private async Task AnimateSettingsHeaderAsync(CancellationTokenSource cancellation, bool visible)
    {
        var token = cancellation.Token;
        try
        {
            await AnimateSectionVisibilityAsync(SettingsHeaderDetails, SettingsBodyViewport, RootLayoutGrid,
                visible, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSettings] Header transition failed: {ex}");
        }
        finally
        {
            if (ReferenceEquals(_settingsHeaderTransition, cancellation))
            {
                _settingsHeaderTransition = null;
                ApplySectionVisibility(SettingsHeaderDetails, SettingsBodyViewport, !_settingsHeaderCollapsed);
                SettingsHeaderVisibilityButton.IsEnabled = true;
            }
            cancellation.Dispose();
        }
    }

    private async Task RestoreSettingsHeaderPreferenceAsync(int session)
    {
        if (_settingsHeaderPreferenceTouched) return;

        try
        {
            _settingsHeaderPreference ??= ReadSettingsHeaderPreferenceAsync();
            var collapsed = await _settingsHeaderPreference;
            if (!_configurationChromeActive || session != _configurationChromeSession ||
                _settingsHeaderPreferenceTouched)
            {
                return;
            }
            _settingsHeaderCollapsed = collapsed;
            ApplySectionVisibility(SettingsHeaderDetails, SettingsBodyViewport, !collapsed);
            UpdateSettingsHeaderMenu();
        }
        catch (Exception ex)
        {
            _settingsHeaderPreference = null;
            Debug.WriteLine($"[PluginSettings] Header preference load failed: {ex}");
        }
    }

    private static async Task<bool> ReadSettingsHeaderPreferenceAsync()
    {
        var value = await App.GetService<ILocalSettingsService>().ReadSettingAsync(HeaderCollapsedPreferenceKey);
        return value != null && Convert.ToBoolean(value);
    }

    private static async Task SaveSettingsHeaderPreferenceAsync(Task previous, bool collapsed)
    {
        try
        {
            await previous;
            await App.GetService<ILocalSettingsService>().SaveSettingAsync(HeaderCollapsedPreferenceKey, collapsed);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSettings] Header preference save failed: {ex}");
        }
    }

    private void UpdateSettingsHeaderMenu()
    {
        SettingsHeaderVisibilityButton.Content = (_settingsHeaderCollapsed
            ? "PluginSettings_ExpandHeader" : "PluginSettings_CollapseHeader").GetLocalized();
    }

    private static void CancelChromeTransition(ref CancellationTokenSource? current)
    {
        var previous = current;
        current = null;
        previous?.Cancel();
    }

    private static async Task AnimateSectionVisibilityAsync(FrameworkElement section, FrameworkElement following,
        FrameworkElement parent, bool visible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var sectionVisual = ElementCompositionPreview.GetElementVisual(section);
        var followingVisual = GetInitializedTranslationVisual(following);
        var compositor = sectionVisual.Compositor;
        section.IsHitTestVisible = false;
        if (!visible && section.Visibility == Visibility.Visible)
        {
            await RunSettingsAnimationBatchAsync(compositor, () =>
            {
                StartChromeFade(sectionVisual, 1, 0);
                return 1;
            }, token);
        }
        token.ThrowIfCancellationRequested();
        var previous = following.TransformToVisual(parent).TransformPoint(new Point(0, 0)).Y;
        sectionVisual.Opacity = 0;
        section.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        parent.UpdateLayout();
        var current = following.TransformToVisual(parent).TransformPoint(new Point(0, 0)).Y;
        var offset = (float)(previous - current);
        if (float.IsFinite(offset) && Math.Abs(offset) > 0.5f)
        {
            await RunSettingsAnimationBatchAsync(compositor, () =>
            {
                StartChromeReposition(followingVisual, offset);
                return 1;
            }, token);
        }
        token.ThrowIfCancellationRequested();
        if (visible)
        {
            await RunSettingsAnimationBatchAsync(compositor, () =>
            {
                StartChromeFade(sectionVisual, 0, 1);
                return 1;
            }, token);
        }
    }

    private static void StartChromeFade(Visual visual, float from, float to)
    {
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0.2f, 1));
        animation.InsertKeyFrame(0, from);
        animation.InsertKeyFrame(1, to, easing);
        animation.Duration = TimeSpan.FromMilliseconds(130);
        animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        visual.Opacity = to;
        visual.StartAnimation(nameof(Visual.Opacity), animation);
    }

    private static void StartChromeReposition(Visual visual, float offset)
    {
        using var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
        animation.InsertKeyFrame(0, new Vector3(0, offset, 0));
        animation.InsertKeyFrame(1, Vector3.Zero, easing);
        animation.Duration = TimeSpan.FromMilliseconds(240);
        animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        visual.Properties.InsertVector3(SettingTranslationProperty, Vector3.Zero);
        visual.Properties.StartAnimation(SettingTranslationProperty, animation);
    }

    private static void ApplySectionVisibility(FrameworkElement section, FrameworkElement following, bool visible)
    {
        RestoreChromeVisual(section, false);
        RestoreChromeVisual(following, true);
        section.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        section.IsHitTestVisible = visible;
    }

    private static void RestoreChromeVisual(UIElement element, bool translation)
    {
        try
        {
            if (translation)
            {
                ResetSettingTranslation(GetInitializedTranslationVisual(element));
            }
            else
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                visual.StopAnimation(nameof(Visual.Opacity));
                visual.Opacity = (float)element.Opacity;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSettings] Chrome animation reset failed: {ex}");
        }
    }
}
