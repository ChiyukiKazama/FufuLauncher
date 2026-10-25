/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System;
using System.ComponentModel;
using System.Diagnostics;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views
{
    public sealed partial class OtherPage : Page
    {
        public OtherViewModel ViewModel
        {
            get;
        }

        public OtherPage()
        {
            ViewModel = App.GetService<OtherViewModel>();
            InitializeComponent();

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            Unloaded += OtherPage_Unloaded;
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.IsRecordingTriggerKey) ||
                e.PropertyName == nameof(ViewModel.IsRecordingClickKey) ||
                e.PropertyName == nameof(ViewModel.IsRecordingStopKey))
            {
                UpdateKeyRegistration();
            }
        }

        private void UpdateKeyRegistration()
        {
            try
            {
                if (App.MainWindow?.Content is UIElement content)
                {
                    content.KeyDown -= GlobalKeyDown;

                    if (ViewModel.IsRecordingTriggerKey || ViewModel.IsRecordingClickKey ||
                        ViewModel.IsRecordingStopKey)
                    {
                        content.KeyDown += GlobalKeyDown;
                        DebugLog.Info("OtherPage",
                            "[OtherPage] Key recording enabled, global key events registered on Window.Content");
                    }
                    else
                    {
                        DebugLog.Info("OtherPage",
                            "[OtherPage] Key recording disabled, global key events unregistered");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("OtherPage", $"[OtherPage] Failed to register/unregister key events: {ex.Message}");
            }
        }

        private void OtherPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

            try
            {
                if (App.MainWindow?.Content is UIElement content)
                {
                    content.KeyDown -= GlobalKeyDown;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("OtherPage", $"[OtherPage] Failed to clean up key events on page unload: {ex.Message}");
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox) textBox.Text = textBox.Text.Trim('"');
        }

        private void ProgramPath_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                _ = ViewModel.ApplyProgramPathCommand.ExecuteAsync(null);
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            EntranceStoryboard.Begin();
        }

        private void GlobalKeyDown(object sender, KeyRoutedEventArgs args)
        {
            try
            {
                if (ViewModel.IsRecordingTriggerKey || ViewModel.IsRecordingClickKey || ViewModel.IsRecordingStopKey)
                {
                    var key = args.Key;
                    DebugLog.Info("OtherPage", $"[OtherPage] Captured key: {key}");

                    if (key == VirtualKey.None) return;

                    if (ViewModel.IsRecordingTriggerKey)
                    {
                        ViewModel.UpdateKey("Trigger", key);
                        DebugLog.Info("OtherPage", $"[OtherPage] Trigger key set: {key}");
                    }
                    else if (ViewModel.IsRecordingClickKey)
                    {
                        ViewModel.UpdateKey("Click", key);
                        DebugLog.Info("OtherPage", $"[OtherPage] Rapid-click key set: {key}");
                    }
                    else if (ViewModel.IsRecordingStopKey)
                    {
                        ViewModel.UpdateKey("Stop", key);
                        DebugLog.Info("OtherPage", $"[OtherPage] Stop hotkey set: {key}");
                    }

                    args.Handled = true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("OtherPage", $"[OtherPage] Key handling error: {ex.Message}");
            }
        }
    }
}