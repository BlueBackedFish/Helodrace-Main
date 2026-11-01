// Frozen from 586d610f before owned weapon editor geometry conversion.
using Helodrace.ModernWar;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Tests.FrozenWeaponUI
{
    public sealed class Dialog_ModularWeaponSelection : Window
    {
        private readonly CompModularWeaponWorkbench bench;
        private Vector2 scrollPosition;

        public Dialog_ModularWeaponSelection(CompModularWeaponWorkbench bench)
        {
            this.bench = bench;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(880f, 620f);

        public override void DoWindowContents(Rect inRect)
        {
            if (GeometryBoundary.BenchThing(bench)?.Spawned != true
                || bench.InstalledPartsBox == null)
            {
                Close();
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(10f, 5f, inRect.width - 20f, 34f),
                "HD_ModularWeapon_SelectWeaponTitle".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(12f, 41f, inRect.width - 24f, 50f),
                "HD_ModularWeapon_SelectWeaponDesc".Translate());

            List<Pawn> owners = bench.AvailableWeaponOwners();
            Rect outRect = new Rect(8f, 100f,
                inRect.width - 16f, inRect.height - 148f);
            Widgets.DrawMenuSection(outRect);
            if (owners.Count == 0)
            {
                Widgets.Label(outRect.ContractedBy(15f),
                    "HD_ModularWeaponBench_NoWeapon".Translate());
            }
            else
            {
                Rect scrollRect = outRect.ContractedBy(7f);
                const float cardHeight = 124f;
                Rect viewRect = new Rect(0f, 0f,
                    scrollRect.width - 18f,
                    Mathf.Max(scrollRect.height,
                        owners.Count * (cardHeight + 7f) + 7f));
                Widgets.BeginScrollView(scrollRect, ref scrollPosition, viewRect);
                for (int i = 0; i < owners.Count; i++)
                {
                    Pawn owner = owners[i];
                    Thing weapon = owner.equipment.Primary;
                    Rect card = new Rect(5f,
                        5f + i * (cardHeight + 7f),
                        viewRect.width - 10f, cardHeight);
                    // Only visible rows request time-sliced icon baking.
                    if (card.yMax < scrollPosition.y
                        || card.y > scrollPosition.y + scrollRect.height) continue;
                    bool hovered = Mouse.IsOver(card);
                    Widgets.DrawBoxSolidWithOutline(card,
                        hovered ? new Color(0.17f, 0.22f, 0.19f)
                            : new Color(0.09f, 0.12f, 0.11f),
                        hovered ? Color.yellow
                            : new Color(0.35f, 0.46f, 0.40f),
                        hovered ? 2 : 1);
                    Rect image = new Rect(card.x + 12f, card.y + 12f, 220f, 100f);
                    DrawWeaponImage(image.ToNative(), weapon);

                    float textX = image.xMax + 14f;
                    float textWidth = Mathf.Max(1f, card.xMax - 170f - textX);
                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(textX, card.y + 18f,
                        textWidth, 30f), owner.LabelShortCap);
                    Text.Font = GameFont.Small;
                    Widgets.Label(new Rect(textX, card.y + 55f,
                        textWidth, 52f), weapon.LabelCap);
                    GUI.color = hovered
                        ? Color.yellow : new Color(0.73f, 0.87f, 0.77f);
                    Widgets.Label(new Rect(card.xMax - 155f, card.y + 49f,
                        142f, 25f),
                        "HD_ModularWeapon_SelectWeaponAction".Translate());
                    GUI.color = Color.white;
                    if (Widgets.ButtonInvisible(card))
                    {
                        if (bench.QueueModificationJob(owner)) Close();
                        else Messages.Message(
                            "HD_ModularWeaponBench_CannotStart".Translate(),
                            GeometryBoundary.BenchThing(bench),
                            MessageTypeDefOf.RejectInput, false);
                    }
                }
                Widgets.EndScrollView();
            }

            if (Widgets.ButtonText(new Rect(inRect.width - 125f,
                inRect.height - 39f, 115f, 30f), "CloseButton".Translate()))
                Close();
        }

        private static void DrawWeaponImage(Waylith.Math.Rect area, Thing weapon)
        {
            CompModularWeaponNode root = weapon.TryGetComp<CompModularWeaponNode>();
            Texture2D icon = ModularWeaponIconCache.Get(root);
            if (icon == null)
            {
                // Keep the existing fallback while the shared cache finishes baking.
                if (weapon.def.uiIcon != null)
                    Widgets.DrawTextureFitted(area.ToGame(), weapon.def.uiIcon, 1f);
                return;
            }

            // Cached assembly icons use a square canvas. Fit their visible pixels
            // so long guns fill the wide preview without transparent padding.
            ModularWeaponTextureData pixels = ModularWeaponTextureData.For(icon);
            if (!pixels.HasPixels) return;
            Waylith.Math.Rect uv = pixels.VisibleUv;
            System.Numerics.Vector2 size = new System.Numerics.Vector2(icon.width * uv.width, icon.height * uv.height);
            size *= Mathf.Min(area.width / Mathf.Max(0.001f, size.X),
                area.height / Mathf.Max(0.001f, size.Y)) * 0.96f;
            GUI.DrawTextureWithTexCoords(new Waylith.Math.Rect(area.center - size * 0.5f, size).ToGame(), icon, uv.ToGame());
        }
    }
}
