using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace FufuLauncher.ViewModels;

public sealed class PluginSettingsGroup : ObservableObject
{
    private readonly ObservableCollection<PluginSettingItem>? _precedingItems;

    public PluginSettingsGroup(ObservableCollection<PluginSettingItem> items,
        ObservableCollection<PluginSettingItem>? precedingItems = null)
    {
        Items = items;
        _precedingItems = precedingItems;
        if (_precedingItems != null)
        {
            _precedingItems.CollectionChanged += OnPrecedingItemsChanged;
        }
    }

    public ObservableCollection<PluginSettingItem> Items { get; }

    public Visibility SeparatorVisibility => _precedingItems?.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    private void OnPrecedingItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SeparatorVisibility));
    }
}