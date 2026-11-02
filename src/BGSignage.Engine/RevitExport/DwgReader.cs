using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BGSignage.Core.Geometry;
using BGSignage.Core.Import;
using BGSignage.Core.Model;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;

namespace BGSignage.Engine.RevitExport
{
    /// <summary>
    /// Reads the linework of a .dwg or .dxf with Revit's own importer (in a scratch family that is thrown away),
    /// in the drawing's units. Lines that meet are joined, so outlines become solid shapes.
    /// </summary>
    internal static class DwgReader
    {
        public static GraphicElement Read(RevitApp app, string path, out string note)
        {
            note = null;
            string template = FamilyBuilder.FindTemplate(app, Hosting.Freestanding)
                ?? throw new InvalidOperationException("No Generic Model family template was found to read the drawing with.");
            Document scratch = app.NewFamilyDocument(template);
            var pieces = new List<Vec2[]>();
            try
            {
                var view = new FilteredElementCollector(scratch).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate)
                           ?? throw new InvalidOperationException("The family template has no plan view to import into.");
                using (var t = new Transaction(scratch, "Read drawing"))
                {
                    t.Start();
                    var options = new DWGImportOptions
                    {
                        ColorMode = ImportColorMode.BlackAndWhite,
                        Unit = ImportUnit.Default,
                        Placement = ImportPlacement.Origin,
                        ThisViewOnly = false,
                    };
                    if (!scratch.Import(path, options, view, out ElementId id))
                        throw new InvalidOperationException("Revit could not import that drawing.");
                    var instance = scratch.GetElement(id);
                    var geometry = instance.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
                    Collect(geometry, pieces);
                    t.RollBack();
                }
            }
            finally
            {
                scratch.Close(false);
            }
            if (pieces.Count == 0) throw new InvalidOperationException("No lines were found in that drawing (hatches and text are not read: explode text to lines first).");
            if (pieces.Count > 20000) note = $"The drawing has {pieces.Count} lines; a cleaned-up drawing of just the graphic will be faster.";
            var chained = CurveChainer.Chain(pieces, 0.05);
            return GraphicElement.FromPaths(System.IO.Path.GetFileNameWithoutExtension(path), System.IO.Path.GetFileName(path), chained);
        }

        static void Collect(GeometryElement geometry, List<Vec2[]> into)
        {
            if (geometry == null) return;
            foreach (var g in geometry)
            {
                switch (g)
                {
                    case GeometryInstance gi: Collect(gi.GetInstanceGeometry(), into); break;
                    case PolyLine pl: into.Add(pl.GetCoordinates().Select(Mm).ToArray()); break;
                    case Curve c:
                        try { into.Add(c.Tessellate().Select(Mm).ToArray()); } catch { }
                        break;
                }
            }
        }

        // Plan X/Y in feet to sign millimetres (y down).
        static Vec2 Mm(XYZ p) => new Vec2(p.X * 304.8, -p.Y * 304.8);
    }
}
