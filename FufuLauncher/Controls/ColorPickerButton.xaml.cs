using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace FufuLauncher.Controls;

public sealed partial class ColorPickerButton : UserControl
{
    private static readonly string[] PaletteHexes =
    {
        "#FF0078D7", "#FF00B7C3", "#FF00CC6A", "#FF107C10",
        "#FFFFB900", "#FFFF8C00", "#FFE81123", "#FFE3008C",
        "#FF8764B8", "#FF5C2D91", "#FF6B69D6", "#FF8E8E93",
        "#FFB4B4B4", "#FFFFFFFF", "#FF1F1F1F", "#FF000000"
    };

    private readonly SolidColorBrush _hueBrush = new();
    private readonly SolidColorBrush _previewBrush = new();
    private readonly SolidColorBrush _buttonSwatchBrush = new();
    private readonly List<Border> _paletteSwatches = new();
    private readonly Style _swatchStyle;
    private readonly Style _swatchSelectedStyle;

    private Border? _selectedSwatch;
    private double _hue;
    private double _saturation;
    private double _value;
    private byte _alpha = 255;
    private bool _isSyncing;
    private bool _isUpdatingColor;
    private bool _isDraggingSpectrum;
    private bool _isDraggingHue;

    public ColorPickerButton()
    {
        InitializeComponent();

        _swatchStyle = (Style)Resources["PaletteSwatchStyle"];
        _swatchSelectedStyle = (Style)Resources["PaletteSwatchSelectedStyle"];

        HueBaseRect.Fill = _hueBrush;
        PreviewSwatch.Background = _previewBrush;
        ButtonSwatch.Background = _buttonSwatchBrush;

        BuildPalette();
        Loaded += OnLoaded;
    }

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color),
        typeof(Windows.UI.Color),
        typeof(ColorPickerButton),
        new PropertyMetadata(Windows.UI.Color.FromArgb(255, 0, 120, 215), OnColorChanged));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(ColorPickerButton),
        new PropertyMetadata(string.Empty, OnHeaderChanged));

    public Windows.UI.Color Color
    {
        get => (Windows.UI.Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public string Header
    {
        get => GetValue(HeaderProperty) as string ?? string.Empty;
        set => SetValue(HeaderProperty, value);
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ColorPickerButton)d;
        if (control._isUpdatingColor)
            return;

        if (e.NewValue is Windows.UI.Color color)
            control.ApplyColor(color);
    }

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ColorPickerButton)d).ApplyHeader(e.NewValue as string);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyHeader(Header);
        ApplyColor(Color);
        DispatcherQueue.TryEnqueue(UpdateThumbPositions);
    }

    private void ApplyHeader(string? header)
    {
        var text = header ?? string.Empty;
        AutomationProperties.SetName(PickerButton, text);
        AutomationProperties.SetName(SpectrumFocusTarget, text);
        AutomationProperties.SetName(HueFocusTarget, text);
    }

    private void ApplyColor(Windows.UI.Color color)
    {
        _alpha = color.A;
        RgbToHsv(color, out _hue, out _saturation, out _value);
        RefreshVisuals(color, false);
    }

    private void RefreshVisuals(Windows.UI.Color color, bool isPreview)
    {
        var hueColor = HsvToRgb(_hue, 1, 1, 255);
        if (_hueBrush.Color != hueColor)
            _hueBrush.Color = hueColor;

        if (_previewBrush.Color != color)
            _previewBrush.Color = color;

        if (!isPreview)
        {
            _isSyncing = true;
            try
            {
                _buttonSwatchBrush.Color = color;
                var hex = ToHex(color);
                PreviewHexText.Text = hex;
                PreviewRgbText.Text = $"RGB({color.R}, {color.G}, {color.B})";
                ButtonHexText.Text = hex;

                if (HexBox.FocusState == FocusState.Unfocused)
                    HexBox.Text = hex;
            }
            finally
            {
                _isSyncing = false;
            }

            UpdatePaletteSelection(color);
        }

        UpdateThumbPositions();
    }

    private void CommitHsvColor()
    {
        var color = HsvToRgb(_hue, _saturation, _value, _alpha);
        _isUpdatingColor = true;
        try
        {
            Color = color;
        }
        finally
        {
            _isUpdatingColor = false;
        }

        RefreshVisuals(color, false);
    }

    private void PreviewHsvColor()
    {
        RefreshVisuals(HsvToRgb(_hue, _saturation, _value, _alpha), true);
    }

    private void UpdateSpectrum(Point position)
    {
        var width = SpectrumCanvas.ActualWidth;
        var height = SpectrumCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        _saturation = Math.Clamp(position.X / width, 0, 1);
        _value = 1 - Math.Clamp(position.Y / height, 0, 1);
        PreviewHsvColor();
    }

    private void UpdateHue(Point position)
    {
        var width = HueCanvas.ActualWidth;
        if (width <= 0)
            return;

        _hue = Math.Clamp(position.X / width, 0, 1) * 360;
        PreviewHsvColor();
    }

    private void UpdateThumbPositions()
    {
        var spectrumWidth = SpectrumCanvas.ActualWidth;
        var spectrumHeight = SpectrumCanvas.ActualHeight;
        if (spectrumWidth > 0 && spectrumHeight > 0)
        {
            var x = Math.Clamp(_saturation, 0, 1) * spectrumWidth;
            var y = (1 - Math.Clamp(_value, 0, 1)) * spectrumHeight;
            Canvas.SetLeft(SpectrumThumb, x - SpectrumThumb.Width / 2);
            Canvas.SetTop(SpectrumThumb, y - SpectrumThumb.Height / 2);
        }

        var hueWidth = HueCanvas.ActualWidth;
        var hueHeight = HueCanvas.ActualHeight;
        if (hueWidth > 0 && hueHeight > 0)
        {
            var x = Math.Clamp(_hue / 360.0, 0, 1) * hueWidth;
            Canvas.SetLeft(HueThumb, x - HueThumb.Width / 2);
            Canvas.SetTop(HueThumb, (hueHeight - HueThumb.Height) / 2);
        }
    }

    private void UpdatePaletteSelection(Windows.UI.Color color)
    {
        Border? selected = null;
        foreach (var swatch in _paletteSwatches)
        {
            if (swatch.Tag is Windows.UI.Color swatchColor && swatchColor == color)
            {
                selected = swatch;
                break;
            }
        }

        if (ReferenceEquals(selected, _selectedSwatch))
            return;

        if (_selectedSwatch is not null)
            _selectedSwatch.Style = _swatchStyle;

        _selectedSwatch = selected;

        if (_selectedSwatch is not null)
            _selectedSwatch.Style = _swatchSelectedStyle;
    }

    private void BuildPalette()
    {
        const int columns = 8;
        var rows = (PaletteHexes.Length + columns - 1) / columns;

        for (var i = 0; i < columns; i++)
            PaletteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < rows; i++)
            PaletteGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < PaletteHexes.Length; i++)
        {
            if (!TryParseHex(PaletteHexes[i], out var color))
                continue;

            var swatch = new Border
            {
                Style = _swatchStyle,
                Background = new SolidColorBrush(color),
                Tag = color
            };

            var button = new Button
            {
                Style = (Style)Resources["PaletteButtonStyle"],
                Content = swatch,
                Tag = color
            };

            AutomationProperties.SetName(button, ToHex(color));
            button.Click += OnPaletteClick;

            Grid.SetColumn(button, i % columns);
            Grid.SetRow(button, i / columns);
            PaletteGrid.Children.Add(button);
            _paletteSwatches.Add(swatch);
        }
    }

    private void OnPaletteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Windows.UI.Color color })
            Color = color;
    }

    private void OnSpectrumPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingSpectrum = true;
        SpectrumFocusTarget.Focus(FocusState.Pointer);
        SpectrumCanvas.CapturePointer(e.Pointer);
        UpdateSpectrum(e.GetCurrentPoint(SpectrumCanvas).Position);
        e.Handled = true;
    }

    private void OnSpectrumPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSpectrum)
            return;

        UpdateSpectrum(e.GetCurrentPoint(SpectrumCanvas).Position);
        e.Handled = true;
    }

    private void OnSpectrumPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSpectrum)
            return;

        _isDraggingSpectrum = false;
        SpectrumCanvas.ReleasePointerCapture(e.Pointer);
        CommitHsvColor();
        e.Handled = true;
    }

    private void OnSpectrumPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSpectrum)
            return;

        _isDraggingSpectrum = false;
        CommitHsvColor();
    }

    private void OnHuePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingHue = true;
        HueFocusTarget.Focus(FocusState.Pointer);
        HueCanvas.CapturePointer(e.Pointer);
        UpdateHue(e.GetCurrentPoint(HueCanvas).Position);
        e.Handled = true;
    }

    private void OnHuePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingHue)
            return;

        UpdateHue(e.GetCurrentPoint(HueCanvas).Position);
        e.Handled = true;
    }

    private void OnHuePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingHue)
            return;

        _isDraggingHue = false;
        HueCanvas.ReleasePointerCapture(e.Pointer);
        CommitHsvColor();
        e.Handled = true;
    }

    private void OnHuePointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingHue)
            return;

        _isDraggingHue = false;
        CommitHsvColor();
    }

    private void OnSpectrumKeyDown(object sender, KeyRoutedEventArgs e)
    {
        const double step = 0.01;
        switch (e.Key)
        {
            case VirtualKey.Left:
                _saturation = Math.Clamp(_saturation - step, 0, 1);
                break;
            case VirtualKey.Right:
                _saturation = Math.Clamp(_saturation + step, 0, 1);
                break;
            case VirtualKey.Up:
                _value = Math.Clamp(_value + step, 0, 1);
                break;
            case VirtualKey.Down:
                _value = Math.Clamp(_value - step, 0, 1);
                break;
            default:
                return;
        }

        CommitHsvColor();
        e.Handled = true;
    }

    private void OnHueKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left:
                _hue = (_hue - 1 + 360) % 360;
                break;
            case VirtualKey.Right:
                _hue = (_hue + 1) % 360;
                break;
            default:
                return;
        }

        CommitHsvColor();
        e.Handled = true;
    }

    private void OnSpectrumGotFocus(object sender, RoutedEventArgs e)
    {
        SpectrumFocusRing.Opacity = 1;
    }

    private void OnSpectrumLostFocus(object sender, RoutedEventArgs e)
    {
        SpectrumFocusRing.Opacity = 0;
    }

    private void OnHueGotFocus(object sender, RoutedEventArgs e)
    {
        HueFocusRing.Opacity = 1;
    }

    private void OnHueLostFocus(object sender, RoutedEventArgs e)
    {
        HueFocusRing.Opacity = 0;
    }

    private void OnPickerHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateThumbPositions();
    }

    private void OnHexTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncing)
            return;

        if (TryParseHex(HexBox.Text, out var color))
            Color = color;
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e)
    {
        if (TryParseHex(HexBox.Text, out var color))
            Color = color;

        SetHexText(ToHex(Color));
    }

    private void SetHexText(string text)
    {
        _isSyncing = true;
        try
        {
            HexBox.Text = text;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnFlyoutOpened(object sender, object e)
    {
        ApplyHeader(Header);
        ApplyColor(Color);
        DispatcherQueue.TryEnqueue(UpdateThumbPositions);
    }

    private void OnFlyoutClosed(object sender, object e)
    {
        if (TryParseHex(HexBox.Text, out var color))
            Color = color;

        SetHexText(ToHex(Color));
    }

    private static void RgbToHsv(Windows.UI.Color color, out double hue, out double saturation, out double value)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        hue = 0;
        if (delta > 0)
        {
            if (max == r)
                hue = 60 * (((g - b) / delta) % 6);
            else if (max == g)
                hue = 60 * (((b - r) / delta) + 2);
            else
                hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
            hue += 360;

        saturation = max <= 0 ? 0 : delta / max;
        value = max;
    }

    private static Windows.UI.Color HsvToRgb(double hue, double saturation, double value, byte alpha)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = value - c;

        double r;
        double g;
        double b;

        if (hue < 60)
        {
            r = c;
            g = x;
            b = 0;
        }
        else if (hue < 120)
        {
            r = x;
            g = c;
            b = 0;
        }
        else if (hue < 180)
        {
            r = 0;
            g = c;
            b = x;
        }
        else if (hue < 240)
        {
            r = 0;
            g = x;
            b = c;
        }
        else if (hue < 300)
        {
            r = x;
            g = 0;
            b = c;
        }
        else
        {
            r = c;
            g = 0;
            b = x;
        }

        return Windows.UI.Color.FromArgb(
            alpha,
            (byte)Math.Round(Math.Clamp((r + m) * 255, 0, 255)),
            (byte)Math.Round(Math.Clamp((g + m) * 255, 0, 255)),
            (byte)Math.Round(Math.Clamp((b + m) * 255, 0, 255)));
    }

    private static bool TryParseHex(string? text, out Windows.UI.Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var hex = text.Trim();
        if (hex.StartsWith("#"))
            hex = hex[1..];

        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";

        if (hex.Length != 6 && hex.Length != 8)
            return false;

        try
        {
            var offset = 0;
            byte a = 255;

            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex[..2], 16);
                offset = 2;
            }

            color = Windows.UI.Color.FromArgb(
                a,
                Convert.ToByte(hex.Substring(offset, 2), 16),
                Convert.ToByte(hex.Substring(offset + 2, 2), 16),
                Convert.ToByte(hex.Substring(offset + 4, 2), 16));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ToHex(Windows.UI.Color color)
    {
        return color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}