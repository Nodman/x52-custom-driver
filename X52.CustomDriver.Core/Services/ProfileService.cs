using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using X52.CustomDriver.Core.Models;

namespace X52.CustomDriver.Core.Services
{
    public class ProfileService
    {
        private readonly string _profilesPath;
        private List<X52Profile> _profiles = new();
        private X52Profile _activeProfile = new();
        // Profile the game watcher last picked; a manual choice stays until this changes
        private X52Profile? _lastAutoTarget;
        private readonly object _switchLock = new();
        private CancellationTokenSource? _ccts;

        public event EventHandler<X52Profile>? OnProfileChanged;
        public IReadOnlyList<X52Profile> Profiles => _profiles;
        public X52Profile ActiveProfile => _activeProfile;

        /// <summary>Set when the profiles file had to be recovered at startup (shown to the user).</summary>
        public string? LoadNotice { get; private set; }

        /// <summary>Raised with a user-facing message when saving fails; Saved when it works again.</summary>
        public event EventHandler<string>? SaveFailed;
        public event EventHandler? Saved;

        public string ProfilesPath => _profilesPath;

        // Each profile as it was last written to (or read from) disk. Edits apply live, but only
        // SAVE writes them; comparing with this tells which profiles have unsaved changes.
        private readonly Dictionary<X52Profile, string> _saved = new(ReferenceEqualityComparer.Instance);

        /// <summary>Raised (on the caller's thread) when any profile's unsaved state changes.</summary>
        public event EventHandler? UnsavedChanged;

        public ProfileService()
        {
            _profilesPath = ChooseProfilesPath(out string? folderNotice);
            LoadProfiles();
            if (folderNotice != null) LoadNotice = LoadNotice == null ? folderNotice : LoadNotice + " " + folderNotice;

            if (!_profiles.Any())
            {
                CreateDefaultProfiles();
            }
            else if (!_profiles.Any(p => p.Name == "Default"))
            {
                // "Default" is the fallback when no game matches; recreate it instead of failing to start
                _profiles.Insert(0, new X52Profile { Name = "Default" });
                LoadNotice = (LoadNotice == null ? "" : LoadNotice + " ") + "The \"Default\" profile was missing and has been recreated.";
                SaveProfiles();
            }

            _activeProfile = _profiles.First(p => p.Name == "Default");
            _lastAutoTarget = _activeProfile;
        }

        /// <summary>
        /// Manual switch. It stays active until a game with its own profile starts or closes;
        /// then automatic switching takes over again.
        /// </summary>
        public void SetActiveProfile(X52Profile profile)
        {
            lock (_switchLock)
            {
                if (!_profiles.Contains(profile) || ReferenceEquals(profile, _activeProfile)) return;
                _activeProfile = profile;
            }
            OnProfileChanged?.Invoke(this, profile);
        }

        private void LoadProfiles()
        {
            // Never overwrites a damaged file: it is kept aside and the .bak copy is used instead
            var (loaded, notice) = SafeJsonFile.Load<List<X52Profile>>(_profilesPath, "The profiles");
            _profiles = loaded ?? new List<X52Profile>();
            LoadNotice = notice;
            MigrateLoadedProfiles();
            // What's on disk counts as saved (the migration only fills in what old files lacked)
            foreach (var p in _profiles) _saved[p] = Snapshot(p);
        }

        /// <summary>
        /// Mappings saved before v1.2.2 have no Action. They were one-shot presses, so make that explicit
        /// (Tap, or Toggle when the old IsToggle flag was set) instead of turning them into Hold.
        /// </summary>
        private void MigrateLoadedProfiles()
        {
            foreach (var profile in _profiles)
                foreach (var m in profile.Mappings)
                    if (string.IsNullOrWhiteSpace(m.Action))
                        m.Action = m.EffectiveAction;
        }

        /// <summary>Saves every profile, including all unsaved edits.</summary>
        public bool SaveProfiles() => WriteFile(_ => true);

        /// <summary>Saves this profile's edits. Other profiles are written as they were last saved.</summary>
        public bool SaveProfile(X52Profile profile) => WriteFile(p => ReferenceEquals(p, profile));

