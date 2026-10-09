using System;
using System.Windows;
using X52.CustomDriver.Core.Interfaces;
using X52.CustomDriver.Core.Services;
using X52.CustomDriver.App.ViewModels;
using System.Windows.Forms; // Needed for NotifyIcon

namespace X52.CustomDriver.App
{
    public partial class App : System.Windows.Application
    {
        private IHidService? _hidService;
        private IVJoyService? _vJoyService;
        private ProfileService? _profileService;
        private NotifyIcon? _notifyIcon;
        private X52ViewModel? _viewModel;

        public static bool IsExiting { get; set; } = false;

        // One driver per Windows user session. The installer uses the same name (AppMutex in setup.iss).
        private const string InstanceMutexName = "AerakonX52Driver";
        private const string ShowEventName = "AerakonX52Driver.ShowWindow";
        private static System.Threading.Mutex? _instanceMutex;
        private static System.Threading.EventWaitHandle? _showEvent;

        // Startup log next to the exe, or in %LocalAppData% when that folder is read-only. Never throws.
        private static string _logPath = "";
        internal static void Log(string line, bool overwrite = false)
        {
            foreach (var path in new[] { _logPath, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AerakonX52Driver", "startup_log.txt") })
            {
                try
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    string text = line.EndsWith("\n") ? line : line + "\n";
                    if (overwrite) System.IO.File.WriteAllText(path, text); else System.IO.File.AppendAllText(path, text);
                    _logPath = path;
                    return;
                }
                catch { /* try the next location */ }
            }
        }

        public const string VJoyDownloadUrl = "https://github.com/jshafer817/vJoy/releases/latest";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Started elevated just to change HidHide? Do that and exit without any UI.
            if (HidHideManager.TryHandleCommandLine(e.Args, out int hidHideExit))
            {
                IsExiting = true;
                Shutdown(hidHideExit);
                return;
            }

            // Already running (e.g. hidden in the tray)? Bring that window up instead of starting a
            // second copy, which would move the cursor twice as fast and send every key twice.
            _instanceMutex = new System.Threading.Mutex(true, InstanceMutexName, out bool firstInstance);
            if (!firstInstance)
            {
                try
                {
                    using var show = System.Threading.EventWaitHandle.OpenExisting(ShowEventName);
                    show.Set();
                }
                catch { /* the other copy is still starting up */ }
                IsExiting = true;
                Shutdown(0);
                return;
            }
            _showEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, ShowEventName);
            new System.Threading.Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(ShowMainWindow));
                }
            }) { IsBackground = true, Name = "ShowWindowSignal" }.Start();

            _logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_log.txt");
            Log("--- Startup Log ---", overwrite: true);

            // Safety net: whatever goes wrong, never leave keys or mouse buttons stuck down
            DispatcherUnhandledException += (s, args) =>
            {
                ReleaseInputAndLog("UI error", args.Exception);
                args.Handled = true; // keep running; the error is shown in the message bar
                _viewModel?.ShowBanner($"Something went wrong: {args.Exception.Message} Held keys and mouse buttons were released. Details: {CrashLogPath}", isError: true);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                ReleaseInputAndLog("Fatal error", args.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                ReleaseInputAndLog("Background task error", args.Exception);
                args.SetObserved();
            };

            try
            {
                Log("Initializing Services...\n");
                _vJoyService = new VJoyService();
                _hidService = new X52HidService();
                _profileService = new ProfileService();
                var settingsService = new SettingsService();

                Log("Connecting Hardware...\n");
                
                // vJoy device 1, or the separate device the user chose for the X52
                uint vJoyId = settingsService.CurrentSettings.VJoyDeviceId;
                if (vJoyId < 1 || vJoyId > 16) vJoyId = 1;
                if (!_vJoyService.Initialize(vJoyId))
                {
                    bool vJoyInstalled = X52.CustomDriver.App.ViewModels.X52ViewModel.FindVJoyTool("vJoyConf.exe") != null;
                    if (!vJoyInstalled)
                    {
                        var answer = System.Windows.MessageBox.Show(
                            "vJoy is not installed.\n\nThis driver sends your X52 to games through the vJoy virtual joystick, so vJoy is required.\n\n" +
                            "Open the vJoy download page now?\n\nAfter installing vJoy, start this driver again – it will offer to set vJoy up for the X52.",
                            "vJoy is required", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (answer == MessageBoxResult.Yes)
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(VJoyDownloadUrl) { UseShellExecute = true }); } catch { }
                        }
                    }
                    else if (X52.CustomDriver.App.ViewModels.X52ViewModel.FindVJoyTool("vJoyConfig.exe") == null)
                    {
                        // Without vJoyConfig the main window can't offer to fix it
                        System.Windows.MessageBox.Show(
                            $"Could not use vJoy device #{vJoyId}.\n\nPossible reasons:\n- The device is not enabled in Configure vJoy.\n- Another program (e.g. another feeder) is using it.\n\n" +
                            $"Open Configure vJoy, make sure device {vJoyId} is enabled, then restart this driver.",
                            "vJoy device not available", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    // Otherwise the main window explains and offers to change the device or use a separate one
                }

                _hidService.Initialize();
                _profileService.StartWatcher();
                
                Log("Hardware Connected. Starting Listener...\n");
                // Always listen: the HID service connects (and reconnects) by itself when the stick is plugged in
                _hidService.StartListening();

                Log("Setup Tray Icon...\n");
                _notifyIcon = new NotifyIcon();
                try 
                {
                    string iconPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(iconPath))
                        _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(iconPath);
                }
                catch (Exception exIcon) { Log($"Icon Error: {exIcon.Message}\n"); }
                
                _notifyIcon.Visible = true;
                _notifyIcon.Text = "Ærakon x52 driver";
                _notifyIcon.DoubleClick += (s, args) => ShowMainWindow();

                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Open", null, (s, args) => ShowMainWindow());

                // Profile submenu: switch the active profile from the tray
                var profileMenu = new ToolStripMenuItem("Profile");
                profileMenu.DropDownItems.Add("..."); // placeholder so the submenu arrow shows
                profileMenu.DropDownOpening += (s, args) =>
                {
                    profileMenu.DropDownItems.Clear();
                    if (_viewModel == null) return;
                    foreach (var profile in _viewModel.Profiles)
                    {
                        var target = profile;
                        var item = new ToolStripMenuItem(profile.HasUnsavedChanges ? profile.Name + "  ●" : profile.Name) { Checked = ReferenceEquals(profile, _viewModel.CurrentProfile) };
                        item.Click += (s2, a2) => _viewModel?.ActivateProfile(target);
                        profileMenu.DropDownItems.Add(item);
                    }
                };
                contextMenu.Items.Add(profileMenu);
                contextMenu.Items.Add("Exit", null, (s, args) =>
                {
                    if (!ConfirmUnsavedProfiles(MainWindow)) return;
                    IsExiting = true;
                    System.Windows.Application.Current.Shutdown();
                });
                _notifyIcon.ContextMenuStrip = contextMenu;

                Log("Starting UI...\n");
                var viewModel = new X52ViewModel(_hidService, _vJoyService, _profileService!, settingsService);
                _viewModel = viewModel;
                var mainWindow = new MainWindow(viewModel);
                Log("Showing MainWindow...\n");

                MainWindow = mainWindow;
                mainWindow.Show();

                Log("Startup Completed Successfully.\n");
            }
            catch (Exception ex)
            {
                string error = $"FATAL ERROR:\n{ex.Message}\n{ex.StackTrace}\n";
                Log(error);
                System.Windows.MessageBox.Show(error, "X52 Driver Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Windows.Application.Current.Shutdown();
            }
        }

        private static string CrashLogPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AerakonX52Driver", "crash.log");

        private void ReleaseInputAndLog(string kind, Exception? ex)
        {
            try { _viewModel?.EmergencyReleaseInput(); } catch { }
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashLogPath)!);
                System.IO.File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}: {ex}\n\n");
            }
            catch { }
        }

        private void ShowMainWindow()
        {
            if (MainWindow != null)
            {
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            }
        }

        /// <summary>
        /// Before the driver closes: if profiles have unsaved edits, ask Save / Don't save / Cancel.
        /// Returns false when closing should be cancelled.
        /// </summary>
        public bool ConfirmUnsavedProfiles(Window? owner)
        {
            if (_profileService == null) return true;
            (MainWindow as MainWindow)?.ProfilesTab.CommitPendingEdits();
            var unsaved = _profileService.UnsavedProfiles();
            if (unsaved.Count == 0) return true;

            string names = string.Join("\n", unsaved.Select(p => "   • " + p.Name));
            string text = $"These profiles have unsaved changes:\n\n{names}\n\n" +
                          "Save them before closing?\n\nYes = save,  No = throw the changes away,  Cancel = keep the driver open.";
            var answer = owner != null && owner.IsVisible
                ? System.Windows.MessageBox.Show(owner, text, "Unsaved profile changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question)
                : System.Windows.MessageBox.Show(text, "Unsaved profile changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Cancel || answer == MessageBoxResult.None) return false;
            if (answer == MessageBoxResult.No) return true;
            if (_profileService.SaveProfiles()) return true;

            ShowMainWindow(); // the message bar says why saving failed
            return false;
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            // Windows is signing out or shutting down
            if (!IsExiting && !ConfirmUnsavedProfiles(MainWindow)) e.Cancel = true;
            else IsExiting = true;
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Profile edits are only written on SAVE (or when the user chose Save on exit)
            _hidService?.StopListening();
            _viewModel?.ShutdownNubMouse();
            _vJoyService?.Shutdown();
            _notifyIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
