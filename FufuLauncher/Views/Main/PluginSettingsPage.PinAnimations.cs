using System.Diagnostics;
using System.Numerics;
using FufuLauncher.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage
{
    private const int SettingPinFadeOutMilliseconds = 120;
    private const int SettingPinFadeInMilliseconds = 220;
    private const int SettingPinRepositionMilliseconds = 280;
    private readonly HashSet<PluginSettingItem> _settingPinAnimationItems = new();
    private CancellationTokenSource? _settingPinAnimationCancellation;
    private TaskCompletionSource<bool>? _settingPinAnimationCompletion;
    private bool _settingPinRevealStarted;

    private async Task AnimateSettingPinAsync(IReadOnlyCollection<PluginSettingItem> items, bool pinned,
        bool clearSelection = false)
    {
        if (!IsLoaded || !_settingEntrancePresentationActive || !ViewModel.IsConfigurationReady ||
            (items.Count == 1 && !clearSelection && _settingPinAnimationItems.Contains(items.First())))
        {
            return;
        }

        var requested = items.Distinct().ToArray();
        var commit = ViewModel.PrepareSettingsPinChange(requested, pinned, clearSelection);
        if (commit == null) return;

        while (_settingPinAnimationCompletion is { } previous)
        {
            await previous.Task;
        }
        if (!IsLoaded || !_settingEntrancePresentationActive || !ViewModel.IsConfigurationReady)
        {
            return;
        }

        var changing = requested.Where(item => item.IsPinned == pinned &&
            (pinned ? ViewModel.Settings.Contains(item) : ViewModel.PinnedSettings.Contains(item))).ToArray();
        if (changing.Length == 0 || !_settingEntranceAnimationsEnabled)
        {
            commit();
            return;
        }

        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _settingPinAnimationCancellation = cancellation;
        _settingPinAnimationCompletion = completion;
        _settingPinAnimationItems.UnionWith(changing);
        _settingPinRevealStarted = false;
        var committed = false;
        try
        {
            ResetSettingsSelection();
            RestoreSettingTransitionVisuals();
            var outgoing = CaptureSettingPinLayout();
            var compositor = ElementCompositionPreview.GetElementVisual(SettingsGrid).Compositor;
            await RunSettingPinAnimationBatchAsync(compositor, () =>
            {
                var started = 0;
                foreach (var entry in outgoing)
                {
                    if (!_settingPinAnimationItems.Contains(entry.Key)) continue;
                    var visual = GetSettingTransitionVisual(entry.Value.Container, entry.Key);
                    StartSettingPinFade(visual, false);
                    started++;
                }
                return started;
            }, token);

            token.ThrowIfCancellationRequested();
            var previousLayout = CaptureSettingPinLayout();
            commit();
            committed = true;
            SettingsGrid.UpdateLayout();
            token.ThrowIfCancellationRequested();
            _settingPinRevealStarted = true;
            var currentLayout = CaptureSettingPinLayout();
            await RunSettingPinAnimationBatchAsync(compositor, () =>
            {
                var started = 0;
                foreach (var entry in currentLayout)
                {
                    if (_settingPinAnimationItems.Contains(entry.Key) ||
                        !previousLayout.TryGetValue(entry.Key, out var previousPosition))
                    {
                        StartSettingPinFade(GetSettingTransitionVisual(entry.Value.Container, entry.Key), true);
                    }
                    else
                    {
                        var offset = previousPosition.Position - entry.Value.Position;
                        if (offset.LengthSquared() < 0.25f) continue;

                        StartSettingPinReposition(GetSettingTransitionVisual(entry.Value.Container, entry.Key), offset);
                    }
                    started++;
                }
                return started;
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _settingEntranceAnimationsEnabled = false;
            if (!committed && !token.IsCancellationRequested &&
                ReferenceEquals(_settingPinAnimationCancellation, cancellation))
            {
                commit();
            }
            Debug.WriteLine($"[PluginSettings] Setting pin animation failed: {ex}");
        }
        finally
        {
            try
            {
                if (ReferenceEquals(_settingPinAnimationCancellation, cancellation))
                {
                    _settingPinAnimationCancellation = null;
                    _settingPinAnimationCompletion = null;
                    _settingPinAnimationItems.Clear();
                    _settingPinRevealStarted = false;
                    RestoreSettingTransitionVisuals();
                    if (IsLoaded && ViewModel.IsConfigurationReady)
                    {
                        CacheMarqueeTargets();
                        UpdateBatchBarPosition();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginSettings] Setting pin animation cleanup failed: {ex}");
            }
            finally
            {
                cancellation.Dispose();
                completion.TrySetResult(true);
            }
        }
    }

    private void CancelSettingPinAnimations()
    {
        var cancellation = _settingPinAnimationCancellation;
        _settingPinAnimationCancellation = null;
        _settingPinAnimationCompletion = null;
        _settingPinAnimationItems.Clear();
        _settingPinRevealStarted = false;
        cancellation?.Cancel();
    }

    private void HoldSettingPinContainer(UIElement container, PluginSettingItem item)
    {
        if (_settingPinRevealStarted || !_settingPinAnimationItems.Contains(item)) return;

        try
        {
            GetSettingTransitionVisual(container, item).Opacity = 0;
        }
        catch (Exception ex)
        {
            _settingEntranceAnimationsEnabled = false;
            Debug.WriteLine($"[PluginSettings] Setting pin container preparation failed: {ex}");
        }
    }

    private Dictionary<PluginSettingItem, (GridViewItem Container, Vector3 Position)> CaptureSettingPinLayout()
    {
        var result = new Dictionary<PluginSettingItem, (GridViewItem Container, Vector3 Position)>();
        foreach (var container in EnumerateRealizedSettingContainers())
        {
            if (container.Content is not PluginSettingItem item || container.ActualWidth <= 0 || container.ActualHeight <= 0)
            {
                continue;
            }
            var position = container.TransformToVisual(SettingsGrid).TransformPoint(new Point(0, 0));
            if (double.IsFinite(position.X) && double.IsFinite(position.Y))
            {
                result[item] = (container, new Vector3((float)position.X, (float)position.Y, 0));
            }
        }
        return result;
    }

    private IEnumerable<GridViewItem> EnumerateRealizedSettingContainers()
    {
        if (SettingsGrid.ItemsPanelRoot is not DependencyObject root) yield break;

        var elements = new Stack<DependencyObject>();
        elements.Push(root);
        while (elements.Count > 0)
        {
            var element = elements.Pop();
            if (element is GridViewItem container)
            {
                yield return container;
                continue;
            }
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            {
                elements.Push(VisualTreeHelper.GetChild(element, index));
            }
        }
    }

    private static void StartSettingPinFade(Visual visual, bool fadeIn)
    {
        var compositor = visual.Compositor;
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0.2f, 1));
        using var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, fadeIn ? 0 : 1);
        animation.InsertKeyFrame(1, fadeIn ? 1 : 0, easing);
        animation.Duration = TimeSpan.FromMilliseconds(fadeIn ? SettingPinFadeInMilliseconds : SettingPinFadeOutMilliseconds);
        animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        visual.Opacity = fadeIn ? 1 : 0;
        visual.StartAnimation(nameof(Visual.Opacity), animation);
    }

    private static void StartSettingPinReposition(Visual visual, Vector3 offset)
    {
        var compositor = visual.Compositor;
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
        using var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0, offset);
        animation.InsertKeyFrame(1, Vector3.Zero, easing);
        animation.Duration = TimeSpan.FromMilliseconds(SettingPinRepositionMilliseconds);
        animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        visual.Properties.InsertVector3(SettingTranslationProperty, Vector3.Zero);
        visual.StartAnimation(SettingTranslationProperty, animation);
    }

    private static async Task RunSettingPinAnimationBatchAsync(Compositor compositor, Func<int> animations,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        void OnCompleted(object? sender, CompositionBatchCompletedEventArgs args) => completion.TrySetResult(true);
        batch.Completed += OnCompleted;
        try
        {
            var started = animations();
            batch.End();
            if (started == 0) return;

            try
            {
                await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(800), token);
            }
            catch (TimeoutException)
            {
                Debug.WriteLine("[PluginSettings] Setting pin animation completion timed out.");
            }
        }
        finally
        {
            if (!batch.IsEnded)
            {
                batch.End();
            }
            batch.Completed -= OnCompleted;
        }
    }
}
