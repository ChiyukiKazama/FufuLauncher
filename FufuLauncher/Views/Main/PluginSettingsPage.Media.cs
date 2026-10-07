/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage
{
    private void OnSettingHelpOpened(object sender, object e)
    {
        if (sender is not Flyout { Content: Grid { Tag: PluginSettingItem item } grid } ||
            grid.FindName("HelpImage") is not Image image)
        {
            return;
        }

        image.Visibility = Visibility.Visible;
        if (grid.FindName("ErrorText") is TextBlock error)
        {
            error.Visibility = Visibility.Collapsed;
        }

        var source = item.HelpImageSource;
        if (grid.FindName("LoadingRing") is ProgressRing ring)
        {
            ring.IsActive = source != null;
            ring.Visibility = source != null ? Visibility.Visible : Visibility.Collapsed;
        }
        image.Source = source;
        if (source == null)
        {
            image.Visibility = Visibility.Collapsed;
            if (grid.FindName("ErrorText") is TextBlock errorText)
            {
                errorText.Visibility = Visibility.Visible;
            }
        }
    }

    private void OnSettingHelpClosed(object sender, object e)
    {
        if (sender is not Flyout { Content: Grid grid })
        {
            return;
        }

        if (grid.FindName("HelpImage") is Image image)
        {
            image.Source = null;
        }
        if (grid.FindName("LoadingRing") is ProgressRing ring)
        {
            ring.IsActive = false;
        }
    }

    private void HelpImage_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Image img && img.Parent is Grid grid)
        {
            if (grid.FindName("LoadingRing") is ProgressRing loadingRing)
            {
                loadingRing.IsActive = false;
                loadingRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void HelpImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Image img && img.Parent is Grid grid)
        {
            img.Visibility = Visibility.Collapsed;

            if (grid.FindName("LoadingRing") is ProgressRing loadingRing)
            {
                loadingRing.IsActive = false;
                loadingRing.Visibility = Visibility.Collapsed;
            }

            if (grid.FindName("ErrorText") is TextBlock errorText)
            {
                errorText.Visibility = Visibility.Visible;
            }
        }
    }
}