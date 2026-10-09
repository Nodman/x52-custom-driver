using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using X52.CustomDriver.Core.Models;

namespace X52.CustomDriver.Core.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private AppSettings _settings = new();

        public AppSettings CurrentSettings => _settings;

        /// <summary>Set when the settings file had to be recovered at startup (shown to the user).</summary>
        public string? LoadNotice { get; private set; }

        /// <summary>Raised with a user-facing message when saving fails; Saved when it works again.</summary>
        public event EventHandler<string>? SaveFailed;
        public event EventHandler? Saved;

        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appData, "AerakonX52Driver");
            if (!Directory.Exists(appFolder)) Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, "settings.json");
            LoadSettings();
        }

        private void LoadSettings()
        {
            var (loaded, notice) = SafeJsonFile.Load<AppSettings>(_settingsPath, "The settings");
            _settings = loaded ?? new AppSettings();
            LoadNotice = notice;
        }

        public void SaveSettings()
        {
            try
            {
                SafeJsonFile.Save(_settingsPath, _settings);
                Saved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                SaveFailed?.Invoke(this, $"Couldn't save settings to {_settingsPath}: {ex.Message}");
            }
            ApplyStartupSetting();
        }

        private void ApplyStartupSetting()
        {
            try
            {
                string keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyName, true))
                {
                    if (key == null) return;

                    string appName = "AerakonX52Driver";
                    if (_settings.RunAtStartup)
                    {
                        string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                        if (!string.IsNullOrEmpty(exePath))
                        {
                             key.SetValue(appName, $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        if (key.GetValue(appName) != null)
                            key.DeleteValue(appName);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to set startup registry key: {ex.Message}");
            }
        }
    }
}
