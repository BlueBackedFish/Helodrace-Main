using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using ForwardBaseService = Helodrace.HelodForwardBaseService;

namespace Helodrace
{
    public class CompProperties_TelegraphTable : CompProperties
    {
        public CompProperties_TelegraphTable()
        {
            compClass = typeof(CompTelegraphTable);
        }
    }

    public class CompTelegraphTable : ThingComp
    {
        private const string UseJobDefName = "HD_UseTelegraphTable";
        private int legacyStoredPrimaryCells;

        private CompRefuelable Refuelable => parent.TryGetComp<CompRefuelable>();

        public int StoredPrimaryCells => Mathf.FloorToInt(Refuelable?.Fuel ?? 0f);
        public bool HasPrimaryCell => Refuelable?.Fuel >= 1f;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref legacyStoredPrimaryCells, "storedPrimaryCells", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && legacyStoredPrimaryCells > 0)
            {
                Refuelable?.Refuel(legacyStoredPrimaryCells);
                legacyStoredPrimaryCells = 0;
            }
        }

        public bool ConsumePrimaryCell()
        {
            CompRefuelable refuelable = Refuelable;
            if (refuelable?.Fuel < 1f)
            {
                return false;
            }

            refuelable.ConsumeFuel(1f);
            return true;
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.CompFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            if (selPawn == null || selPawn.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            string label = "HD_TelegraphTable_Use_Label".Translate(parent.LabelShort);
            if (!selPawn.CanReach(parent, PathEndMode.InteractionCell, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
                yield break;
            }

            if (!selPawn.CanReserve(parent))
            {
                yield return new FloatMenuOption(label + ": " + "Reserved".Translate(), null);
                yield break;
            }

            yield return new FloatMenuOption(label, delegate
            {
                JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(UseJobDefName);
                if (jobDef == null)
                {
                    Log.ErrorOnce("Helodrace: HD_UseTelegraphTable JobDef is missing.", 72851041);
                    return;
                }

                Job job = JobMaker.MakeJob(jobDef, parent);
                selPawn.jobs.TryTakeOrderedJob(job);
            });
        }
    }

    public class JobDriver_UseTelegraphTable : JobDriver
    {
        private const TargetIndex TableInd = TargetIndex.A;

        private Thing TelegraphTable => job.GetTarget(TableInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TelegraphTable, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TableInd);
            this.FailOnBurningImmobile(TableInd);

            yield return Toils_Goto.GotoThing(TableInd, PathEndMode.InteractionCell);

            yield return new Toil
            {
                initAction = delegate
                {
                    pawn.pather.StopDead();
                    pawn.rotationTracker.FaceTarget(TelegraphTable);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Find.WindowStack.Add(new Dialog_TelegraphTable(TelegraphTable, pawn));
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    public class Dialog_TelegraphTable : Window
    {
        private const float MaxForwardBaseDistance = 60f;
        private const float GoldStandardSthalerSilverValue = HelodForwardBaseServiceUtility.GoldStandardSthalerSilverValue;
        private static readonly ForwardBaseService[] InfantryServices = { ForwardBaseService.InfantryMortarSupport, ForwardBaseService.InfantrySniperSupport, ForwardBaseService.InfantryDeployment };
        private static readonly ForwardBaseService[] ArtilleryServices = { ForwardBaseService.Artillery105mmSupport, ForwardBaseService.Artillery155mmSupport, ForwardBaseService.W48Support };
        private static readonly ForwardBaseService[] AirForceServices = { ForwardBaseService.CloseAirSupport };
        private static readonly ForwardBaseService[] LogisticsServices = { ForwardBaseService.LogisticsFreshFood, ForwardBaseService.LogisticsPreservedFood, ForwardBaseService.LogisticsMedicalSupplies, ForwardBaseService.LogisticsWeapons };
        private static readonly ForwardBaseService[] AllForwardBaseServices = { ForwardBaseService.InfantryMortarSupport, ForwardBaseService.InfantrySniperSupport, ForwardBaseService.InfantryDeployment, ForwardBaseService.Artillery105mmSupport, ForwardBaseService.Artillery155mmSupport, ForwardBaseService.W48Support, ForwardBaseService.LogisticsFreshFood, ForwardBaseService.LogisticsPreservedFood, ForwardBaseService.LogisticsMedicalSupplies, ForwardBaseService.LogisticsWeapons, ForwardBaseService.CloseAirSupport };

        private readonly Thing telegraphTable;
        private readonly Pawn operatorPawn;
        private TelegraphTab selectedTab;
        private MilitaryActivity selectedMilitaryActivity;
        private string selectedMilitaryFactionDefName;
        private ForwardBaseKind selectedForwardBaseKind;
        private ContractCostKind selectedContractCostKind;
        private ContractDuration selectedContractDuration = ContractDuration.Days30;
        private int selectedForwardBaseTile = -1;
        private bool includeInfantryMortarSupport;
        private bool includeInfantrySniperSupport;
        private bool includeInfantryDeployment;
        private bool includeLogisticsFreshFood;
        private bool includeLogisticsPreservedFood;
        private bool includeLogisticsMedicalSupplies;
        private bool includeLogisticsWeapons;
        private bool includeCloseAirSupport;
        private bool includeArtillery105mmSupport;
        private bool includeArtillery155mmSupport;
        private bool includeW48Support;
        private readonly Dictionary<ForwardBaseService, int> ffpServiceUnitCounts = new Dictionary<ForwardBaseService, int>();
        private readonly Dictionary<ForwardBaseService, string> ffpServiceUnitBuffers = new Dictionary<ForwardBaseService, string>();
        private MarketSubTab selectedMarketSubTab;
        private int selectedMarketIndex;
        private int tradeCount = 1;
        private string tradeCountBuffer = "1";
        private Vector2 marketLogScroll;
        private readonly ContractDocumentSurface forwardBaseDocumentSurface = new ContractDocumentSurface();
        private Vector2 forwardBaseContractsScroll;
        private Vector2 infantryServiceScroll;
        private Vector2 artilleryServiceScroll;
        private Vector2 logisticsServiceScroll;
        private Vector2 airForceServiceScroll;

        private static readonly Vector2 TelegraphWindowSize = new Vector2(960f, 760f);
        private static readonly Vector2 ContractWindowSize = new Vector2(820f, 1080f);
        private static readonly Vector2 ContractViewerWindowSize = new Vector2(1020f, 1080f);
        private const float ContractEntranceDuration = 0.6f;
        private bool contractEntranceAnimating;
        private float contractEntranceStartedAt;
        private Vector2 contractEntranceStartPosition;
        private Vector2 contractEntranceTargetPosition;

        public override Vector2 InitialSize => selectedMilitaryActivity == MilitaryActivity.ForwardBaseContract
            ? ContractWindowSize
            : TelegraphWindowSize;

        public Dialog_TelegraphTable(Thing telegraphTable, Pawn operatorPawn)
        {
            this.telegraphTable = telegraphTable;
            this.operatorPawn = operatorPawn;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        private void StartContractEntranceAnimation()
        {
            windowRect.size = ContractWindowSize;
            contractEntranceTargetPosition = new Vector2(
                Mathf.Max(0f, (UI.screenWidth - ContractWindowSize.x) * 0.5f),
                Mathf.Max(0f, (UI.screenHeight - ContractWindowSize.y) * 0.5f));
            contractEntranceStartPosition = new Vector2(contractEntranceTargetPosition.x, -ContractWindowSize.y - 24f);
            windowRect.position = contractEntranceStartPosition;
            contractEntranceStartedAt = Time.realtimeSinceStartup;
            contractEntranceAnimating = true;
        }

        private void UpdateContractEntranceAnimation()
        {
            if (!contractEntranceAnimating)
            {
                return;
            }

            float progress = Mathf.Clamp01((Time.realtimeSinceStartup - contractEntranceStartedAt) / ContractEntranceDuration);
            float easedProgress = 1f - Mathf.Pow(1f - progress, 3f);
            windowRect.position = Vector2.Lerp(contractEntranceStartPosition, contractEntranceTargetPosition, easedProgress);
            if (progress >= 1f)
            {
                windowRect.position = contractEntranceTargetPosition;
                contractEntranceAnimating = false;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (selectedTab == TelegraphTab.Military && selectedMilitaryActivity == MilitaryActivity.ForwardBaseContract)
            {
                UpdateContractEntranceAnimation();
                closeOnClickedOutside = false;
                doWindowBackground = false;
                doCloseX = false;
                draggable = true;
                DrawA4StandaloneForwardBaseContract(inRect);
                return;
            }

            closeOnClickedOutside = true;
            doWindowBackground = true;
            doCloseX = true;
            draggable = true;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "HD_TelegraphTable_Window_Title".Translate());
            Text.Font = GameFont.Small;

            Rect statusRect = new Rect(inRect.x, inRect.y + 46f, inRect.width, 86f);
            Widgets.DrawMenuSection(statusRect);
            Rect statusInner = statusRect.ContractedBy(12f);
            CompTelegraphTable telegraphComp = telegraphTable.TryGetComp<CompTelegraphTable>();
            Widgets.Label(new Rect(statusInner.x, statusInner.y, statusInner.width, 24f), "HD_TelegraphTable_Station".Translate(telegraphTable?.LabelCap ?? "Unknown".Translate()));
            Widgets.Label(new Rect(statusInner.x, statusInner.y + 26f, statusInner.width, 24f), "HD_TelegraphTable_Operator".Translate(operatorPawn?.LabelShortCap ?? "Unknown".Translate()));
            Widgets.Label(new Rect(statusInner.x, statusInner.y + 52f, statusInner.width, 24f), "HD_TelegraphTable_Status_Ready_WithCells".Translate(telegraphComp?.StoredPrimaryCells ?? 0));

            bool marketUnlocked = FinancialNetworkFinished();
            Rect tabRect = new Rect(inRect.x, statusRect.yMax + 10f, inRect.width, 32f);
            DrawTabs(tabRect, marketUnlocked);

            Rect bodyRect = new Rect(inRect.x, tabRect.yMax + 8f, inRect.width, inRect.height - statusRect.height - tabRect.height - 132f);
            Widgets.DrawMenuSection(bodyRect);
            Rect bodyInner = bodyRect.ContractedBy(12f);

            if (selectedTab == TelegraphTab.Military)
            {
                DrawMilitaryTab(bodyInner);
            }
            else if (selectedTab == TelegraphTab.Market && marketUnlocked)
            {
                DrawMarketTab(bodyInner);
            }
            else
            {
                DrawOrdersTab(bodyInner);
            }

            Rect closeRect = new Rect(inRect.xMax - 120f, inRect.yMax - 38f, 120f, 38f);
            if (Widgets.ButtonText(closeRect, "CloseButton".Translate()))
            {
                Close();
            }
        }

        private void DrawTabs(Rect tabRect, bool marketUnlocked)
        {
            float tabWidth = marketUnlocked ? 120f : 140f;
            Rect ordersRect = new Rect(tabRect.x, tabRect.y, tabWidth, tabRect.height);
            if (Widgets.ButtonText(ordersRect, "HD_TelegraphTable_Tab_Orders".Translate(), selectedTab == TelegraphTab.Orders))
            {
                selectedTab = TelegraphTab.Orders;
            }

            Rect militaryRect = new Rect(ordersRect.xMax + 8f, tabRect.y, tabWidth, tabRect.height);
            if (Widgets.ButtonText(militaryRect, "HD_TelegraphTable_Tab_Military".Translate(), selectedTab == TelegraphTab.Military))
            {
                selectedTab = TelegraphTab.Military;
            }

            if (marketUnlocked)
            {
                Rect marketRect = new Rect(militaryRect.xMax + 8f, tabRect.y, tabWidth, tabRect.height);
                if (Widgets.ButtonText(marketRect, "HD_TelegraphTable_Tab_Market".Translate(), selectedTab == TelegraphTab.Market))
                {
                    selectedTab = TelegraphTab.Market;
                }
            }
        }

        private void DrawOrdersTab(Rect bodyInner)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(bodyInner.x, bodyInner.y, bodyInner.width, 30f), "HD_TelegraphTable_Orders_Title".Translate());
            Text.Font = GameFont.Small;

            Rect descriptionRect = new Rect(bodyInner.x, bodyInner.y + 38f, bodyInner.width, 72f);
            Widgets.Label(descriptionRect, "HD_TelegraphTable_Placeholder".Translate());

            float buttonWidth = (bodyInner.width - 20f) / 3f;
            float buttonY = bodyInner.yMax - 48f;
            DrawDisabledButton(new Rect(bodyInner.x, buttonY, buttonWidth, 40f), "HD_TelegraphTable_Button_Contracts".Translate());
            DrawDisabledButton(new Rect(bodyInner.x + buttonWidth + 10f, buttonY, buttonWidth, 40f), "HD_TelegraphTable_Button_Bonds".Translate());
            DrawDisabledButton(new Rect(bodyInner.x + (buttonWidth + 10f) * 2f, buttonY, buttonWidth, 40f), "HD_TelegraphTable_Button_Exchange".Translate());
        }

        private void DrawMilitaryTab(Rect bodyInner)
        {
            if (selectedMilitaryActivity == MilitaryActivity.ForwardBaseContract)
            {
                DrawA4StandaloneForwardBaseContract(bodyInner);
                return;
            }

            if (selectedMilitaryActivity == MilitaryActivity.ForwardBaseContracts)
            {
                DrawForwardBaseContracts(bodyInner);
                return;
            }

            DrawMilitaryActivityList(bodyInner);
        }

        private void DrawMilitaryActivityList(Rect bodyInner)
        {
            EnsureSelectedMilitaryFaction();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(bodyInner.x, bodyInner.y, bodyInner.width, 30f), "HD_TelegraphTable_Military_Title".Translate());
            Text.Font = GameFont.Small;

            Rect factionLabelRect = new Rect(bodyInner.x, bodyInner.y + 38f, bodyInner.width, 24f);
            Widgets.Label(factionLabelRect, "HD_TelegraphTable_Military_Faction".Translate());
            DrawMilitaryFactionSelector(new Rect(bodyInner.x, bodyInner.y + 66f, bodyInner.width, 34f));

            Rect descriptionRect = new Rect(bodyInner.x, bodyInner.y + 110f, bodyInner.width, 52f);
            Widgets.Label(descriptionRect, "HD_TelegraphTable_Military_Placeholder".Translate());

            float buttonWidth = (bodyInner.width - 20f) / 3f;
            float buttonY = bodyInner.y + 176f;
            if (Widgets.ButtonText(new Rect(bodyInner.x, buttonY, buttonWidth, 42f), "HD_TelegraphTable_Military_RequestSupport".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ForwardBaseSupport(SelectedMilitaryFaction(), this, telegraphTable?.TryGetComp<CompTelegraphTable>()));
            }
            if (Widgets.ButtonText(new Rect(bodyInner.x + buttonWidth + 10f, buttonY, buttonWidth, 42f), "HD_TelegraphTable_Military_ForwardBase".Translate(), selectedMilitaryActivity == MilitaryActivity.ForwardBaseContract))
            {
                selectedMilitaryActivity = MilitaryActivity.ForwardBaseContract;
                windowRect.size = ContractWindowSize;
                doWindowBackground = false;
                doCloseX = false;
                closeOnClickedOutside = false;
                StartContractEntranceAnimation();
            }
            if (Widgets.ButtonText(new Rect(bodyInner.x + (buttonWidth + 10f) * 2f, buttonY, buttonWidth, 42f), "HD_TelegraphTable_Military_ForwardBaseContracts".Translate(), selectedMilitaryActivity == MilitaryActivity.ForwardBaseContracts))
            {
                selectedMilitaryActivity = MilitaryActivity.ForwardBaseContracts;
            }

            Rect emptyRect = new Rect(bodyInner.x, buttonY + 58f, bodyInner.width, bodyInner.yMax - buttonY - 58f);
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(emptyRect, "HD_TelegraphTable_Military_SelectActivity".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawMilitaryFactionSelector(Rect rect)
        {
            List<Faction> factions = MilitaryCooperationFactions();
            if (factions.Count == 0)
            {
                DrawDisabledButton(rect, "HD_TelegraphTable_Military_FactionNone".Translate());
                return;
            }

            float gap = 8f;
            float width = (rect.width - gap * (factions.Count - 1)) / factions.Count;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction faction = factions[i];
                Rect buttonRect = new Rect(rect.x + (width + gap) * i, rect.y, width, rect.height);
                bool selected = SelectedMilitaryFaction() == faction;
                if (Widgets.ButtonText(buttonRect, MilitaryFactionLabel(faction), selected))
                {
                    selectedMilitaryFactionDefName = faction.def.defName;
                }
            }
        }

        private void EnsureSelectedMilitaryFaction()
        {
            if (SelectedMilitaryFaction() != null)
            {
                return;
            }

            List<Faction> factions = MilitaryCooperationFactions();
            if (factions.Count > 0)
            {
                selectedMilitaryFactionDefName = factions[0].def.defName;
            }
        }

        private Faction SelectedMilitaryFaction()
        {
            if (!selectedMilitaryFactionDefName.NullOrEmpty())
            {
                FactionDef selectedDef = DefDatabase<FactionDef>.GetNamedSilentFail(selectedMilitaryFactionDefName);
                Faction selected = selectedDef == null ? null : Find.FactionManager.FirstFactionOfDef(selectedDef);
                if (selected != null)
                {
                    return selected;
                }
            }

            List<Faction> factions = MilitaryCooperationFactions();
            return factions.Count > 0 ? factions[0] : null;
        }

        private string SelectedMilitaryFactionLabel()
        {
            return MilitaryFactionLabel(SelectedMilitaryFaction());
        }

        private static string MilitaryFactionLabel(Faction faction)
        {
            return faction == null ? "HD_TelegraphTable_Military_FactionNone".Translate().ToString() : faction.Name;
        }

        private static List<Faction> MilitaryCooperationFactions()
        {
            List<Faction> factions = new List<Faction>();
            if (Find.FactionManager?.AllFactionsListForReading != null)
            {
                List<Faction> allFactions = Find.FactionManager.AllFactionsListForReading;
                for (int i = 0; i < allFactions.Count; i++)
                {
                    Faction faction = allFactions[i];
                    if (IsMilitaryCooperationFaction(faction))
                    {
                        factions.Add(faction);
                    }
                }
            }

            if (factions.Count == 0)
            {
                Faction fallback = FactionOfDef("HD_HelodCivilLowFaction");
                if (fallback != null)
                {
                    factions.Add(fallback);
                }
            }

            return factions;
        }

        private static bool IsMilitaryCooperationFaction(Faction faction)
        {
            return faction?.def?.defName != null
                && faction != Faction.OfPlayer
                && !faction.defeated
                && faction.def.defName.StartsWith("HD_Helod");
        }

        private void DrawForwardBaseContracts(Rect rect)
        {
            Rect backRect = new Rect(rect.x, rect.y, 96f, 32f);
            if (Widgets.ButtonText(backRect, "Back".Translate()))
            {
                selectedMilitaryActivity = MilitaryActivity.None;
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 110f, rect.y + 3f, rect.width - 110f, 30f), "HD_TelegraphTable_ForwardBaseContracts_Title".Translate());
            Text.Font = GameFont.Small;

            Rect listRect = new Rect(rect.x, rect.y + 44f, rect.width, rect.height - 44f);
            Widgets.DrawMenuSection(listRect);
            Rect inner = listRect.ContractedBy(10f);

            List<WorldObject> contracts = ForwardBaseContractObjects();
            if (contracts.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(inner, "HD_TelegraphTable_ForwardBaseContracts_None".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            const float rowHeight = 76f;
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, contracts.Count * rowHeight);
            Widgets.BeginScrollView(inner, ref forwardBaseContractsScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < contracts.Count; i++)
            {
                WorldObject contract = contracts[i];
                Rect row = new Rect(0f, y, viewRect.width, rowHeight - 8f);
                Widgets.DrawBox(row);
                Widgets.DrawHighlightIfMouseover(row);

                string status = ForwardBaseContractStatus(contract);
                string rowText = string.Format("HD_TelegraphTable_ForwardBaseContracts_Row".Translate().ToString(), status, contract.Tile);
                Widgets.Label(new Rect(row.x + 8f, row.y + 6f, row.width - 220f, 22f), contract.LabelCap);
                Widgets.Label(new Rect(row.x + 8f, row.y + 28f, row.width - 220f, 22f), rowText);

                Rect infoRect = new Rect(row.xMax - 208f, row.y + 17f, 96f, 34f);
                if (Widgets.ButtonText(infoRect, "HD_TelegraphTable_ForwardBaseContracts_Info".Translate()))
                {
                    Find.WindowStack.Add(new ForwardBaseContractViewer(contract, ForwardBaseContractInfo(contract)));
                }

                Rect jumpRect = new Rect(row.xMax - 104f, row.y + 17f, 96f, 34f);
                if (Widgets.ButtonText(jumpRect, "HD_TelegraphTable_ForwardBaseContracts_Jump".Translate()))
                {
                    CameraJumper.TryJump(new GlobalTargetInfo(contract.Tile));
                }

                y += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawA4StandaloneForwardBaseContract(Rect inRect)
        {
            EnsureSelectedMilitaryFaction();
            Faction selectedFaction = SelectedMilitaryFaction();
            float credit = MilitaryCredit(telegraphTable.Map, selectedFaction);
            bool hasSelectedTile = selectedForwardBaseTile >= 0;
            float distance = hasSelectedTile ? NearestMilitarySettlementDistance(selectedForwardBaseTile, selectedFaction) : MaxForwardBaseDistance;
            EnsureForwardBaseSelectionCredit(credit);

            Rect paper = forwardBaseDocumentSurface.Begin(inRect);
            Widgets.DrawBoxSolid(paper, Color.white);
            Widgets.DrawBox(paper);
            Rect content = paper.ContractedBy(22f);
            float y = content.y;
            GUI.color = Color.black;
            Text.Anchor = TextAnchor.UpperLeft;

            Texture2D logo = SelectedProviderLogo();
            if (logo != null)
            {
                GUI.color = Color.white;
                Widgets.DrawTextureFitted(new Rect(content.x, y, 88f, 88f), logo, 0.9f);
                GUI.color = Color.black;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(content.x + 106f, y + 3f, content.width - 260f, 30f), ProviderCompanyName(selectedFaction));
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.12f, 0.12f, 0.12f);
            Widgets.Label(new Rect(content.x + 106f, y + 38f, content.width - 260f, 22f), "EXPEDITIONARY LOGISTICS DIVISION");
            Widgets.Label(new Rect(content.x + 106f, y + 61f, content.width - 260f, 22f), "Official provider document");
            Widgets.Label(new Rect(content.xMax - 150f, y + 8f, 150f, 22f), "FORM FB-01");
            Widgets.Label(new Rect(content.xMax - 150f, y + 34f, 150f, 22f), "DRAFT / UNEXECUTED");
            GUI.color = Color.black;
            y += 100f;
            Widgets.DrawLineHorizontal(content.x, y, content.width);
            y += 18f;

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(content.x, y, content.width, 30f), "FORWARD BASE SERVICE AGREEMENT");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            y += 36f;
            y += DrawContractParagraph(new Rect(content.x, y, content.width, 80f), "This agreement records the terms under which the provider will establish and operate a forward base for the contracting party. Click any field to amend the draft.") + 12f;

            y = DrawContractArticleHeader(content, y, "1. PARTIES AND DESTINATION");
            string providerValue = ProviderCompanyName(selectedFaction) + "  /  " + MilitaryFactionLabel(selectedFaction);
            float providerHeight = ContractEditableRowHeight(providerValue, content.width);
            Rect providerRect = new Rect(content.x, y, content.width, providerHeight);
            if (DrawContractEditableRow(providerRect, "PROVIDER", providerValue))
            {
                OpenProviderEditor();
            }
            y += providerHeight + 8f;
            string locationValue = hasSelectedTile
                ? "Tile " + selectedForwardBaseTile + "  /  " + distance.ToString("F0") + " tiles from nearest settlement"
                : "Select a world tile";
            float locationHeight = ContractEditableRowHeight(locationValue, content.width);
            Rect locationRect = new Rect(content.x, y, content.width, locationHeight);
            if (DrawContractEditableRow(locationRect, "DESTINATION", locationValue))
            {
                forwardBaseDocumentSurface.End();
                BeginForwardBaseTileTargeting();
                return;
            }
            y += locationHeight + 10f;

            y = DrawContractArticleHeader(content, y, "2. SERVICE TERM");
            Rect baseRect = new Rect(content.x, y, content.width * 0.5f - 9f, 32f);
            if (DrawContractEditableRow(baseRect, "BASE TYPE", ForwardBaseKindLabel(selectedForwardBaseKind)))
            {
                OpenBaseKindEditor(credit);
            }
            Rect costRect = new Rect(content.x + content.width * 0.5f + 9f, y, content.width * 0.5f - 9f, 32f);
            if (DrawContractEditableRow(costRect, "COST MODEL", ContractCostKindLabel(selectedContractCostKind)))
            {
                OpenCostKindEditor(credit);
            }
            y += 36f;
            Rect durationRect = new Rect(content.x, y, content.width, 32f);
            if (DrawContractEditableRow(durationRect, "TERM", ContractDurationLabel(selectedContractDuration)))
            {
                OpenDurationEditor();
            }
            y += 42f;

            y = DrawContractArticleHeader(content, y, "3. SCHEDULE A - SELECTED SERVICES");
            Rect servicesRect = new Rect(content.x, y, content.width, 250f);
            Widgets.DrawBox(servicesRect);
            Widgets.DrawHighlightIfMouseover(servicesRect);
            Widgets.Label(new Rect(servicesRect.x + 12f, servicesRect.y + 10f, servicesRect.width - 24f, 22f), "The following services are incorporated into this agreement:");
            DrawContractServiceText(servicesRect.ContractedBy(12f));
            if (Widgets.ButtonInvisible(servicesRect))
            {
                GUI.color = Color.white;
                Find.WindowStack.Add(new ForwardBaseServiceEditor(this));
                GUI.color = Color.black;
            }
            y += 266f;

            y = DrawContractArticleHeader(content, y, "4. COMMERCIAL TERMS");
            Rect creditRect = new Rect(content.x, y, content.width, 26f);
            Widgets.Label(creditRect, "CREDIT AVAILABLE     " + credit.ToString("F0"));
            y += 24f;
            string total = hasSelectedTile
                ? FormatContractValue(EstimateForwardBaseCost(credit, distance))
                : "HD_TelegraphTable_ForwardBase_CostPending".Translate().ToString();
            Rect amountRect = new Rect(content.x, y, content.width, 32f);
            if (DrawContractEditableRow(amountRect, "ESTIMATED VALUE", ContractAmountLabel(selectedContractCostKind) + "  /  " + total))
            {
                OpenCostKindEditor(credit);
            }
            y += 36f;
            y += DrawContractParagraph(new Rect(content.x, y, content.width, 80f), "FFP service quantities are stocked per 30-day billing period. Cost reimbursement services remain available without a contracted credit or call-count ceiling.") + 12f;

            y = DrawContractArticleHeader(content, y, "5. EXECUTION");
            y += DrawContractParagraph(new Rect(content.x, y, content.width, 80f), "This draft becomes effective when the contracting party authorizes construction and supplies the required primary cell.") + 10f;
            float signatureGap = 24f;
            float signatureWidth = (content.width - signatureGap) * 0.5f;
            Widgets.DrawLineHorizontal(content.x, y, signatureWidth);
            Widgets.DrawLineHorizontal(content.x + signatureWidth + signatureGap, y, signatureWidth);
            Widgets.Label(new Rect(content.x, y + 8f, signatureWidth, 22f), "AUTHORIZED PROVIDER");
            Widgets.Label(new Rect(content.x + signatureWidth + signatureGap, y + 8f, signatureWidth, 22f), "CONTRACTING PARTY");
            Rect executeRect = new Rect(content.x + signatureWidth + signatureGap, y + 32f, signatureWidth, 36f);
            GUI.color = Color.white;
            if (Widgets.ButtonText(executeRect, "CONCLUDE CONTRACT"))
            {
                TryStartForwardBaseConstructionContract(credit, distance);
            }
            GUI.color = Color.black;
            forwardBaseDocumentSurface.End();

            Rect closeRect = new Rect(inRect.xMax - 34f, inRect.y + 2f, 30f, 30f);
            Widgets.DrawHighlightIfMouseover(closeRect);
            GUI.color = Color.black;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(closeRect, "×");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (Widgets.ButtonInvisible(closeRect))
            {
                Close();
            }
            GUI.color = Color.white;
        }

        private static float DrawContractArticleHeader(Rect content, float y, string title)
        {
            GUI.color = new Color(0.1f, 0.1f, 0.1f);
            Widgets.Label(new Rect(content.x, y, content.width, 22f), title);
            GUI.color = Color.black;
            Widgets.DrawLineHorizontal(content.x, y + 24f, content.width);
            return y + 28f;
        }

        private void DrawStandaloneForwardBaseContract(Rect inRect)
        {
            EnsureSelectedMilitaryFaction();
            Faction selectedFaction = SelectedMilitaryFaction();
            float credit = MilitaryCredit(telegraphTable.Map, selectedFaction);
            bool hasSelectedTile = selectedForwardBaseTile >= 0;
            float distance = hasSelectedTile ? NearestMilitarySettlementDistance(selectedForwardBaseTile, selectedFaction) : MaxForwardBaseDistance;
            EnsureForwardBaseSelectionCredit(credit);

            Widgets.DrawBoxSolid(inRect, Color.white);
            Widgets.DrawBox(inRect);
            Rect paper = inRect.ContractedBy(28f);
            float y = paper.y;

            Texture2D logo = SelectedProviderLogo();
            if (logo != null)
            {
                GUI.color = Color.white;
                Widgets.DrawTextureFitted(new Rect(paper.x, y, 72f, 72f), logo, 0.9f);
                GUI.color = Color.black;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(paper.x + 88f, y + 2f, paper.width - 90f, 30f), ProviderCompanyName(selectedFaction));
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.12f, 0.12f, 0.12f);
            Widgets.Label(new Rect(paper.x + 88f, y + 36f, paper.width - 90f, 24f), "FORWARD BASE SERVICE AGREEMENT");
            Widgets.Label(new Rect(paper.x + 88f, y + 58f, paper.width - 90f, 20f), "Provider-issued contract draft");
            GUI.color = Color.white;
            y += 88f;
            Widgets.DrawLineHorizontal(paper.x, y, paper.width);
            y += 16f;

            Rect providerRect = new Rect(paper.x, y, paper.width, 34f);
            if (DrawContractEditableRow(providerRect, "PROVIDER", ProviderCompanyName(selectedFaction) + "  /  " + MilitaryFactionLabel(selectedFaction)))
            {
                OpenProviderEditor();
            }
            y += 40f;

            Rect locationRect = new Rect(paper.x, y, paper.width, 34f);
            if (DrawContractEditableRow(locationRect, "LOCATION", hasSelectedTile
                ? "Tile " + selectedForwardBaseTile + "  /  " + distance.ToString("F0") + " tiles from nearest settlement"
                : "Select a world tile"))
            {
                BeginForwardBaseTileTargeting();
                return;
            }
            y += 40f;

            float columnGap = 18f;
            float columnWidth = (paper.width - columnGap) / 2f;
            Rect baseRect = new Rect(paper.x, y, columnWidth, 34f);
            if (DrawContractEditableRow(baseRect, "BASE TYPE", ForwardBaseKindLabel(selectedForwardBaseKind)))
            {
                OpenBaseKindEditor(credit);
            }
            Rect costRect = new Rect(baseRect.xMax + columnGap, y, columnWidth, 34f);
            if (DrawContractEditableRow(costRect, "COST MODEL", ContractCostKindLabel(selectedContractCostKind)))
            {
                OpenCostKindEditor(credit);
            }
            y += 40f;

            Rect durationRect = new Rect(paper.x, y, paper.width, 34f);
            if (DrawContractEditableRow(durationRect, "TERM", ContractDurationLabel(selectedContractDuration)))
            {
                OpenDurationEditor();
            }
            y += 48f;

            Rect servicesRect = new Rect(paper.x, y, paper.width, 176f);
            Widgets.DrawBox(servicesRect);
            Widgets.DrawHighlightIfMouseover(servicesRect);
            Widgets.Label(new Rect(servicesRect.x + 12f, servicesRect.y + 8f, servicesRect.width - 24f, 22f), "SELECTED SERVICES  /  click to edit");
            DrawContractServiceText(servicesRect.ContractedBy(12f));
            if (Widgets.ButtonInvisible(servicesRect))
            {
                GUI.color = Color.white;
                Find.WindowStack.Add(new ForwardBaseServiceEditor(this));
                GUI.color = Color.black;
            }
            y += 188f;

            Rect creditRect = new Rect(paper.x, y, paper.width, 26f);
            Widgets.Label(creditRect, "CREDIT AVAILABLE     " + credit.ToString("F0"));
            y += 28f;

            string total = hasSelectedTile
                ? FormatContractValue(EstimateForwardBaseCost(credit, distance))
                : "HD_TelegraphTable_ForwardBase_CostPending".Translate().ToString();
            Rect amountRect = new Rect(paper.x, y, paper.width, 32f);
            if (DrawContractEditableRow(amountRect, "ESTIMATED CONTRACT VALUE", ContractAmountLabel(selectedContractCostKind) + "  /  " + total))
            {
                OpenCostKindEditor(credit);
            }
            y += 48f;

            Rect draftRect = new Rect(paper.x, paper.yMax - 42f, paper.width, 38f);
            if (Widgets.ButtonText(draftRect, "SIGN / DRAFT CONTRACT"))
            {
                TryStartForwardBaseConstructionContract(credit, distance);
            }
        }

        private bool DrawContractEditableRow(Rect rect, string caption, string value)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            GUI.color = new Color(0.1f, 0.1f, 0.1f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, 154f, rect.height - 12f), caption);
            GUI.color = Color.black;
            Widgets.Label(new Rect(rect.x + 164f, rect.y + 6f, rect.width - 172f, rect.height - 12f), value);
            GUI.color = Color.black;
            return Widgets.ButtonInvisible(rect);
        }

        private static float ContractEditableRowHeight(string value, float width)
        {
            return Mathf.Max(34f, Text.CalcHeight(value, width - 172f) + 12f);
        }

        private static float DrawContractParagraph(Rect rect, string text)
        {
            float height = Mathf.Max(22f, Text.CalcHeight(text, rect.width));
            GUI.color = Color.black;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, height), text);
            return height;
        }

        private void DrawContractServiceText(Rect rect)
        {
            float columnGap = 18f;
            float columnWidth = (rect.width - columnGap) * 0.5f;
            Rect leftColumn = new Rect(rect.x, rect.y, columnWidth, rect.height);
            Rect rightColumn = new Rect(rect.x + columnWidth + columnGap, rect.y, columnWidth, rect.height);
            float leftY = leftColumn.y + 26f;
            float rightY = rightColumn.y + 26f;
            DrawContractServiceGroup(leftColumn, ref leftY, ForwardBaseServiceType.Artillery, ArtilleryServices);
            DrawContractServiceGroup(leftColumn, ref leftY, ForwardBaseServiceType.Infantry, InfantryServices);
            DrawContractServiceGroup(rightColumn, ref rightY, ForwardBaseServiceType.AirForce, AirForceServices);
            DrawContractServiceGroup(rightColumn, ref rightY, ForwardBaseServiceType.Logistics, LogisticsServices);
            if (leftY <= leftColumn.y + 26f && rightY <= rightColumn.y + 26f)
            {
                GUI.color = new Color(0.16f, 0.16f, 0.16f);
                Widgets.Label(new Rect(rect.x, rect.y + 26f, rect.width, 22f), "- " + "HD_TelegraphTable_ForwardBase_Service_None".Translate());
                GUI.color = Color.black;
            }
        }

        private void DrawContractServiceGroup(Rect rect, ref float y, ForwardBaseServiceType type, ForwardBaseService[] services)
        {
            bool hasService = false;
            for (int i = 0; i < services.Length; i++)
            {
                if (IsServiceSelected(services[i]))
                {
                    hasService = true;
                    break;
                }
            }

            if (!hasService)
            {
                return;
            }

            GUI.color = new Color(0.12f, 0.12f, 0.12f);
            Widgets.Label(new Rect(rect.x, y, rect.width, 20f), ForwardBaseServiceTypeLabel(type).ToUpperInvariant());
            GUI.color = Color.black;
            y += 20f;
            for (int i = 0; i < services.Length; i++)
            {
                ForwardBaseService service = services[i];
                if (!IsServiceSelected(service))
                {
                    continue;
                }

                string line = "- " + ForwardBaseServiceLabel(service);
                if (selectedContractCostKind == ContractCostKind.FFP)
                {
                    line += "  /  " + FfpServiceUnitCount(service).ToString() + " per 30 days";
                }
                float lineHeight = Mathf.Max(20f, Text.CalcHeight(line, rect.width - 12f));
                Widgets.Label(new Rect(rect.x + 12f, y, rect.width - 12f, lineHeight), line);
                y += lineHeight;
            }
        }

        private static string ProviderCompanyName(Faction faction)
        {
            return IsHighProvider(faction) ? "Vanguard Expeditionary Group" : "Pioneer Overseas Service Company";
        }

        private static bool IsHighProvider(Faction faction)
        {
            return faction?.def?.defName != null && faction.def.defName.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Texture2D SelectedProviderLogo()
        {
            return ContentFinder<Texture2D>.Get(IsHighProvider(SelectedMilitaryFaction()) ? "Icons/HD_VEG" : "Icons/HD_POSC", false);
        }

        private void OpenProviderEditor()
        {
            List<Faction> factions = MilitaryCooperationFactions();
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction faction = factions[i];
                options.Add(new FloatMenuOption(ProviderCompanyName(faction) + "  /  " + MilitaryFactionLabel(faction), delegate
                {
                    selectedMilitaryFactionDefName = faction.def.defName;
                }));
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("HD_TelegraphTable_Military_FactionNone".Translate(), null));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenBaseKindEditor(float credit)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<ForwardBaseKind> kinds = new List<ForwardBaseKind> { ForwardBaseKind.PB, ForwardBaseKind.FB, ForwardBaseKind.COP, ForwardBaseKind.FOB };
            for (int i = 0; i < kinds.Count; i++)
            {
                ForwardBaseKind kind = kinds[i];
                bool unlocked = credit >= RequiredCredit(kind);
                Action baseAction = unlocked ? (Action)delegate
                {
                    selectedForwardBaseKind = kind;
                    EnsureForwardBaseSelectionCredit(credit);
                } : null;
                options.Add(new FloatMenuOption(ForwardBaseKindLabel(kind) + (unlocked ? string.Empty : "  (locked)"), baseAction));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenCostKindEditor(float credit)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(ContractCostKindLabel(ContractCostKind.FFP), delegate
            {
                selectedContractCostKind = ContractCostKind.FFP;
            }));
            bool reimbursementUnlocked = credit >= RequiredCredit(ContractCostKind.CostReimbursement);
            Action reimbursementAction = reimbursementUnlocked ? (Action)delegate
            {
                selectedContractCostKind = ContractCostKind.CostReimbursement;
            } : null;
            options.Add(new FloatMenuOption(ContractCostKindLabel(ContractCostKind.CostReimbursement) + (reimbursementUnlocked ? string.Empty : "  (locked)"), reimbursementAction));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenDurationEditor()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            ContractDuration[] durations =
            {
                ContractDuration.Days30,
                ContractDuration.Days60,
                ContractDuration.Days120,
                ContractDuration.Days240
            };
            for (int i = 0; i < durations.Length; i++)
            {
                ContractDuration duration = durations[i];
                options.Add(new FloatMenuOption(ContractDurationLabel(duration), delegate
                {
                    selectedContractDuration = duration;
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void TryStartForwardBaseConstructionContract(float credit, float distance)
        {
            if (selectedForwardBaseTile < 0)
            {
                Messages.Message("HD_TelegraphTable_ForwardBase_NoLocation".Translate(), telegraphTable, MessageTypeDefOf.RejectInput);
                return;
            }

            StringBuilder reason = new StringBuilder();
            if (!TileFinder.IsValidTileForNewSettlement(selectedForwardBaseTile, reason, false))
            {
                Messages.Message("HD_TelegraphTable_ForwardBase_CannotBuildHere".Translate(reason.ToString()), telegraphTable, MessageTypeDefOf.RejectInput);
                return;
            }

            if (Find.WorldObjects.AnyWorldObjectAt(selectedForwardBaseTile))
            {
                Messages.Message("HD_TelegraphTable_ForwardBase_TileOccupied".Translate(), telegraphTable, MessageTypeDefOf.RejectInput);
                return;
            }

            CompTelegraphTable telegraphComp = telegraphTable.TryGetComp<CompTelegraphTable>();
            if (telegraphComp?.HasPrimaryCell != true)
            {
                Messages.Message("HD_TelegraphTable_ActionNeedsPrimaryCell".Translate(), telegraphTable, MessageTypeDefOf.RejectInput);
                return;
            }

            WorldObjectDef constructionDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("HD_ForwardBaseConstruction");
            if (constructionDef == null)
            {
                Messages.Message("HD_TelegraphTable_ForwardBase_ContractFailed".Translate(), telegraphTable, MessageTypeDefOf.RejectInput);
                return;
            }

            HelodForwardBaseConstruction construction = WorldObjectMaker.MakeWorldObject(constructionDef) as HelodForwardBaseConstruction;
            if (construction == null)
            {
                Messages.Message("HD_TelegraphTable_ForwardBase_ContractFailed".Translate(), telegraphTable, MessageTypeDefOf.RejectInput);
                return;
            }

            construction.Tile = selectedForwardBaseTile;
            Faction selectedFaction = SelectedMilitaryFaction();
            construction.SetFaction(selectedFaction ?? Faction.OfPlayer);
            construction.StartConstruction(GenDate.TicksPerDay * 7);
            construction.SetContractInfo(BuildForwardBaseContractInfo(credit, distance));
            construction.SetContractServices(SelectedServices(), SelectedServiceUnitCounts());
            construction.ConfigureContract(ToForwardBaseCostKind(selectedContractCostKind), ContractDurationDays(selectedContractDuration), credit);
            Find.WorldObjects.Add(construction);
            telegraphComp.ConsumePrimaryCell();
            Messages.Message("HD_TelegraphTable_ForwardBase_ConstructionStarted".Translate(7), construction, MessageTypeDefOf.PositiveEvent);
            Close();
        }

        private string BuildForwardBaseContractInfo(float credit, float distance)
        {
            List<string> lines = new List<string>();
            lines.Add("HD_TelegraphTable_ForwardBaseContracts_Title".Translate().ToString());
            lines.Add("HD_TelegraphTable_ForwardBaseContracts_InfoLocation".Translate(selectedForwardBaseTile, distance.ToString("F0")).ToString());
            lines.Add("HD_TelegraphTable_ForwardBaseContracts_InfoFaction".Translate(SelectedMilitaryFactionLabel()).ToString());
            lines.Add("HD_TelegraphTable_ForwardBaseContracts_InfoBase".Translate(ForwardBaseKindLabel(selectedForwardBaseKind)).ToString());
            lines.Add("HD_TelegraphTable_ForwardBaseContracts_InfoCostKind".Translate(ContractCostKindLabel(selectedContractCostKind)).ToString());
            lines.Add("HD_TelegraphTable_ForwardBaseContracts_InfoDuration".Translate(ContractDurationLabel(selectedContractDuration)).ToString());
            lines.Add("HD_TelegraphTable_ForwardBaseContracts_InfoServices".Translate(SelectedServiceSummary()).ToString());
            lines.Add("HD_TelegraphTable_ForwardBaseContracts_InfoServiceUnit".Translate(HelodForwardBaseServiceUtility.ServiceBillingPeriodDays).ToString());
            if (selectedContractCostKind != ContractCostKind.FFP)
            {
                lines.Add("HD_TelegraphTable_ForwardBaseContracts_InfoCredit".Translate(credit.ToString("F0")).ToString());
            }
            lines.Add("HD_TelegraphTable_ForwardBaseContracts_InfoAmount".Translate(
                ContractAmountLabel(selectedContractCostKind),
                FormatContractValue(EstimateForwardBaseCost(credit, distance))
            ).ToString());

            return string.Join("\n", lines.ToArray());
        }

        private static List<WorldObject> ForwardBaseContractObjects()
        {
            List<WorldObject> contracts = new List<WorldObject>();
            if (Find.WorldObjects?.AllWorldObjects == null)
            {
                return contracts;
            }

            for (int i = 0; i < Find.WorldObjects.AllWorldObjects.Count; i++)
            {
                WorldObject worldObject = Find.WorldObjects.AllWorldObjects[i];
                if (worldObject is HelodForwardBaseConstruction || worldObject is HelodForwardBase)
                {
                    contracts.Add(worldObject);
                }
            }

            contracts.Sort((a, b) => a.Tile.tileId.CompareTo(b.Tile.tileId));
            return contracts;
        }

        private static string ForwardBaseContractStatus(WorldObject worldObject)
        {
            HelodForwardBaseConstruction construction = worldObject as HelodForwardBaseConstruction;
            if (construction != null)
            {
                int ticksLeft = Mathf.Max(0, construction.CompleteTick - Find.TickManager.TicksGame);
                return "HD_TelegraphTable_ForwardBaseContracts_StatusConstruction".Translate(ticksLeft.ToStringTicksToPeriod()).ToString();
            }

            return "HD_TelegraphTable_ForwardBaseContracts_StatusComplete".Translate().ToString();
        }

        private static string ForwardBaseContractInfo(WorldObject worldObject)
        {
            HelodForwardBaseConstruction construction = worldObject as HelodForwardBaseConstruction;
            if (construction != null && !construction.ContractInfo.NullOrEmpty())
            {
                return construction.ContractInfo;
            }

            HelodForwardBase forwardBase = worldObject as HelodForwardBase;
            if (forwardBase != null && !forwardBase.ContractInfo.NullOrEmpty())
            {
                return forwardBase.ContractInfo;
            }

            return string.Format("HD_TelegraphTable_ForwardBaseContracts_InfoFallback".Translate().ToString(), worldObject.LabelCap, worldObject.Tile);
        }

        private void BeginForwardBaseTileTargeting()
        {
            ForwardBaseKind baseKind = selectedForwardBaseKind;
            ContractCostKind costKind = selectedContractCostKind;
            ContractDuration duration = selectedContractDuration;
            bool[] selectedServices = SelectedServiceFlags();
            int[] selectedServiceUnitCounts = SelectedServiceUnitCounts();
            string factionDefName = selectedMilitaryFactionDefName;

            Close(false);
            if (telegraphTable?.Map != null)
            {
                CameraJumper.TryJump(new GlobalTargetInfo(telegraphTable.Map.Tile));
            }

            Find.WorldTargeter.BeginTargeting(delegate(GlobalTargetInfo target)
            {
                if (target.Tile < 0)
                {
                    Messages.Message("HD_TelegraphTable_ForwardBase_InvalidLocation".Translate(), MessageTypeDefOf.RejectInput);
                    ReopenForwardBaseContract(baseKind, costKind, duration, selectedForwardBaseTile, selectedServices, selectedServiceUnitCounts, factionDefName);
                    return false;
                }

                ReopenForwardBaseContract(baseKind, costKind, duration, target.Tile, selectedServices, selectedServiceUnitCounts, factionDefName);
                return true;
            }, true);
        }

        private void ReopenForwardBaseContract(ForwardBaseKind baseKind, ContractCostKind costKind, ContractDuration duration, int tile, bool[] selectedServices, int[] serviceUnitCounts, string factionDefName)
        {
            Dialog_TelegraphTable dialog = new Dialog_TelegraphTable(telegraphTable, operatorPawn)
            {
                selectedTab = TelegraphTab.Military,
                selectedMilitaryActivity = MilitaryActivity.ForwardBaseContract,
                selectedForwardBaseKind = baseKind,
                selectedContractCostKind = costKind,
                selectedContractDuration = duration,
                selectedForwardBaseTile = tile,
                selectedMilitaryFactionDefName = factionDefName
            };
            dialog.doWindowBackground = false;
            dialog.doCloseX = false;
            dialog.closeOnClickedOutside = false;
            dialog.windowRect.size = ContractWindowSize;
            dialog.ApplySelectedServiceFlags(selectedServices);
            dialog.ApplySelectedServiceUnitCounts(serviceUnitCounts);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Find.WindowStack.Add(dialog);
        }

        private void DrawForwardBaseKindOptions(Rect rect, float credit)
        {
            float gap = 8f;
            float width = (rect.width - gap * 3f) / 4f;
            DrawForwardBaseKindOption(new Rect(rect.x, rect.y, width, rect.height), ForwardBaseKind.PB, credit);
            DrawForwardBaseKindOption(new Rect(rect.x + (width + gap), rect.y, width, rect.height), ForwardBaseKind.FB, credit);
            DrawForwardBaseKindOption(new Rect(rect.x + (width + gap) * 2f, rect.y, width, rect.height), ForwardBaseKind.COP, credit);
            DrawForwardBaseKindOption(new Rect(rect.x + (width + gap) * 3f, rect.y, width, rect.height), ForwardBaseKind.FOB, credit);
        }

        private void DrawForwardBaseKindOption(Rect rect, ForwardBaseKind kind, float credit)
        {
            bool locked = credit < RequiredCredit(kind);
            if (locked)
            {
                DrawLockedOption(rect, ForwardBaseKindLabel(kind), RequiredCredit(kind));
                return;
            }

            if (Widgets.ButtonText(rect, ForwardBaseKindLabel(kind), selectedForwardBaseKind == kind))
            {
                selectedForwardBaseKind = kind;
            }
        }

        private void DrawContractCostKindOptions(Rect rect, float credit)
        {
            float gap = 8f;
            float width = (rect.width - gap) / 2f;
            DrawContractCostKindOption(new Rect(rect.x, rect.y, width, rect.height), ContractCostKind.FFP, credit);
            DrawContractCostKindOption(new Rect(rect.x + width + gap, rect.y, width, rect.height), ContractCostKind.CostReimbursement, credit);
        }

        private void DrawContractCostKindOption(Rect rect, ContractCostKind kind, float credit)
        {
            bool locked = credit < RequiredCredit(kind);
            if (locked)
            {
                DrawLockedOption(rect, ContractCostKindLabel(kind), RequiredCredit(kind));
                return;
            }

            if (Widgets.ButtonText(rect, ContractCostKindLabel(kind), selectedContractCostKind == kind))
            {
                selectedContractCostKind = kind;
            }
        }

        private void DrawContractDurationOptions(Rect rect)
        {
            float gap = 8f;
            float width = (rect.width - gap * 3f) / 4f;
            DrawContractDurationOption(new Rect(rect.x, rect.y, width, rect.height), ContractDuration.Days30);
            DrawContractDurationOption(new Rect(rect.x + width + gap, rect.y, width, rect.height), ContractDuration.Days60);
            DrawContractDurationOption(new Rect(rect.x + (width + gap) * 2f, rect.y, width, rect.height), ContractDuration.Days120);
            DrawContractDurationOption(new Rect(rect.x + (width + gap) * 3f, rect.y, width, rect.height), ContractDuration.Days240);
        }

        private void DrawContractDurationOption(Rect rect, ContractDuration duration)
        {
            if (Widgets.ButtonText(rect, ContractDurationLabel(duration), selectedContractDuration == duration))
            {
                selectedContractDuration = duration;
            }
        }

        private void DrawForwardBaseServiceOptions(Rect rect, float credit)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float gap = 10f;
            float columnWidth = (inner.width - gap) / 2f;
            float categoryHeight = (inner.height - gap) / 2f;
            DrawForwardBaseServiceCategory(new Rect(inner.x, inner.y, columnWidth, categoryHeight), ForwardBaseServiceType.Infantry, InfantryServices, credit, ref infantryServiceScroll);
            DrawForwardBaseServiceCategory(new Rect(inner.x + columnWidth + gap, inner.y, columnWidth, categoryHeight), ForwardBaseServiceType.Artillery, ArtilleryServices, credit, ref artilleryServiceScroll);
            DrawForwardBaseServiceCategory(new Rect(inner.x, inner.y + categoryHeight + gap, columnWidth, categoryHeight), ForwardBaseServiceType.Logistics, LogisticsServices, credit, ref logisticsServiceScroll);
            DrawForwardBaseServiceCategory(new Rect(inner.x + columnWidth + gap, inner.y + categoryHeight + gap, columnWidth, categoryHeight), ForwardBaseServiceType.AirForce, AirForceServices, credit, ref airForceServiceScroll);
        }

        private void DrawForwardBaseServiceCategory(Rect rect, ForwardBaseServiceType type, ForwardBaseService[] services, float credit, ref Vector2 scrollPosition)
        {
            Widgets.DrawBox(rect);
            Rect titleRect = new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, 22f);
            bool typeAllowed = BaseAllowsServiceType(selectedForwardBaseKind, type);
            GUI.color = typeAllowed ? Color.white : Color.gray;
            Widgets.Label(titleRect, ForwardBaseServiceTypeLabel(type));
            GUI.color = Color.white;

            Rect listRect = new Rect(rect.x + 6f, rect.y + 30f, rect.width - 12f, rect.height - 36f);
            if (services.Length == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(listRect.ContractedBy(2f), "HD_TelegraphTable_ForwardBase_ServiceType_Empty".Translate());
                GUI.color = Color.white;
                return;
            }

            const float rowHeight = 28f;
            const float rowGap = 3f;
            float viewHeight = Mathf.Max(listRect.height, services.Length * (rowHeight + rowGap));
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            float y = 0f;
            for (int i = 0; i < services.Length; i++)
            {
                DrawForwardBaseServiceOption(new Rect(0f, y, viewRect.width, rowHeight), services[i], credit);
                y += rowHeight + rowGap;
            }
            Widgets.EndScrollView();
        }

        private void DrawForwardBaseServiceOption(Rect rect, ForwardBaseService service, float credit)
        {
            string label = ForwardBaseServiceLabel(service) + "  " + "HD_TelegraphTable_ForwardBase_ServiceLevel".Translate(ForwardBaseServiceLevel(service));
            if (service == ForwardBaseService.W48Support && !includeArtillery155mmSupport)
            {
                SetServiceSelected(service, false);
                DrawServiceDependencyOption(rect, label);
                return;
            }
            if (!IsServiceAvailableForBase(selectedForwardBaseKind, service))
            {
                SetServiceSelected(service, false);
                DrawUnavailableOption(rect, label);
                return;
            }

            bool locked = credit < RequiredCredit(service);
            if (locked)
            {
                SetServiceSelected(service, false);
                DrawLockedOption(rect, label, RequiredCredit(service));
                return;
            }

            bool selected = IsServiceSelected(service);
            if (selectedContractCostKind == ContractCostKind.FFP && selected)
            {
                float countFieldWidth = 64f;
                float countLabelWidth = 82f;
                Widgets.CheckboxLabeled(
                    new Rect(rect.x, rect.y, rect.width - countFieldWidth - countLabelWidth - 8f, rect.height),
                    label,
                    ref selected);
                Widgets.Label(
                    new Rect(rect.xMax - countFieldWidth - countLabelWidth - 4f, rect.y, countLabelWidth, rect.height),
                    "HD_TelegraphTable_ForwardBase_ServiceUnitCountShort".Translate());
                int units = FfpServiceUnitCount(service);
                string buffer = FfpServiceUnitBuffer(service);
                Widgets.TextFieldNumeric(
                    new Rect(rect.xMax - countFieldWidth, rect.y + 2f, countFieldWidth, rect.height - 4f),
                    ref units,
                    ref buffer,
                    1,
                    999999);
                ffpServiceUnitCounts[service] = Mathf.Clamp(units, 1, 999999);
                ffpServiceUnitBuffers[service] = buffer;
            }
            else
            {
                Widgets.CheckboxLabeled(rect, label, ref selected);
            }
            SetServiceSelected(service, selected);
        }

        private int FfpServiceUnitCount(ForwardBaseService service)
        {
            int units;
            if (!ffpServiceUnitCounts.TryGetValue(service, out units))
            {
                units = HelodForwardBaseServiceUtility.DefaultFfpServiceUnitsPerBillingPeriod;
                ffpServiceUnitCounts[service] = units;
            }

            return Mathf.Clamp(units, 1, 999999);
        }

        private string FfpServiceUnitBuffer(ForwardBaseService service)
        {
            string buffer;
            if (!ffpServiceUnitBuffers.TryGetValue(service, out buffer))
            {
                buffer = FfpServiceUnitCount(service).ToString();
                ffpServiceUnitBuffers[service] = buffer;
            }

            return buffer;
        }

        private static void DrawLockedOption(Rect rect, string label, float requiredCredit)
        {
            GUI.color = Color.gray;
            Widgets.DrawOptionBackground(rect, false);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label + "  " + "HD_TelegraphTable_ForwardBase_Locked".Translate(requiredCredit.ToString("F0")));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, "HD_TelegraphTable_ForwardBase_LockedTooltip".Translate(requiredCredit.ToString("F0")));
            GUI.color = Color.white;
        }

        private static void DrawUnavailableOption(Rect rect, string label)
        {
            GUI.color = Color.gray;
            Widgets.DrawOptionBackground(rect, false);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label + "  " + "HD_TelegraphTable_ForwardBase_ServiceUnavailableForBase".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, "HD_TelegraphTable_ForwardBase_ServiceUnavailableForBaseTooltip".Translate());
            GUI.color = Color.white;
        }

        private static void DrawServiceDependencyOption(Rect rect, string label)
        {
            GUI.color = Color.gray;
            Widgets.DrawOptionBackground(rect, false);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label + "  " + "HD_TelegraphTable_ForwardBase_ServiceDependency".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, "HD_TelegraphTable_ForwardBase_ServiceDependency_M114".Translate());
            GUI.color = Color.white;
        }

        private static string ForwardBaseKindLabel(ForwardBaseKind kind)
        {
            return ("HD_TelegraphTable_ForwardBase_Kind_" + kind).Translate();
        }

        private static string ContractCostKindLabel(ContractCostKind kind)
        {
            return ("HD_TelegraphTable_ForwardBase_Cost_" + kind).Translate();
        }

        private static string ContractAmountLabel(ContractCostKind kind)
        {
            switch (kind)
            {
                case ContractCostKind.FFP:
                    return "HD_TelegraphTable_ForwardBase_FixedPrice".Translate().ToString();
                case ContractCostKind.CostReimbursement:
                    return "HD_TelegraphTable_ForwardBase_ReimbursableEstimate".Translate().ToString();
                default:
                    return "HD_TelegraphTable_ForwardBase_EstimatedCost".Translate().ToString();
            }
        }

        private static string ContractDurationLabel(ContractDuration duration)
        {
            return "HD_TelegraphTable_ForwardBase_DurationDays".Translate(ContractDurationDays(duration)).ToString();
        }

        private static string ForwardBaseServiceLabel(ForwardBaseService service)
        {
            return ("HD_TelegraphTable_ForwardBase_Service_" + service).Translate();
        }

        private static string ForwardBaseServiceTypeLabel(ForwardBaseServiceType type)
        {
            return ("HD_TelegraphTable_ForwardBase_ServiceType_" + type).Translate();
        }

        private void EnsureForwardBaseSelectionCredit(float credit)
        {
            if (credit < RequiredCredit(selectedForwardBaseKind))
            {
                selectedForwardBaseKind = credit >= RequiredCredit(ForwardBaseKind.FB) ? ForwardBaseKind.FB : ForwardBaseKind.PB;
            }

            if (credit < RequiredCredit(selectedContractCostKind))
            {
                selectedContractCostKind = ContractCostKind.FFP;
            }

            for (int i = 0; i < AllForwardBaseServices.Length; i++)
            {
                ForwardBaseService service = AllForwardBaseServices[i];
                if (credit < RequiredCredit(service) || !IsServiceAvailableForBase(selectedForwardBaseKind, service))
                {
                    SetServiceSelected(service, false);
                }
            }

            if (!includeArtillery155mmSupport)
            {
                includeW48Support = false;
            }
        }

        private string SelectedServiceSummary()
        {
            List<string> services = new List<string>();
            for (int i = 0; i < AllForwardBaseServices.Length; i++)
            {
                ForwardBaseService service = AllForwardBaseServices[i];
                if (IsServiceSelected(service))
                {
                    services.Add(selectedContractCostKind == ContractCostKind.FFP
                        ? "HD_TelegraphTable_ForwardBase_ServiceUnitCount".Translate(
                            ForwardBaseServiceLabel(service),
                            FfpServiceUnitCount(service)).ToString()
                        : ForwardBaseServiceLabel(service));
                }
            }

            return services.Count == 0 ? "HD_TelegraphTable_ForwardBase_Service_None".Translate().ToString() : string.Join(", ", services.ToArray());
        }

        private List<ForwardBaseService> SelectedServices()
        {
            List<ForwardBaseService> services = new List<ForwardBaseService>();
            for (int i = 0; i < AllForwardBaseServices.Length; i++)
            {
                ForwardBaseService service = AllForwardBaseServices[i];
                if (IsServiceSelected(service))
                {
                    services.Add(service);
                }
            }

            return services;
        }

        private int[] SelectedServiceUnitCounts()
        {
            int[] counts = new int[AllForwardBaseServices.Length];
            for (int i = 0; i < AllForwardBaseServices.Length; i++)
            {
                counts[i] = FfpServiceUnitCount(AllForwardBaseServices[i]);
            }

            return counts;
        }

        private float EstimateForwardBaseCost(float credit, float distance)
        {
            float baseCost = BaseCost(selectedForwardBaseKind);
            float serviceCost = SelectedServiceCost();

            float contractMultiplier = ContractCostMultiplier(selectedContractCostKind);
            float durationMultiplier = ContractDurationMultiplier(selectedContractCostKind, selectedContractDuration);
            float servicePeriods = ServiceBillingPeriodCount(selectedContractDuration);
            float distanceMultiplier = 1f + Mathf.Clamp01(distance / MaxForwardBaseDistance) * 0.45f;
            float creditMultiplier = Mathf.Lerp(1.20f, 0.82f, Mathf.InverseLerp(100f, 3500f, credit));
            float contractBase = baseCost * contractMultiplier * durationMultiplier;
            float services = selectedContractCostKind == ContractCostKind.CostReimbursement ? 0f : serviceCost * servicePeriods;
            return ContractSilverValue(Mathf.Max(1f, (contractBase + services) * distanceMultiplier * creditMultiplier));
        }

        private static float ContractSilverValue(float goldStandardSthalerValue)
        {
            return goldStandardSthalerValue * GoldStandardSthalerSilverValue;
        }

        private static string FormatContractValue(float silverValue)
        {
            float sthalerValue = silverValue / CurrentSthalerSilverValue();
            return "HD_TelegraphTable_ForwardBase_SthalerAmount".Translate(sthalerValue.ToString("F0")).ToString();
        }

        private static float CurrentSthalerSilverValue()
        {
            return HelodForwardBaseServiceUtility.CurrentSthalerSilverValue();
        }

        private float SelectedServiceCost()
        {
            float serviceCost = 0f;
            for (int i = 0; i < AllForwardBaseServices.Length; i++)
            {
                ForwardBaseService service = AllForwardBaseServices[i];
                if (IsServiceSelected(service))
                {
                    serviceCost += ServiceCost(service);
                }
            }

            return serviceCost;
        }

        private bool[] SelectedServiceFlags()
        {
            return new[]
            {
                includeInfantryMortarSupport,
                includeInfantryDeployment,
                includeLogisticsFreshFood,
                includeLogisticsPreservedFood,
                includeLogisticsMedicalSupplies,
                includeLogisticsWeapons,
                includeCloseAirSupport,
                includeInfantrySniperSupport,
                includeArtillery105mmSupport,
                includeArtillery155mmSupport,
                includeW48Support
            };
        }

        private void ApplySelectedServiceFlags(bool[] flags)
        {
            if (flags == null || flags.Length < 7)
            {
                return;
            }

            includeInfantryMortarSupport = flags[0];
            includeInfantryDeployment = flags[1];
            includeLogisticsFreshFood = flags[2];
            includeLogisticsPreservedFood = flags[3];
            includeLogisticsMedicalSupplies = flags[4];
            includeLogisticsWeapons = flags[5];
            includeCloseAirSupport = flags[6];
            includeInfantrySniperSupport = flags.Length > 7 && flags[7];
            includeArtillery105mmSupport = flags.Length > 8 && flags[8];
            includeArtillery155mmSupport = flags.Length > 9 && flags[9];
            includeW48Support = flags.Length > 10 && flags[10];
        }

        private void ApplySelectedServiceUnitCounts(int[] counts)
        {
            if (counts == null)
            {
                return;
            }

            for (int i = 0; i < AllForwardBaseServices.Length && i < counts.Length; i++)
            {
                int units = Mathf.Clamp(counts[i], 1, 999999);
                ForwardBaseService service = AllForwardBaseServices[i];
                ffpServiceUnitCounts[service] = units;
                ffpServiceUnitBuffers[service] = units.ToString();
            }
        }

        private bool IsServiceSelected(ForwardBaseService service)
        {
            switch (service)
            {
                case ForwardBaseService.InfantryMortarSupport:
                    return includeInfantryMortarSupport;
                case ForwardBaseService.InfantrySniperSupport:
                    return includeInfantrySniperSupport;
                case ForwardBaseService.InfantryDeployment:
                    return includeInfantryDeployment;
                case ForwardBaseService.LogisticsFreshFood:
                    return includeLogisticsFreshFood;
                case ForwardBaseService.LogisticsPreservedFood:
                    return includeLogisticsPreservedFood;
                case ForwardBaseService.LogisticsMedicalSupplies:
                    return includeLogisticsMedicalSupplies;
                case ForwardBaseService.LogisticsWeapons:
                    return includeLogisticsWeapons;
                case ForwardBaseService.CloseAirSupport:
                    return includeCloseAirSupport;
                case ForwardBaseService.Artillery105mmSupport:
                    return includeArtillery105mmSupport;
                case ForwardBaseService.Artillery155mmSupport:
                    return includeArtillery155mmSupport;
                case ForwardBaseService.W48Support:
                    return includeW48Support;
                default:
                    return false;
            }
        }

        private void SetServiceSelected(ForwardBaseService service, bool selected)
        {
            switch (service)
            {
                case ForwardBaseService.InfantryMortarSupport:
                    includeInfantryMortarSupport = selected;
                    break;
                case ForwardBaseService.InfantrySniperSupport:
                    includeInfantrySniperSupport = selected;
                    break;
                case ForwardBaseService.InfantryDeployment:
                    includeInfantryDeployment = selected;
                    break;
                case ForwardBaseService.LogisticsFreshFood:
                    includeLogisticsFreshFood = selected;
                    break;
                case ForwardBaseService.LogisticsPreservedFood:
                    includeLogisticsPreservedFood = selected;
                    break;
                case ForwardBaseService.LogisticsMedicalSupplies:
                    includeLogisticsMedicalSupplies = selected;
                    break;
                case ForwardBaseService.LogisticsWeapons:
                    includeLogisticsWeapons = selected;
                    break;
                case ForwardBaseService.CloseAirSupport:
                    includeCloseAirSupport = selected;
                    break;
                case ForwardBaseService.Artillery105mmSupport:
                    includeArtillery105mmSupport = selected;
                    break;
                case ForwardBaseService.Artillery155mmSupport:
                    includeArtillery155mmSupport = selected;
                    if (!selected)
                    {
                        includeW48Support = false;
                    }
                    break;
                case ForwardBaseService.W48Support:
                    includeW48Support = selected;
                    break;
            }
        }

        private void DrawMarketTab(Rect bodyInner)
        {
            HelodMarketState market = HelodMarketState.Current;
            if (market == null)
            {
                Widgets.Label(bodyInner, "HD_TelegraphTable_MarketUnavailable".Translate());
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(bodyInner.x, bodyInner.y, bodyInner.width, 30f), "HD_TelegraphTable_Market_Title".Translate(market.StandardLabel()));
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(bodyInner.x, bodyInner.y + 30f, bodyInner.width, 24f), "HD_TelegraphTable_Market_OilIndex".Translate(market.OilIndexValue.ToString("F1")));

            Rect listRect = new Rect(bodyInner.x, bodyInner.y + 62f, 270f, bodyInner.height - 62f);
            Rect contentRect = new Rect(listRect.xMax + 16f, bodyInner.y + 62f, bodyInner.width - listRect.width - 16f, bodyInner.height - 62f);

            if (selectedMarketIndex >= 0 && selectedMarketIndex < HelodMarketState.DisplayAssets.Count && IsMarketAssetLocked(HelodMarketState.DisplayAssets[selectedMarketIndex], market))
            {
                selectedMarketIndex = 0;
            }

            for (int i = 0; i < HelodMarketState.DisplayAssets.Count; i++)
            {
                HelodMarketAsset asset = HelodMarketState.DisplayAssets[i];
                Rect row = new Rect(listRect.x, listRect.y + i * 36f, listRect.width, 34f);
                bool locked = IsMarketAssetLocked(asset, market);
                if (locked)
                {
                    GUI.color = Color.gray;
                    Widgets.DrawOptionBackground(row, false);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(row, asset.Label);
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(row, "HD_TelegraphTable_Market_SthalerLocked".Translate());
                    continue;
                }

                if (Widgets.ButtonText(row, asset.Label, selectedMarketIndex == i))
                {
                    selectedMarketIndex = i;
                }
            }

            selectedMarketIndex = Mathf.Clamp(selectedMarketIndex, 0, HelodMarketState.DisplayAssets.Count - 1);
            HelodMarketAsset selected = HelodMarketState.DisplayAssets[selectedMarketIndex];
            DrawMarketSubTabs(new Rect(contentRect.x, contentRect.y, contentRect.width, 32f));
            Rect subContentRect = new Rect(contentRect.x, contentRect.y + 40f, contentRect.width, contentRect.height - 40f);
            if (selectedMarketSubTab == MarketSubTab.Trade)
            {
                DrawTradeControls(subContentRect, market, selected);
            }
            else
            {
                DrawMarketLog(subContentRect, market, selected);
            }
        }

        private void DrawMarketSubTabs(Rect rect)
        {
            float tabWidth = 120f;
            Rect logRect = new Rect(rect.x, rect.y, tabWidth, rect.height);
            Rect tradeRect = new Rect(logRect.xMax + 8f, rect.y, tabWidth, rect.height);
            if (Widgets.ButtonText(logRect, "HD_TelegraphTable_Market_SubTab_Log".Translate(), selectedMarketSubTab == MarketSubTab.Log))
            {
                selectedMarketSubTab = MarketSubTab.Log;
            }

            if (Widgets.ButtonText(tradeRect, "HD_TelegraphTable_Market_SubTab_Trade".Translate(), selectedMarketSubTab == MarketSubTab.Trade))
            {
                selectedMarketSubTab = MarketSubTab.Trade;
            }
        }

        private void DrawMarketLog(Rect rect, HelodMarketState market, HelodMarketAsset asset)
        {
            CompTelegraphTable telegraphComp = telegraphTable.TryGetComp<CompTelegraphTable>();
            bool observedToday = market.WasObservedToday(asset.defName);
            if (!observedToday && telegraphComp?.ConsumePrimaryCell() == true)
            {
                market.ObserveAsset(asset);
                observedToday = true;
            }

            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(12f);

            float current = market.QuotedPrice(asset);
            float currentBid = BidFromMid(current, asset);
            float currentAsk = AskFromMid(current, asset);
            float currentSpread = currentAsk - currentBid;
            int today = CurrentDay();
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f), "HD_TelegraphTable_Market_CurrentQuote".Translate(asset.Label, FormatMarketValue(current, asset, market)));
            if (market.IsFixedGoldStandardGold(asset))
            {
                Widgets.Label(new Rect(inner.x, inner.y + 24f, inner.width, 20f), "HD_TelegraphTable_Market_FixedGoldPrice".Translate(FormatMarketValue(current, asset, market)));
                Widgets.Label(new Rect(inner.x, inner.y + 44f, inner.width, 20f), "HD_TelegraphTable_Market_FixedGoldDetail".Translate(FormatSettlement(SettlementHoursFor(asset, today))));
            }
            else
            {
                Widgets.Label(new Rect(inner.x, inner.y + 24f, inner.width, 20f), "HD_TelegraphTable_Market_LogEntry_Prices".Translate(FormatMarketValue(currentBid, asset, market), FormatMarketValue(currentAsk, asset, market)));
                Widgets.Label(new Rect(inner.x, inner.y + 44f, inner.width, 20f), "HD_TelegraphTable_Market_LogEntry_Details".Translate(FormatMarketValue(currentSpread, asset, market), FormatSettlement(SettlementHoursFor(asset, today))));
            }

            float logTop = observedToday ? inner.y + 72f : inner.y + 96f;
            if (!observedToday)
            {
                Widgets.Label(new Rect(inner.x, inner.y + 70f, inner.width, 24f), "HD_TelegraphTable_Market_QueryNeedsPrimaryCell".Translate());
            }

            Rect logRect = new Rect(inner.x, logTop, inner.width, inner.yMax - logTop);
            Widgets.DrawBoxSolid(logRect, new Color(0.08f, 0.08f, 0.08f, 0.35f));
            Widgets.DrawBox(logRect);
            Rect logInner = logRect.ContractedBy(10f);

            List<HelodMarketObservation> observations = market.ObservationLogFor(asset.defName);
            if (observations.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(logRect, "HD_TelegraphTable_Market_NotEnoughHistory".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            const float rowHeight = 52f;
            Rect viewRect = new Rect(0f, 0f, logInner.width - 16f, observations.Count * rowHeight);
            Widgets.BeginScrollView(logInner, ref marketLogScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < observations.Count; i++)
            {
                HelodMarketObservation observation = observations[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight - 6f);
                Widgets.DrawHighlightIfMouseover(rowRect);
                Widgets.DrawBox(rowRect);

                string dayText = "HD_TelegraphTable_Market_Day".Translate(observation.day);
                string priceLine;
                string detailLine;
                if (observation.value.HasValue)
                {
                    float mid = observation.value.Value;
                    float bid = BidFromMid(mid, asset);
                    float ask = AskFromMid(mid, asset);
                    float spread = ask - bid;
                    if (market.IsFixedGoldStandardGold(asset))
                    {
                        priceLine = dayText + "  " + "HD_TelegraphTable_Market_FixedGoldPrice".Translate(FormatMarketValue(mid, asset, market));
                        detailLine = "HD_TelegraphTable_Market_FixedGoldDetail".Translate(FormatSettlement(SettlementHoursFor(asset, observation.day)));
                    }
                    else
                    {
                        priceLine = dayText + "  " + "HD_TelegraphTable_Market_LogEntry_Prices".Translate(
                            FormatMarketValue(bid, asset, market),
                            FormatMarketValue(ask, asset, market)
                        );
                        detailLine = "HD_TelegraphTable_Market_LogEntry_Details".Translate(
                            FormatMarketValue(spread, asset, market),
                            FormatSettlement(SettlementHoursFor(asset, observation.day))
                        );
                    }
                }
                else
                {
                    priceLine = dayText + "  " + "HD_TelegraphTable_Market_NoInformation".Translate();
                    detailLine = "HD_TelegraphTable_Market_NoInformationDetail".Translate();
                    GUI.color = Color.gray;
                }

                Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 5f, rowRect.width - 16f, 20f), priceLine);
                Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 25f, rowRect.width - 16f, 20f), detailLine);
                TooltipHandler.TipRegion(rowRect, priceLine + "\n" + detailLine);
                GUI.color = Color.white;
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawTradeControls(Rect rect, HelodMarketState market, HelodMarketAsset asset)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(12f);
            ThingDef deliveryDef = HelodMarketState.DeliveryThingDef(asset);
            if (deliveryDef == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inner, "HD_TelegraphTable_Trade_IndexOnly".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 30f), "HD_TelegraphTable_Market_SubTab_Trade".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y + 40f, inner.width, 24f), "HD_TelegraphTable_Trade_Credit".Translate(market.CreditLimit(telegraphTable.Map).ToString("F0"), market.CurrencyLabel()));
            Widgets.Label(new Rect(inner.x, inner.y + 74f, 80f, 24f), "HD_TelegraphTable_Trade_Count".Translate());
            Widgets.TextFieldNumeric(new Rect(inner.x + 88f, inner.y + 74f, 160f, 24f), ref tradeCount, ref tradeCountBuffer, 1, 999999);

            bool locked = market.HasPendingTrade(asset.defName);
            Rect sellRect = new Rect(inner.x, inner.y + 112f, 150f, 34f);
            Rect buyRect = new Rect(sellRect.xMax + 8f, sellRect.y, sellRect.width, 30f);
            int count = Mathf.Clamp(tradeCount, 1, 999999);
            int settlementHours = SettlementHoursFor(asset, CurrentDay());

            if (locked)
            {
                DrawDisabledButton(sellRect, "HD_TelegraphTable_Trade_Sell".Translate());
                DrawDisabledButton(buyRect, "HD_TelegraphTable_Trade_Buy".Translate());
                TooltipHandler.TipRegion(new Rect(inner.x, sellRect.y, inner.width, sellRect.height), "HD_TelegraphTable_Trade_PendingLocked".Translate());
                Widgets.Label(new Rect(inner.x, sellRect.yMax + 12f, inner.width, 24f), "HD_TelegraphTable_Trade_PendingLocked".Translate());
                return;
            }

            if (Widgets.ButtonText(sellRect, "HD_TelegraphTable_Trade_Sell".Translate()))
            {
                TryStartUiTrade(market, asset, HelodMarketTradeSide.Sell, count, settlementHours);
            }

            if (Widgets.ButtonText(buyRect, "HD_TelegraphTable_Trade_Buy".Translate()))
            {
                TryStartUiTrade(market, asset, HelodMarketTradeSide.Buy, count, settlementHours);
            }

            Widgets.Label(new Rect(inner.x, sellRect.yMax + 12f, inner.width, 24f), "HD_TelegraphTable_Trade_EstimatedSettlement".Translate(FormatSettlement(settlementHours)));
            if (market.IsFixedGoldStandardGold(asset))
            {
                Widgets.Label(new Rect(inner.x, sellRect.yMax + 38f, inner.width, 24f), "HD_TelegraphTable_Market_FixedGoldPrice".Translate(FormatMarketValue(market.QuotedPrice(asset), asset, market)));
            }
        }

        private void TryStartUiTrade(HelodMarketState market, HelodMarketAsset asset, HelodMarketTradeSide side, int count, int settlementHours)
        {
            CompTelegraphTable telegraphComp = telegraphTable.TryGetComp<CompTelegraphTable>();
            if (telegraphComp?.HasPrimaryCell != true)
            {
                Messages.Message("HD_TelegraphTable_ActionNeedsPrimaryCell".Translate(), telegraphTable, MessageTypeDefOf.RejectInput);
                return;
            }

            if (market.TryStartTrade(telegraphTable.Map, telegraphTable.Position, asset, side, count, settlementHours, out string failReason))
            {
                telegraphComp.ConsumePrimaryCell();
                Messages.Message("HD_TelegraphTable_Trade_Started".Translate(asset.Label, count), telegraphTable, MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Messages.Message(failReason, telegraphTable, MessageTypeDefOf.RejectInput);
            }
        }

        private static float BidFromMid(float mid, HelodMarketAsset asset)
        {
            return Mathf.Max(0.01f, mid * (1f - asset.spreadPct * 0.5f));
        }

        private static float AskFromMid(float mid, HelodMarketAsset asset)
        {
            return Mathf.Max(0.01f, mid * (1f + asset.spreadPct * 0.5f));
        }

        private static string FormatMarketValue(float value, HelodMarketAsset asset, HelodMarketState market)
        {
            if (asset.defName == "HD_OilIndex")
            {
                return value.ToString("F1");
            }

            if (asset.defName == "HD_Money")
            {
                return "HD_TelegraphTable_Market_SthalerFxValue".Translate(value.ToString("F2"));
            }

            return value.ToString("F2") + " " + market.CurrencyLabel();
        }

        private static string FormatSettlement(int hours)
        {
            if (hours < 24)
            {
                return "HD_TelegraphTable_Market_SettlementHours".Translate(hours);
            }

            int days = Mathf.CeilToInt(hours / 24f);
            return "HD_TelegraphTable_Market_SettlementDays".Translate(days);
        }

        private static int SettlementHoursFor(HelodMarketAsset asset, int day)
        {
            if (asset.settlementVarianceHours <= 0)
            {
                return asset.settlementHours;
            }

            int hash = 17;
            for (int i = 0; i < asset.defName.Length; i++)
            {
                hash = hash * 31 + asset.defName[i];
            }

            hash = Mathf.Abs(hash + day * 1103515245);
            int variance = hash % (asset.settlementVarianceHours * 2 + 1) - asset.settlementVarianceHours;
            return Mathf.Max(1, asset.settlementHours + variance);
        }

        private static int CurrentDay()
        {
            return Find.TickManager?.TicksGame / GenDate.TicksPerDay ?? 0;
        }

        private static bool IsMarketAssetLocked(HelodMarketAsset asset, HelodMarketState market)
        {
            return asset.defName == "HD_Money" && market.Standard != HelodMarketStandard.Silver;
        }

        private static bool FinancialNetworkFinished()
        {
            ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HelodFinancialNetwork");
            return research != null && research.IsFinished;
        }

        private static float MilitaryCredit(Map map, Faction faction)
        {
            float wealth = Mathf.Max(0f, map?.wealthWatcher?.WealthTotal ?? 0f);
            float wealthCredit = Mathf.Sqrt(Mathf.Max(wealth, 1f)) * 16f;
            float goodwillCredit = MilitaryGoodwill(faction) * 7f;
            float marketCredit = HelodMarketState.Current?.CreditLimit(map) ?? 100f;
            return Mathf.Max(100f, wealthCredit + goodwillCredit + marketCredit * 0.25f);
        }

        private static int MilitaryGoodwill(Faction faction)
        {
            return faction?.PlayerGoodwill ?? 0;
        }

        private static Faction FactionOfDef(string defName)
        {
            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(defName);
            return def == null ? null : Find.FactionManager.FirstFactionOfDef(def);
        }

        private static float NearestMilitarySettlementDistance(int tile, Faction faction)
        {
            if (tile < 0 || faction == null || Find.WorldObjects?.Settlements == null || Find.WorldGrid == null)
            {
                return MaxForwardBaseDistance;
            }

            float nearest = MaxForwardBaseDistance;
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement?.Faction != faction || settlement.Destroyed)
                {
                    continue;
                }

                float distance = Find.WorldGrid.ApproxDistanceInTiles(tile, settlement.Tile);
                nearest = Mathf.Min(nearest, distance);
            }

            return nearest;
        }

        private static float RequiredCredit(ForwardBaseKind kind)
        {
            switch (kind)
            {
                case ForwardBaseKind.FB:
                    return 450f;
                case ForwardBaseKind.COP:
                    return 950f;
                case ForwardBaseKind.FOB:
                    return 1700f;
                default:
                    return 100f;
            }
        }

        private static float RequiredCredit(ContractCostKind kind)
        {
            switch (kind)
            {
                case ContractCostKind.CostReimbursement:
                    return 700f;
                default:
                    return 100f;
            }
        }

        private static float RequiredCredit(ForwardBaseService service)
        {
            switch (service)
            {
                case ForwardBaseService.InfantryDeployment:
                    return 350f;
                case ForwardBaseService.LogisticsFreshFood:
                    return 450f;
                case ForwardBaseService.LogisticsPreservedFood:
                    return 520f;
                case ForwardBaseService.LogisticsMedicalSupplies:
                    return 650f;
                case ForwardBaseService.LogisticsWeapons:
                    return 900f;
                case ForwardBaseService.CloseAirSupport:
                    return 1800f;
                case ForwardBaseService.Artillery105mmSupport:
                    return 900f;
                case ForwardBaseService.Artillery155mmSupport:
                    return 1500f;
                case ForwardBaseService.W48Support:
                    return 2000f;
                default:
                    return 250f;
            }
        }

        private static int ForwardBaseServiceLevel(ForwardBaseService service)
        {
            switch (service)
            {
                case ForwardBaseService.InfantryDeployment:
                case ForwardBaseService.LogisticsMedicalSupplies:
                    return 2;
                case ForwardBaseService.LogisticsWeapons:
                case ForwardBaseService.CloseAirSupport:
                case ForwardBaseService.Artillery155mmSupport:
                    return 3;
                case ForwardBaseService.W48Support:
                    return 4;
                case ForwardBaseService.Artillery105mmSupport:
                    return 2;
                default:
                    return 1;
            }
        }

        private static ForwardBaseServiceType ForwardBaseServiceTypeOf(ForwardBaseService service)
        {
            switch (service)
            {
                case ForwardBaseService.InfantryMortarSupport:
                case ForwardBaseService.InfantrySniperSupport:
                case ForwardBaseService.InfantryDeployment:
                    return ForwardBaseServiceType.Infantry;
                case ForwardBaseService.LogisticsFreshFood:
                case ForwardBaseService.LogisticsPreservedFood:
                case ForwardBaseService.LogisticsMedicalSupplies:
                case ForwardBaseService.LogisticsWeapons:
                    return ForwardBaseServiceType.Logistics;
                case ForwardBaseService.CloseAirSupport:
                    return ForwardBaseServiceType.AirForce;
                default:
                    return ForwardBaseServiceType.Artillery;
            }
        }

        private static bool BaseAllowsServiceType(ForwardBaseKind baseKind, ForwardBaseServiceType type)
        {
            switch (baseKind)
            {
                case ForwardBaseKind.PB:
                    return type == ForwardBaseServiceType.Infantry;
                case ForwardBaseKind.FB:
                    return type == ForwardBaseServiceType.Artillery;
                case ForwardBaseKind.COP:
                    return type == ForwardBaseServiceType.Artillery || type == ForwardBaseServiceType.Infantry || type == ForwardBaseServiceType.Logistics;
                case ForwardBaseKind.FOB:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsServiceAvailableForBase(ForwardBaseKind baseKind, ForwardBaseService service)
        {
            switch (baseKind)
            {
                case ForwardBaseKind.PB:
                    return service == ForwardBaseService.InfantryMortarSupport
                        || service == ForwardBaseService.InfantrySniperSupport;
                case ForwardBaseKind.FB:
                    return ForwardBaseServiceTypeOf(service) == ForwardBaseServiceType.Artillery;
                case ForwardBaseKind.COP:
                    return ForwardBaseServiceTypeOf(service) == ForwardBaseServiceType.Artillery
                        || ForwardBaseServiceTypeOf(service) == ForwardBaseServiceType.Infantry
                        || service == ForwardBaseService.LogisticsFreshFood
                        || service == ForwardBaseService.LogisticsPreservedFood
                        || service == ForwardBaseService.LogisticsMedicalSupplies;
                case ForwardBaseKind.FOB:
                    return true;
                default:
                    return false;
            }
        }

        private static float BaseCost(ForwardBaseKind kind)
        {
            switch (kind)
            {
                case ForwardBaseKind.FB:
                    return 650f;
                case ForwardBaseKind.COP:
                    return 1250f;
                case ForwardBaseKind.FOB:
                    return 2400f;
                default:
                    return 320f;
            }
        }

        private static float ContractCostMultiplier(ContractCostKind kind)
        {
            switch (kind)
            {
                case ContractCostKind.CostReimbursement:
                    return 0.05f;
                default:
                    return 1f;
            }
        }

        private static int ContractDurationDays(ContractDuration duration)
        {
            switch (duration)
            {
                case ContractDuration.Days60:
                    return 60;
                case ContractDuration.Days120:
                    return 120;
                case ContractDuration.Days240:
                    return 240;
                default:
                    return 30;
            }
        }

        private static float ContractDurationMultiplier(ContractCostKind kind, ContractDuration duration)
        {
            float durationFactor = Mathf.Max(1f, ContractDurationDays(duration) / 30f);
            switch (kind)
            {
                case ContractCostKind.FFP:
                    return Mathf.Pow(durationFactor, 0.72f);
                case ContractCostKind.CostReimbursement:
                    return 1f + (durationFactor - 1f) * 0.04f;
                default:
                    return 1f;
            }
        }

        private static float ServiceBillingPeriodCount(ContractDuration duration)
        {
            return Mathf.CeilToInt(ContractDurationDays(duration) / (float)HelodForwardBaseServiceUtility.ServiceBillingPeriodDays);
        }

        private static float ServiceCost(ForwardBaseService service)
        {
            return HelodForwardBaseServiceUtility.ServiceBaseCost(service);
        }

        private static HelodForwardBaseCostKind ToForwardBaseCostKind(ContractCostKind kind)
        {
            switch (kind)
            {
                case ContractCostKind.CostReimbursement:
                    return HelodForwardBaseCostKind.CostReimbursement;
                default:
                    return HelodForwardBaseCostKind.FFP;
            }
        }

        private static void DrawDisabledButton(Rect rect, string label)
        {
            GUI.color = Color.gray;
            Widgets.DrawOptionBackground(rect, false);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private sealed class ForwardBaseContractViewer : Window
        {
            private readonly WorldObject contract;
            private readonly string contractInfo;

            public override Vector2 InitialSize => ContractViewerWindowSize;

            public ForwardBaseContractViewer(WorldObject contract, string contractInfo)
            {
                this.contract = contract;
                this.contractInfo = contractInfo ?? string.Empty;
                doCloseX = false;
                closeOnClickedOutside = false;
                absorbInputAroundWindow = true;
                doWindowBackground = false;
                draggable = true;
            }

            public override void DoWindowContents(Rect inRect)
            {
                Widgets.DrawBoxSolid(inRect, Color.white);
                float pageWidth = 740f;
                float pageHeight = pageWidth * (297f / 210f);
                float noteWidth = 120f;
                float noteGap = 14f;
                float compositionWidth = pageWidth + noteGap + noteWidth;
                Rect paper = new Rect((inRect.width - compositionWidth) * 0.5f, 14f, pageWidth, pageHeight);
                Widgets.DrawBoxSolid(paper, Color.white);
                Widgets.DrawBox(paper);
                Rect content = paper.ContractedBy(22f);
                Faction faction = contract?.Faction;
                float y = content.y;

                GUI.color = Color.black;
                Texture2D logo = ContentFinder<Texture2D>.Get(
                    Dialog_TelegraphTable.IsHighProvider(faction) ? "Icons/HD_VEG" : "Icons/HD_POSC",
                    false);
                if (logo != null)
                {
                    GUI.color = Color.white;
                    Widgets.DrawTextureFitted(new Rect(content.x, y, 88f, 88f), logo, 0.9f);
                    GUI.color = Color.black;
                }

                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(content.x + 106f, y + 3f, content.width - 260f, 30f), Dialog_TelegraphTable.ProviderCompanyName(faction));
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(content.x + 106f, y + 38f, content.width - 260f, 22f), "FORWARD BASE SERVICE AGREEMENT");
                Widgets.Label(new Rect(content.x + 106f, y + 61f, content.width - 260f, 22f), "EXECUTED CONTRACT RECORD");
                Widgets.Label(new Rect(content.xMax - 170f, y + 8f, 170f, 22f), "CONTRACT RECORD");
                Widgets.Label(new Rect(content.xMax - 170f, y + 34f, 170f, 22f), contract?.LabelCap ?? "Unknown contract");
                y += 100f;
                Widgets.DrawLineHorizontal(content.x, y, content.width);
                y += 18f;

                Widgets.Label(new Rect(content.x, y, content.width, 22f), "PROVIDER");
                y += 24f;
                Widgets.Label(new Rect(content.x + 12f, y, content.width - 12f, 24f), Dialog_TelegraphTable.ProviderCompanyName(faction) + "  /  " + (faction?.Name ?? "Unknown faction"));
                y += 36f;
                Widgets.Label(new Rect(content.x, y, content.width, 22f), "STATUS");
                y += 24f;
                Widgets.Label(new Rect(content.x + 12f, y, content.width - 12f, 24f), Dialog_TelegraphTable.ForwardBaseContractStatus(contract));
                y += 42f;
                Widgets.DrawLineHorizontal(content.x, y, content.width);
                y += 18f;
                Widgets.Label(new Rect(content.x, y, content.width, 22f), "CONTRACT TERMS");
                y += 30f;
                GUI.color = Color.black;
                float infoHeight = Mathf.Max(22f, Text.CalcHeight(contractInfo, content.width));
                Rect termsRect = new Rect(content.x, y, content.width, infoHeight + 16f);
                Widgets.DrawBox(termsRect);
                Widgets.Label(termsRect.ContractedBy(8f), contractInfo);

                Rect noteRect = new Rect(paper.xMax + noteGap, paper.y + 176f, noteWidth, 126f);
                Widgets.DrawBoxSolid(noteRect, new Color(1f, 0.93f, 0.45f));
                Widgets.DrawBox(noteRect);
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(noteRect.x + 10f, noteRect.y + 10f, noteRect.width - 20f, 24f), "POST-IT");
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(noteRect.x + 10f, noteRect.y + 42f, noteRect.width - 20f, 22f), "TIME REMAINING");
                Widgets.Label(new Rect(noteRect.x + 10f, noteRect.y + 68f, noteRect.width - 20f, 42f), RemainingContractTime());

                Rect closeRect = new Rect(inRect.xMax - 34f, inRect.y + 2f, 30f, 30f);
                Widgets.DrawHighlightIfMouseover(closeRect);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(closeRect, "×");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(closeRect))
                {
                    Close();
                }
                GUI.color = Color.white;
            }

            private string RemainingContractTime()
            {
                int ticksLeft = 0;
                HelodForwardBaseConstruction construction = contract as HelodForwardBaseConstruction;
                if (construction != null)
                {
                    ticksLeft = construction.CompleteTick - (Find.TickManager?.TicksGame ?? 0);
                }
                else
                {
                    HelodForwardBase forwardBase = contract as HelodForwardBase;
                    if (forwardBase != null)
                    {
                        ticksLeft = forwardBase.ContractEndTick - (Find.TickManager?.TicksGame ?? 0);
                    }
                }

                return ticksLeft > 0 ? ticksLeft.ToStringTicksToPeriod() : "Completed";
            }
        }

        private sealed class ContractDocumentSurface
        {
            public Rect Begin(Rect windowRect)
            {
                Widgets.DrawBoxSolid(windowRect, Color.white);
                float pageWidth = Mathf.Min(740f, windowRect.width - 28f);
                float pageHeight = pageWidth * (297f / 210f);
                return new Rect((windowRect.width - pageWidth) * 0.5f, 14f, pageWidth, pageHeight);
            }

            public void End()
            {
            }
        }

        private class ForwardBaseServiceEditor : Window
        {
            private readonly Dialog_TelegraphTable owner;
            private Vector2 scrollPosition;

            public override Vector2 InitialSize => new Vector2(720f, 680f);

            public ForwardBaseServiceEditor(Dialog_TelegraphTable owner)
            {
                this.owner = owner;
                doCloseX = true;
                closeOnClickedOutside = false;
                absorbInputAroundWindow = true;
                doWindowBackground = false;
            }

            public override void DoWindowContents(Rect inRect)
            {
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.DrawWindowBackground(inRect);
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(inRect.x + 16f, inRect.y + 12f, inRect.width - 32f, 30f), "EDIT SELECTED SERVICES");
                Text.Font = GameFont.Small;
                float descriptionHeight = Dialog_TelegraphTable.DrawContractParagraph(new Rect(inRect.x + 16f, inRect.y + 45f, inRect.width - 32f, 60f), "Click a text line to include or remove it. FFP lines also define stock per 30 days.");
                GUI.color = Color.white;

                Rect listRect = new Rect(inRect.x + 16f, inRect.y + 52f + descriptionHeight, inRect.width - 32f, inRect.height - 68f - descriptionHeight);
                Rect viewRect = new Rect(0f, 0f, listRect.width - 18f, 640f);
                Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
                float y = 0f;
                DrawServiceGroup(viewRect, ref y, ForwardBaseServiceType.Artillery, ArtilleryServices);
                DrawServiceGroup(viewRect, ref y, ForwardBaseServiceType.Infantry, InfantryServices);
                DrawServiceGroup(viewRect, ref y, ForwardBaseServiceType.AirForce, AirForceServices);
                DrawServiceGroup(viewRect, ref y, ForwardBaseServiceType.Logistics, LogisticsServices);
                Widgets.EndScrollView();
            }

            private void DrawServiceGroup(Rect viewRect, ref float y, ForwardBaseServiceType type, ForwardBaseService[] services)
            {
                GUI.color = Color.white;
                Widgets.Label(new Rect(viewRect.x, y, viewRect.width, 24f), Dialog_TelegraphTable.ForwardBaseServiceTypeLabel(type).ToUpperInvariant());
                y += 28f;
                float credit = Dialog_TelegraphTable.MilitaryCredit(owner.telegraphTable.Map, owner.SelectedMilitaryFaction());
                for (int i = 0; i < services.Length; i++)
                {
                    ForwardBaseService service = services[i];
                    bool available = Dialog_TelegraphTable.IsServiceAvailableForBase(owner.selectedForwardBaseKind, service)
                        && credit >= Dialog_TelegraphTable.RequiredCredit(service)
                        && (service != ForwardBaseService.W48Support || owner.includeArtillery155mmSupport);
                    Rect row = new Rect(viewRect.x, y, viewRect.width, 32f);
                    if (!available)
                    {
                        GUI.color = new Color(0.22f, 0.22f, 0.22f);
                        Widgets.Label(new Rect(row.x + 10f, row.y + 6f, row.width - 20f, 20f), "- " + Dialog_TelegraphTable.ForwardBaseServiceLabel(service) + "  (unavailable)");
                        GUI.color = Color.white;
                        y += 38f;
                        continue;
                    }

                    Widgets.DrawHighlightIfMouseover(row);
                    float countWidth = owner.selectedContractCostKind == ContractCostKind.FFP ? 116f : 0f;
                    bool selected = owner.IsServiceSelected(service);
                    Rect checkRect = new Rect(row.x + 6f, row.y, row.width - countWidth - 12f, row.height);
                    Widgets.CheckboxLabeled(checkRect, Dialog_TelegraphTable.ForwardBaseServiceLabel(service), ref selected);
                    owner.SetServiceSelected(service, selected);

                    if (owner.selectedContractCostKind == ContractCostKind.FFP && owner.IsServiceSelected(service))
                    {
                        int units = owner.FfpServiceUnitCount(service);
                        string buffer = owner.FfpServiceUnitBuffer(service);
                        Widgets.Label(new Rect(row.xMax - countWidth, row.y + 6f, 60f, 20f), "per 30d");
                        Widgets.TextFieldNumeric(new Rect(row.xMax - 48f, row.y + 3f, 48f, 26f), ref units, ref buffer, 1, 999999);
                        owner.ffpServiceUnitCounts[service] = Mathf.Clamp(units, 1, 999999);
                        owner.ffpServiceUnitBuffers[service] = buffer;
                    }
                    GUI.color = Color.white;
                    y += 38f;
                }
                GUI.color = Color.white;
                y += 8f;
            }
        }

        private enum TelegraphTab
        {
            Orders,
            Military,
            Market
        }

        private enum MilitaryActivity
        {
            None,
            ForwardBaseContract,
            ForwardBaseContracts
        }

        private enum ForwardBaseKind
        {
            PB,
            FB,
            COP,
            FOB
        }

        private enum ContractCostKind
        {
            FFP,
            CostReimbursement
        }

        private enum ContractDuration
        {
            Days30,
            Days60,
            Days120,
            Days240
        }

        private enum ForwardBaseServiceType
        {
            Artillery,
            Infantry,
            AirForce,
            Logistics
        }

        private enum MarketSubTab
        {
            Log,
            Trade
        }
    }
}
