using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;
using RimWorld;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularAttachmentEditor
    {
        private readonly ModularPlacementWorkspace workspace;
        private readonly Dictionary<CompModularWeaponNode, ViewState> views = new Dictionary<CompModularWeaponNode, ViewState>();
        private HashSet<string> selectedPaths = new HashSet<string> {"root"};
        private float nextWorkspaceRefresh;
        private bool batchDirty;

        private sealed class ViewState
        {
            public string Path;
            public HashSet<string> Selected;
            public int Mount, Socket;
            public EditTarget Target;
            public EditorSection Section;
            public float Zoom;
            public Vector2 Pan, Scroll;
        }

        public override void PreClose()
        {
            workspace.Refresh();
            SaveView();
            base.PreClose();
        }

        private void SaveView()
        {
            if (root == null) return;
            views[root] = new ViewState {Path=selectedPath, Selected=selectedPaths, Mount=selectedMountIndex,
                Socket=selectedSocketIndex, Target=target, Section=section, Zoom=zoom, Pan=pan, Scroll=treeScroll};
        }

        private void SwitchWeapon(CompModularWeaponNode next)
        {
            if (!ModularPlacementWorkspace.IsLive(next) || next == root) return;
            workspace.Refresh();
            SaveView();
            root = next;
            if (!views.TryGetValue(root, out ViewState view))
                view = new ViewState {Path="root", Selected=new HashSet<string> {"root"}, Zoom=1f, Target=EditTarget.Graphic};
            selectedPath=view.Path; selectedPaths=view.Selected; selectedMountIndex=view.Mount; selectedSocketIndex=view.Socket;
            target=view.Target; section=view.Section; zoom=view.Zoom; pan=view.Pan; treeScroll=view.Scroll;
            activeEditKey=null; inspectorScrollContext=null; inspectorScroll=Vector2.zero;
            numericBuffers.Clear(); textBuffers.Clear();
            workspace.Visit(root);
            batchDirty=workspace.Batch.HasUnsavedChanges;
        }

        private void DrawWorkspaceControls(Rect rect)
        {
            if (Event.current.type == EventType.Layout && Time.realtimeSinceStartup >= nextWorkspaceRefresh)
            {
                workspace.Refresh(); batchDirty=workspace.Batch.HasUnsavedChanges;
                nextWorkspaceRefresh=Time.realtimeSinceStartup+.4f;
            }
            if (Widgets.ButtonText(new Rect(324f,36f,Mathf.Max(140f,Mathf.Min(360f,rect.width-530f)),30f),
                "HD_Placement_SwitchWeapon".Translate())) ShowWeapons();
            string state=(batchDirty ? "HD_Placement_Unsaved" : "HD_Placement_Saved").Translate();
            Rect summary=new Rect(0f,74f,rect.width,28f);
            Widgets.Label(summary,"HD_Placement_BatchStatus".Translate(workspace.Batch.WeaponCount,workspace.Batch.PartCount,state,selectedPaths.Count));
            TooltipHandler.TipRegion(summary,"HD_Placement_SessionHint".Translate());
        }

        private void ShowWeapons()
        {
            var candidates=new List<CompModularWeaponNode>();
            var seen=new HashSet<CompModularWeaponNode>();
            Action<Thing> add=thing =>
            {
                CompModularWeaponNode comp=thing?.TryGetComp<CompModularWeaponNode>();
                if (ModularPlacementWorkspace.IsLive(comp) && comp.Props.isAssemblyRoot && comp.IsTopLevelAssembly && seen.Add(comp))
                    candidates.Add(comp);
            };
            foreach (CompModularWeaponNode visited in workspace.Roots) add(visited?.parent);
            if (Find.Maps != null)
                foreach (Map map in Find.Maps)
                {
                    var things=new List<Thing>();
                    ThingOwnerUtility.GetAllThingsRecursively(map,ThingRequest.ForGroup(ThingRequestGroup.Everything),things);
                    foreach (Thing thing in things) add(thing);
                }
            candidates.Sort((a,b)=>string.Compare(a.parent.Label,b.parent.Label,StringComparison.CurrentCulture));
            var options=new List<FloatMenuOption>();
            foreach (CompModularWeaponNode candidate in candidates)
            {
                CompModularWeaponNode captured=candidate;
                options.Add(new FloatMenuOption((candidate==root ? "✓ " : "")+candidate.parent.LabelCap+" #"+candidate.parent.thingIDNumber,
                    ()=>SwitchWeapon(captured)));
            }
            if (options.Count>0) Find.WindowStack.Add(new FloatMenu(options));
        }

        private void CopyBatch()
        {
            try
            {
                workspace.Refresh(); GUIUtility.systemCopyBuffer=workspace.Batch.Capture().Xml;
                Messages.Message("HD_Placement_Copied".Translate(),MessageTypeDefOf.PositiveEvent,false);
            }
            catch(Exception exception) { ExportFailed(exception); }
        }

        private void SaveBatch()
        {
            try
            {
                workspace.Refresh();
                string path=workspace.Batch.Save(Path.Combine(GenFilePaths.SaveDataFolderPath,"Helodrace","ModularWeaponExports"));
                batchDirty=workspace.Batch.HasUnsavedChanges;
                Messages.Message("HD_Placement_Exported".Translate(workspace.Batch.WeaponCount,workspace.Batch.PartCount,path),MessageTypeDefOf.PositiveEvent,false);
                Log.Message("[Helodrace] Modular placement batch saved to "+path);
            }
            catch(Exception exception) { ExportFailed(exception); }
        }

        private static void ExportFailed(Exception exception)
        {
            Log.Error("[Helodrace] Could not export modular placement batch: "+exception);
            Messages.Message("HD_Placement_ExportFailed".Translate(),MessageTypeDefOf.RejectInput,false);
        }

        private void SelectNode(ModularRenderNode node, bool additive)
        {
            if (!additive) selectedPaths.Clear();
            if (additive && selectedPaths.Contains(node.path) && selectedPaths.Count>1) selectedPaths.Remove(node.path);
            else selectedPaths.Add(node.path);
            selectedPath=selectedPaths.Contains(node.path) ? node.path : new List<string>(selectedPaths)[0];
            selectedMountIndex=0; selectedSocketIndex=0; activeEditKey=null;
            if (selectedPaths.Count>1) {section=EditorSection.Placement;target=EditTarget.Graphic;}
        }

        internal static void MoveGraphics(List<ModularRenderNode> nodes, Vector2 rootDelta)
        {
            var moved=new HashSet<CompProperties_ModularWeaponNode>();
            foreach (ModularRenderNode node in nodes)
            {
                if (!moved.Add(node.Props)) continue; // Shared Def: move once, not once per instance.
                Vector2 local=ModularTransform2D.Rotate(rootDelta,-node.transform.angle);
                node.Props.graphicOffset+=new Vector3(local.x/SafeSignedScale(node.transform.scale.x),0f,
                    local.y/SafeSignedScale(node.transform.scale.y));
            }
        }
    }
}
