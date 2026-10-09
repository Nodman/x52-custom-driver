using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using X52.CustomDriver.App.ViewModels;
using X52.CustomDriver.Core.Models;

namespace X52.CustomDriver.App
{
    /// <summary>
    /// Profiles tab: profile list, game .exe, and per-profile button mappings, axis curves
    /// and thumb stick mouse settings. Everything is saved automatically.
    /// </summary>
    public partial class ProfilesView : System.Windows.Controls.UserControl
    {
        private X52ViewModel? _vm;
        private X52Profile? _selected;
        private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

        // Friendly names, same as the LIVE tab; profiles still store the button IDs
        public IReadOnlyList<ButtonCatalog.ButtonInfo> AvailableButtons => ButtonCatalog.Mappable;

        public List<int> AvailableModes { get; } = new() { 0, 1, 2, 3 };

        public List<string> AvailableActions { get; } = new() { "Hold", "Repeat", "Tap", "Toggle" };

        public ProfilesView()
        {
            InitializeComponent();

            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); _vm?.SaveProfiles(); };

            // Autosave: any slider, checkbox or text change inside this view schedules a save
            AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, e) => ScheduleSave()));
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((s, e) => ScheduleSave()));
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((s, e) => ScheduleSave()));
            AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new System.Windows.Controls.TextChangedEventHandler((s, e) => ScheduleSave()));
            Unloaded += (s, e) => SaveNow();
        }

        public void Initialize(X52ViewModel vm)
        {
            _vm = vm;
            CurveEditor.Initialize(vm);
            ProfilesList.ItemsSource = vm.ProfileService.Profiles;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(X52ViewModel.CurrentProfile)) UpdateActiveText();
            };
            UpdateActiveText();
            ProfilesList.SelectedItem = vm.CurrentProfile;
            if (ProfilesList.SelectedItem == null && vm.ProfileService.Profiles.Count > 0) ProfilesList.SelectedIndex = 0;
        }

        private void ScheduleSave()
        {
            if (_vm == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void SaveNow()
        {
            if (_saveTimer.IsEnabled) _saveTimer.Stop();
            _vm?.SaveProfiles();
        }

        private void UpdateActiveText()
        {
            if (_vm == null) return;
            ActiveProfileText.Text = $"Active now: {_vm.CurrentProfile.Name}";
            UpdateMakeActiveButton();
        }

        private void UpdateMakeActiveButton()
        {
            if (_vm == null) return;
            bool isActive = _selected != null && ReferenceEquals(_selected, _vm.CurrentProfile);
            MakeActiveButton.IsEnabled = _selected != null && !isActive;
            MakeActiveButton.Content = isActive ? "ACTIVE ✓" : "MAKE ACTIVE";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            CommitName();
            // Finish any cell that is still being edited in the mappings table
            MappingsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            SaveNow();

            SaveButton.Content = "SAVED ✓";
            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            reset.Tick += (s, a) => { reset.Stop(); SaveButton.Content = "SAVE"; };
            reset.Start();
        }

        private void MakeActive_Click(object sender, RoutedEventArgs e)
        {
            if (_vm != null && _selected != null) _vm.ActivateProfile(_selected);
        }

        private static bool IsDefault(X52Profile p) => p.Name == "Default";

        // --- Profile list ---

        private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            _selected = ProfilesList.SelectedItem as X52Profile;
            EditorArea.IsEnabled = _selected != null;
            EditorArea.DataContext = _selected;

            if (_selected == null)
            {
                MappingsGrid.ItemsSource = null;
                MousePanel.DataContext = null;
                CurveEditor.SetProfile(null);
                return;
            }

            NameBox.Text = _selected.Name;
            bool isDefault = IsDefault(_selected);
            NameBox.IsEnabled = !isDefault;
            ProcessBox.IsEnabled = !isDefault;
            PickProcessButton.IsEnabled = !isDefault;
            DeleteProfileButton.IsEnabled = !isDefault;
            NameBox.ToolTip = isDefault ? "Default is used whenever no game profile matches, so it can't be renamed." : null;

            MappingsGrid.ItemsSource = _selected.Mappings;
            MousePanel.DataContext = _vm.EnsureMouseSettings(_selected);
            CurveEditor.SetProfile(_selected);
            UpdateMakeActiveButton();
        }

        private void RefreshList(X52Profile? select)
        {
            ProfilesList.Items.Refresh();
            ProfilesList.SelectedItem = select;
        }

        private void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            string name = "New Profile";
            for (int i = 2; _vm.ProfileService.Profiles.Any(p => p.Name == name); i++) name = $"New Profile {i}";
            var profile = new X52Profile { Name = name };
            _vm.EnsureMouseSettings(profile);
            _vm.ProfileService.AddProfile(profile);
            RefreshList(profile);
            NameBox.Focus();
            NameBox.SelectAll();
        }

        private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || _selected == null) return;
            var copy = _vm.ProfileService.Duplicate(_selected);
            RefreshList(copy);
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || _selected == null || IsDefault(_selected)) return;
            var answer = System.Windows.MessageBox.Show($"Delete profile \"{_selected.Name}\"?", "Delete profile",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            _vm.ProfileService.RemoveProfile(_selected);
            RefreshList(_vm.ProfileService.Profiles.FirstOrDefault());
        }

        // --- Game .exe picker: lists programs with a window that are running right now ---

        private void PickProcess_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || IsDefault(_selected)) return;

            var items = new List<(string title, string exe)>();
            int ownPid = Environment.ProcessId;
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (p.Id == ownPid || p.MainWindowHandle == IntPtr.Zero) continue;
                    string title = p.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    items.Add((title, p.ProcessName));
                }
                catch { /* some system processes can't be inspected */ }
                finally { p.Dispose(); }
            }

            var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = PickProcessButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            if (items.Count == 0)
            {
                menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "No programs with a window found – start the game first", IsEnabled = false });
            }
            else
            {
                foreach (var (title, exe) in items.OrderBy(i => i.title, StringComparer.CurrentCultureIgnoreCase))
                {
                    var item = new System.Windows.Controls.MenuItem { Header = $"{title}   —   {exe}.exe" };
                    string chosen = exe;
                    item.Click += (s, a) =>
                    {
                        if (_selected == null) return;
                        _selected.ProcessName = chosen;
                        ProcessBox.Text = chosen;
                        ScheduleSave();
                    };
                    menu.Items.Add(item);
                }
            }
            menu.IsOpen = true;
        }

        // --- Name (validated: not empty, unique, "Default" is reserved) ---

        private void NameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) CommitName();
        }

        private void NameBox_LostFocus(object sender, RoutedEventArgs e) => CommitName();

        private void CommitName()
        {
            if (_vm == null || _selected == null || IsDefault(_selected)) return;
            string name = NameBox.Text.Trim();
            bool taken = _vm.ProfileService.Profiles.Any(p => p != _selected && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (name.Length == 0 || taken || name == "Default")
            {
                NameBox.Text = _selected.Name; // revert
                return;
            }
            if (name != _selected.Name)
            {
                _selected.Name = name;
                ScheduleSave();
            }
        }

        // --- Button mappings ---

        private void AddMapping_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            var mapping = new ButtonMapping { ButtonName = "Trigger", KeySequence = new List<string> { "SPACE" }, Action = "Hold" };
            _selected.Mappings.Add(mapping);
            MappingsGrid.SelectedItem = mapping;
            MappingsGrid.ScrollIntoView(mapping);
            ScheduleSave();
        }

        private void RemoveMapping_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            if (MappingsGrid.SelectedItem is ButtonMapping mapping)
            {
                _selected.Mappings.Remove(mapping);
                ScheduleSave();
            }
        }

        private void MappingsGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) => ScheduleSave();

        // --- MACRO RECORDING LOGIC ---

        private void MacroBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;

            e.Handled = true; // Prevent default typing

            // Handle special editing keys
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                textBox.Text = "";
                UpdateBinding(textBox);
                return;
            }
            
            if (e.Key == System.Windows.Input.Key.Back)
            {
                // Remove last token
                string current = textBox.Text;
                int lastPlus = current.LastIndexOf('+');
                if (lastPlus >= 0)
                {
                    textBox.Text = current.Substring(0, lastPlus);
                }
                else
                {
                    textBox.Text = "";
                }
                UpdateBinding(textBox);
                return;
            }

            // Ignore repeats to prevent flooding
            if (e.IsRepeat) return;

            // Get string representation
            string keyStr = GetKeyString(e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key);
            if (string.IsNullOrEmpty(keyStr)) return;

            // Reset text if it was fully selected (user wants to replace)
            if (textBox.SelectedText == textBox.Text && !string.IsNullOrEmpty(textBox.Text))
            {
                textBox.Text = "";
            }

            string separator = string.IsNullOrEmpty(textBox.Text) ? "" : "+";
            
            // Avoid adding duplicate modifiers consecutively if holding down
            if (!string.IsNullOrEmpty(textBox.Text))
            {
                string[] parts = textBox.Text.Split('+');
                string lastKey = parts[parts.Length - 1];
                if (lastKey == keyStr) return; // Don't repeat "LCTRL+LCTRL"
            }

            textBox.Text += separator + keyStr;
            textBox.CaretIndex = textBox.Text.Length; // Move caret to end
            UpdateBinding(textBox);
        }

        private void UpdateBinding(System.Windows.Controls.TextBox textBox)
        {
            BindingExpression be = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
            be?.UpdateSource();
        }

        private string GetKeyString(System.Windows.Input.Key k)
        {
            // Map WPF keys to our internal format
            switch (k)
            {
                case System.Windows.Input.Key.LeftCtrl: return "LCTRL";
                case System.Windows.Input.Key.RightCtrl: return "RCTRL";
                case System.Windows.Input.Key.LeftShift: return "LSHIFT";
                case System.Windows.Input.Key.RightShift: return "RSHIFT";
                case System.Windows.Input.Key.LeftAlt: return "LALT";
                case System.Windows.Input.Key.RightAlt: return "RALT";
                case System.Windows.Input.Key.LWin: return "LWIN";
                case System.Windows.Input.Key.RWin: return "RWIN";
                case System.Windows.Input.Key.Enter: return "ENTER";
                case System.Windows.Input.Key.Space: return "SPACE";
                case System.Windows.Input.Key.Tab: return "TAB";
                case System.Windows.Input.Key.Escape: return "ESCAPE";
                case System.Windows.Input.Key.Back: return "BACKSPACE";
                case System.Windows.Input.Key.Delete: return "DELETE";
                case System.Windows.Input.Key.Up: return "UP";
                case System.Windows.Input.Key.Down: return "DOWN";
                case System.Windows.Input.Key.Left: return "LEFT";
                case System.Windows.Input.Key.Right: return "RIGHT";
                case System.Windows.Input.Key.D0: return "0";
                case System.Windows.Input.Key.D1: return "1";
                case System.Windows.Input.Key.D2: return "2";
                case System.Windows.Input.Key.D3: return "3";
                case System.Windows.Input.Key.D4: return "4";
                case System.Windows.Input.Key.D5: return "5";
                case System.Windows.Input.Key.D6: return "6";
                case System.Windows.Input.Key.D7: return "7";
                case System.Windows.Input.Key.D8: return "8";
                case System.Windows.Input.Key.D9: return "9";
            }

            if (k >= System.Windows.Input.Key.A && k <= System.Windows.Input.Key.Z) return k.ToString();
            if (k >= System.Windows.Input.Key.F1 && k <= System.Windows.Input.Key.F12) return k.ToString();
            
            // NumPad
            if (k >= System.Windows.Input.Key.NumPad0 && k <= System.Windows.Input.Key.NumPad9) return k.ToString().Replace("NumPad", "");

            return ""; // Unknown key or not mapped
        }
    }
}
