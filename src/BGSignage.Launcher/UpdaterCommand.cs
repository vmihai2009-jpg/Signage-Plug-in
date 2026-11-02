using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BGSignage.Launcher
{
    /// <summary>
    /// Installs a new release on the spot: the latest one on GitHub, a BGSignage-x.y.z.zip already in Downloads,
    /// or a zip picked by hand. The next click on Sign Designer runs it; Revit does not need restarting.
    /// Current Windows user only, no administrator rights.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class UpdaterCommand : IExternalCommand
    {
        const string Title = "BG Signage - Updater";

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var running = Engine.Current();
            var local = Releases.NewestZip(Releases.Downloads);
            (Version Version, string Url, string Name)? online = null;
            string onlineError = null;
            try { online = LatestOnGitHub(); }
            catch (Exception ex) { onlineError = ex.Message; }

            bool Newer(Version v) => running == null || v > running.Version;
            bool onlineNewer = online != null && Newer(online.Value.Version);
            bool localNewer = local != null && Newer(local.Value.Version) && !(onlineNewer && online.Value.Version >= local.Value.Version);

            var ask = new TaskDialog(Title)
            {
                MainInstruction = onlineNewer ? $"v{Releases.Short(online.Value.Version)} is available"
                    : localNewer ? $"v{Releases.Short(local.Value.Version)} is ready to install"
                    : "You have the latest version",
                MainContent = $"Running now: {running?.ToString() ?? "none"} (Revit {Releases.Year})" +
                              (online != null ? $"\nLatest on GitHub: v{Releases.Short(online.Value.Version)}" : $"\nCould not check GitHub: {onlineError}"),
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            if (onlineNewer)
                ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Download and install v{Releases.Short(online.Value.Version)} now",
                    "It takes effect straight away: no need to restart Revit.");
            if (localNewer)
                ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Install v{Releases.Short(local.Value.Version)} from Downloads",
                    Path.GetFileName(local.Value.File));
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Choose a zip...", "Any BG Signage release zip, including an older one to go back to.");

            string zip;
            switch (ask.Show())
            {
                case TaskDialogResult.CommandLink1:
                    try { zip = Download(online.Value.Url, online.Value.Name); }
                    catch (Exception ex) { message = "The download failed: " + ex.Message; return Result.Failed; }
                    break;
                case TaskDialogResult.CommandLink2: zip = local.Value.File; break;
                case TaskDialogResult.CommandLink3:
                    using (var pick = new System.Windows.Forms.OpenFileDialog
                    {
                        Title = "Pick the BG Signage release zip",
                        Filter = "BG Signage release (BGSignage-*.zip)|BGSignage-*.zip|Zip files (*.zip)|*.zip",
                        InitialDirectory = Directory.Exists(Releases.Downloads) ? Releases.Downloads : "",
                    })
                    {
                        if (pick.ShowDialog() != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;
                        zip = pick.FileName;
                    }
                    break;
                default: return Result.Cancelled;
            }

            Installed done;
            try { done = Installer.FromZip(zip, Engine.LauncherVersion, DateTime.Now); }
            catch (Exception ex) { message = "The update did not install: " + ex.Message; return Result.Failed; }
            try { Installer.Tidy(Path.GetFileName(done.EngineFolder), Engine.LauncherFolder); } catch { }
            App.RefreshUpdater();

            TaskDialog.Show(Title,
                $"v{Releases.Short(done.Engine)} is installed and ready. Sign Designer and Edit Sign use it from the next click: no need to restart Revit." +
                (done.Launcher != null ? "\n\nThis release also updates the ribbon buttons themselves; that part shows the next time Revit starts." : "") +
                "\n\nIf the Sign Designer window is open, close it and open it again.");
            return Result.Succeeded;
        }

        static HttpClient Client()
        {
#if NETFRAMEWORK
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
#endif
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BGSignage-Updater");
            return client;
        }

        /// <summary>The newest published release and its zip, from the GitHub API.</summary>
        static (Version, string, string)? LatestOnGitHub()
        {
            using (var client = Client())
            {
                var json = client.GetStringAsync($"https://api.github.com/repos/{Releases.Repo}/releases/latest").GetAwaiter().GetResult();
                var url = Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"").Cast<Match>()
                    .Select(m => m.Groups[1].Value).FirstOrDefault(u => Releases.ZipVersion(u) != null);
                if (url == null) return null;
                return (Releases.ZipVersion(url), url, Path.GetFileName(new Uri(url).AbsolutePath));
            }
        }

        static string Download(string url, string name)
        {
            Directory.CreateDirectory(Releases.Downloads);
            var target = Path.Combine(Releases.Downloads, name);
            using (var client = Client())
            {
                var bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
                File.WriteAllBytes(target, bytes);
            }
            return target;
        }
    }
}
