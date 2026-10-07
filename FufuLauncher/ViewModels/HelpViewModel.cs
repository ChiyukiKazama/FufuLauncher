/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class HelpViewModel : ObservableObject
{
    private static string NormalizeMarkdownForWinUi(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private readonly HttpClient _httpClient = new();
    private const string ConfigUrl = "https://fu1.fun/api/docs-config";
    private const string ContentBaseUrl = "https://fu1.fun/api/docs/zh-CN";

    public ObservableCollection<DocCategory> AllCategories
    {
        get;
    } = new();

    public ObservableCollection<DocSearchHit> SearchHits
    {
        get;
    } = new();

    public readonly Dictionary<DocItem, string> PreloadedContents = new();

    private static readonly Regex s_whitespaceCollapse = new(@"\s+", RegexOptions.Compiled);

    private string _originalContent = string.Empty;
    private string _translatedContent = string.Empty;
    private CancellationTokenSource? _translationCts;

    [ObservableProperty] private bool _isTranslating;

    [ObservableProperty] private bool _isTranslated;

    [ObservableProperty] private string _translationProgress = string.Empty;

    [ObservableProperty] private bool _showTranslateButton;

    [ObservableProperty] private string _translateButtonText = string.Empty;

    private static string CollapseForPreview(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var one = s_whitespaceCollapse.Replace(text.Trim(), " ");
        if (one.Length <= maxLen)
            return one;
        return one[..maxLen].TrimEnd() + "…";
    }

    private static string SnippetAroundMatch(string content, string lowerFilter)
    {
        var lower = content.ToLowerInvariant();
        var idx = lower.IndexOf(lowerFilter, StringComparison.Ordinal);
        if (idx < 0)
            return CollapseForPreview(content, 200);

        const int radius = 96;
        var start = Math.Max(0, idx - radius);
        var end = Math.Min(content.Length, idx + lowerFilter.Length + radius);
        var slice = content[start..end];
        var collapsed = s_whitespaceCollapse.Replace(slice.Replace("\r", "").Replace("\n", " "), " ").Trim();
        if (start > 0)
            collapsed = "…" + collapsed;
        if (end < content.Length)
            collapsed += "…";
        return collapsed;
    }

    public void UpdateSearchHits(string? filter)
    {
        SearchHits.Clear();
        var f = filter?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(f))
            return;

        var lowerFilter = f.ToLowerInvariant();

        foreach (var cat in AllCategories)
        {
            foreach (var item in cat.Items)
            {
                var titleMatch = item.Title.ToLowerInvariant().Contains(lowerFilter);
                var fileMatch = item.File.ToLowerInvariant().Contains(lowerFilter);
                PreloadedContents.TryGetValue(item, out var body);
                var contentMatch = !string.IsNullOrEmpty(body) &&
                                   body.ToLowerInvariant().Contains(lowerFilter);

                if (!titleMatch && !fileMatch && !contentMatch)
                    continue;

                string preview;
                if (contentMatch && body != null)
                    preview = SnippetAroundMatch(body, lowerFilter);
                else if (!string.IsNullOrEmpty(body))
                    preview = CollapseForPreview(body, 220);
                else
                    preview = "正文仍在后台预加载，匹配来自标题或路径";

                SearchHits.Add(new DocSearchHit
                {
                    Item = item,
                    CategoryName = cat.CategoryName,
                    Preview = preview
                });
            }
        }
    }

    [ObservableProperty] private string _markdownContent = "从左侧目录选择一个项目以查看详细内容";

    [ObservableProperty] private string _currentTitle = "请选择文档";

    [ObservableProperty] private string _currentCategory = string.Empty;

    [ObservableProperty] private string _currentAuthor = string.Empty;

    [ObservableProperty] private string _currentDate = string.Empty;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string _markdownUriPrefix = $"{ContentBaseUrl}/";

    private static string GetMarkdownDirectoryPrefix(string relativeFilePath)
    {
        var normalized = relativeFilePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalized))
            return $"{ContentBaseUrl}/";

        var lastSlash = normalized.LastIndexOf('/');
        var dirPart = lastSlash >= 0 ? normalized[..lastSlash] : "";
        if (string.IsNullOrEmpty(dirPart))
            return $"{ContentBaseUrl}/";

        var encoded = string.Join("/", dirPart.Split('/').Select(Uri.EscapeDataString));
        return $"{ContentBaseUrl}/{encoded}/";
    }

    private static bool NeedsTranslation()
    {
        var culture = ResourceExtensions.CurrentCulture;
        if (string.IsNullOrEmpty(culture))
            return false;
        return culture != "zh-CN" && culture != "zh-TW";
    }

    private void UpdateTranslateButtonState()
    {
        ShowTranslateButton = NeedsTranslation();
        TranslateButtonText = IsTranslated
            ? "HelpPage_ShowOriginalBtn".GetLocalized()
            : "HelpPage_TranslateBtn".GetLocalized();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        UpdateTranslateButtonState();
        try
        {
            var json = await _httpClient.GetStringAsync(ConfigUrl);
            var categories = JsonSerializer.Deserialize<List<DocCategory>>(json);

            AllCategories.Clear();
            if (categories != null)
            {
                foreach (var category in categories)
                {
                    foreach (var item in category.Items)
                    {
                        item.Category = category.CategoryName;
                    }

                    AllCategories.Add(category);
                }
            }

            _ = Task.Run(PreloadAllDocumentsAsync);
        }
        catch (Exception ex)
        {
            CurrentTitle = "初始化失败";
            MarkdownContent = $"无法加载目录配置: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PreloadAllDocumentsAsync()
    {
        foreach (var category in AllCategories)
        {
            foreach (var item in category.Items)
            {
                try
                {
                    string filePart = string.Join("/", item.File.Split('/').Select(Uri.EscapeDataString));
                    string requestUrl = $"{ContentBaseUrl}/{filePart}";
                    var response = await _httpClient.GetAsync(requestUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        var raw = await response.Content.ReadAsStringAsync();
                        PreloadedContents[item] = NormalizeMarkdownForWinUi(raw);
                    }
                }
                catch
                {
                }
            }
        }
    }

    public async Task LoadDocumentAsync(DocItem item)
    {
        CancelTranslation();
        IsTranslated = false;
        _translatedContent = string.Empty;
        TranslationProgress = string.Empty;

        IsLoading = true;
        MarkdownUriPrefix = GetMarkdownDirectoryPrefix(item.File);
        CurrentTitle = item.Title;
        CurrentCategory = item.Category;
        CurrentAuthor = item.Author;
        CurrentDate = string.Empty;
        MarkdownContent = "加载中...";

        try
        {
            string filePart = string.Join("/", item.File.Split('/').Select(Uri.EscapeDataString));
            string requestUrl = $"{ContentBaseUrl}/{filePart}";
            var response = await _httpClient.GetAsync(requestUrl);

            if (response.IsSuccessStatusCode)
            {
                var raw = await response.Content.ReadAsStringAsync();
                var content = NormalizeMarkdownForWinUi(raw);
                MarkdownContent = content;
                _originalContent = content;
                PreloadedContents[item] = content;

                if (response.Content.Headers.LastModified.HasValue)
                {
                    CurrentDate = response.Content.Headers.LastModified.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                }
                else
                {
                    CurrentDate = string.Empty;
                }

                UpdateTranslateButtonState();

                if (NeedsTranslation())
                {
                    _ = TranslateDocumentAsync();
                }
            }
            else
            {
                MarkdownContent = $"无法获取文档内容 (HTTP {response.StatusCode})";
                CurrentDate = string.Empty;
                _originalContent = string.Empty;
            }
        }
        catch (Exception ex)
        {
            MarkdownContent = $"文档加载发生异常: {ex.Message}";
            CurrentDate = string.Empty;
            _originalContent = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task TranslateDocumentAsync()
    {
        if (string.IsNullOrEmpty(_originalContent))
            return;

        var culture = ResourceExtensions.CurrentCulture;
        if (string.IsNullOrEmpty(culture))
            return;

        CancelTranslation();
        _translationCts = new CancellationTokenSource();
        var ct = _translationCts.Token;

        IsTranslating = true;
        TranslationProgress = string.Format("HelpPage_Translating".GetLocalized(), 0, "...");

        try
        {
            var result = await TranslationService.Instance.TranslateMarkdownAsync(
                _originalContent,
                culture,
                onProgress: (completed, total, currentText) =>
                {
                    App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                    {
                        TranslationProgress = string.Format("HelpPage_Translating".GetLocalized(), completed, total);
                        MarkdownContent = currentText;
                    });
                },
                ct: ct);

            if (!ct.IsCancellationRequested)
            {
                _translatedContent = result;
                MarkdownContent = result;
                IsTranslated = true;
                TranslationProgress = "HelpPage_TranslateComplete".GetLocalized();
                UpdateTranslateButtonState();
            }
        }
        catch (OperationCanceledException)
        {
            TranslationProgress = string.Empty;
        }
        catch (Exception)
        {
            TranslationProgress = "HelpPage_TranslateFailed".GetLocalized();
        }
        finally
        {
            IsTranslating = false;
        }
    }

    [RelayCommand]
    private async Task ToggleTranslationAsync()
    {
        if (IsTranslating)
        {
            CancelTranslation();
            MarkdownContent = _originalContent;
            IsTranslated = false;
            IsTranslating = false;
            TranslationProgress = string.Empty;
            UpdateTranslateButtonState();
            return;
        }

        if (IsTranslated)
        {
            MarkdownContent = _originalContent;
            IsTranslated = false;
            UpdateTranslateButtonState();
        }
        else
        {
            if (!string.IsNullOrEmpty(_translatedContent))
            {
                MarkdownContent = _translatedContent;
                IsTranslated = true;
                UpdateTranslateButtonState();
            }
            else
            {
                await TranslateDocumentAsync();
            }
        }
    }

    private void CancelTranslation()
    {
        if (_translationCts != null)
        {
            _translationCts.Cancel();
            _translationCts.Dispose();
            _translationCts = null;
        }
    }
}