using RimWorld;
using Verse;
using Rect = Waylith.Math.Rect;

namespace Helodrace;

public sealed class Dialog_ProductionPlanning : Window
{
    private readonly MapComponent_ProductionPlanning plan;
    private readonly string[] buffers = new string[6];
    public Dialog_ProductionPlanning(MapComponent_ProductionPlanning plan)
    {
        this.plan = plan; doCloseX = true; doCloseButton = true; forcePause = true; absorbInputAroundWindow = true;
    }
    public override UnityEngine.Vector2 InitialSize => new System.Numerics.Vector2(800, 540).ToGame();
    public override void DoWindowContents(UnityEngine.Rect inRect)
    {
        Rect rect = inRect.ToNative();
        Text.Font = GameFont.Medium; Widgets.Label(new Rect(rect.x, rect.y, rect.width, 36).ToGame(), "HD_ProductionPlanning_Title".Translate()); Text.Font = GameFont.Small;
        Widgets.Label(new Rect(rect.x, rect.y + 42, rect.width, 74).ToGame(), "HD_ProductionPlanning_Description".Translate());
        Widgets.CheckboxLabeled(new Rect(rect.x, rect.y + 120, rect.width, 28).ToGame(), "HD_ProductionPlanning_Enabled".Translate(), ref plan.Enabled);
        Widgets.Label(new Rect(rect.x + 320, rect.y + 160, 90, 28).ToGame(), "HD_ProductionPlanning_Target".Translate());
        Widgets.Label(new Rect(rect.x + 430, rect.y + 160, 300, 28).ToGame(), "HD_ProductionPlanning_Columns".Translate());
        for (int i = 0; i < plan.Goals.Count; i++)
        {
            var goal = plan.Goals[i]; float y = rect.y + 192 + i * 36;
            Widgets.Label(new Rect(rect.x, y, 310, 30).ToGame(), goal.Product.LabelCap);
            Widgets.TextFieldNumeric(new Rect(rect.x + 320, y, 88, 28).ToGame(), ref goal.Target, ref buffers[i], 0, 10000);
            Widgets.Label(new Rect(rect.x + 430, y, 300, 30).ToGame(), $"{plan.Stock(goal.Product)} / {plan.Pending(goal.Product)} / {goal.Completed}");
        }
        Widgets.Label(new Rect(rect.x, rect.y + 415, rect.width, 48).ToGame(), "HD_ProductionPlanning_Status".Translate());
    }
    public override void PostClose() { base.PostClose(); plan.Refresh(); }
}
