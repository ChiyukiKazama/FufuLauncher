/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace Updater;

public sealed class Strings : INotifyPropertyChanged
{
    private const string ChineseLanguage = "zh-CN";
    private const string EnglishLanguage = "en-US";

    private Dictionary<string, string> _resources;
    private string _currentLanguage;

    public static Strings Instance { get; } = new();

    public static event EventHandler? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string CurrentLanguage => Instance._currentLanguage;

    public string this[string key] => GetValue(key);

    private Strings()
    {
        _currentLanguage = DetectLanguage();
        _resources = LoadResources(_currentLanguage);
    }

    public static string Get(string key) => Instance.GetValue(key);

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Instance.GetValue(key), args);

    public static void ToggleLanguage() =>
        Instance.SetLanguage(Instance._currentLanguage == ChineseLanguage ? EnglishLanguage : ChineseLanguage);

    private string GetValue(string key) =>
        _resources.TryGetValue(key, out string? value) ? value : key;

    private void SetLanguage(string language)
    {
        if (string.Equals(_currentLanguage, language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentLanguage = language;
        _resources = LoadResources(language);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string DetectLanguage()
    {
        string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase) ? ChineseLanguage : EnglishLanguage;
    }

    private static Dictionary<string, string> LoadResources(string language)
    {
        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        MergeResw(resources, "Updater.Strings.en-US.resw");

        if (language != EnglishLanguage)
        {
            MergeResw(resources, $"Updater.Strings.{language}.resw");
        }

        return resources;
    }

    private static void MergeResw(Dictionary<string, string> target, string resourceName)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return;
        }

        XDocument document = XDocument.Load(stream);
        foreach (XElement data in document.Root?.Elements("data") ?? Enumerable.Empty<XElement>())
        {
            string? key = data.Attribute("name")?.Value;
            string? value = data.Element("value")?.Value;
            if (!string.IsNullOrEmpty(key) && value != null)
            {
                target[key] = value;
            }
        }
    }
}
