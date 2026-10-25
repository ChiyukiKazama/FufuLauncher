/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Views;

public sealed partial class DataCenterWindow : Window
{
    public DataCenterWindow()
    {
        InitializeComponent();

        RootGrid.RequestedTheme = App.GetService<IThemeSelectorService>().Theme;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        WindowManagerHelper.ResizeWithDpi(AppWindow, this, 1120, 760);
        WindowManagerHelper.CenterWindowOnScreen(AppWindow, 1120, 760);

        ContentFrame.Navigate(typeof(DataPage), this);
    }
}