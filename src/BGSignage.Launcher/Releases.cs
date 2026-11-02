using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BGSignage.Launcher
{
    /// <summary>One installed copy: a folder named "yyyyMMdd-HHmmssfff_v1.2.3" (when it was installed, and which release).</summary>
    internal sealed class Install
    {
        public Install(string folder, string stamp, Version version) { Folder = folder; Stamp = stamp; Version = version; }
        public string Folder { get; }
        public string Stamp { get; }
        public Version Version { get; }
        public string Name => Path.GetFileName(Folder);
        public override string ToString() => "v" + Releases.Short(Version);
    }

    /// <summary>
    /// Where things are installed and which copy runs. Layout under %AppData%\Autodesk\Revit\Addins\&lt;year&gt;\BGSignage:
    ///   launcher\&lt;stamp&gt;_v&lt;version&gt;\  the small part Revit loads at start-up (ribbon buttons, Updater)
    ///   engine\&lt;stamp&gt;_v&lt;version&gt;\    the designer and family builder; the launcher runs the newest on each click
    /// </summary>
    internal static class Releases
    {
#if REVIT2024
        public const string Year = "2024";
#elif REVIT2025
        public const string Year = "2025";
#else
        public const string Year = "2026";
#endif
        public const string Repo = "vmihai2009-jpg/Signage-Plug-in";

        static readonly Regex ZipName = new Regex(@"BGSignage-(\d+(?:\.\d+){1,3})\.zip$", RegexOptions.IgnoreCase);
        static readonly Regex FolderName = new Regex(@"^(\d{8}-\d{6,9})_v(\d+(?:\.\d+){1,3})$");

        public static string AddinsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", Year);
        public static string Root => Path.Combine(AddinsFolder, "BGSignage");
        public static string EngineRoot => Path.Combine(Root, "engine");
        public static string Downloads => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        public static string NewFolderName(Version v, DateTime when) => $"{when:yyyyMMdd-HHmmssfff}_v{Short(v)}";

        /// <summary>The installed copies in a folder, oldest install first.</summary>
        public static List<Install> List(string root)
        {
            if (!Directory.Exists(root)) return new List<Install>();
            return Directory.GetDirectories(root)
                .Select(d => new { Dir = d, M = FolderName.Match(Path.GetFileName(d)) })
                .Where(x => x.M.Success && Version.TryParse(x.M.Groups[2].Value, out _))
                .Select(x => new Install(x.Dir, x.M.Groups[1].Value, Version.Parse(x.M.Groups[2].Value)))
                .OrderBy(i => i.Stamp, StringComparer.Ordinal).ToList();
        }

        public static Version ZipVersion(string path)
        {
            var m = ZipName.Match(Path.GetFileName(path) ?? "");
            return m.Success && Version.TryParse(m.Groups[1].Value, out var v) ? v : null;
        }

        /// <summary>The highest-numbered release zip in a folder, if any.</summary>
        public static (string File, Version Version)? NewestZip(string folder)
        {
            if (!Directory.Exists(folder)) return null;
            var best = Directory.GetFiles(folder, "BGSignage-*.zip")
                .Select(f => (File: f, V: ZipVersion(f)))
                .Where(x => x.V != null).OrderByDescending(x => x.V).FirstOrDefault();
            return best.File == null ? ((string, Version)?)null : (best.File, best.V);
        }

        public static string Short(Version v) => v.Build >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
    }
}
