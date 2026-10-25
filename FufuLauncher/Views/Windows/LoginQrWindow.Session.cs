/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Views;

internal class LoginSession
{
    public CancellationTokenSource Cts
    {
        get;
    } = new();

    public string Ticket
    {
        get;
        set;
    }

    public string GameAppId
    {
        get;
        set;
    }

    public string GameDevice
    {
        get;
        set;
    }

    public LoginType Type
    {
        get;
        set;
    }

    public DateTimeOffset QrCreatedAt
    {
        get;
        set;
    }

    public bool FlowStarted
    {
        get;
        set;
    }

    public void Cancel() => Cts.Cancel();
}

internal enum LoginType
{
    AppQr,
    GameQr,
    WebPassport
}

internal enum QrPollOutcome
{
    Confirmed,
    Refresh,
    Stopped
}