        /// <summary>
        /// Writes the profile list. Profiles chosen by <paramref name="takeCurrent"/> (and ones never
        /// saved) are written as they are now; the rest as they were last saved, so their unsaved
        /// edits stay unsaved. Used for SAVE and for create / duplicate / delete.
        /// </summary>
        private bool WriteFile(Func<X52Profile, bool> takeCurrent)
        {
            try
            {
                var written = new List<X52Profile>();
                var output = new List<X52Profile>();
                foreach (var p in _profiles.ToList())
                {
                    if (takeCurrent(p) || !_saved.TryGetValue(p, out string? json))
                    {
                        output.Add(p);
                        written.Add(p);
                    }
                    else
                    {
                        output.Add(JsonSerializer.Deserialize<X52Profile>(json) ?? p);
                    }
                }

                SafeJsonFile.Save(_profilesPath, output);
                foreach (var p in written) _saved[p] = Snapshot(p);
                RefreshUnsavedFlags();
                Saved?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                SaveFailed?.Invoke(this, $"Couldn't save profiles to {_profilesPath}: {ex.Message} Your changes are still active; try SAVE again.");
                return false;
            }
        }

        private static string Snapshot(X52Profile p) => JsonSerializer.Serialize(p);

        private bool IsUnsaved(X52Profile p) => !_saved.TryGetValue(p, out string? json) || json != Snapshot(p);

        /// <summary>Profiles with edits that aren't saved yet (checked now, not from the flags).</summary>
        public List<X52Profile> UnsavedProfiles() => _profiles.Where(IsUnsaved).ToList();

        public bool HasUnsavedChanges => _profiles.Any(p => p.HasUnsavedChanges);

        /// <summary>Re-check every profile against its saved copy and update HasUnsavedChanges.</summary>
        public void RefreshUnsavedFlags()
        {
            bool changed = false;
            foreach (var p in _profiles)
            {
                bool unsaved = IsUnsaved(p);
                if (p.HasUnsavedChanges != unsaved) { p.HasUnsavedChanges = unsaved; changed = true; }
            }
            if (changed) UnsavedChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Throw away a profile's unsaved edits. The objects are updated in place, so everything
        /// that holds on to the profile (or its mouse settings) keeps working.
        /// </summary>
        public void RevertProfile(X52Profile profile)
        {
            if (!_saved.TryGetValue(profile, out string? json)) return;
            var saved = JsonSerializer.Deserialize<X52Profile>(json);
            if (saved == null) return;

            profile.Name = saved.Name;
            profile.ProcessName = saved.ProcessName;
            profile.Mappings.Clear();
            foreach (var m in saved.Mappings) profile.Mappings.Add(m);
            profile.AxisSettings = saved.AxisSettings;
            if (saved.Mouse != null)
            {
                if (profile.Mouse == null) profile.Mouse = saved.Mouse;
                else
                {
                    var m = profile.Mouse;
                    m.Enabled = saved.Mouse.Enabled; m.MoveCursor = saved.Mouse.MoveCursor;
                    m.LeftClick = saved.Mouse.LeftClick; m.MiddleClick = saved.Mouse.MiddleClick; m.Scroll = saved.Mouse.Scroll;
                    m.Speed = saved.Mouse.Speed; m.Deadzone = saved.Mouse.Deadzone;
                }
            }
            RefreshUnsavedFlags();
        }

        /// <summary>
        /// profiles.json lives next to the exe (so the portable zip stays portable). If that folder
        /// isn't writable (e.g. unzipped into Program Files), use %LocalAppData%\AerakonX52Driver instead,
        /// starting from the copy next to the exe if there is one.
        /// </summary>
        private static string ChooseProfilesPath(out string? notice)
        {
            notice = null;
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string local = Path.Combine(exeDir, "profiles.json");
            if (IsFolderWritable(exeDir)) return local;

            string fallbackDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AerakonX52Driver");
            Directory.CreateDirectory(fallbackDir);
            string fallback = Path.Combine(fallbackDir, "profiles.json");
            if (!File.Exists(fallback) && File.Exists(local))
            {
                try { File.Copy(local, fallback); } catch { }
            }
            notice = $"The driver's folder ({exeDir.TrimEnd('\\')}) is read-only, so profiles are saved in {fallbackDir}.";
            return fallback;
        }

        private static bool IsFolderWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch { return false; }
        }

        private void CreateDefaultProfiles()
        {
            _profiles.Add(new X52Profile { Name = "Default" });
            _profiles.Add(new X52Profile { 
                Name = "MSFS 2020", 
                ProcessName = "FlightSimulator",
                AxisSettings = new AxisSettings { SensitivityX = 0.8, SensitivityY = 0.8 }
            });
            _profiles.Add(new X52Profile { 
                Name = "DCS World", 
                ProcessName = "DCS",
                Mappings = new ObservableCollection<ButtonMapping> {
                    new ButtonMapping { ButtonName = "ButtonD", KeySequence = new List<string>{"LSHIFT", "G"}, Action = "Tap" }
                }
            });
            // Wardogs: the game process runs behind Easy Anti-Cheat as WardogsClient-Win64-Shipping.exe
            _profiles.Add(new X52Profile
            {
                Name = "Wardogs",
                ProcessName = "WardogsClient-Win64-Shipping",
                Mappings = new ObservableCollection<ButtonMapping> {
                    new ButtonMapping { ButtonName = "SliderMax", KeySequence = new List<string>{"F"}, Action = "Hold" }
                },
                AxisSettings = new AxisSettings
                {
                    CurveX = new AxisCurve { Curvature = 0.6 },
                    CurveY = new AxisCurve { Curvature = 0.6 },
                    CurveTwist = new AxisCurve { Curvature = 0.6 }
                },
                Mouse = new ThumbMouseSettings
                {
                    Enabled = true, MoveCursor = true, LeftClick = false, MiddleClick = true, Scroll = true,
                    Speed = 1200, Deadzone = 1
                }
            });
            SaveProfiles();
        }

