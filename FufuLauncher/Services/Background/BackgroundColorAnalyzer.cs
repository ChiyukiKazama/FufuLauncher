/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Services.Background;

internal sealed class BackgroundColorPalette
{
    public Windows.UI.Color ThemeColor
    {
        get;
        init;
    }

    public Windows.UI.Color SourceColor
    {
        get;
        init;
    }

    public double Coverage
    {
        get;
        init;
    }

    public double Flatness
    {
        get;
        init;
    }

    public double Saturation
    {
        get;
        init;
    }

    public bool IsFillColor
    {
        get;
        init;
    }
}

internal static class BackgroundColorAnalyzer
{
    private const int MaxSampleEdge = 160;
    private const int HueBucketCount = 18;
    private const int MinSampleCount = 64;

    private const double NeutralSaturation = 0.10;
    private const double MinThemeSaturation = 0.08;
    private const double MinAlternativeCoverage = 0.03;
    private const double MinAlternativeScoreRatio = 0.30;
    private const double MaxFillCoverage = 0.85;
    private const double CoverageScoreExponent = 0.85;
    private const int FlatDeltaThreshold = 32;

    private const double ThemeMinSaturation = 0.42;
    private const double ThemeMaxSaturation = 0.82;
    private const double ThemeMinLightness = 0.38;
    private const double ThemeMaxLightness = 0.66;

