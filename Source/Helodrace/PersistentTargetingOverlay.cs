using System;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class MapComponent_PersistentTargetingOverlay : MapComponent
    {
        private static Map overlayMap;
        private static Action<LocalTargetInfo> drawAction;

        public MapComponent_PersistentTargetingOverlay(Map map) : base(map) { }

        public static void Set(Map map, Action<LocalTargetInfo> action)
        {
            overlayMap = map;
            drawAction = action;
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            if (map != overlayMap || drawAction == null) return;
            if (!Find.Targeter.IsTargeting)
            {
                overlayMap = null;
                drawAction = null;
                return;
            }
            drawAction(new LocalTargetInfo(UI.MouseCell()));
        }
    }
}
