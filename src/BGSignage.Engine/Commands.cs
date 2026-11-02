using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BGSignage.Core.Model;
using BGSignage.Engine.RevitExport;
using BGSignage.Engine.UI;

namespace BGSignage.Engine
{
    // The ribbon buttons (in the launcher) call these. They run inside Revit's API context.

    /// <summary>Opens the Sign Designer with a new sign.</summary>
    [Transaction(TransactionMode.Manual)]
    public class DesignerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                RevitBridge.EnsureCreated();
                var doc = data.Application.ActiveUIDocument?.Document;
                var materials = doc == null || doc.IsFamilyDocument ? null : SignExporter.ProjectMaterials(doc);
                DesignerWindow.Open(data.Application, null, materials, SignExporter.ProjectSigns(doc));
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Opens a placed sign (selected, or picked) in the Sign Designer, to change it and update the family.</summary>
    [Transaction(TransactionMode.Manual)]
    public class EditSignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                RevitBridge.EnsureCreated();
                var uidoc = data.Application.ActiveUIDocument;
                if (uidoc == null) { message = "Open a project first."; return Result.Failed; }
                var doc = uidoc.Document;
                // Only use the selection when it is exactly one sign; otherwise ask, so the right one opens.
                var selected = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).OfType<FamilyInstance>().Where(IsSign).ToList();
                FamilyInstance instance = selected.Count == 1 && uidoc.Selection.GetElementIds().Count == 1 ? selected[0] : null;
                if (instance == null)
                {
                    try
                    {
                        var r = uidoc.Selection.PickObject(ObjectType.Element, new SignFilter(), "Pick a sign made with the Sign Designer");
                        instance = doc.GetElement(r) as FamilyInstance;
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                }
                var sign = instance == null ? null : SignExporter.ReadBack(doc, instance.Symbol);
                if (sign == null) { message = "That is not a sign made with the Sign Designer."; return Result.Failed; }
                DesignerWindow.Open(data.Application, sign, SignExporter.ProjectMaterials(doc), SignExporter.ProjectSigns(doc));
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        static bool IsSign(FamilyInstance fi) => fi?.Symbol?.LookupParameter(FamilyBuilder.SignData) != null;

        sealed class SignFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is FamilyInstance fi && IsSign(fi);
            public bool AllowReference(Reference r, XYZ p) => true;
        }
    }
}
