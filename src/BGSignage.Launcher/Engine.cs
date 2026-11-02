using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BGSignage.Launcher
{
    /// <summary>
    /// Runs a command from the installed engine. On every click it looks again for the newest copy, so a version
    /// installed by the Updater is used straight away. Each copy is loaded once per Revit session, from memory
    /// (so its files are never locked), next to any older copy already loaded.
    /// </summary>
    internal static class Engine
    {
        static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        public static Version LauncherVersion => typeof(Engine).Assembly.GetName().Version ?? new Version(0, 0, 0);
        public static string LauncherFolder => Path.GetDirectoryName(typeof(Engine).Assembly.Location);

        /// <summary>The engine the next click runs: the newest installed, or one built next to the launcher (manual installs).</summary>
        public static Install Current()
        {
            var newest = Releases.List(Releases.EngineRoot).LastOrDefault();
            if (newest != null) return newest;
            var beside = Path.Combine(LauncherFolder, Installer.EngineDll);
            return File.Exists(beside) ? new Install(LauncherFolder, "", Installer.VersionOf(beside)) : null;
        }

        static Assembly Load(Install engine)
        {
            if (Loaded.TryGetValue(engine.Folder, out var assembly)) return assembly;
            var dll = Path.Combine(engine.Folder, Installer.EngineDll);
            var pdb = Path.ChangeExtension(dll, ".pdb");
#if NETFRAMEWORK
            assembly = File.Exists(pdb) ? Assembly.Load(File.ReadAllBytes(dll), File.ReadAllBytes(pdb)) : Assembly.Load(File.ReadAllBytes(dll));
#else
            var context = new System.Runtime.Loader.AssemblyLoadContext("BGSignage " + Path.GetFileName(engine.Folder));
            using (var code = new MemoryStream(File.ReadAllBytes(dll)))
            using (var symbols = File.Exists(pdb) ? new MemoryStream(File.ReadAllBytes(pdb)) : null)
                assembly = context.LoadFromStream(code, symbols);
#endif
            Loaded[engine.Folder] = assembly;
            return assembly;
        }

        public static Result Run(string command, ExternalCommandData data, ref string message, ElementSet elements)
        {
            var engine = Current();
            if (engine == null)
            {
                message = "BG Signage is not fully installed. Run Install.bat from the release zip, or use the Updater.";
                return Result.Failed;
            }
            Assembly assembly;
            try { assembly = Load(engine); }
            catch (Exception ex)
            {
                message = $"Could not load BG Signage {engine} from {engine.Folder}: {ex.Message}";
                return Result.Failed;
            }
            var type = assembly.GetType(command);
            if (type == null || !(Activator.CreateInstance(type) is IExternalCommand tool))
            {
                message = $"This tool is not in BG Signage {engine}. Install a newer release with the Updater.";
                return Result.Failed;
            }
            return tool.Execute(data, ref message, elements);
        }
    }
}
