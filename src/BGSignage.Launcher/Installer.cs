using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace BGSignage.Launcher
{
    /// <summary>What an install put in place. Launcher is null when the release's launcher was not newer than the one running.</summary>
    internal sealed class Installed
    {
        public Version Engine;
        public string EngineFolder;
        public Version Launcher;
    }

    /// <summary>
    /// Installs a release zip without restarting Revit: the engine goes into a new folder that the next click
    /// runs; the launcher (ribbon, Updater) is replaced only when the release has a newer one, and that part shows
    /// after the next restart. Nothing running is touched.
    /// </summary>
    internal static class Installer
    {
        public const string EngineDll = "BGSignage.Engine.dll";
        public const string LauncherDll = "BGSignage.Launcher.dll";
        const string AddInId = "7c1f3b2e-5a4d-4e8b-9f61-2d0b8a6c4e17";

        public static Installed FromZip(string zip, Version runningLauncher, DateTime now)
        {
            var temp = Path.Combine(Path.GetTempPath(), "BGSignage-update-" + Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(zip, temp);
                // The zip has one folder per Revit version (R2024, R2025, R2026): use ours.
                var folders = Directory.GetFiles(temp, EngineDll, SearchOption.AllDirectories).Select(Path.GetDirectoryName).ToList();
                var source = folders.FirstOrDefault(f => Path.GetFileName(f).Equals("R" + Releases.Year, StringComparison.OrdinalIgnoreCase))
                             ?? (folders.Count == 1 ? folders[0] : null)
                             ?? throw new InvalidDataException($"That zip has no build for Revit {Releases.Year} (an R{Releases.Year} folder with {EngineDll}).");
                return FromFolder(source, runningLauncher, now, Releases.Root);
            }
            finally { try { Directory.Delete(temp, true); } catch { /* Windows clears temp */ } }
        }

        public static Installed FromFolder(string source, Version runningLauncher, DateTime now, string root)
        {
            bool IsLauncher(string f) => Path.GetFileName(f).StartsWith("BGSignage.Launcher", StringComparison.OrdinalIgnoreCase);

            var result = new Installed { Engine = VersionOf(Path.Combine(source, EngineDll)) };
            result.EngineFolder = Copy(source, Path.Combine(root, "engine", Releases.NewFolderName(result.Engine, now)), f => !IsLauncher(f));

            var launcher = Path.Combine(source, LauncherDll);
            if (File.Exists(launcher) && (runningLauncher == null || VersionOf(launcher) > runningLauncher))
            {
                result.Launcher = VersionOf(launcher);
                var folder = Copy(source, Path.Combine(root, "launcher", Releases.NewFolderName(result.Launcher, now)), IsLauncher);
                WriteManifest(Path.GetDirectoryName(root), Path.Combine(folder, LauncherDll));
            }
            return result;
        }

        public static Version VersionOf(string dll) => AssemblyName.GetAssemblyName(dll).Version ?? new Version(0, 0, 0);

        /// <summary>Copies into a temporary name first, so a half-finished copy is never picked up.</summary>
        static string Copy(string source, string target, Func<string, bool> include)
        {
            var partial = target + ".partial";
            if (Directory.Exists(partial)) Directory.Delete(partial, true);
            Directory.CreateDirectory(partial);
            foreach (var f in Directory.GetFiles(source).Where(include))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".dll" || ext == ".pdb" || ext == ".json" || ext == ".txt") File.Copy(f, Path.Combine(partial, Path.GetFileName(f)), true);
            }
            Directory.Move(partial, target);
            return target;
        }

        /// <summary>The .addin file Revit reads at start-up, pointing at the launcher.</summary>
        public static void WriteManifest(string addinsFolder, string launcherPath)
        {
            string relative = launcherPath.StartsWith(addinsFolder, StringComparison.OrdinalIgnoreCase)
                ? launcherPath.Substring(addinsFolder.Length).TrimStart('\\', '/') : launcherPath;
            var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<RevitAddIns>\r\n  <AddIn Type=\"Application\">\r\n" +
                      "    <Name>BG Signage</Name>\r\n" +
                      $"    <Assembly>{relative}</Assembly>\r\n" +
                      $"    <AddInId>{AddInId}</AddInId>\r\n" +
                      "    <FullClassName>BGSignage.Launcher.App</FullClassName>\r\n" +
                      "    <VendorId>BARRGAZETAS</VendorId>\r\n    <VendorDescription>Barr Gazetas</VendorDescription>\r\n" +
                      "  </AddIn>\r\n</RevitAddIns>\r\n";
            var path = Path.Combine(addinsFolder, "BGSignage.addin");
            File.WriteAllText(path + ".tmp", xml);
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
        }

        /// <summary>Deletes engine copies older than the last few, and launchers other than the one running.</summary>
        public static void Tidy(string runningEngine, string runningLauncherFolder, int keepOlder = 2)
        {
            void Delete(string dir) { try { Directory.Delete(dir, true); } catch { /* still in use */ } }
            var engines = Releases.List(Path.Combine(Releases.Root, "engine"));
            var newest = engines.LastOrDefault();
            var older = engines.Where(e => e != newest && e.Name != runningEngine).ToList();
            foreach (var old in older.Take(Math.Max(0, older.Count - keepOlder))) Delete(old.Folder);
            var launchers = Releases.List(Path.Combine(Releases.Root, "launcher"));
            foreach (var old in launchers.Take(Math.Max(0, launchers.Count - 1)))
                if (!string.Equals(Path.GetFullPath(old.Folder).TrimEnd('\\'), Path.GetFullPath(runningLauncherFolder ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    Delete(old.Folder);
            foreach (var dir in Directory.Exists(Releases.Root) ? Directory.GetDirectories(Releases.Root, "*.partial", SearchOption.AllDirectories) : new string[0])
                Delete(dir);
        }
    }
}
