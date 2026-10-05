using FufuLauncher.Helpers;
using FufuLauncher.Services;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

internal static class DeviceBanConsentDialog
{
    internal static async Task ShowIfNeededAsync(MotherboardBanService service)
    {
        try { await ShowAsync(service); }
        catch (Exception ex) { Debug.WriteLine($"[设备限制] 告知窗口未完成: {ex.GetType().Name}"); }
    }

    private static async Task ShowAsync(MotherboardBanService service)
    {
        if (!await service.NeedsConsentAsync() || App.MainWindow?.Content?.XamlRoot is not { } xamlRoot) return;
        var dialog = new ContentDialog
        {
            Title = "DeviceBan_ConsentTitle".GetLocalized(),
            Content = new TextBlock
            {
                Text = string.Format("DeviceBan_ConsentMessage".GetLocalized().Replace("\\n", "\n"), service.ServiceAddress),
                TextWrapping = TextWrapping.Wrap, MaxWidth = 520
            },
            PrimaryButtonText = "DeviceBan_ConsentAccept".GetLocalized(),
            CloseButtonText = "DeviceBan_NormalLaunch".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await service.GiveConsentAsync();
    }
}
