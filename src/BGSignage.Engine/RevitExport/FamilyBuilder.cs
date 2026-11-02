using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BGSignage.Core.Geometry;
using BGSignage.Core.Layout;
using BGSignage.Core.Model;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;

namespace BGSignage.Engine.RevitExport
{
    /// <summary>
    /// Builds a loadable, parametric sign family (.rfa) from a sign: one extrusion per letter, rule and the panel,
    /// all driven by family type parameters, so thicknesses, stand-offs, materials and the panel on/off can be
    /// changed in Revit afterwards (Edit Type).
    /// </summary>
    internal static class FamilyBuilder
    {
        // Parameter names. Kept stable: Edit Sign reads them back.
        public const string HasPanel = "BG_Has_Backpanel";
        public const string PanelThickness = "BG_Panel_Thickness";
        public const string PanelMaterial = "BG_Panel_Material";
        public const string LetterDepth = "BG_Letter_Depth";
        public const string LetterStandoff = "BG_Letter_Standoff";
        public const string LetterMaterial = "BG_Letter_Material";
        public const string AccentMaterial = "BG_Accent_Material";
        public const string ShowRules = "BG_Show_Rules";
        public const string RuleDepth = "BG_Rule_Depth";
        public const string RuleStandoff = "BG_Rule_Standoff";
        public const string RuleMaterial = "BG_Rule_Material";
        const string LetterBack = "BG_Letter_Back";
        const string LetterFront = "BG_Letter_Front";
        const string RuleBack = "BG_Rule_Back";
        const string RuleFront = "BG_Rule_Front";
        public const string SignData = "BG_Sign_Data";
        public const string SignText = "BG_Sign_Text";
        public const string SignWidth = "BG_Sign_Width";
        public const string SignHeight = "BG_Sign_Height";
        public const string SizePercent = "BG_Size_Percent";

        static double Ft(double mm) => mm / 304.8;

        public sealed class Result
        {
            public string Path;
            public int Solids;
            public List<string> Warnings = new List<string>();
            /// <summary>Type names when the family has several sizes.</summary>
            public List<string> TypeNames = new List<string>();
        }

