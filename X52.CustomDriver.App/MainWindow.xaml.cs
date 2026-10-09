using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using X52.CustomDriver.App.ViewModels;

namespace X52.CustomDriver.App
{
    public partial class MainWindow : Window
    {
        // Global emergency hotkey: Ctrl+Alt+M turns the thumb stick mouse on/off from anywhere
        private const int HotkeyId = 0x5852;
        private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
        private const uint VK_M = 0x4D;
        private const int WM_HOTKEY = 0x0312;
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private HwndSource? _hwndSource;

        public MainWindow(X52ViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            ProfilesTab.Initialize(viewModel);

            // Version comes from the project file (<Version>), so it's set in one place
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            if (v != null) TitleText.Text = $"Ærakon x52 driver v{v.Major}.{v.Minor}.{v.Build}  ·  community fork";
            StickView.Initialize(viewModel);
            PovIndicator.Initialize(viewModel);
            viewModel.RefreshHidHideStatus();
            UpdateHideButtons();

            // Offer to fix vJoy / hide the real X52 once the window is up
            ContentRendered += async (s, e) =>
            {
                if (_startupChecksDone) return;
                _startupChecksDone = true;
                await RunStartupChecksAsync();
            };

            // Create the window handle now so the hotkey works even while the window is hidden in the tray
            var handle = new WindowInteropHelper(this).EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);
            RegisterHotKey(handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_M);
            Closed += (s, e) =>
            {
                UnregisterHotKey(handle, HotkeyId);
                _hwndSource?.RemoveHook(WndProc);
            };
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId && DataContext is X52ViewModel vm)
            {
                vm.ToggleNubMouse();
                handled = true;
            }
            return IntPtr.Zero;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                if (DataContext is X52ViewModel vm && vm.MinimizeToTray)
                {
                    this.Hide();
                }
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!App.IsExiting)
            {
                if (DataContext is X52ViewModel vm && vm.CloseToTray)
                {
                    e.Cancel = true;
                    this.Hide();
                }
            }
            base.OnClosing(e);
        }

        private void CalibrateCenter_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is X52ViewModel vm) vm.CalibrateCenter();
        }

        private void ResetCalibration_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is X52ViewModel vm) vm.ResetCalibration();
        }

        private void ActiveProfileCombo_DropDownOpened(object? sender, EventArgs e)
        {
            // The profile list is a plain list: refresh so new/renamed profiles show up
            ActiveProfileCombo.Items.Refresh();
            if (DataContext is X52ViewModel vm) ActiveProfileCombo.SelectedItem = vm.CurrentProfile;
        }

        // --- Startup checks ---

        private bool _startupChecksDone;

        private async Task RunStartupChecksAsync()
        {
            if (DataContext is not X52ViewModel vm) return;
            var cfg = vm.Settings.CurrentSettings;

            // 1) vJoy device not set up for the X52
            if (vm.IsVJoyActive && vm.VJoyNeedsSetup && X52ViewModel.FindVJoyTool("vJoyConfig.exe") != null
                && cfg.DeclinedVJoySetupFor != vm.VJoySignature)
            {
                var answer = System.Windows.MessageBox.Show(this,
                    vm.VJoyInfoText.Split("  →")[0] + "\n\n" +
                    "For every X52 button, all three modes and the 8-way hat to reach your games, vJoy device 1 needs " +
                    "128 buttons and a POV hat.\n\nSet up vJoy now? Windows will ask for admin permission once.",
                    "Set up vJoy for the X52", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                {
                    string error = await vm.SetUpVJoyAsync();
                    System.Windows.MessageBox.Show(this,
                        error.Length == 0 ? "vJoy is set up: 128 buttons and the 8-way hat." : error,
                        "vJoy", MessageBoxButton.OK, error.Length == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                else
                {
                    // Don't ask again unless the vJoy configuration changes (SETTINGS still has the button)
                    cfg.DeclinedVJoySetupFor = vm.VJoySignature;
                    vm.Settings.SaveSettings();
                }
            }

            // 2) Real X52 still visible to games, and HidHide is available to hide it
            await Task.Delay(1500); // give the stick a moment to connect
            vm.RefreshHidHideStatus();
            if (vm.HidHideInstalled && !vm.HideRealX52Enabled && vm.IsX52Connected && !cfg.DeclinedHideRealX52)
            {
                var answer = System.Windows.MessageBox.Show(this,
                    "Games can see both the real X52 and the Ærakon virtual stick, so every input arrives twice.\n\n" +
                    "Hide the real X52 from games now? Only this driver will still see it. Windows will ask for admin permission once.",
                    "Hide the real X52 from games", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                {
                    string error = await vm.HideRealX52Async();
                    if (error.Length > 0)
                        System.Windows.MessageBox.Show(this, error, "HidHide", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    cfg.DeclinedHideRealX52 = true;
                    vm.Settings.SaveSettings();
                }
                UpdateHideButtons();
            }
        }

        // --- Hide real X52 (HidHide) ---

        private void MainTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Only react to the tab control itself (child lists also raise SelectionChanged)
            if (e.OriginalSource != MainTabs || DataContext is not X52ViewModel vm) return;
            vm.RefreshHidHideStatus();
            UpdateHideButtons();
        }

        private void UpdateHideButtons()
        {
            if (DataContext is not X52ViewModel vm) return;
            bool busy = vm.HideRealX52Busy;
            HideX52Button.IsEnabled = vm.HidHideInstalled && !busy;
            HideX52Button.Content = vm.HideRealX52Enabled ? "HIDE AGAIN (THIS USB PORT)" : "HIDE REAL X52";
            ShowX52Button.IsEnabled = vm.HidHideInstalled && vm.HideRealX52Enabled && !busy;
            GetHidHideButton.Visibility = vm.HidHideInstalled ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void HideX52_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not X52ViewModel vm) return;
            HideX52Message.Text = "Waiting for admin permission…";
            HideX52Button.IsEnabled = ShowX52Button.IsEnabled = false;
            string error = await vm.HideRealX52Async();
            HideX52Message.Text = error;
            UpdateHideButtons();
        }

        private async void ShowX52_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not X52ViewModel vm) return;
            HideX52Message.Text = "Waiting for admin permission…";
            HideX52Button.IsEnabled = ShowX52Button.IsEnabled = false;
            string error = await vm.ShowRealX52Async();
            HideX52Message.Text = error;
            UpdateHideButtons();
        }

        private void GetHidHide_Click(object sender, RoutedEventArgs e) => HidHideManager.OpenDownloadPage();

        private void DismissBanner_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is X52ViewModel vm) vm.DismissBanner();
        }

        // --- vJoy / Game Controllers ---

        private void OpenJoyCpl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "joy.cpl") { UseShellExecute = true });
                VJoyMessage.Text = "";
            }
            catch (Exception ex) { VJoyMessage.Text = ex.Message; }
        }

        private async void SetUpVJoy_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not X52ViewModel vm) return;
            SetUpVJoyButton.IsEnabled = false;
            VJoyMessage.Foreground = System.Windows.Media.Brushes.Gray;
            VJoyMessage.Text = "Waiting for admin permission… vJoy restarts its device, games may lose it for a moment.";
            string error = await vm.SetUpVJoyAsync();
            VJoyMessage.Foreground = error.Length == 0 ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed;
            VJoyMessage.Text = error.Length == 0 ? "✓ vJoy is set up: 128 buttons and the hat. Restart games that were running." : error;
            SetUpVJoyButton.IsEnabled = true;
        }

        private void OpenVJoyMonitor_Click(object sender, RoutedEventArgs e)
        {
            var exe = X52ViewModel.FindVJoyTool("JoyMonitor.exe");
            if (exe == null)
            {
                VJoyMessage.Text = "vJoy Monitor wasn't found. It's an optional part of the vJoy installer (\"vJoy Monitoring application\").";
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                VJoyMessage.Text = "";
            }
            catch (Exception ex) { VJoyMessage.Text = ex.Message; }
        }

        private void OpenVJoyConf_Click(object sender, RoutedEventArgs e)
        {
            var exe = X52ViewModel.FindVJoyTool("vJoyConf.exe");
            if (exe == null)
            {
                VJoyMessage.Text = "Configure vJoy wasn't found. Open it from the Start menu (search \"Configure vJoy\").";
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                VJoyMessage.Text = "After changing vJoy, restart this driver.";
            }
            catch (Exception ex) { VJoyMessage.Text = ex.Message; }
        }

        private void RotateNub_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is X52ViewModel vm) vm.RotateNubMouse();
        }

    }
}