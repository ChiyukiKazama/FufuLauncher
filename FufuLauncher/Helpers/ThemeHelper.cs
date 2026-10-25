/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using Windows.UI;

namespace FufuLauncher.Helpers
{
    public static class ThemeHelper
    {
        public static void ApplyThemeColor(string hexColor)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hexColor))
                {
                    return;
                }

                Color color = ParseColor(hexColor);


                Application.Current.Resources["SystemAccentColor"] = color;


                Application.Current.Resources["SystemAccentColorLight1"] = ChangeColorLightness(color, 0.15f);
                Application.Current.Resources["SystemAccentColorLight2"] = ChangeColorLightness(color, 0.30f);
                Application.Current.Resources["SystemAccentColorLight3"] = ChangeColorLightness(color, 0.45f);
                Application.Current.Resources["SystemAccentColorDark1"] = ChangeColorLightness(color, -0.15f);
                Application.Current.Resources["SystemAccentColorDark2"] = ChangeColorLightness(color, -0.30f);
                Application.Current.Resources["SystemAccentColorDark3"] = ChangeColorLightness(color, -0.45f);

                Application.Current.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(color);


                if (App.MainWindow?.Content is FrameworkElement rootElement)
                {
                    var currentTheme = rootElement.RequestedTheme;
                    rootElement.RequestedTheme = ElementTheme.Light;
                    rootElement.RequestedTheme = ElementTheme.Dark;
                    rootElement.RequestedTheme = currentTheme;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("ThemeHelper", $"Failed to apply theme color: {ex.Message}");
            }
        }

        public static Color GetSystemAccentColor()
        {
            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                return uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            }
            catch
            {
                return Color.FromArgb(255, 0, 120, 215);
            }
        }

        public static string ToHexColor(Color color)
        {
            return color.A == 255
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static Color ParseColor(string hex)
        {
            hex = hex.Replace("#", "");
            byte a = 255;
            byte r = 0;
            byte g = 0;
            byte b = 0;

            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex.Substring(0, 2), 16);
                r = Convert.ToByte(hex.Substring(2, 2), 16);
                g = Convert.ToByte(hex.Substring(4, 2), 16);
                b = Convert.ToByte(hex.Substring(6, 2), 16);
            }
            else if (hex.Length == 6)
            {
                r = Convert.ToByte(hex.Substring(0, 2), 16);
                g = Convert.ToByte(hex.Substring(2, 2), 16);
                b = Convert.ToByte(hex.Substring(4, 2), 16);
            }

            return Color.FromArgb(a, r, g, b);
        }

        private static Color ChangeColorLightness(Color color, float factor)
        {
            float r = color.R;
            float g = color.G;
            float b = color.B;

            if (factor < 0)
            {
                factor = 1 + factor;
                r *= factor;
                g *= factor;
                b *= factor;
            }
            else
            {
                r = (255 - r) * factor + r;
                g = (255 - g) * factor + g;
                b = (255 - b) * factor + b;
            }

            return Color.FromArgb(color.A, (byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255),
                (byte)Math.Clamp(b, 0, 255));
        }
    }
}