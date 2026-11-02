using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BGSignage.Core.Model;

namespace BGSignage.Engine.RevitExport
{
    internal static class SignExporter
    {
        public static string SafeName(string name)
        {
            var bad = Path.GetInvalidFileNameChars().Concat(new[] { '[', ']', '{', '}', ';', ':' }).ToArray();
            var s = new string((name ?? "Sign").Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim();
            return s.Length == 0 ? "Sign" : s;
        }

        /// <summary>Builds the .rfa, loads it into the open project (replacing an earlier version) and optionally starts placing it.</summary>
        public static string Export(UIApplication ui, SignDocument sign, string folder, bool place)
        {
            var uidoc = ui.ActiveUIDocument ?? throw new InvalidOperationException("Open a Revit project first.");
            var doc = uidoc.Document;
            if (doc.IsFamilyDocument) throw new InvalidOperationException("Switch to a project (not a family) to load the sign into.");

            string name = SafeName(sign.Name);
            Directory.CreateDirectory(folder);
            string rfa = Path.Combine(folder, name + ".rfa");
            var built = FamilyBuilder.CreateFamilyFile(ui.Application, sign, rfa);
            try { SignSerializer.Save(sign, Path.ChangeExtension(rfa, ".bgsign")); } catch { /* the family holds the design too */ }

            Family family;
            using (var t = new Transaction(doc, "Load sign " + name))
            {
                t.Start();
                if (!doc.LoadFamily(rfa, new OverwriteFamily(), out family) || family == null)
                    family = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => f.Name == name);
                if (family == null) throw new InvalidOperationException("Revit did not load the family " + rfa);
                foreach (var id in family.GetFamilySymbolIds())
                    if (doc.GetElement(id) is FamilySymbol s && !s.IsActive) s.Activate();
                t.Commit();
            }

            var symbol = family.GetFamilySymbolIds().Select(id => doc.GetElement(id)).OfType<FamilySymbol>().FirstOrDefault();
            if (place && symbol != null) uidoc.PostRequestForElementTypePlacement(symbol);

            var lines = new List<string> { $"\"{name}\" is loaded ({built.Solids} solids).", rfa };
            if (built.Warnings.Count > 0) lines.Add("\nNotes:\n• " + string.Join("\n• ", built.Warnings.Distinct().Take(8)));
            if (place) lines.Add(sign.Build.Hosting == Hosting.WallBased
                ? "\nClick a wall to place it. Set its height with \"Elevation from Level\"; the arrows flip it to the other face."
                : sign.Build.Hosting == Hosting.FaceBased ? "\nClick a face (wall, column...) to place it." : "\nClick to place it.");
            return string.Join("\n", lines);
        }

        sealed class OverwriteFamily : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = true; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family; overwriteParameterValues = true; return true;
            }
        }

        /// <summary>
        /// Reads the design back from a placed sign, including any thickness, material or panel changes made in
        /// Revit with Edit Type, so the designer opens what Revit shows.
        /// </summary>
        public static SignDocument ReadBack(Document doc, FamilySymbol symbol)
        {
            var json = symbol.LookupParameter(FamilyBuilder.SignData)?.AsString();
            if (string.IsNullOrWhiteSpace(json)) return null;
            var sign = SignSerializer.FromJson(json);
            sign.Name = symbol.Family.Name;
            double Mm(string p, double fallback)
            {
                var par = symbol.LookupParameter(p);
                return par != null && par.StorageType == StorageType.Double ? par.AsDouble() * 304.8 : fallback;
            }
            MaterialRef Mat(string p, MaterialRef fallback)
            {
                var par = symbol.LookupParameter(p);
                if (par == null || par.StorageType != StorageType.ElementId) return fallback;
                if (!(doc.GetElement(par.AsElementId()) is Material m)) return fallback;
                return new MaterialRef(m.Name, m.Color.IsValid ? m.Color.Red : fallback.R, m.Color.IsValid ? m.Color.Green : fallback.G, m.Color.IsValid ? m.Color.Blue : fallback.B);
            }
            var b = sign.Build;
            b.PanelThickness = Mm(FamilyBuilder.PanelThickness, b.PanelThickness);
            b.LetterDepth = Mm(FamilyBuilder.LetterDepth, b.LetterDepth);
            b.LetterStandoff = Mm(FamilyBuilder.LetterStandoff, b.LetterStandoff);
            b.RuleDepth = Mm(FamilyBuilder.RuleDepth, b.RuleDepth);
            b.RuleStandoff = Mm(FamilyBuilder.RuleStandoff, b.RuleStandoff);
            b.PanelMaterial = Mat(FamilyBuilder.PanelMaterial, b.PanelMaterial);
            b.LetterMaterial = Mat(FamilyBuilder.LetterMaterial, b.LetterMaterial);
            b.AccentMaterial = Mat(FamilyBuilder.AccentMaterial, b.AccentMaterial);
            b.RuleMaterial = Mat(FamilyBuilder.RuleMaterial, b.RuleMaterial);
            var has = symbol.LookupParameter(FamilyBuilder.HasPanel);
            if (has != null && has.StorageType == StorageType.Integer) sign.Panel.Enabled = has.AsInteger() == 1;
            return sign;
        }

        /// <summary>Project materials (name and shaded colour), for the material pickers.</summary>
        public static List<MaterialRef> ProjectMaterials(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                .Select(m => new MaterialRef(m.Name, m.Color.IsValid ? m.Color.Red : (byte)128, m.Color.IsValid ? m.Color.Green : (byte)128, m.Color.IsValid ? m.Color.Blue : (byte)128))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
