using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PatriotPower
{
    public partial class App : Application
    {
        private TaskbarIcon? _taskbarIcon;
        private MainWindow? _flyoutWindow;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Prevent the app from closing when the main window closes (Tray-First behavior)
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Hook into Windows shutdown sequence to guarantee enough time for PowerShell
            // to execute before the OS forcefully terminates the application process.
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;

            // Initialize the Tray Icon in code to prevent XAML parsing and lifecycle issues
            _taskbarIcon = new TaskbarIcon
            {
                IconSource = new BitmapImage(new Uri("pack://application:,,,/icon.ico")),
                ToolTipText = "PatriotPower - dGPU Controller"
            };

            // Bind to a single left click
            _taskbarIcon.TrayLeftMouseUp += TrayIcon_TrayLeftMouseUp;

            // Automatically reveal the flyout window upon application startup
            ShowFlyout();
        }

        private void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            // The OS is shutting down or the user is logging off. 
            // We must restore the GPU immediately.
            GpuManager.RestoreGpuSynchronously();
        }

        private void TrayIcon_TrayLeftMouseUp(object sender, RoutedEventArgs e)
        {
            ShowFlyout();
        }

        private void ShowFlyout()
        {
            // Create the window once. If it exists, toggle the animation.
            if (_flyoutWindow == null)
            {
                _flyoutWindow = new MainWindow();
            }

            _flyoutWindow.Toggle();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Clean up the system event hook to prevent memory leaks
            SystemEvents.SessionEnding -= SystemEvents_SessionEnding;

            // Update the tray icon to give the user visual feedback during the hardware restoration wait
            if (_taskbarIcon != null)
            {
                _taskbarIcon.ToolTipText = "PatriotPower - Shutting down...";
            }

            // Unobtrusive cleanup: Guarantee the dGPU is re-enabled before the app terminates
            GpuManager.RestoreGpuSynchronously();

            _taskbarIcon?.Dispose();
            base.OnExit(e);
        }
    }
}