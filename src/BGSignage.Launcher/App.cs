using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace BGSignage.Launcher
{
    /// <summary>
    /// What Revit loads at start-up: the "Signage" panel on the shared Barr Gazetas tab
    /// (Sign Designer, Edit Sign and Updater).
    /// </summary>
    public class App : IExternalApplication
    {
        // Shared with the other Barr Gazetas add-ins; the name must match exactly.
        const string TabName = "Barr Gazetas";
        static PushButton _updater;

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TabName); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* tab already made by another BG add-in */ }

            var me = Assembly.GetExecutingAssembly().Location;
            var panel = app.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == "Signage") ?? app.CreateRibbonPanel(TabName, "Signage");

            panel.AddItem(new PushButtonData("BGSignDesigner", "Sign\nDesigner", me, typeof(DesignerButton).FullName)
            {
                ToolTip = "Design a sign with InDesign-style type tools and a live preview, then export it as a parametric Revit family.",
                LongDescription = "Text frames with font, size, leading, kerning, tracking, scaling, baseline shift, skew, rotation, caps, " +
                                  "underline and paragraph rules; bars and rails; logos from .dwg or .ai; backpanel, depths and materials " +
                                  "that stay editable in Revit.",
                Image = Icon("sign16.png"), LargeImage = Icon("sign32.png"),
            });
            panel.AddItem(new PushButtonData("BGEditSign", "Edit\nSign", me, typeof(EditSignButton).FullName)
            {
                ToolTip = "Open a placed sign in the Sign Designer. Exporting again updates every sign of that family.",
                Image = Icon("edit16.png"), LargeImage = Icon("edit32.png"),
            });
            _updater = panel.AddItem(new PushButtonData("BGSignageUpdater", "Updater", me, typeof(UpdaterCommand).FullName)
            {
                Image = Icon("update16.png"), LargeImage = Icon("update32.png"),
            }) as PushButton;

            try { Installer.Tidy(Engine.Current()?.Name, Engine.LauncherFolder); } catch { /* never worth failing start-up for */ }
            RefreshUpdater();
            return Result.Succeeded;
        }

        /// <summary>Shows on the Updater button which version runs and whether a newer zip is waiting in Downloads.</summary>
        internal static void RefreshUpdater()
        {
            if (_updater == null) return;
            try
            {
                var running = Engine.Current();
                var waiting = Releases.NewestZip(Releases.Downloads);
                bool newer = waiting != null && (running == null || waiting.Value.Version > running.Version);
                _updater.ItemText = newer ? "Update\nready" : "Updater";
                _updater.ToolTip = $"Running: {running?.ToString() ?? "none"}. " +
                                   (newer ? $"v{Releases.Short(waiting.Value.Version)} is waiting in Downloads. " : "") +
                                   "Checks GitHub for a new release (or uses a zip you downloaded) and installs it straight away: no Revit restart.";
            }
            catch { }
        }

        static ImageSource Icon(string name)
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = stream;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch { return null; } // a missing icon must never stop the add-in loading
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
    }
}
