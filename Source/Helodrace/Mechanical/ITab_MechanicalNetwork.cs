using RimWorld;
using UnityEngine;
using Waylith.Math;
using Vector2 = System.Numerics.Vector2;
using Rect = Waylith.Math.Rect;
using Color = Waylith.Math.Color;
using Verse;

namespace Helodrace
{
    public class ITab_MechanicalNetwork : ITab
    {
        private Vector2 scrollPosition = Vector2.Zero;

        public ITab_MechanicalNetwork()
        {
            this.size = new UnityEngine.Vector2(400f, 300f);
            this.labelKey = "HD_ITab_MechanicalNetwork_Title";
        }

        protected override void FillTab()
        {
            Thing selectedThing = this.SelThing;
            if (selectedThing == null) return;

            CompMechanicalNode node = selectedThing.TryGetComp<CompMechanicalNode>();
            if (node == null || node.Network == null)
            {
                Rect rect = UiMath.Inset(new Rect(0f, 0f, this.size.x, this.size.y), 10f);
                Widgets.Label(rect.ToGame(), "HD_ITab_MechanicalNetwork_NotConnected".Translate().Resolve());
                return;
            }

            MechanicalNetwork net = node.Network;

            Rect outRect = UiMath.Inset(new Rect(0f, 0f, this.size.x, this.size.y), 10f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, 400f);

            UnityEngine.Vector2 gameScroll = scrollPosition.ToGame();
            Widgets.BeginScrollView(outRect.ToGame(), ref gameScroll, viewRect.ToGame(), true);
            scrollPosition = gameScroll.ToNative();

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect.ToGame());

            Text.Font = GameFont.Medium;
            listing.Label("HD_ITab_MechanicalNetwork_Title".Translate().Resolve());
            Text.Font = GameFont.Small;
            listing.GapLine();

            if (net.NominalGridRPM > 0f && net.GridRPM < net.NominalGridRPM)
            {
                GUI.color = Color.yellow.ToGame();
                listing.Label("HD_ITab_MechanicalNetwork_StatusOverload".Translate().Resolve());
                GUI.color = Color.white.ToGame();
                listing.GapLine();
            }

            // Power (Torque)
            listing.Label("HD_ITab_MechanicalNetwork_PowerOutput".Translate(net.CurrentPowerOutput.ToString("F0")).Resolve());
            listing.Label("HD_ITab_MechanicalNetwork_MaxPower".Translate(net.CurrentMaximumPower.ToString("F0")).Resolve());
            listing.Label("HD_ITab_MechanicalNetwork_PowerNeeded".Translate(net.CurrentPowerNeeded.ToString("F0")).Resolve());
            
            float surplus = net.CurrentPowerOutput - net.CurrentPowerNeeded;
            GUI.color = (surplus >= 0 ? Color.green : Color.red).ToGame();
            string surplusStr = (surplus > 0 ? "+" : "") + surplus.ToString("F0");
            listing.Label("HD_ITab_MechanicalNetwork_PowerSurplus".Translate(surplusStr).Resolve());
            GUI.color = Color.white.ToGame();
            listing.Gap();

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
