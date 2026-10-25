/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Dispatching;

namespace FufuLauncher.Services.Mcp;

internal static class McpUi
{
    private const int DefaultTimeoutMs = 20000;

    public static DispatcherQueue GetDispatcherQueue()
    {
        var window = App.MainWindow;
        if (window == null)
        {
            throw new McpToolException("Launcher main window is not ready yet.");
        }

        return window.DispatcherQueue ??
               throw new McpToolException("Unable to obtain the launcher UI thread.");
    }

    public static Task<T> InvokeAsync<T>(Func<Task<T>> action, int timeoutMs = DefaultTimeoutMs)
    {
        var dispatcher = GetDispatcherQueue();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    completion.TrySetResult(await action());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            throw new McpToolException("Unable to dispatch the operation to the launcher UI thread.");
        }

        return completion.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
    }

    public static Task InvokeAsync(Func<Task> action, int timeoutMs = DefaultTimeoutMs) =>
        InvokeAsync<bool>(async () =>
        {
            await action();
            return true;
        }, timeoutMs);
}