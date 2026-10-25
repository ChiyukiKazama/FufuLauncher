/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Services.Mcp;

internal static class McpToolCategories
{
    public const string Plugins = "plugins";
    public const string Game = "game";
    public const string Interface = "interface";
    public const string System = "system";
}

internal static class McpToolCatalog
{
    private const string EmptySchema = """{"type":"object","properties":{},"additionalProperties":false}""";

    public static IReadOnlyList<McpToolDefinition> All
    {
        get;
    } =
    [
        new("list_plugins", McpToolCategories.Plugins, "McpTool_ListPlugins", "McpTool_ListPluginsCaption",
            "List every plugin installed under the launcher Plugins directory, including official launcher plugins (FuFuPlugin / YuanShen-UnlockerLite) and third-party plugins. Returns folder id, display name, enabled state, config path and version.",
            EmptySchema),

        new("set_plugin_enabled", McpToolCategories.Plugins, "McpTool_SetPluginEnabled",
            "McpTool_SetPluginEnabledCaption",
            "Enable or disable an installed plugin by folder name, display name or dll file name. Works for both official and third-party plugins. The launcher policy restrictions (lightweight mode / constraint policy) and locked files are enforced.",
            """{"type":"object","properties":{"plugin":{"type":"string","description":"Plugin folder name, display name or dll file name."},"enabled":{"type":"boolean","description":"true to enable, false to disable."}},"required":["plugin","enabled"],"additionalProperties":false}"""),

        new("get_plugin_config", McpToolCategories.Plugins, "McpTool_GetPluginConfig",
            "McpTool_GetPluginConfigCaption",
            "Read the structured configuration of an installed plugin. Returns the [General] information and every configurable option (section, display name, type, current value, choices). This is the only supported way to inspect plugin configuration.",
            """{"type":"object","properties":{"plugin":{"type":"string","description":"Plugin folder name, display name or dll file name."}},"required":["plugin"],"additionalProperties":false}"""),

        new("set_plugin_config", McpToolCategories.Plugins, "McpTool_SetPluginConfig",
            "McpTool_SetPluginConfigCaption",
            "Change one existing option value of a plugin configuration. Only options that already exist in the config file can be modified; raw file editing and adding new sections are not supported. The launcher rewrites only the value line, keeping the rest of the file untouched.",
            """{"type":"object","properties":{"plugin":{"type":"string","description":"Plugin folder name, display name or dll file name."},"section":{"type":"string","description":"Existing section name as reported by get_plugin_config."},"value":{"type":["string","boolean","number"],"description":"New value. Booleans, integers, numbers or text, depending on the option type."}},"required":["plugin","section","value"],"additionalProperties":false}"""),

        new("search_plugin_store", McpToolCategories.Plugins, "McpTool_SearchPluginStore",
            "McpTool_SearchPluginStoreCaption",
            "Search the official plugin store. Returns plugin ids, names, versions, descriptions, download counts and whether the plugin is already installed.",
            """{"type":"object","properties":{"query":{"type":"string","description":"Optional search text."},"category":{"type":"string","description":"Optional store category id."},"page":{"type":"integer","minimum":1,"default":1},"pageSize":{"type":"integer","minimum":1,"maximum":50,"default":20}},"additionalProperties":false}"""),

        new("install_plugin", McpToolCategories.Plugins, "McpTool_InstallPlugin", "McpTool_InstallPluginCaption",
            "Start a background plugin installation and return a job id. Provide exactly one source: pluginId (official plugin store), path (a local .dll or archive on the launcher machine), or url (an http(s) archive download). When the store requires human verification, a verification window is opened for the user; use get_install_status to check progress and cancel_install to stop the installation.",
            """{"type":"object","properties":{"pluginId":{"type":"string","description":"Plugin store id."},"accessKey":{"type":"string","description":"Access key for private store plugins."},"path":{"type":"string","description":"Local .dll, .zip, .7z or .rar path on the launcher machine."},"url":{"type":"string","description":"http(s) URL of an archive to download and install."},"overwrite":{"type":"boolean","default":false,"description":"Replace an existing plugin folder when installing from path or url."}},"additionalProperties":false}"""),

        new("get_install_status", McpToolCategories.Plugins, "McpTool_GetInstallStatus",
            "McpTool_GetInstallStatusCaption",
            "Return the progress of a plugin installation started by install_plugin, including whether the user still has to complete verification. Without jobId, the most recent installation jobs are returned.",
            """{"type":"object","properties":{"jobId":{"type":"string","description":"Installation job id. Omit to list recent jobs."}},"additionalProperties":false}"""),

        new("cancel_install", McpToolCategories.Plugins, "McpTool_CancelInstall",
            "McpTool_CancelInstallCaption",
            "Cancel a running or waiting plugin installation started by install_plugin. A verification window that is still open is closed.",
            """{"type":"object","properties":{"jobId":{"type":"string","description":"Installation job id."}},"required":["jobId"],"additionalProperties":false}"""),

        new("get_game_status", McpToolCategories.Game, "McpTool_GetGameStatus", "McpTool_GetGameStatusCaption",
            "Return the launcher game state: configured game path, whether the game is running, current server scheme and installed game version.",
            EmptySchema),

        new("start_game", McpToolCategories.Game, "McpTool_StartGame", "McpTool_StartGameCaption",
            "Start the game through the launcher with the current launch configuration. Fails when the game is already running or no game path is configured.",
            EmptySchema),

        new("stop_game", McpToolCategories.Game, "McpTool_StopGame", "McpTool_StopGameCaption",
            "Terminate the running game process and any integrated helper process started by the launcher.",
            EmptySchema),

        new("list_servers", McpToolCategories.Game, "McpTool_ListServers", "McpTool_ListServersCaption",
            "List the supported game server schemes (channel / sub-channel), the currently active scheme and whether each scheme matches the installed client.",
            EmptySchema),

        new("switch_server", McpToolCategories.Game, "McpTool_SwitchServer", "McpTool_SwitchServerCaption",
            "Switch the game server scheme by writing the channel, sub-channel and cps fields into the game config.ini. Only schemes compatible with the installed client (Chinese or oversea) are accepted, and the game must be closed.",
            """{"type":"object","properties":{"serverId":{"type":"string","description":"Server id from list_servers, for example 1-1-cn."}},"required":["serverId"],"additionalProperties":false}"""),

        new("open_page", McpToolCategories.Interface, "McpTool_OpenPage", "McpTool_OpenPageCaption",
            "Navigate the launcher main window to a page.",
            """{"type":"object","properties":{"page":{"type":"string","enum":["home","control_panel","game_settings","account","other","plugins","plugin_store","plugin_settings","help","settings"],"description":"Page id."}},"required":["page"],"additionalProperties":false}"""),

        new("open_tool_window", McpToolCategories.Interface, "McpTool_OpenToolWindow",
            "McpTool_OpenToolWindowCaption",
            "Open one of the toolbox tool windows, for example gacha analysis, data center, calculator or achievements.",
            """{"type":"object","properties":{"tool":{"type":"string","enum":["gacha_analysis","data_center","calculator","cycle_challenge","achievements","inventory","player_info","daily_note","bbs","video_resources","community","miyoushe"],"description":"Tool id."}},"required":["tool"],"additionalProperties":false}"""),

        new("set_window_state", McpToolCategories.Interface, "McpTool_SetWindowState",
            "McpTool_SetWindowStateCaption",
            "Maximize, minimize or restore the launcher main window.",
            """{"type":"object","properties":{"state":{"type":"string","enum":["maximize","minimize","restore"],"description":"Target window state."}},"required":["state"],"additionalProperties":false}"""),

        new("read_crash_log", McpToolCategories.System, "McpTool_ReadCrashLog", "McpTool_ReadCrashLogCaption",
            "Read the tail of the launcher crash log (CrashLog.txt) without modifying it.",
            """{"type":"object","properties":{"maxLines":{"type":"integer","minimum":1,"maximum":500,"default":120,"description":"Number of trailing lines to return."}},"additionalProperties":false}"""),

        new("check_update", McpToolCategories.System, "McpTool_CheckUpdate", "McpTool_CheckUpdateCaption",
            "Check whether a newer launcher version is available. This only queries the update server: the update program is never launched and no local state is changed.",
            EmptySchema)
    ];

    public static McpToolDefinition? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(tool => string.Equals(tool.Id, id, StringComparison.OrdinalIgnoreCase));
}