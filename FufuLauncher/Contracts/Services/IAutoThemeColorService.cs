/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Contracts.Services;

public interface IAutoThemeColorService
{
    string? ThemeColorHex
    {
        get;
    }

    Task<bool> IsEnabledAsync();

    Task<bool> ApplyForBackgroundAsync(ImageSource? backgroundImage);

    Task<bool> ApplyForCurrentBackgroundAsync();

    Task RestoreManualColorsAsync();
}