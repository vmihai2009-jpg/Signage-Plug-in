using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BGSignage.Core.Model
{
    /// <summary>Reads and writes signs as JSON (.bgsign files and the family's BG_Sign_Data parameter).</summary>
    public static class SignSerializer
    {
        static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(SignDocument));

        public static string ToJson(SignDocument doc)
        {
            using (var ms = new MemoryStream())
            {
                Serializer.WriteObject(ms, doc);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static SignDocument FromJson(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                return ((SignDocument)Serializer.ReadObject(ms)).Normalise();
        }

        public static SignDocument Clone(SignDocument doc) => FromJson(ToJson(doc));

        public static void Save(SignDocument doc, string path) => File.WriteAllText(path, ToJson(doc), Encoding.UTF8);
        public static SignDocument Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));
    }
}
