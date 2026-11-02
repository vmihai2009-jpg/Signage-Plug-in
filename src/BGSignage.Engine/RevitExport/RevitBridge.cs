using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace BGSignage.Engine.RevitExport
{
    /// <summary>
    /// The designer window stays open while you work in Revit, so it cannot change the model directly. It queues
    /// jobs here and Revit runs them as soon as it is ready (an external event).
    /// </summary>
    internal sealed class RevitBridge : IExternalEventHandler
    {
        readonly Queue<Action<UIApplication>> _jobs = new Queue<Action<UIApplication>>();
        ExternalEvent _event;

        public static RevitBridge Instance { get; private set; }

        /// <summary>Must be called from a Revit command (inside Revit's API context).</summary>
        public static void EnsureCreated()
        {
            if (Instance != null) return;
            Instance = new RevitBridge();
            Instance._event = ExternalEvent.Create(Instance);
        }

        public void Run(Action<UIApplication> job)
        {
            lock (_jobs) _jobs.Enqueue(job);
            _event.Raise();
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                Action<UIApplication> job;
                lock (_jobs)
                {
                    if (_jobs.Count == 0) return;
                    job = _jobs.Dequeue();
                }
                try { job(app); }
                catch (Exception ex) { TaskDialog.Show("BG Signage", ex.Message); }
            }
        }

        public string GetName() => "BG Signage";
    }
}
