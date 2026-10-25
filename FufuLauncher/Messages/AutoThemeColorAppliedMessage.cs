/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Messages;

public sealed class AutoThemeColorAppliedMessage
{
    public AutoThemeColorAppliedMessage(string themeColorHex, string launchButtonColorHex)
    {
        ThemeColorHex = themeColorHex;
        LaunchButtonColorHex = launchButtonColorHex;
    }

    public string ThemeColorHex
    {
        get;
    }

    public string LaunchButtonColorHex
    {
        get;
    }
}
