/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    #region 导航与滚动联动

    private void SettingsNavigationView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        DebugLog.Info("SettingsPage.Navigation", "SettingsNav.SelectionChanged",
            $"tag={(args.SelectedItem as NavigationViewItem)?.Tag} navigating={_isNavigatingFromMenu}");
        if (_isNavigatingFromMenu) return;

        if (args.SelectedItem is NavigationViewItem selectedItem &&
            selectedItem.Tag is string tag)
        {
            _ = ViewModel.RequestSectionAsync(tag);

            _isNavigatingFromMenu = true;


            _navLockTimer?.Stop();
            _navLockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _navLockTimer.Tick += (s, e) =>
            {
                ((DispatcherTimer)s).Stop();
                _isNavigatingFromMenu = false;
            };
            _navLockTimer.Start();

            var element = FindName(tag) as FrameworkElement;
            if (element != null)
            {
                var animateScroll = tag != SettingsSectionIds.ImageSource;
                if (element.ActualHeight > 0)
                {
                    BringElementIntoView(element, animateScroll);
                }
                else
                {
                    RoutedEventHandler loadedHandler = null;
                    loadedHandler = (s, e) =>
                    {
                        BringElementIntoView(element, animateScroll);
                        element.Loaded -= loadedHandler;
                    };
                    element.Loaded += loadedHandler;
                }
            }
        }
    }

    private void SettingsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;


        if (_isNavigatingFromMenu)
        {
            _navLockTimer?.Stop();
            _isNavigatingFromMenu = false;
            return;
        }

        var scrollViewer = (ScrollViewer)sender;
        double anchor = scrollViewer.Padding.Top + 1;
        var visibleTag = (string?)null;

        foreach (var tag in _sectionTags)
        {
            var element = FindName(tag) as FrameworkElement;
            if (element == null) continue;

            var transform = element.TransformToVisual(scrollViewer);
            var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            if (position.Y <= anchor)
            {
                visibleTag = tag;
            }
        }

        if (visibleTag != null)
        {
            _ = ViewModel.RequestSectionAsync(visibleTag);

            _isNavigatingFromMenu = true;
            var targetItem = SettingsNavigationView.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == visibleTag);
            if (targetItem != null && (NavigationViewItem?)SettingsNavigationView.SelectedItem != targetItem)
            {
                SettingsNavigationView.SelectedItem = targetItem;
            }

            _isNavigatingFromMenu = false;
        }
    }

    private void BringElementIntoView(FrameworkElement element, bool animate = true)
    {
        DebugLog.Info("SettingsPage.Navigation", "SettingsNav.BringIntoView",
            $"element={element?.Name} animate={animate}");
        if (element == null) return;

        var bringIntoViewOptions = new BringIntoViewOptions
        {
            AnimationDesired = animate,
            VerticalAlignmentRatio = 0.0
        };

        element.StartBringIntoView(bringIntoViewOptions);
    }

    public async Task NavigateToUpdateSectionAsync()
    {
        await ViewModel.RequestSectionAsync(SettingsSectionIds.Updates);

        var updateNavItem = SettingsNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "UpdateItem");

        if (updateNavItem != null)
        {
            SettingsNavigationView.SelectedItem = updateNavItem;
        }

        await Task.Delay(120);

        if (UpdateItem != null)
        {
            BringElementIntoView(UpdateItem);
        }

        await Task.Delay(120);

        CheckUpdateButton?.Focus(FocusState.Programmatic);
    }

    public async Task NavigateToCheckinSettingsAsync()
    {
        await ViewModel.RequestSectionAsync(SettingsSectionIds.Checkin);

        var checkinNavItem = SettingsNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "CheckinSettingsItem");

        if (checkinNavItem != null)
        {
            SettingsNavigationView.SelectedItem = checkinNavItem;
        }

        await Task.Delay(120);

        if (CheckinSettingsItem != null)
        {
            BringElementIntoView(CheckinSettingsItem);
        }
    }

    public async Task NavigateToNotificationPositionAsync()
    {
        await ViewModel.RequestSectionAsync(SettingsSectionIds.WindowBehavior);

        var windowBehaviorNavItem = SettingsNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "WindowBehaviorItem");

        if (windowBehaviorNavItem != null)
        {
            SettingsNavigationView.SelectedItem = windowBehaviorNavItem;
        }

        await Task.Delay(120);

        if (NotificationPositionSettingRow != null)
        {
            BringElementIntoView(NotificationPositionSettingRow);
        }
    }

    public async Task NavigateToTrustInstallAsync()
    {
        await ViewModel.RequestSectionAsync(SettingsSectionIds.SecurityAuth);

        var securityItem = SettingsNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "SecurityAuthItem");

        if (securityItem != null)
        {
            SettingsNavigationView.SelectedItem = securityItem;
        }

        await Task.Delay(120);

        if (ModTrustInstallRow != null)
        {
            BringElementIntoView(ModTrustInstallRow);
        }

        await Task.Delay(120);

        ModTrustInstallUserButton?.Focus(FocusState.Programmatic);
    }

    #endregion
}