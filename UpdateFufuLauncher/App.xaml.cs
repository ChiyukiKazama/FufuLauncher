/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace Updater
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (Environment.OSVersion.Version.Build < 18363)
            {
                MessageBox.Show(Strings.Get("AppUnsupportedWindowsMessage"), Strings.Get("AppUnsupportedWindowsTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }
            
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                var originalArgs = Environment.GetCommandLineArgs();
                string args = string.Join(" ", originalArgs.Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

                ProcessStartInfo psi = new()
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = args
                };
                try
                {
                    Process.Start(psi);
                }
                catch
                {
                    
                }

                Environment.Exit(0);
            }
            base.OnStartup(e);
        }
    }
}
