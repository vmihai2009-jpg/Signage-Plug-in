using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BGSignage.Launcher
{
    // Ribbon buttons. Each click hands over to the newest installed engine, so these never need to change.

    [Transaction(TransactionMode.Manual)]
    public class DesignerButton : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            Engine.Run("BGSignage.Engine.DesignerCommand", data, ref message, elements);
    }

    [Transaction(TransactionMode.Manual)]
    public class EditSignButton : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            Engine.Run("BGSignage.Engine.EditSignCommand", data, ref message, elements);
    }
}
