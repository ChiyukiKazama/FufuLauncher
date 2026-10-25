/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml;

namespace FufuLauncher.Models;

public sealed class McpAuditEntryView
{
    public string Title
    {
        get;
        init;
    } = string.Empty;

    public string Meta
    {
        get;
        init;
    } = string.Empty;

    public string ResultText
    {
        get;
        init;
    } = string.Empty;

    public string DetailMeta
    {
        get;
        init;
    } = string.Empty;

    public string DetailMessage
    {
        get;
        init;
    } = string.Empty;

    public Visibility DetailMessageVisibility
    {
        get;
        init;
    }

    public Visibility SuccessVisibility
    {
        get;
        init;
    }

    public Visibility FailureVisibility
    {
        get;
        init;
    }
}