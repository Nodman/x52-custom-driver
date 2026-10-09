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
                else if (System.Windows.Application.Current is App app)
                {
                    // Closing the window ends the driver: ask about unsaved profile edits first
                    if (!app.ConfirmUnsavedProfiles(this)) { e.Cancel = true; return; }
                    App.IsExiting = true;
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

            // 1) No vJoy device that fits the X52 (too few buttons, used by another program, removed)
            if (X52ViewModel.FindVJoyTool("vJoyConfig.exe") != null && vm.VJoyNeedsSetup
                && vm.VJoyDeviceStatus != X52ViewModel.VJoyUnknown
                && cfg.DeclinedVJoySetupFor != vm.VJoySignature)
            {
                string signature = vm.VJoySignature;
                string? result = await AskAndSetUpVJoyAsync(vm);
                if (result == null)
                {
                    // Don't ask again unless the vJoy situation changes (SETTINGS still has the button)
                    cfg.DeclinedVJoySetupFor = signature;
                    vm.Settings.SaveSettings();
                }
                else
                {
                    System.Windows.MessageBox.Show(this, result.Length == 0 ? VJoyDoneText(vm) : result,
                        "vJoy", MessageBoxButton.OK, result.Length == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
                    string error = await HideWithInvertedModeCheckAsync(vm);
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
            else if (vm.HidHideInstalled && vm.HideRealX52Enabled && !vm.IsX52Connected
                     && cfg.HiddenInstanceIds.Count > 0)
            {
                // Typical after the driver was moved or reinstalled elsewhere: HidHide still hides the
                // X52, but only from the old exe path, so this copy can't see it either.
                vm.ShowBanner("Can't see the X52. If it is plugged in, it is hidden from this copy of the driver " +
                              "(was the driver moved?). Open SETTINGS → FIX ACCESS.", isError: true);
            }
            UpdateHideButtons();
        }

        /// <summary>
        /// Hide the X52. If HidHide is in inverted mode (a global setting other tools may rely on),
        /// explain and only switch it to normal mode when the user agrees.
        /// </summary>
        private async Task<string> HideWithInvertedModeCheckAsync(X52ViewModel vm)
        {
            string error = await vm.HideRealX52Async();
            if (error != HidHideManager.InvertedModeMessage) return error;

            var answer = System.Windows.MessageBox.Show(this,
                "HidHide is set to \"inverted\" mode (its \"Inverse application cloak\" option): listed programs are the ones " +
                "that can NOT see hidden devices.\n\n" +
                "To hide the X52 from games, HidHide must be switched to normal mode. This is a global HidHide setting: " +
                "if you set up inverted mode on purpose for another tool, devices you hid for it may become visible " +
                "to other programs, or hidden from programs that could see them before.\n\n" +
                "Switch HidHide to normal mode and hide the X52?",
                "HidHide inverted mode", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return "Not changed: HidHide is in inverted mode.";
            return await vm.HideRealX52Async(switchToNormalMode: true);
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
            // Hiding on but the stick is invisible to us: most likely the driver moved (HidHide only
            // lets the old exe path see it). Hiding again adds this exe to HidHide's allowed list.
            bool needsAccess = vm.HideRealX52Enabled && !vm.IsX52Connected && vm.Settings.CurrentSettings.HiddenInstanceIds.Count > 0;
            HideX52Button.IsEnabled = vm.HidHideInstalled && !busy;
            HideX52Button.Content = needsAccess ? "FIX ACCESS"
                : vm.HideRealX52Enabled ? "HIDE AGAIN (THIS USB PORT)" : "HIDE REAL X52";
            ShowX52Button.IsEnabled = vm.HidHideInstalled && vm.HideRealX52Enabled && !busy;
            GetHidHideButton.Visibility = vm.HidHideInstalled ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void HideX52_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not X52ViewModel vm) return;
            HideX52Message.Text = "Waiting for admin permission…";
            HideX52Button.IsEnabled = ShowX52Button.IsEnabled = false;
            string error = await HideWithInvertedModeCheckAsync(vm);
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
            VJoyMessage.Text = "Waiting for admin permission… vJoy restarts its devices, games may lose them for a moment.";
            string? error = await AskAndSetUpVJoyAsync(vm);
            VJoyMessage.Foreground = error == "" ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed;
            VJoyMessage.Text = error == null ? "" : error.Length == 0 ? "✓ " + VJoyDoneText(vm) : error;
            SetUpVJoyButton.IsEnabled = true;
        }

        private static string VJoyDoneText(X52ViewModel vm) =>
            $"vJoy is set up: device #{vm.VJoyDeviceId} has 128 buttons and the 8-way hat. Restart games that were running." +
            (vm.VJoyDeviceId != 1 ? " Games see it as a separate controller: bind the X52 once in each game." : "");

        /// <summary>
        /// Explain what's wrong with the X52's vJoy device and let the user choose: change that device,
        /// create a separate device just for the X52, or not now. Returns null for "not now",
        /// "" when vJoy was set up, otherwise an error message.
        /// </summary>
        private async Task<string?> AskAndSetUpVJoyAsync(X52ViewModel vm)
        {
            uint current = vm.VJoyDeviceId;
            int status = vm.VJoyDeviceStatus;
            if (!vm.IsVJoyActive && status == X52ViewModel.VJoyUnknown)
                return "vJoy isn't installed or isn't working. Install vJoy, then restart this driver.";

            bool busy = status == X52ViewModel.VJoyBusy;
            bool missing = status == X52ViewModel.VJoyMissing;
            uint? free = vm.FindFreeVJoyDeviceId();

            string problem = vm.IsVJoyActive ? vm.DescribeVJoyDevice(current) + " – not enough for the X52."
                           : busy ? $"vJoy device #{current} is used by another program (another feeder or game tool)."
                           : missing ? $"vJoy device #{current} doesn't exist (removed in Configure vJoy?)."
                           : vm.DescribeVJoyDevice(current) + ", but the driver couldn't use it.";
            string message = problem + "\n\n" +
                "For every X52 button in all three modes and the 8-way hat, the X52 needs a vJoy device with " +
                "128 buttons and a POV hat. Windows will ask for admin permission once, and vJoy briefly restarts " +
                "all its devices.";

            var choices = new List<ChoiceDialog.Choice>();
            var devices = new List<uint?>();
            if (!busy)
            {
                string label = missing ? $"CREATE DEVICE #{current} AGAIN" : $"CHANGE DEVICE #{current}";
                string desc = missing ? $"Make vJoy device #{current} again, with the X52 layout."
                    : current == 1 ? "Give vJoy device 1 the X52 layout. Other programs and game bindings that use device 1 will see the new layout."
                    : $"Give vJoy device #{current} the X52 layout.";
                choices.Add(new ChoiceDialog.Choice(label, desc, IsDefault: true));
                devices.Add(current);
            }
            if (free != null && (busy || (current == 1 && !missing)))
            {
                choices.Add(new ChoiceDialog.Choice($"USE A SEPARATE DEVICE (#{free})",
                    $"Create vJoy device #{free} just for the X52. Device #{current} stays as it is. " +
                    "Games see the X52 as a new controller, so bind it once in each game. Windows gives all vJoy " +
                    "devices one shared name, so both will be listed as \"vJoy Device\" (the LIVE tab shows which number is the X52).",
                    IsDefault: busy));
                devices.Add(free);
            }
            if (devices.Count == 0)
                return $"vJoy device #{current} is used by another program and vJoy has no room for another device (16 max). Close the other program or free a device in Configure vJoy.";
            choices.Add(new ChoiceDialog.Choice("NOT NOW", "You can do this later in SETTINGS → SET UP VJOY."));
            devices.Add(null);

            int pick = ChoiceDialog.Ask(this, "Set up vJoy for the X52", message, choices);
            if (pick < 0 || devices[pick] == null) return null;
            return await vm.SetUpVJoyAsync(devices[pick]!.Value);
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