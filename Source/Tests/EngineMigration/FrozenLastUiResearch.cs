using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
namespace Helodrace.Tests.FrozenLastUi
{
public static class ResearchWorkspaceWindowPatch
    {
        private static readonly System.Reflection.FieldInfo tabsField = AccessTools.Field(typeof(MainTabWindow_Research), "tabs");

        public static bool Prefix(MainTabWindow_Research __instance, Rect __0, float __1)
        {
            if (!ResearchWindowCompatibility.Vanilla.IsActive(__instance)
                || __instance.CurTab?.defName != "HelodTechTab") return true;
            var tabs = ((IEnumerable<TabRecord>)tabsField.GetValue(__instance)).ToList();
            Rect header = __0; header.xMax = __1;
            __0.yMin += TabDrawer.GetOverflowTabHeight(header, tabs, 100f, 200f);
            Widgets.DrawMenuSection(__0);
            TabDrawer.DrawTabsOverflow(header, tabs, 100f, 200f);
            // A tab click may have switched away while drawing the shared tab strip.
            if (__instance.CurTab?.defName == "HelodTechTab")
                return !ResearchWindowCompatibility.TryDraw(__instance, __0.ContractedBy(8));
            return false;
        }
    }
internal static class ResearchWindowCompatibility
{
 internal static readonly Host Vanilla=new();
 internal sealed class Host
 {
  internal bool IsActive(Window window)
  {
   object host=typeof(global::Helodrace.ResearchUI.ResearchWindowCompatibility).GetField("Vanilla",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
   return (bool)host.GetType().GetMethod("IsActive").Invoke(host,new object[]{window});
  }
 }
 internal static bool TryDraw(Window window,Rect rect)=>global::Helodrace.ResearchUI.ResearchWindowCompatibility.TryDraw(window,rect);
}
}
