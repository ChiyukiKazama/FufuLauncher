/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.ComponentModel;

namespace FufuLauncher.Models;

public sealed class McpToolToggleItem : ObservableObject
{
    private readonly Action<string, bool> _onChanged;
    private bool _isEnabled;

    public McpToolToggleItem(string id, string title, string description, bool isEnabled,
        Action<string, bool> onChanged)
    {
        Id = id;
        Title = title;
        Description = description;
        _isEnabled = isEnabled;
        _onChanged = onChanged;
    }

    public string Id
    {
        get;
    }

    public string Title
    {
        get;
    }

    public string Description
    {
        get;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _onChanged(Id, value);
            }
        }
    }
}