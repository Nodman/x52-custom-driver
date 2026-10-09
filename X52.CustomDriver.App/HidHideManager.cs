using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nefarius.Drivers.HidHide;

namespace X52.CustomDriver.App
{
    /// <summary>
    /// Hides the physical X52 from every program except this driver, using HidHide
    /// (https://github.com/nefarius/HidHide). Games then only see the virtual (vJoy) stick.
    ///
    /// Reading HidHide's state works without admin rights; changing it does not. Changes are made
    /// by starting this same exe elevated with a "--hidhide" command, which does the work and exits.
    /// </summary>
    public static class HidHideManager
    {
        public const string DownloadUrl = "https://github.com/nefarius/HidHide/releases/latest";

        private const string CommandSwitch = "--hidhide";
        private const int ExitOk = 0, ExitNotInstalled = 2, ExitError = 3, ExitInvertedMode = 4;

        /// <summary>Returned as the message when HidHide is in inverted mode and we didn't change it.</summary>
        public const string InvertedModeMessage = "HIDHIDE_INVERTED_MODE";

        public static string ExePath =>
            Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "X52.CustomDriver.App.exe");

        private static string ResultFile => Path.Combine(Path.GetTempPath(), "AerakonX52_hidhide_result.txt");

        public static bool IsInstalled
        {
            get
            {
                try { return new HidHideControlService().IsInstalled; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Whether the given device is currently hidden. Null when it can't be read without admin
        /// rights (some HidHide versions only let administrators read the configuration).
        /// </summary>
        public static bool? IsHidden(string instanceId)
        {
            try
            {
                var svc = new HidHideControlService();
                if (!svc.IsInstalled) return false;
                bool blocked = svc.BlockedInstanceIds.Any(id => string.Equals(id, instanceId, StringComparison.OrdinalIgnoreCase));
                return blocked && svc.IsActive;
            }
            catch
            {
                return null;
            }
        }

        // ---------------- Unelevated side ----------------

        /// <summary>
        /// Hide these devices from everything except this exe. Shows one UAC prompt.
        /// If HidHide is in inverted mode, nothing is changed and InvertedModeMessage is returned,
        /// unless <paramref name="switchToNormalMode"/> is true (the user agreed).
        /// </summary>
        public static Task<(bool ok, string message)> HideAsync(IEnumerable<string> instanceIds, bool switchToNormalMode = false) =>
            RunElevatedAsync(switchToNormalMode ? "hide-normal-mode" : "hide", instanceIds);

        /// <summary>Make these devices visible to games again. Shows one UAC prompt.</summary>
        public static Task<(bool ok, string message)> ShowAsync(IEnumerable<string> instanceIds) =>
            RunElevatedAsync("show", instanceIds);

        private static Task<(bool ok, string message)> RunElevatedAsync(string mode, IEnumerable<string> instanceIds)
        {
            return Task.Run<(bool, string)>(() =>
            {
                try { File.Delete(ResultFile); } catch { }

                string ids = string.Join("|", instanceIds);
                var psi = new ProcessStartInfo
                {
                    FileName = ExePath,
                    Arguments = $"{CommandSwitch} {mode} \"{ids}\" \"{ExePath}\"",
                    UseShellExecute = true,
                    Verb = "runas", // ask Windows for admin rights
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                try
                {
                    using var p = Process.Start(psi);
                    if (p == null) return (false, "Could not start the helper.");
                    if (!p.WaitForExit(30000)) return (false, "The helper did not finish in time.");

                    string detail = "";
                    try { detail = File.Exists(ResultFile) ? File.ReadAllText(ResultFile).Trim() : ""; } catch { }

                    return p.ExitCode switch
                    {
                        ExitOk => (true, detail),
                        ExitNotInstalled => (false, "HidHide is not installed."),
                        ExitInvertedMode => (false, InvertedModeMessage),
                        _ => (false, detail.Length > 0 ? detail : $"HidHide change failed (code {p.ExitCode}).")
                    };
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    return (false, "Cancelled – admin permission is needed to change HidHide.");
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });
        }

        // ---------------- Elevated side ----------------

        /// <summary>
        /// Call first thing at startup. Returns true (with an exit code) when this process was
        /// started as the elevated helper and the app should exit straight away.
        /// </summary>
        public static bool TryHandleCommandLine(string[] args, out int exitCode)
        {
            exitCode = ExitOk;
            if (args.Length < 1 || !string.Equals(args[0], CommandSwitch, StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                string mode = args.Length > 1 ? args[1] : "";
                var ids = (args.Length > 2 ? args[2] : "")
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                string appPath = args.Length > 3 ? args[3] : ExePath;

                if (mode == "uninstall-request")
                {
                    // Started (not elevated) by the uninstaller as the user who used the driver:
                    // read that user's hidden IDs here, then do the actual change elevated.
                    var hidden = ReadHiddenIdsFromSettings();
                    if (!IsInstalled) return true; // nothing to undo
                    var (ok, message) = RunElevatedAsync("uninstall", hidden).GetAwaiter().GetResult();
                    exitCode = ok ? ExitOk : ExitError;
                    if (!ok) WriteResult(message);
                    return true;
                }

                var svc = new HidHideControlService();
                if (!svc.IsInstalled)
                {
                    exitCode = ExitNotInstalled;
                    return true;
                }

                if (mode == "hide" || mode == "hide-normal-mode")
                {
                    // We need HidHide's normal mode (only listed programs may see hidden devices).
                    // Inverted mode is a global choice the user may rely on: only change it when asked.
                    if (svc.IsAppListInverted)
                    {
                        if (mode != "hide-normal-mode")
                        {
                            exitCode = ExitInvertedMode;
                            return true;
                        }
                        svc.IsAppListInverted = false;
                    }

                    if (!svc.ApplicationPaths.Any(p => string.Equals(p, appPath, StringComparison.OrdinalIgnoreCase)))
                        svc.AddApplicationPath(appPath);

                    foreach (var id in ids)
                        if (!svc.BlockedInstanceIds.Any(b => string.Equals(b, id, StringComparison.OrdinalIgnoreCase)))
                            svc.AddBlockedInstanceId(id);

                    svc.IsActive = true;
                    WriteResult("Hidden");
                }
                else if (mode == "uninstall")
                {
                    // Called by the uninstaller: make the X52 visible again and remove this exe from
                    // HidHide's list. The hidden device IDs come from this user's settings.json.
                    var hiddenIds = ids.Length > 0 ? ids : ReadHiddenIdsFromSettings();
                    foreach (var id in hiddenIds)
                        foreach (var b in svc.BlockedInstanceIds.Where(b => string.Equals(b, id, StringComparison.OrdinalIgnoreCase)).ToList())
                            svc.RemoveBlockedInstanceId(b);
                    foreach (var p in svc.ApplicationPaths.Where(p => string.Equals(p, appPath, StringComparison.OrdinalIgnoreCase)).ToList())
                        svc.RemoveApplicationPath(p);
                    WriteResult("Uninstalled");
                }
                else if (mode == "show")
                {
                    foreach (var id in ids)
                        foreach (var b in svc.BlockedInstanceIds.Where(b => string.Equals(b, id, StringComparison.OrdinalIgnoreCase)).ToList())
                            svc.RemoveBlockedInstanceId(b);
                    // Global hiding stays on: the user may hide other devices with HidHide too
                    WriteResult("Visible");
                }
                else
                {
                    exitCode = ExitError;
                    WriteResult($"Unknown HidHide command '{mode}'.");
                }
            }
            catch (Exception ex)
            {
                exitCode = ExitError;
                WriteResult(ex.Message);
            }
            return true;
        }

        private static string[] ReadHiddenIdsFromSettings()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AerakonX52Driver", "settings.json");
                var settings = System.Text.Json.JsonSerializer.Deserialize<X52.CustomDriver.Core.Models.AppSettings>(File.ReadAllText(path));
                return settings?.HiddenInstanceIds?.ToArray() ?? Array.Empty<string>();
            }
            catch { return Array.Empty<string>(); }
        }

        private static void WriteResult(string text)
        {
            try { File.WriteAllText(ResultFile, text); } catch { }
        }

        public static void OpenDownloadPage()
        {
            try { Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true }); } catch { }
        }
    }
}
