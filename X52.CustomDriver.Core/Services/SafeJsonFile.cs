using System;
using System.IO;
using System.Text.Json;

namespace X52.CustomDriver.Core.Services
{
    /// <summary>
    /// Crash-safe JSON files with a backup.
    ///
    /// Save: write "file.tmp" (flushed to disk), then atomically swap it in with File.Replace,
    ///       which keeps the previous version as "file.bak". A crash or power loss mid-save can
    ///       only ever leave a stray .tmp, never a half-written file.
    /// Load: if the file can't be read, keep it as "name.corrupt-&lt;time&gt;.json" (never overwritten),
    ///       fall back to the .bak copy and say so; only if both fail return nothing.
    /// </summary>
    public static class SafeJsonFile
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        /// <summary>Saves atomically. Throws on failure (callers decide how to report it).</summary>
        public static void Save<T>(string path, T value)
        {
            string json = JsonSerializer.Serialize(value, WriteOptions);
            string tmp = path + ".tmp";
            string bak = path + ".bak";

            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(true);
            }

            if (File.Exists(path))
                File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
            else
                File.Move(tmp, path);
        }

        /// <summary>
        /// Loads the file, falling back to its backup. Returns the value (or null when there is
        /// nothing usable) and a message for the user when something had to be recovered.
        /// </summary>
        public static (T? value, string? notice) Load<T>(string path, string what) where T : class
        {
            string bak = path + ".bak";

            if (!File.Exists(path))
            {
                // e.g. first start; or the main file vanished but a backup is there
                if (File.Exists(bak) && TryRead<T>(bak, out var fromBak, out _))
                {
                    TryCopy(bak, path);
                    return (fromBak, $"{what} file was missing. Restored the last good copy.");
                }
                return (null, null);
            }

            if (TryRead<T>(path, out var value, out string error))
                return (value, null);

            // Keep the damaged file for the user, never overwrite it
            string kept = KeepCorruptCopy(path);

            if (File.Exists(bak) && TryRead<T>(bak, out var restored, out _))
            {
                // Put the good copy back in place so the next save keeps a good .bak too
                TryCopy(bak, path);
                return (restored, $"{what} file was damaged ({error}). Restored the last good copy. The damaged file was kept as {Path.GetFileName(kept)}.");
            }

            return (null, $"{what} file was damaged ({error}) and no backup could be read. Starting with defaults. The damaged file was kept as {Path.GetFileName(kept)}.");
        }

        private static bool TryRead<T>(string file, out T? value, out string error) where T : class
        {
            value = null;
            error = "";
            try
            {
                string json = File.ReadAllText(file);
                value = JsonSerializer.Deserialize<T>(json);
                if (value == null) { error = "empty file"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex is JsonException ? "invalid JSON" : ex.Message;
                return false;
            }
        }

        private static string KeepCorruptCopy(string path)
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string target = Path.Combine(dir, $"{name}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            for (int i = 2; File.Exists(target); i++)
                target = Path.Combine(dir, $"{name}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}-{i}.json");
            try { File.Copy(path, target, overwrite: false); } catch { /* best effort */ }
            return target;
        }

        private static void TryCopy(string from, string to)
        {
            try { File.Copy(from, to, overwrite: true); } catch { /* best effort */ }
        }
    }
}