        public static Result CreateFamilyFile(RevitApp app, SignDocument sign, string rfaPath)
        {
            var result = new Result { Path = rfaPath };
            var build = sign.Build;
            // Outlines at Revit precision; no edge shorter than Revit accepts.
            var geometry = SignBuilder.Build(sign, WpfFontProvider.Instance, 0.1);
            result.Warnings.AddRange(geometry.Warnings);
            double minEdgeMm = Math.Max(app.ShortCurveTolerance * 304.8 * 1.3, 0.5);

            string template = FindTemplate(app, build.Hosting)
                ?? throw new FileNotFoundException(
                    $"No Generic Model {(build.Hosting == Hosting.FaceBased ? "face based " : build.Hosting == Hosting.WallBased ? "wall based " : "")}family template was found in {app.FamilyTemplatePath}. " +
                    "Check Options > File Locations > Family Template Files in Revit.");

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(rfaPath)));
            Document fam = app.NewFamilyDocument(template);
            try
            {
                using (var t = new Transaction(fam, "Build sign"))
                {
                    t.Start();
                    var options = t.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(new QuietWarnings());
                    t.SetFailureHandlingOptions(options);

                    try
                    {
                        var signage = Category.GetCategory(fam, BuiltInCategory.OST_Signage);
                        if (signage != null) fam.OwnerFamily.FamilyCategory = signage;
                    }
                    catch { /* stays a Generic Model */ }

                    var fm = fam.FamilyManager;
                    if (fm.CurrentType == null) fm.NewType(sign.Name);
                    var sizes = build.FamilySizes.Count > 0 ? build.FamilySizes : new List<int> { 100 };
                    bool multi = sizes.Count > 1;

                    // Parameters (type parameters: Edit Type changes every sign of that type).
                    var pHas = Param(fm, HasPanel, GroupTypeId.Construction, SpecTypeId.Boolean.YesNo);
                    var pPanelT = Param(fm, PanelThickness, GroupTypeId.Construction, SpecTypeId.Length);
                    var pLetterD = Param(fm, LetterDepth, GroupTypeId.Construction, SpecTypeId.Length);
                    var pLetterS = Param(fm, LetterStandoff, GroupTypeId.Construction, SpecTypeId.Length);
                    var pShowRules = Param(fm, ShowRules, GroupTypeId.Construction, SpecTypeId.Boolean.YesNo);
                    var pRuleD = Param(fm, RuleDepth, GroupTypeId.Construction, SpecTypeId.Length);
                    var pRuleS = Param(fm, RuleStandoff, GroupTypeId.Construction, SpecTypeId.Length);
                    var pLetterBack = Param(fm, LetterBack, GroupTypeId.Construction, SpecTypeId.Length);
                    var pLetterFront = Param(fm, LetterFront, GroupTypeId.Construction, SpecTypeId.Length);
                    var pRuleBack = Param(fm, RuleBack, GroupTypeId.Construction, SpecTypeId.Length);
                    var pRuleFront = Param(fm, RuleFront, GroupTypeId.Construction, SpecTypeId.Length);
                    var pPanelM = Param(fm, PanelMaterial, GroupTypeId.Materials, SpecTypeId.Reference.Material);
                    // One material parameter per layer (named after it), created when a piece first needs it.
                    var materialNames = sign.MaterialParameterNames();
                    var materialParams = new Dictionary<string, FamilyParameter>(StringComparer.OrdinalIgnoreCase);
                    FamilyParameter MaterialParam(Piece piece)
                    {
                        if (!materialNames.TryGetValue((piece.ElementId ?? "", piece.Material), out var pname))
                            pname = piece.Material == MaterialSlot.Accent ? AccentMaterial : piece.Material == MaterialSlot.Rules ? RuleMaterial : LetterMaterial;
                        if (materialParams.TryGetValue(pname, out var existing)) return existing;
                        var fp = Param(fm, pname, GroupTypeId.Materials, SpecTypeId.Reference.Material);
                        fm.Set(fp, MaterialId(fam, sign.MaterialFor(piece.ElementId, piece.Material)));
                        materialParams[pname] = fp;
                        return fp;
                    }
                    var pData = Param(fm, SignData, GroupTypeId.Data, SpecTypeId.String.Text);
                    var pText = Param(fm, SignText, GroupTypeId.IdentityData, SpecTypeId.String.Text);
                    var pW = Param(fm, SignWidth, GroupTypeId.Data, SpecTypeId.Length);
                    var pH = Param(fm, SignHeight, GroupTypeId.Data, SpecTypeId.Length);

                    fm.Set(pHas, sign.Panel.Enabled ? 1 : 0);
                    fm.Set(pShowRules, 1);
                    fm.Set(pPanelT, Ft(Math.Max(build.PanelThickness, 0.5)));
                    fm.Set(pLetterD, Ft(Math.Max(build.LetterDepth, 0.5)));
                    fm.Set(pLetterS, Ft(Math.Max(build.LetterStandoff, 0)));
                    fm.Set(pRuleD, Ft(Math.Max(build.RuleDepth, 0.5)));
                    fm.Set(pRuleS, Ft(Math.Max(build.RuleStandoff, 0)));
                    fm.Set(pPanelM, MaterialId(fam, build.PanelMaterial));
                    fm.Set(pData, SignSerializer.ToJson(sign));
                    fm.Set(pText, string.Join(" / ", sign.Frames.Select(f => (f.Text ?? "").Replace('\n', ' ').Trim()).Where(s => s.Length > 0)));
                    fm.Set(pW, Ft(geometry.PanelRect.Width * sizes[0] / 100.0));
                    fm.Set(pH, Ft(geometry.PanelRect.Height * sizes[0] / 100.0));

                    // Sizes: one family type per size ("75%", "100%"...). Every size's geometry is in the family and
                    // only the one matching the type's BG_Size_Percent is visible, so changing the type resizes the sign.
                    FamilyParameter pSize = null;
                    var visLetters = new Dictionary<int, FamilyParameter>();
                    var visPanel = new Dictionary<int, FamilyParameter>();
                    var visRules = new Dictionary<int, FamilyParameter>();
                    if (multi)
                    {
                        pSize = Param(fm, SizePercent, GroupTypeId.Geometry, SpecTypeId.Int.Integer);
                        foreach (var size in sizes)
                        {
                            visLetters[size] = Param(fm, $"BG_Show_{size}pc", GroupTypeId.Visibility, SpecTypeId.Boolean.YesNo);
                            visPanel[size] = Param(fm, $"BG_Show_{size}pc_Panel", GroupTypeId.Visibility, SpecTypeId.Boolean.YesNo);
                            visRules[size] = Param(fm, $"BG_Show_{size}pc_Rules", GroupTypeId.Visibility, SpecTypeId.Boolean.YesNo);
                        }
                        fm.Set(pSize, sizes[0]);
                        foreach (var size in sizes)
                        {
                            fm.SetFormula(visLetters[size], $"{SizePercent} = {size}");
                            fm.SetFormula(visPanel[size], $"and({HasPanel}, {SizePercent} = {size})");
                            fm.SetFormula(visRules[size], $"and({ShowRules}, {SizePercent} = {size})");
                        }
                    }

                    // Letters and rules sit on the panel face when the panel is on, on the wall when it is off.
                    fm.SetFormula(pLetterBack, $"if({HasPanel}, {PanelThickness}, 0 * {PanelThickness}) + {LetterStandoff}");
                    fm.SetFormula(pLetterFront, $"{LetterBack} + {LetterDepth}");
                    fm.SetFormula(pRuleBack, $"if({HasPanel}, {PanelThickness}, 0 * {PanelThickness}) + {RuleStandoff}");
                    fm.SetFormula(pRuleFront, $"{RuleBack} + {RuleDepth}");

                    // Sign plane. Face based: the host face is Z=0 and depth goes along +Z. Wall based and freestanding:
                    // the sign stands in the XZ plane; depth goes away from the wall, towards the viewer.
                    var origin = geometry.Origin(build.Origin);
                    bool faceBased = build.Hosting == Hosting.FaceBased;
                    double faceY = 0, outward = -1; // the template's "Front" looks along +Y, so the front is -Y
                    if (build.Hosting == Hosting.WallBased) WallFace(fam, out faceY, out outward);
                    Func<Vec2, XYZ> map;
                    Plane plane;
                    if (faceBased)
                    {
                        map = p => new XYZ(Ft(p.X - origin.X), Ft(origin.Y - p.Y), 0);
                        plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
                    }
                    else
                    {
                        // Seen from the outward side, x must still run left to right.
                        double xs = outward < 0 ? 1 : -1;
                        map = p => new XYZ(xs * Ft(p.X - origin.X), faceY, Ft(origin.Y - p.Y));
                        var o = new XYZ(0, faceY, 0);
                        plane = outward < 0 ? Plane.CreateByOriginAndBasis(o, XYZ.BasisX, XYZ.BasisZ)   // normal -Y
                                            : Plane.CreateByOriginAndBasis(o, XYZ.BasisZ, XYZ.BasisX);  // normal +Y
                    }
                    var sketch = SketchPlane.Create(fam, plane);

                    double panelT = sign.Panel.Enabled ? build.PanelThickness : 0;
                    int failed = 0;
                    foreach (var size in sizes)
                    {
                        // The face scales about the insertion point; thicknesses stay the type parameters.
                        double k = size / 100.0;
                        Func<Vec2, XYZ> mapK = p => map(new Vec2(origin.X + (p.X - origin.X) * k, origin.Y + (p.Y - origin.Y) * k));
                        var pr = geometry.PanelRect;

                        // Panel, with true arcs for rounded corners and ellipses.
                        var panel = Extrude(fam, PanelProfile(sign.Panel, pr, mapK, faceBased, k), sketch, 0, build.PanelThickness);
                        if (panel != null)
                        {
                            Bind(fm, panel, BuiltInParameter.EXTRUSION_END_PARAM, pPanelT);
                            Bind(fm, panel, BuiltInParameter.MATERIAL_ID_PARAM, pPanelM);
                            Bind(fm, panel, BuiltInParameter.IS_VISIBLE_PARAM, multi ? visPanel[size] : pHas);
                            result.Solids++;
                        }
                        else result.Warnings.Add($"The backpanel{(multi ? $" at {size}%" : "")} could not be built.");

                        foreach (var piece in geometry.Pieces)
                        {
                            bool rule = piece.Role == PieceRole.Rule;
                            double back = panelT + (rule ? build.RuleStandoff : build.LetterStandoff);
                            double front = back + (rule ? build.RuleDepth : build.LetterDepth);
                            var made = ExtrudePiece(fam, piece, mapK, sketch, back, front, minEdgeMm / k);
                            if (made.Count == 0) { failed++; continue; }
                            var slotParam = MaterialParam(piece);
                            foreach (var e in made)
                            {
                                Bind(fm, e, BuiltInParameter.EXTRUSION_START_PARAM, rule ? pRuleBack : pLetterBack);
                                Bind(fm, e, BuiltInParameter.EXTRUSION_END_PARAM, rule ? pRuleFront : pLetterFront);
                                Bind(fm, e, BuiltInParameter.MATERIAL_ID_PARAM, slotParam);
                                if (multi) Bind(fm, e, BuiltInParameter.IS_VISIBLE_PARAM, rule ? visRules[size] : visLetters[size]);
                                else if (rule) Bind(fm, e, BuiltInParameter.IS_VISIBLE_PARAM, pShowRules);
                                result.Solids++;
                            }
                        }
                    }
                    if (failed > 0)
                        result.Warnings.Add($"{failed} small shape(s) could not be modelled (usually details too fine for Revit at this size).");

                    // One type per size. A new type starts as a copy of the current one, so only the size differs.
                    if (multi)
                    {
                        result.TypeNames.Clear();
                        for (int i = 0; i < sizes.Count; i++)
                        {
                            string typeName = sizes[i] + "%";
                            if (i == 0) fm.RenameCurrentType(typeName);
                            else fm.NewType(typeName);
                            fm.Set(pSize, sizes[i]);
                            fm.Set(pW, Ft(geometry.PanelRect.Width * sizes[i] / 100.0));
                            fm.Set(pH, Ft(geometry.PanelRect.Height * sizes[i] / 100.0));
                            result.TypeNames.Add(typeName);
                        }
                    }

                    t.Commit();
                }

                fam.SaveAs(rfaPath, new SaveAsOptions { OverwriteExistingFile = true });
            }
            finally
            {
                fam.Close(false);
            }
            return result;
        }

        /// <summary>
        /// Finds the wall in a wall-based template: the face the sign goes on, and which way is out of the wall
        /// (-1 = towards -Y). The wall in Autodesk's templates fills +Y behind the face at Y=0, but this is read
        /// from the template so a different office template also works.
        /// </summary>
        static void WallFace(Document fam, out double faceY, out double outward)
        {
            faceY = 0; outward = -1;
            var wall = new FilteredElementCollector(fam).OfClass(typeof(Wall)).FirstElement();
            if (wall == null) return;
            // The wall's two big faces (normals along Y). The sign goes on the one on the host reference plane
            // (Y = 0 in Autodesk's templates), facing out of the wall: never inside it or on its far side.
            var faces = new List<(double Y, double Normal)>();
            var geo = wall.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine });
            if (geo != null)
                foreach (var obj in geo)
                {
                    var solids = obj is GeometryInstance gi ? gi.GetInstanceGeometry().OfType<Solid>() : new[] { obj as Solid };
                    foreach (var solid in solids.Where(s => s != null && s.Faces.Size > 0))
                        foreach (Face f in solid.Faces)
                            if (f is PlanarFace pf && Math.Abs(pf.FaceNormal.Y) > 0.99)
                                faces.Add((pf.Origin.Y, Math.Sign(pf.FaceNormal.Y)));
                }
            if (faces.Count > 0)
            {
                // Prefer the face at Y = 0; between faces equally near, the one facing -Y (the template's Front view).
                var best = faces.OrderBy(f => Math.Round(Math.Abs(f.Y), 6)).ThenBy(f => f.Normal).First();
                faceY = best.Y; outward = best.Normal;
                return;
            }
            var bb = wall.get_BoundingBox(null);
            if (bb == null) return;
            if (bb.Max.Y <= 1e-6) { faceY = bb.Max.Y; outward = 1; }
            else { faceY = Math.Abs(bb.Min.Y) < 1e-3 ? 0 : bb.Min.Y; outward = -1; }
        }

        static FamilyParameter Param(FamilyManager fm, string name, ForgeTypeId group, ForgeTypeId spec) =>
            fm.get_Parameter(name) ?? fm.AddParameter(name, group, spec, false);

        static void Bind(FamilyManager fm, Element e, BuiltInParameter bip, FamilyParameter fp)
        {
            var p = e.get_Parameter(bip);
            if (p == null || fp == null) return;
            try { fm.AssociateElementParameterToFamilyParameter(p, fp); } catch { /* left at its built value */ }
        }

        static ElementId MaterialId(Document fam, MaterialRef m)
        {
            string name = string.IsNullOrWhiteSpace(m?.Name) ? "BG Sign" : m.Name.Trim();
            var existing = new FilteredElementCollector(fam).OfClass(typeof(Material)).Cast<Material>()
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing.Id;
            // Same name as the project's material, so the project's own (with its render appearance) is used once loaded.
            var id = Material.Create(fam, name);
            if (fam.GetElement(id) is Material mat && m != null) mat.Color = new Color(m.R, m.G, m.B);
            return id;
        }

        /// <param name="k">Size factor: the outline is drawn through <paramref name="map"/> (which scales positions); radii scale here.</param>
        static CurveArrArray PanelProfile(BackPanel panel, Rect2 r, Func<Vec2, XYZ> map, bool faceBased, double k)
        {
            var arr = new CurveArrArray();
            var loop = new CurveArray();
            if (panel.Shape == PanelShape.Ellipse)
            {
                var c = map(r.Center);
                var yAxis = faceBased ? XYZ.BasisY : XYZ.BasisZ;
                double rx = Ft(r.Width * k / 2), ry = Ft(r.Height * k / 2);
                loop.Append(Ellipse.CreateCurve(c, rx, ry, XYZ.BasisX, yAxis, 0, Math.PI));
                loop.Append(Ellipse.CreateCurve(c, rx, ry, XYZ.BasisX, yAxis, Math.PI, 2 * Math.PI));
            }
            else
            {
                double rad = Math.Max(0, Math.Min(panel.CornerRadius, Math.Min(r.Width, r.Height) / 2 - 0.5));
                if (rad * k < 1)
                {
                    var pts = Polygons.Rectangle(r.MinX, r.MinY, r.MaxX, r.MaxY);
                    for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(map(pts[i]), map(pts[(i + 1) % 4])));
                }
                else
                {
                    double x0 = r.MinX, x1 = r.MaxX, y0 = r.MinY, y1 = r.MaxY, c = rad * (1 - Math.Sqrt(0.5));
                    void L(double ax, double ay, double bx, double by)
                    {
                        if ((Math.Abs(ax - bx) + Math.Abs(ay - by)) * k > 0.5) loop.Append(Line.CreateBound(map(new Vec2(ax, ay)), map(new Vec2(bx, by))));
                    }
                    void A(double ax, double ay, double mx, double my, double bx, double by) =>
                        loop.Append(Arc.Create(map(new Vec2(ax, ay)), map(new Vec2(bx, by)), map(new Vec2(mx, my))));
                    L(x0 + rad, y0, x1 - rad, y0);
                    A(x1 - rad, y0, x1 - c, y0 + c, x1, y0 + rad);
                    L(x1, y0 + rad, x1, y1 - rad);
                    A(x1, y1 - rad, x1 - c, y1 - c, x1 - rad, y1);
                    L(x1 - rad, y1, x0 + rad, y1);
                    A(x0 + rad, y1, x0 + c, y1 - c, x0, y1 - rad);
                    L(x0, y1 - rad, x0, y0 + rad);
                    A(x0, y0 + rad, x0 + c, y0 + c, x0 + rad, y0);
                }
            }
            arr.Append(loop);
            return arr;
        }

        /// <summary>
        /// One extrusion for the whole piece (a letter with its holes, an underline...). If Revit refuses the
        /// sketch, each island (outer loop + its holes) is tried on its own, then with coarser outlines.
        /// </summary>
        static List<Extrusion> ExtrudePiece(Document fam, Piece piece, Func<Vec2, XYZ> map, SketchPlane sketch, double back, double front, double minEdgeMm)
        {
            var made = new List<Extrusion>();
            foreach (var flatness in new[] { 0.03, 0.15 })
            {
                double minEdge = flatness > 0.1 ? minEdgeMm * 2 : minEdgeMm;
                var loops = piece.Loops.Select(l => Polygons.Simplify(l, minEdge, flatness)).Where(l => l != null).ToList();
                if (loops.Count == 0) return made;
                var whole = Extrude(fam, Profile(loops, map), sketch, back, front);
                if (whole != null) { made.Add(whole); return made; }
                var islands = SignBuilder.Islands(loops);
                if (islands.Count > 1)
                {
                    var parts = islands.Select(i => Extrude(fam, Profile(i, map), sketch, back, front)).Where(e => e != null).ToList();
                    if (parts.Count == islands.Count) return parts;
                    foreach (var e in parts) fam.Delete(e.Id);
                }
            }
            return made;
        }

        static CurveArrArray Profile(IEnumerable<Vec2[]> loops, Func<Vec2, XYZ> map)
        {
            var arr = new CurveArrArray();
            foreach (var l in loops)
            {
                var ca = new CurveArray();
                for (int i = 0; i < l.Length; i++) ca.Append(Line.CreateBound(map(l[i]), map(l[(i + 1) % l.Length])));
                arr.Append(ca);
            }
            return arr;
        }

        /// <summary>An extrusion from <paramref name="back"/> to <paramref name="front"/> mm, or null if Revit refuses it.</summary>
        static Extrusion Extrude(Document fam, CurveArrArray profile, SketchPlane sketch, double back, double front)
        {
            using (var st = new SubTransaction(fam))
            {
                st.Start();
                try
                {
                    var e = fam.FamilyCreate.NewExtrusion(true, profile, sketch, Ft(Math.Max(front, back + 0.5)));
                    e.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM)?.Set(Ft(back));
                    // No regeneration per solid (it made big signs very slow); a solid Revit can't build is
                    // removed when the family is committed.
                    st.Commit();
                    return e;
                }
                catch
                {
                    st.RollBack();
                    return null;
                }
            }
        }

        internal static string FindTemplate(RevitApp app, Hosting hosting)
        {
            var names = hosting == Hosting.FaceBased
                ? new[] { "Metric Generic Model face based.rft", "Generic Model face based.rft", "M_Generic Model face based.rft" }
                : hosting == Hosting.WallBased
                ? new[] { "Metric Generic Model wall based.rft", "Generic Model wall based.rft", "M_Generic Model wall based.rft" }
                : new[] { "Metric Generic Model.rft", "Generic Model.rft", "M_Generic Model.rft" };
            var roots = new List<string>();
            if (!string.IsNullOrEmpty(app.FamilyTemplatePath)) { roots.Add(app.FamilyTemplatePath); roots.Add(System.IO.Path.GetDirectoryName(app.FamilyTemplatePath.TrimEnd('\\'))); }
            roots.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "RVT " + app.VersionNumber, "Family Templates"));
            foreach (var name in names)
                foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
                {
                    string direct = System.IO.Path.Combine(root, name);
                    if (File.Exists(direct)) return direct;
                    try
                    {
                        var found = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault();
                        if (found != null) return found;
                    }
                    catch { }
                }
            return null;
        }

        /// <summary>
        /// Clears warnings, and resolves errors by deleting the element that caused them (e.g. one letter Revit
        /// cannot model), so one bad shape never stops the whole sign from being built.
        /// </summary>
        sealed class QuietWarnings : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                bool resolved = false;
                foreach (var f in a.GetFailureMessages())
                {
                    if (f.GetSeverity() == FailureSeverity.Warning) a.DeleteWarning(f);
                    else if (f.HasResolutions()) { a.ResolveFailure(f); resolved = true; }
                }
                return resolved ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
            }
        }
    }
}
