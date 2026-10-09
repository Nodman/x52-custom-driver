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
            StickView.Initialize(viewModel);
            viewModel.RefreshHidHideStatus();
            UpdateHideButtons();

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

        private void OpenVJoyConf_Click(object sender, RoutedEventArgs e)
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] candidates =
            {
                System.IO.Path.Combine(pf, "vJoy", "x64", "vJoyConf.exe"),
                System.IO.Path.Combine(pf, "vJoy", "vJoyConf.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "vJoy", "vJoyConf.exe")
            };
            var exe = candidates.FirstOrDefault(System.IO.File.Exists);
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