        public void AddProfile(X52Profile profile)
        {
            if (_profiles.Any(p => p.Name == profile.Name)) return;
            _profiles.Add(profile);
            WriteFile(p => ReferenceEquals(p, profile));
        }

        public void RemoveProfile(X52Profile profile)
        {
            if (_profiles.Count <= 1) return; // Don't delete the last profile
            _profiles.Remove(profile);
            _saved.Remove(profile);
            if (ReferenceEquals(_lastAutoTarget, profile)) _lastAutoTarget = null;
            WriteFile(_ => false);
            UnsavedChanged?.Invoke(this, EventArgs.Empty);
            if (ReferenceEquals(_activeProfile, profile))
                SetActiveProfile(_profiles.FirstOrDefault(p => p.Name == "Default") ?? _profiles[0]);
        }

        public void UpdateProfile(X52Profile profile)
        {
            var index = _profiles.FindIndex(p => p.Name == profile.Name);
            if (index != -1)
            {
                _profiles[index] = profile;
                if (_activeProfile.Name == profile.Name)
                {
                    _activeProfile = profile;
                    OnProfileChanged?.Invoke(this, _activeProfile);
                }
                SaveProfile(profile);
            }
        }

        // "DCS.exe", " dcs " and "DCS" all match the DCS process
        private static string NormalizeProcessName(string name)
        {
            name = name.Trim();
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }

        /// <summary>
        /// The GAME .EXE field may hold several names separated by , or ; and may use wildcards:
        /// * = any text, ? = one character. Case doesn't matter, ".exe" is optional.
        /// e.g. "DCS, DCS_server" or "Wardogs*".
        /// </summary>
        public static bool MatchesProcess(string pattern, string runningProcessName)
        {
            foreach (var part in pattern.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = NormalizeProcessName(part);
                if (p.Length == 0) continue;
                if (p.IndexOfAny(new[] { '*', '?' }) < 0)
                {
                    if (string.Equals(p, runningProcessName, StringComparison.OrdinalIgnoreCase)) return true;
                    continue;
                }
                string regex = "^" + System.Text.RegularExpressions.Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(runningProcessName, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Deep copy (via JSON) for "duplicate profile".</summary>
        public X52Profile Duplicate(X52Profile source)
        {
            var copy = JsonSerializer.Deserialize<X52Profile>(JsonSerializer.Serialize(source)) ?? new X52Profile();
            string baseName = source.Name + " copy";
            string name = baseName;
            for (int i = 2; _profiles.Any(p => p.Name == name); i++) name = $"{baseName} {i}";
            copy.Name = name;
            copy.ProcessName = null;
            AddProfile(copy);
            return copy;
        }

        public void StartWatcher()
        {
            _ccts = new CancellationTokenSource();
            Task.Run(() => WatchLoop(_ccts.Token));
        }

        public void StopWatcher() => _ccts?.Cancel();

        private async Task WatchLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var runningProcesses = Process.GetProcesses().Select(p => p.ProcessName).ToList();
                    
                    var matchedProfile = _profiles.FirstOrDefault(p =>
                        !string.IsNullOrWhiteSpace(p.ProcessName) &&
                        runningProcesses.Any(running => MatchesProcess(p.ProcessName!, running)));

                    var targetProfile = matchedProfile ?? _profiles.FirstOrDefault(p => p.Name == "Default") ?? _profiles[0];

                    // Only switch when the running game changes, so a manual choice isn't undone every 5 s
                    bool changed = false;
                    lock (_switchLock)
                    {
                        if (!ReferenceEquals(targetProfile, _lastAutoTarget))
                        {
                            _lastAutoTarget = targetProfile;
                            if (!ReferenceEquals(targetProfile, _activeProfile))
                            {
                                _activeProfile = targetProfile;
                                changed = true;
                            }
                        }
                    }
                    if (changed) OnProfileChanged?.Invoke(this, targetProfile);
                }
                catch { }

                await Task.Delay(5000, token);
            }
        }
    }
}
