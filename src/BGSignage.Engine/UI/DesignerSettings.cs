using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BGSignage.Engine.UI
{
    /// <summary>The designer's remembered choices, in %AppData%\BGSignage\designer.json.</summary>
    [DataContract]
    internal sealed class DesignerSettings
    {
        [DataMember] public string ExportFolder { get; set; }
        [DataMember] public bool PlaceAfterExport { get; set; } = true;
        [DataMember] public string LastFolder { get; set; }
        [DataMember] public bool ShowGrid { get; set; } = true;
        [DataMember] public bool SnapToGrid { get; set; }
        [DataMember] public double GridMm { get; set; } = 10;

        [OnDeserializing] void Defaults(StreamingContext _) { PlaceAfterExport = true; ShowGrid = true; GridMm = 10; }

        public string Folder => string.IsNullOrWhiteSpace(ExportFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BG Signage")
            : ExportFolder;

        static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BGSignage", "designer.json");
        static readonly DataContractJsonSerializer Json = new DataContractJsonSerializer(typeof(DesignerSettings));

        public static DesignerSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var f = File.OpenRead(FilePath)) return (DesignerSettings)Json.ReadObject(f) ?? new DesignerSettings();
            }
            catch { }
            return new DesignerSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var f = File.Create(FilePath)) Json.WriteObject(f, this);
            }
            catch { /* settings are a convenience */ }
        }
    }
}
