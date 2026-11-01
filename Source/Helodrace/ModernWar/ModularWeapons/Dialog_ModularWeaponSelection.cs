using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Rect = Waylith.Math.Rect;
using Vector2 = System.Numerics.Vector2;
using Color = Waylith.Math.Color;
using Verse;

namespace Helodrace.ModernWar
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

        public override UnityEngine.Vector2 InitialSize => new UnityEngine.Vector2(880f, 620f);

        public override void DoWindowContents(UnityEngine.Rect gameRect)
        {
            Rect inRect = gameRect.ToNative();
            if (bench?.BenchThing?.Spawned != true
                || bench.InstalledPartsBox == null)
            {
                Close();
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(10f, 5f, inRect.width - 20f, 34f).ToGame(),
                "HD_ModularWeapon_SelectWeaponTitle".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(12f, 41f, inRect.width - 24f, 50f).ToGame(),
                "HD_ModularWeapon_SelectWeaponDesc".Translate());

            List<Pawn> owners = bench.AvailableWeaponOwners();
            Rect outRect = new Rect(8f, 100f,
                inRect.width - 16f, inRect.height - 148f);
            Widgets.DrawMenuSection(outRect.ToGame());
            if (owners.Count == 0)
            {
                Widgets.Label(UiMath.Inset(outRect, 15f).ToGame(),
                    "HD_ModularWeaponBench_NoWeapon".Translate());
            }
            else
            {
                Rect scrollRect = UiMath.Inset(outRect, 7f);
                const float cardHeight = 124f;
                Rect viewRect = new Rect(0f, 0f,
                    scrollRect.width - 18f,
                    Mathf.Max(scrollRect.height,
                        owners.Count * (cardHeight + 7f) + 7f));
                UnityEngine.Vector2 gameScroll = scrollPosition.ToGame();
                Widgets.BeginScrollView(scrollRect.ToGame(), ref gameScroll, viewRect.ToGame());
                scrollPosition = gameScroll.ToNative();
                for (int i = 0; i < owners.Count; i++)
                {
                    Pawn owner = owners[i];
                    Thing weapon = owner.equipment.Primary;
                    Rect card = new Rect(5f,
                        5f + i * (cardHeight + 7f),
                        viewRect.width - 10f, cardHeight);
                    // Only visible rows request time-sliced icon baking.
                    if (card.yMax < scrollPosition.Y
                        || card.y > scrollPosition.Y + scrollRect.height) continue;
                    bool hovered = Mouse.IsOver(card.ToGame());
                    Widgets.DrawBoxSolidWithOutline(card.ToGame(), (
                        hovered ? new Color(0.17f, 0.22f, 0.19f)
                            : new Color(0.09f, 0.12f, 0.11f)).ToGame(), (
                        hovered ? Color.yellow
                            : new Color(0.35f, 0.46f, 0.40f)).ToGame(),
                        hovered ? 2 : 1);
                    Rect image = new Rect(card.x + 12f, card.y + 12f, 220f, 100f);
                    DrawWeaponImage(image, weapon);

                    float textX = image.xMax + 14f;
                    float textWidth = Mathf.Max(1f, card.xMax - 170f - textX);
                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(textX, card.y + 18f,
                        textWidth, 30f).ToGame(), owner.LabelShortCap);
                    Text.Font = GameFont.Small;
                    Widgets.Label(new Rect(textX, card.y + 55f,
                        textWidth, 52f).ToGame(), weapon.LabelCap);
                    GUI.color = (hovered
                        ? Color.yellow : new Color(0.73f, 0.87f, 0.77f)).ToGame();
                    Widgets.Label(new Rect(card.xMax - 155f, card.y + 49f,
                        142f, 25f).ToGame(),
                        "HD_ModularWeapon_SelectWeaponAction".Translate());
                    GUI.color = Color.white.ToGame();
                    if (Widgets.ButtonInvisible(card.ToGame()))
                    {
                        if (bench.QueueModificationJob(owner)) Close();
                        else Messages.Message(
                            "HD_ModularWeaponBench_CannotStart".Translate(),
                            bench.BenchThing,
                            MessageTypeDefOf.RejectInput, false);
                    }
                }
                Widgets.EndScrollView();
            }

            if (Widgets.ButtonText(new Rect(inRect.width - 125f,
                inRect.height - 39f, 115f, 30f).ToGame(), "CloseButton".Translate()))
                Close();
        }

        private static void DrawWeaponImage(Rect area, Thing weapon)
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
            Rect uv = pixels.VisibleUv;
            Vector2 size = new Vector2(icon.width * uv.width, icon.height * uv.height);
            size *= Mathf.Min(area.width / Mathf.Max(0.001f, size.X),
                area.height / Mathf.Max(0.001f, size.Y)) * 0.96f;
            GUI.DrawTextureWithTexCoords(new Rect(area.center - size * 0.5f, size).ToGame(), icon, uv.ToGame());
        }
    }
}
