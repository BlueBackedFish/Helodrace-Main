using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Waylith.Math;
using Rect = Waylith.Math.Rect;
using Vector2 = System.Numerics.Vector2;

namespace Helodrace.Research
{
    // An operating guide shown once when its research project is completed.
    // label is the guide title and guideText its body; both are translated through DefInjected.
    public sealed class ResearchGuideDef : Def
    {
        public ResearchProjectDef research;
        [MustTranslate] public string guideText;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (research == null) yield return "research is not set";
            if (guideText.NullOrEmpty()) yield return "guideText is empty";
        }
    }

    public sealed class GameComponent_ResearchGuides : GameComponent
    {
        private const int CheckInterval = 250;
        private HashSet<string> shown = new HashSet<string>();
        private bool initialized;

        public GameComponent_ResearchGuides(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % CheckInterval == 0) CheckResearch();
        }

        public static GameComponent_ResearchGuides Current => Verse.Current.Game?.GetComponent<GameComponent_ResearchGuides>();
        public bool HasShown(ResearchGuideDef guide) => shown.Contains(guide.defName);

        public void CheckResearch()
        {
            // Research finished before this component first runs (scenario baseline,
            // or a save made before the guides existed) is recorded without letters.
            bool silent = !initialized;
            initialized = true;
            foreach (ResearchGuideDef guide in DefDatabase<ResearchGuideDef>.AllDefsListForReading)
            {
                if (shown.Contains(guide.defName) || !guide.research.IsFinished) continue;
                shown.Add(guide.defName);
                if (!silent) Send(guide);
            }
        }

        private static void Send(ResearchGuideDef guide)
        {
            var letter = (ChoiceLetter_ResearchGuide)LetterMaker.MakeLetter(
                "HD_ResearchGuide_LetterLabel".Translate(guide.LabelCap),
                "HD_ResearchGuide_LetterText".Translate(guide.research.LabelCap, guide.LabelCap),
                DefDatabase<LetterDef>.GetNamed("HD_ResearchGuideLetter"));
            letter.guide = guide;
            Find.LetterStack.ReceiveLetter(letter);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref shown, "shown", LookMode.Value);
            Scribe_Values.Look(ref initialized, "initialized");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) shown ??= new HashSet<string>();
        }
    }

    public sealed class ChoiceLetter_ResearchGuide : ChoiceLetter
    {
        public ResearchGuideDef guide;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (guide != null)
                {
                    yield return new DiaOption("HD_ResearchGuide_Open".Translate())
                    {
                        action = () => Find.WindowStack.Add(new Dialog_ResearchGuide(guide)),
                        resolveTree = true
                    };
                }
                yield return Option_Close;
            }
        }

        // Clicking the letter opens the guide itself; the letter stays in the archive.
        public override void OpenLetter()
        {
            if (guide == null)
            {
                base.OpenLetter();
                return;
            }
            Find.LetterStack.RemoveLetter(this);
            Find.WindowStack.Add(new Dialog_ResearchGuide(guide));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref guide, "guide");
        }
    }

    public sealed class Dialog_ResearchGuide : Window
    {
        private readonly ResearchGuideDef guide;
        private Vector2 scroll;

        public Dialog_ResearchGuide(ResearchGuideDef guide)
        {
            this.guide = guide;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override UnityEngine.Vector2 InitialSize => new Vector2(
            Mathf.Min(720f, UI.screenWidth - 60f), Mathf.Min(680f, UI.screenHeight - 60f)).ToGame();

        public override void DoWindowContents(UnityEngine.Rect gameRect)
        {
            Rect inRect = gameRect.ToNative();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, 36f).ToGame(), guide.LabelCap);
            Text.Font = GameFont.Small;
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(new Rect(inRect.x, inRect.y + 38f, inRect.width, 24f).ToGame(),
                "HD_ResearchGuide_UnlockedBy".Translate(guide.research.LabelCap));
            GUI.color = UnityEngine.Color.white;

            float top = inRect.y + 70f;
            Rect outRect = new Rect(inRect.x, top, inRect.width, inRect.yMax - top - CloseButSize.y - 12f);
            float textWidth = outRect.width - 18f;
            float textHeight = Text.CalcHeight(guide.guideText, textWidth);
            Rect view = new Rect(0f, 0f, textWidth, Mathf.Max(outRect.height, textHeight));
            UnityEngine.Vector2 gameScroll = scroll.ToGame();
            Widgets.BeginScrollView(outRect.ToGame(), ref gameScroll, view.ToGame());
            scroll = gameScroll.ToNative();
            Widgets.Label(new Rect(0f, 0f, textWidth, textHeight).ToGame(), guide.guideText);
            Widgets.EndScrollView();
        }
    }
}
