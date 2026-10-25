/*
Copyright (c) FufuLauncher Dev Team. All Rights Reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Globalization;
using System.Text;
using FufuLauncher.Helpers;
using FufuLauncher.Models.CycleChallenge;
using FufuLauncher.Models.CycleChallenge.Mapping;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FufuLauncher.Views.Controls.CycleChallenge;

public sealed partial class StygianPanel : UserControl
{
    private HardChallengeDisplay _display = HardChallengeDisplay.CreateState(StygianDisplayState.NotParticipated);
    private bool _showCoop;

    public StygianPanel()
    {
        InitializeComponent();
    }


    public HardChallengeDisplay Display => _display;


    public string? Region
    {
        get;
        set;
    }


    public StygianMode Mode => _showCoop ? _display.Coop : _display.Solo;


    public bool IsSoloSelected => _display.ShowContent && !_showCoop;


    public bool IsCoopSelected => _display.ShowContent && _showCoop;


    public bool ShowNoCoopHint => _display.ShowContent && !_display.HasCoopData;


    public bool ShowModeEmpty => _display.ShowContent && !Mode.HasData;


    public void Show(string payloadJson)
    {
        HardChallengeDisplay display;
        try
        {
            display = HardChallengeMapper.Parse(payloadJson, Region);
        }
        catch (Exception ex)
        {
            DebugLog.Info("StygianPanel", $"[StygianPanel] Failed to parse payload: {ex}");
            display = HardChallengeDisplay.CreateError(ex.Message);
        }

        SetDisplay(display);
    }


    private void SetDisplay(HardChallengeDisplay display)
    {
        _display = display;


        _showCoop = !display.Solo.HasData && display.Coop.HasData;

        SoloChip.IsEnabled = display.Solo.HasData;
        CoopChip.IsEnabled = display.Coop.HasData;
        ToolTipService.SetToolTip(
            CoopChip,
            display.HasCoopData ? null : "Stygian_NoCoopData".GetLocalized());

        RefreshBindings();
    }


    private void RefreshBindings()
    {
        try
        {
            Bindings.Update();
        }
        catch (Exception ex)
        {
            DebugLog.Info("StygianPanel", $"[StygianPanel] Refresh binding failed: {ex.Message}");
        }


        SoloChip.IsChecked = IsSoloSelected;
        CoopChip.IsChecked = IsCoopSelected;
    }

    private void OnSoloChipClick(object sender, RoutedEventArgs e)
    {
        if (!_display.Solo.HasData)
        {
            RefreshBindings();
            return;
        }

        _showCoop = false;
        RefreshBindings();
    }

    private void OnCoopChipClick(object sender, RoutedEventArgs e)
    {
        if (!_display.HasCoopData)
        {
            RefreshBindings();
            return;
        }

        _showCoop = true;
        RefreshBindings();
    }


    private void OnMonsterDescriptionLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBlock block || block.Tag is not StygianMonster monster)
        {
            return;
        }

        try
        {
            CycleChallengeRichTextParser.Apply(block, monster.Descriptions);
        }
        catch (Exception ex)
        {
            DebugLog.Info("StygianPanel",
                $"[StygianPanel] Failed to parse monster description rich text: {ex.Message}");
            block.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run { Text = string.Join(Environment.NewLine, monster.Descriptions) });
            block.Blocks.Add(paragraph);
        }
    }
}

internal static class CycleChallengeRichTextParser
{
    private const string ColorOpenPrefix = "<color=";
    private const string ColorCloseTag = "</color>";
    private const string LinkOpenPrefix = "{LINK#";
    private const string LinkCloseTag = "{/LINK}";


    private static readonly Color LinkForeground = Color.FromArgb(0xFF, 0xF3, 0x90, 0x01);


    public static void Apply(RichTextBlock target, IReadOnlyList<string>? descriptions)
    {
        target.Blocks.Clear();
        if (descriptions is null || descriptions.Count == 0)
        {
            return;
        }

        foreach (var description in descriptions)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            var paragraph = BuildParagraph(description);
            if (paragraph.Inlines.Count > 0)
            {
                target.Blocks.Add(paragraph);
            }
        }
    }


    internal static Paragraph BuildParagraph(string text)
    {
        var paragraph = new Paragraph();
        Underline? currentLink = null;

        foreach (var segment in Tokenize(text))
        {
            switch (segment.Kind)
            {
                case SegmentKind.LinkOpen:

                    if (currentLink is not null)
                    {
                        paragraph.Inlines.Add(currentLink);
                    }


                    currentLink = new Underline
                    {
                        Foreground = new SolidColorBrush(LinkForeground),
                    };
                    continue;

                case SegmentKind.LinkClose:
                    if (currentLink is not null)
                    {
                        paragraph.Inlines.Add(currentLink);
                        currentLink = null;
                    }

                    continue;

                case SegmentKind.LineBreak:
                    AddInline(paragraph, currentLink, new LineBreak());
                    continue;
            }

            if (segment.Text.Length == 0)
            {
                continue;
            }

            var run = new Run { Text = segment.Text };
            if (segment.Foreground is { } color)
            {
                run.Foreground = new SolidColorBrush(color);
            }

            AddInline(paragraph, currentLink, run);
        }


        if (currentLink is not null)
        {
            paragraph.Inlines.Add(currentLink);
        }

        return paragraph;
    }

    private static void AddInline(Paragraph paragraph, Span? link, Inline inline)
    {
        if (link is not null)
        {
            link.Inlines.Add(inline);
        }
        else
        {
            paragraph.Inlines.Add(inline);
        }
    }


    private enum SegmentKind
    {
        Text,
        LineBreak,
        LinkOpen,
        LinkClose,
    }


    private readonly record struct RichSegment(string Text, Color? Foreground, SegmentKind Kind);

    private static List<RichSegment> Tokenize(string text)
    {
        var segments = new List<RichSegment>();
        TokenizeCore(text, null, segments);
        return segments;
    }

    private static void TokenizeCore(string text, Color? inheritedColor, List<RichSegment> segments)
    {
        var plain = new StringBuilder();

        void FlushPlain()
        {
            if (plain.Length > 0)
            {
                segments.Add(new RichSegment(plain.ToString(), inheritedColor, SegmentKind.Text));
                plain.Clear();
            }
        }

        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];


            if (current == '\n')
            {
                FlushPlain();
                segments.Add(new RichSegment(string.Empty, inheritedColor, SegmentKind.LineBreak));
                index++;
                continue;
            }


            if (current == '\\' && index + 1 < text.Length && text[index + 1] == 'n')
            {
                FlushPlain();
                segments.Add(new RichSegment(string.Empty, inheritedColor, SegmentKind.LineBreak));
                index += 2;
                continue;
            }


            if (current == '{' && TryReadLinkTag(text, index, out var linkKind, out var linkEnd))
            {
                FlushPlain();
                segments.Add(new RichSegment(string.Empty, inheritedColor, linkKind));
                index = linkEnd;
                continue;
            }


            if (current == '<' && TryReadColorTag(text, index, out var contentStart, out var color))
            {
                var closeStart = text.IndexOf(ColorCloseTag, contentStart, StringComparison.OrdinalIgnoreCase);
                if (closeStart >= 0)
                {
                    FlushPlain();
                    var inner = text[contentStart..closeStart];

                    TokenizeCore(inner, color, segments);
                    index = closeStart + ColorCloseTag.Length;
                    continue;
                }
            }


            plain.Append(current);
            index++;
        }

        FlushPlain();
    }


    private static bool TryReadLinkTag(string text, int startIndex, out SegmentKind kind, out int nextIndex)
    {
        kind = SegmentKind.Text;
        nextIndex = startIndex;

        var span = text.AsSpan(startIndex);
        if (span.StartsWith(LinkCloseTag, StringComparison.OrdinalIgnoreCase))
        {
            kind = SegmentKind.LinkClose;
            nextIndex = startIndex + LinkCloseTag.Length;
            return true;
        }

        if (!span.StartsWith(LinkOpenPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tagEnd = text.IndexOf('}', startIndex);
        if (tagEnd < 0)
        {
            return false;
        }

        kind = SegmentKind.LinkOpen;
        nextIndex = tagEnd + 1;
        return true;
    }


    private static bool TryReadColorTag(string text, int startIndex, out int contentStart, out Color color)
    {
        contentStart = 0;
        color = default;

        if (!text.AsSpan(startIndex).StartsWith(ColorOpenPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hexStart = startIndex + ColorOpenPrefix.Length;
        var tagEnd = text.IndexOf('>', hexStart);
        if (tagEnd < 0)
        {
            return false;
        }

        var hex = text[hexStart..tagEnd];
        if (!TryParseColor(hex, out color))
        {
            return false;
        }

        contentStart = tagEnd + 1;
        return true;
    }


    private static bool TryParseColor(string? raw, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var hex = raw.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        return hex.Length switch
        {
            8 => TryParseHex(hex, out color, withAlpha: true),
            6 => TryParseHex(hex, out color, withAlpha: false),

            3 => TryParseShortHex(hex, out color),
            4 => TryParseShortHex(hex, out color),
            _ => false,
        };
    }

    private static bool TryParseHex(string hex, out Color color, bool withAlpha)
    {
        color = default;

        if (!TryParseByte(hex.AsSpan(0, 2), out var r)
            || !TryParseByte(hex.AsSpan(2, 2), out var g)
            || !TryParseByte(hex.AsSpan(4, 2), out var b))
        {
            return false;
        }

        byte a = 0xFF;
        if (withAlpha && !TryParseByte(hex.AsSpan(6, 2), out a))
        {
            return false;
        }

        color = Color.FromArgb(a, r, g, b);
        return true;
    }

    private static bool TryParseShortHex(string hex, out Color color)
    {
        color = default;

        if (!TryParseNibble(hex[0], out var r)
            || !TryParseNibble(hex[1], out var g)
            || !TryParseNibble(hex[2], out var b))
        {
            return false;
        }

        byte a = 0xFF;
        if (hex.Length == 4 && !TryParseNibble(hex[3], out a))
        {
            return false;
        }

        color = Color.FromArgb(a, r, g, b);
        return true;
    }

    private static bool TryParseByte(ReadOnlySpan<char> span, out byte value) =>
        byte.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    private static bool TryParseNibble(char c, out byte value)
    {
        value = 0;
        if (!byte.TryParse(c.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var nibble))
        {
            return false;
        }

        value = (byte)((nibble << 4) | nibble);
        return true;
    }
}