using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace PatriotPower
{
    public partial class MainWindow : Window
    {
        private bool _isSafetyCheckComplete = false;
        private bool _isAnimating = false;
        private bool _pendingDeactivation = false;

        // Power Metrics
        private DispatcherTimer _powerPollTimer;
        private double _peakWattage = 0.0;

        // Registry Key for saving settings natively
        private const string RegKeyPath = @"Software\PatriotPower";

        public MainWindow()
        {
            InitializeComponent();

            // Initialize the metrics polling timer (0.5 seconds)
            _powerPollTimer = new DispatcherTimer();
            _powerPollTimer.Interval = TimeSpan.FromSeconds(0.5);
            _powerPollTimer.Tick += PowerPollTimer_Tick;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Load the Pin State from the Windows Registry
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegKeyPath))
                {
                    var pinState = key.GetValue("IsPinned", 0);
                    PinButton.IsChecked = Convert.ToInt32(pinState) == 1;
                }
            }
            catch { /* Silently handle registry access restrictions */ }

            _powerPollTimer.Start();

            // Perform Safety Check only on the first load
            if (!_isSafetyCheckComplete)
            {
                StatusText.Text = "Analyzing GPU Topology...";
                EnduranceToggle.IsEnabled = false;

                try
                {
                    bool isSafe = await GpuManager.IsSystemSafeForEnduranceMode();

                    if (isSafe)
                    {
                        // 1. Read current system state
                        bool isEnduranceActive = await GpuManager.IsEnduranceModeActiveAsync();

                        // 2. Set the UI to match the hardware BEFORE attaching the events.
                        // This prevents the WPF toggle event from misfiring and modifying the system on startup.
                        EnduranceToggle.IsChecked = isEnduranceActive;

                        // 3. Attach event handlers now that the baseline state is firmly established
                        EnduranceToggle.Checked += EnduranceToggle_Checked;
                        EnduranceToggle.Unchecked += EnduranceToggle_Unchecked;

                        if (isEnduranceActive)
                        {
                            StatusText.Text = string.Empty; // Clear existing text to use rich Inlines
                            StatusText.Inlines.Add(new Run("Conserving energy, dGPU disabled ") { Foreground = new SolidColorBrush(Color.FromRgb(0, 100, 0)) });
                            StatusText.Inlines.Add(new Run("♥") { Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 180)) }); // Hot Pink
                        }
                        else
                        {
                            StatusText.Text = "Not power saving";
                            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85)); // Dark Gray
                        }

                        EnduranceToggle.IsEnabled = true;
                        _isSafetyCheckComplete = true;
                    }
                    else
                    {
                        StatusText.Text = "💔 UNSAFE CONFIGURATION 💔\nOnly 1 GPU detected. Toggling would cause a Black Screen.";
                        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(200, 0, 0)); // Red
                        EnduranceToggle.IsEnabled = false;
                    }
                }
                catch (Exception ex)
                {
                    StatusText.Text = "💔 SYSTEM CHECK FAILED 💔";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(200, 0, 0)); // Red
                    MessageBox.Show($"Failed to verify system safety: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            // Save the Pin State to the Windows Registry
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegKeyPath))
                {
                    key.SetValue("IsPinned", PinButton.IsChecked == true ? 1 : 0);
                }
            }
            catch { /* Silently handle registry access restrictions */ }
        }

        private void PowerPollTimer_Tick(object? sender, EventArgs e)
        {
            // Only update UI if the window is currently visible to save CPU cycles
            if (this.Visibility != Visibility.Visible) return;

            // Step 1: Always check the raw metrics first to catch hybrid power drain
            var metrics = PowerMonitor.GetPowerMetrics();

            if (metrics.Watts > 0)
            {
                // The battery is actively losing charge (either on battery, or hybrid draining on AC)
                CurrentDrawText.Text = $"{metrics.Watts:F1} W";
                CurrentDrawText.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51)); // Dark Text

                // Track the absolute peak wattage as a session high-score only
                if (metrics.Watts > _peakWattage)
                {
                    _peakWattage = metrics.Watts;
                }
                PeakDrawText.Text = $"Peak: {_peakWattage:F1} W";

                // Display truthful Estimated Battery Life
                if (metrics.TimeRemaining.HasValue)
                {
                    int hours = (int)metrics.TimeRemaining.Value.TotalHours;
                    int minutes = metrics.TimeRemaining.Value.Minutes;
                    TimeRemainingText.Text = $"Battery Remaining: {hours}h {minutes}m";
                }
                else
                {
                    TimeRemainingText.Text = "Calculating time...";
                }
            }
            // Step 2: If we are pulling 0 Watts from the battery, check if we are on AC power
            else if (PowerMonitor.IsPluggedIn())
            {
                CurrentDrawText.Text = "AC Power";
                CurrentDrawText.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)); // Gray
                PeakDrawText.Text = "Battery is not discharging";
                TimeRemainingText.Text = "";
            }
            // Step 3: Edge case where battery is present but not discharging, and not on AC (e.g., driver delay)
            else
            {
                CurrentDrawText.Text = "Reading...";
                CurrentDrawText.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)); // Light Gray
                PeakDrawText.Text = "";
                TimeRemainingText.Text = "";
            }
        }

        public void Toggle()
        {
            if (_isAnimating) return;

            if (this.Visibility == Visibility.Visible)
            {
                AnimateOut();
            }
            else
            {
                AnimateIn();
            }
        }

        private void AnimateIn()
        {
            _isAnimating = true;
            _pendingDeactivation = false;

            var desktopWorkingArea = SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width - 10;
            double targetTop = desktopWorkingArea.Bottom - this.Height - 10;

            double startTop = targetTop + this.Height;
            this.Top = startTop;
            this.Opacity = 0.0;

            this.Show();
            this.Activate();

            // Force a metrics update instantly upon opening instead of waiting for the timer
            PowerPollTimer_Tick(null, EventArgs.Empty);

            var animationDuration = new Duration(TimeSpan.FromMilliseconds(500));

            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = animationDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation slideUp = new DoubleAnimation
            {
                From = startTop,
                To = targetTop,
                Duration = animationDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            slideUp.Completed += (s, e) =>
            {
                _isAnimating = false;

                if (_pendingDeactivation && PinButton.IsChecked == false)
                {
                    _pendingDeactivation = false;
                    AnimateOut();
                }
            };

            this.BeginAnimation(Window.OpacityProperty, fadeIn);
            this.BeginAnimation(Window.TopProperty, slideUp);
        }

        private void AnimateOut()
        {
            _isAnimating = true;
            _pendingDeactivation = false;

            var desktopWorkingArea = SystemParameters.WorkArea;
            double startTop = this.Top;
            double targetTop = desktopWorkingArea.Bottom;

            var animationDuration = new Duration(TimeSpan.FromMilliseconds(500));

            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = animationDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            DoubleAnimation slideDown = new DoubleAnimation
            {
                From = startTop,
                To = targetTop,
                Duration = animationDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            slideDown.Completed += (s, e) =>
            {
                this.Hide();
                _isAnimating = false;
                _pendingDeactivation = false;

                this.BeginAnimation(Window.OpacityProperty, null);
                this.BeginAnimation(Window.TopProperty, null);
            };

            this.BeginAnimation(Window.OpacityProperty, fadeOut);
            this.BeginAnimation(Window.TopProperty, slideDown);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (PinButton.IsChecked == true)
            {
                return;
            }

            if (this.Visibility == Visibility.Visible)
            {
                if (_isAnimating)
                {
                    _pendingDeactivation = true;
                }
                else
                {
                    AnimateOut();
                }
            }
        }

        private async void EnduranceToggle_Checked(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Switching to Endurance Mode...";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 100, 0)); // Dark Green
            try
            {
                await GpuManager.EnableEnduranceModeAsync();

                StatusText.Text = string.Empty; // Clear existing text to use rich Inlines
                StatusText.Inlines.Add(new Run("Conserving energy, dGPU disabled ") { Foreground = new SolidColorBrush(Color.FromRgb(0, 100, 0)) });
                StatusText.Inlines.Add(new Run("♥") { Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 180)) }); // Hot Pink
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to toggle GPU: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Temporarily unhook to prevent an infinite loop, revert UI, and re-hook
                EnduranceToggle.Checked -= EnduranceToggle_Checked;
                EnduranceToggle.IsChecked = false;
                EnduranceToggle.Checked += EnduranceToggle_Checked;
            }
        }

        private async void EnduranceToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Waking up High-Performance GPU...";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85)); // Dark Gray
            try
            {
                await GpuManager.DisableEnduranceModeAsync();
                StatusText.Text = "Not power saving";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to toggle GPU: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                EnduranceToggle.Unchecked -= EnduranceToggle_Unchecked;
                EnduranceToggle.IsChecked = true;
                EnduranceToggle.Unchecked += EnduranceToggle_Unchecked;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}