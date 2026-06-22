using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class ITab_MechanicalNetwork : ITab
    {
        private Vector2 scrollPosition = Vector2.zero;

        public ITab_MechanicalNetwork()
        {
            this.size = new Vector2(400f, 300f);
            this.labelKey = "HD_ITab_MechanicalNetwork_Title";
        }

        protected override void FillTab()
        {
            Thing selectedThing = this.SelThing;
            if (selectedThing == null) return;

            CompMechanicalNode node = selectedThing.TryGetComp<CompMechanicalNode>();
            if (node == null || node.Network == null)
            {
                Rect rect = new Rect(0f, 0f, this.size.x, this.size.y).ContractedBy(10f);
                Widgets.Label(rect, "HD_ITab_MechanicalNetwork_NotConnected".Translate().Resolve());
                return;
            }

            MechanicalNetwork net = node.Network;

            Rect outRect = new Rect(0f, 0f, this.size.x, this.size.y).ContractedBy(10f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, 400f);

            Widgets.BeginScrollView(outRect, ref this.scrollPosition, viewRect, true);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            Text.Font = GameFont.Medium;
            listing.Label("HD_ITab_MechanicalNetwork_Title".Translate().Resolve());
            Text.Font = GameFont.Small;
            listing.GapLine();

            if (net.HasRpmMismatch)
            {
                GUI.color = Color.red;
                listing.Label("HD_ITab_MechanicalNetwork_StatusMismatch".Translate().Resolve());
                GUI.color = Color.white;
                listing.GapLine();
            }
            else if (net.CurrentPowerNeeded > net.CurrentPowerOutput && net.CurrentPowerOutput > 0)
            {
                GUI.color = Color.yellow;
                listing.Label("HD_ITab_MechanicalNetwork_StatusOverload".Translate().Resolve());
                GUI.color = Color.white;
                listing.GapLine();
            }

            // Power (Torque)
            listing.Label("HD_ITab_MechanicalNetwork_PowerOutput".Translate(net.CurrentPowerOutput.ToString("F0")).Resolve());
            listing.Label("HD_ITab_MechanicalNetwork_PowerNeeded".Translate(net.CurrentPowerNeeded.ToString("F0")).Resolve());
            
            float surplus = net.CurrentPowerOutput - net.CurrentPowerNeeded;
            GUI.color = surplus >= 0 ? Color.green : Color.red;
            string surplusStr = (surplus > 0 ? "+" : "") + surplus.ToString("F0");
            listing.Label("HD_ITab_MechanicalNetwork_PowerSurplus".Translate(surplusStr).Resolve());
            GUI.color = Color.white;
            listing.Gap();

            // RPM
            listing.Label("HD_ITab_MechanicalNetwork_GridRPM".Translate(net.GridRPM.ToString("F0"), net.GridInaccuracy.ToString("F0")).Resolve());
            listing.GapLine();

            // Connected Devices
            listing.Label("HD_ITab_MechanicalNetwork_Nodes".Translate(net.nodes.Count.ToString()).Resolve());
            int emitters = 0, users = 0, transmitters = 0;
            foreach (var n in net.nodes)
            {
                if (n is CompMechanicalEmitter) emitters++;
                else if (n is CompMechanicalUser) users++;
                else if (n is CompMechanicalTransmitter) transmitters++;
            }
            listing.Label("HD_ITab_MechanicalNetwork_Emitters".Translate(emitters.ToString()).Resolve());
            listing.Label("HD_ITab_MechanicalNetwork_Users".Translate(users.ToString()).Resolve());
            listing.Label("HD_ITab_MechanicalNetwork_Transmitters".Translate(transmitters.ToString()).Resolve());

            listing.End();
            Widgets.EndScrollView();
        }
    }
}