    public static BackgroundColorPalette? Analyze(byte[] bgraPixels, int width, int height)
    {
        if (bgraPixels == null || width <= 0 || height <= 0)
            return null;

        var pixelCount = (long)width * height;
        if (pixelCount * 4 > bgraPixels.Length)
            return null;

        var buckets = new HueBucket[HueBucketCount];
        var neutralBucket = new HueBucket();
        var totalSamples = 0;
        var stride = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / (double)MaxSampleEdge));

        for (var y = 0; y < height; y += stride)
        {
            var rowOffset = (long)y * width;
            for (var x = 0; x < width; x += stride)
            {
                var index = (int)((rowOffset + x) * 4);
                if (bgraPixels[index + 3] < 128)
                    continue;

                var blue = bgraPixels[index];
                var green = bgraPixels[index + 1];
                var red = bgraPixels[index + 2];

                RgbToHsl(red, green, blue, out var hue, out var saturation, out var lightness);
                var sample = new ColorSample(red, green, blue, saturation,
                    ComputeQuality(saturation, lightness),
                    IsFlatArea(bgraPixels, width, height, x, y, stride, red, green, blue));

                totalSamples++;
                if (saturation < NeutralSaturation)
                {
                    neutralBucket.Accumulate(sample);
                    continue;
                }

                var bucketIndex = Math.Clamp((int)(hue / (360.0 / HueBucketCount)), 0, HueBucketCount - 1);
                buckets[bucketIndex].Accumulate(sample);
            }
        }

        if (totalSamples < MinSampleCount)
            return null;

        var candidates = new List<ColorCandidate>(HueBucketCount + 1);
        foreach (var bucket in buckets)
            AddCandidate(bucket, totalSamples, candidates);

        AddCandidate(neutralBucket, totalSamples, candidates);

        if (candidates.Count == 0)
            return null;

        var chosen = SelectCandidate(candidates);
        if (chosen.Saturation < MinThemeSaturation)
            return null;

        var themeColor = NormalizeThemeColor(chosen.Color);
        return new BackgroundColorPalette
        {
            SourceColor = chosen.Color,
            ThemeColor = themeColor,
            Coverage = chosen.Coverage,
            Flatness = chosen.Flatness,
            Saturation = chosen.Saturation,
            IsFillColor = chosen.IsFill
        };
    }

    private static void AddCandidate(HueBucket bucket, int totalSamples, List<ColorCandidate> candidates)
    {
        if (bucket.Count < 2 || totalSamples <= 0)
            return;

        var coverage = bucket.Count / (double)totalSamples;
        if (coverage < 0.004)
            return;

        var weightSum = bucket.WeightSum <= 0 ? 1 : bucket.WeightSum;
        var color = Windows.UI.Color.FromArgb(
            255,
            ClampByte(bucket.WeightedRed / weightSum),
            ClampByte(bucket.WeightedGreen / weightSum),
            ClampByte(bucket.WeightedBlue / weightSum));

        var quality = bucket.QualitySum / bucket.Count;
        var saturation = bucket.SaturationSum / bucket.Count;
        var flatness = bucket.FlatCount / (double)bucket.Count;
        var score = Math.Pow(coverage, CoverageScoreExponent) * quality;

        candidates.Add(new ColorCandidate
        {
            Color = color,
            Coverage = coverage,
            Flatness = flatness,
            Saturation = saturation,
            Score = score,
            IsFill = IsLikelyFill(coverage, flatness, saturation)
        });
    }

    private static ColorCandidate SelectCandidate(List<ColorCandidate> candidates)
    {
        candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
        var best = candidates[0];
        if (!best.IsFill)
            return best;

        var fillCoverage = 0.0;
        foreach (var candidate in candidates)
        {
            if (candidate.IsFill)
                fillCoverage += candidate.Coverage;
        }

        if (fillCoverage > MaxFillCoverage)
            return best;

        foreach (var candidate in candidates)
        {
            if (candidate.IsFill || candidate.Coverage < MinAlternativeCoverage)
                continue;
            if (candidate.Score < best.Score * MinAlternativeScoreRatio)
                continue;

            return candidate;
        }

        return best;
    }

    private static bool IsLikelyFill(double coverage, double flatness, double saturation)
    {
        if (coverage >= 0.65)
            return true;
        if (coverage >= 0.35 && saturation < 0.22)
            return true;
        if (coverage >= 0.45 && flatness >= 0.35)
            return true;
        if (coverage >= 0.30 && flatness >= 0.55)
            return true;
        return false;
    }

    private static Windows.UI.Color NormalizeThemeColor(Windows.UI.Color color)
    {
        RgbToHsl(color.R, color.G, color.B, out var hue, out var saturation, out var lightness);

        var adjustedSaturation = Math.Clamp(saturation * 1.10 + 0.10, ThemeMinSaturation, ThemeMaxSaturation);
        var adjustedLightness = Math.Clamp(lightness, ThemeMinLightness, ThemeMaxLightness);
        return HslToRgb(hue, adjustedSaturation, adjustedLightness);
    }

    private static double ComputeQuality(double saturation, double lightness)
    {
        var saturationFactor = SmoothStep(0.10, 0.50, saturation);
        if (saturationFactor <= 0)
            return 0;

        var lightnessDistance = Math.Abs(lightness * 2 - 1);
        var lightnessFactor = 1.0 - Math.Pow(lightnessDistance, 2.2);
        return saturationFactor * (0.35 + 0.65 * lightnessFactor);
    }

    private static bool IsFlatArea(byte[] pixels, int width, int height, int x, int y, int stride, byte red, byte green,
        byte blue)
    {
        var delta = 0;
        var comparisons = 0;

        var rightX = x + stride;
        if (rightX < width)
        {
            var index = (y * width + rightX) * 4;
            if (pixels[index + 3] >= 128)
            {
                delta += Math.Abs(pixels[index + 2] - red)
                         + Math.Abs(pixels[index + 1] - green)
                         + Math.Abs(pixels[index] - blue);
                comparisons++;
            }
        }

        var bottomY = y + stride;
        if (bottomY < height)
        {
            var index = (bottomY * width + x) * 4;
            if (pixels[index + 3] >= 128)
            {
                delta += Math.Abs(pixels[index + 2] - red)
                         + Math.Abs(pixels[index + 1] - green)
                         + Math.Abs(pixels[index] - blue);
                comparisons++;
            }
        }

        return comparisons > 0 && delta / comparisons <= FlatDeltaThreshold;
    }

    private static void RgbToHsl(byte red, byte green, byte blue, out double hue, out double saturation,
        out double lightness)
    {
        var r = red / 255.0;
        var g = green / 255.0;
        var b = blue / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        lightness = (max + min) / 2.0;

        if (delta <= double.Epsilon)
        {
            hue = 0;
            saturation = 0;
            return;
        }

        saturation = lightness <= 0.5
            ? delta / (max + min)
            : delta / (2.0 - max - min);

        if (max == r)
            hue = (g - b) / delta + (g < b ? 6 : 0);
        else if (max == g)
            hue = (b - r) / delta + 2;
        else
            hue = (r - g) / delta + 4;

        hue *= 60;
    }

    private static Windows.UI.Color HslToRgb(double hue, double saturation, double lightness)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);

        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var secondary = chroma * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var match = lightness - chroma / 2;

        double red;
        double green;
        double blue;

        if (hue < 60)
        {
            red = chroma;
            green = secondary;
            blue = 0;
        }
        else if (hue < 120)
        {
            red = secondary;
            green = chroma;
            blue = 0;
        }
        else if (hue < 180)
        {
            red = 0;
            green = chroma;
            blue = secondary;
        }
        else if (hue < 240)
        {
            red = 0;
            green = secondary;
            blue = chroma;
        }
        else if (hue < 300)
        {
            red = secondary;
            green = 0;
            blue = chroma;
        }
        else
        {
            red = chroma;
            green = 0;
            blue = secondary;
        }

        return Windows.UI.Color.FromArgb(
            255,
            (byte)Math.Round(Math.Clamp((red + match) * 255, 0, 255)),
            (byte)Math.Round(Math.Clamp((green + match) * 255, 0, 255)),
            (byte)Math.Round(Math.Clamp((blue + match) * 255, 0, 255)));
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        if (edge1 <= edge0)
            return value >= edge1 ? 1 : 0;

        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static byte ClampByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private readonly struct ColorSample
    {
        public ColorSample(byte red, byte green, byte blue, double saturation, double quality, bool isFlat)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Saturation = saturation;
            Quality = quality;
            IsFlat = isFlat;
        }

        public byte Red
        {
            get;
        }

        public byte Green
        {
            get;
        }

        public byte Blue
        {
            get;
        }

        public double Saturation
        {
            get;
        }

        public double Quality
        {
            get;
        }

        public bool IsFlat
        {
            get;
        }
    }

    private struct HueBucket
    {
        public int Count;
        public int FlatCount;
        public double QualitySum;
        public double WeightSum;
        public double WeightedRed;
        public double WeightedGreen;
        public double WeightedBlue;
        public double SaturationSum;

        public void Accumulate(ColorSample sample)
        {
            var weight = sample.Quality > 0 ? sample.Quality : 0.001;

            Count++;
            QualitySum += sample.Quality;
            WeightSum += weight;
            WeightedRed += sample.Red * weight;
            WeightedGreen += sample.Green * weight;
            WeightedBlue += sample.Blue * weight;
            SaturationSum += sample.Saturation;
            if (sample.IsFlat)
                FlatCount++;
        }
    }

    private struct ColorCandidate
    {
        public Windows.UI.Color Color;
        public double Coverage;
        public double Flatness;
        public double Saturation;
        public double Score;
        public bool IsFill;
    }
}

internal static class BackgroundColorPaletteCache
{
    private static readonly ConditionalWeakTable<ImageSource, BackgroundColorPalette> Palettes = new();

    public static void Set(ImageSource? source, BackgroundColorPalette? palette)
    {
        if (source == null || palette == null)
            return;

        Palettes.AddOrUpdate(source, palette);
    }

    public static BackgroundColorPalette? Get(ImageSource? source)
    {
        if (source == null)
            return null;

        return Palettes.TryGetValue(source, out var palette) ? palette : null;
    }
}