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

        private void RotateNub_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is X52ViewModel vm) vm.RotateNubMouse();
        }

    }
}