"""Medieval-baseline research rebalance for Helod scenarios (2026-11-06).

Run from the repository root.
  python Source/Analysis/ResearchMedieval/research_rebalance.py          report only
  python Source/Analysis/ResearchMedieval/research_rebalance.py --write  update XML
  python Source/Analysis/ResearchMedieval/research_rebalance.py --check  verify XML

Helod scenarios research like a Medieval colony: the engine's
CostFactor(Medieval) is 1 for Medieval and earlier and 1.5 for Industrial and
later (the engine caps the gap at Industrial). Helod base costs rise with the
era of the knowledge. Vanilla projects linked to Helod knowledge cost the Helod
knowledge they grant, so neither tree is a shortcut. Other vanilla projects keep
their base-game cost.
"""
import argparse, json, math, re, xml.etree.ElementTree as E
from pathlib import Path

FACTOR = 1.5        # CostFactor(Medieval) for Industrial and later research.
OLD_FACTOR = 2.0    # CostFactor(Neolithic) used before this change.
DAY = 495           # Research points per 24 h at speed 1 (Docs/I_Research_Balance.md).

# Era tiers. Start-completed foundations keep their values.
values = {
    # Start (completed by the scenario through its vanilla links).
    'HelodFrontierSpirit': 100, 'HelodMechanicalEngineering': 500, 'HelodExperimentalAnalysis': 500,
    # Frontier: first power, metal, cells, oil and breech-loading arms.
    'HelodIronMaking': 600, 'HelodSteamEngine': 700, 'HelodBasicMachineTool': 500, 'HelodPrimaryCell': 400,
    'HelodIngredientExtraction': 500, 'HelodTelegraph': 500, 'HelodLineShaft': 600,
    'HelodBreechLoadingGun': 500, 'HelodBasicOilDevelopment': 700,
    # Late frontier industry.
    'HelodMetalCuttingMachine': 800, 'HelodMetalFormingMachine': 800, 'HelodLeverAction': 600,
    'HelodHeatPump': 700, 'HelodLargeSteamEngine': 900, 'HelodBasicEngine': 1100,
    'HelodHighOilDevelopment': 1000, 'HelodBasicPetrochemistry': 1100, 'HelodBasicElectronics': 800,
    'HelodBasicMotor': 1000, 'HelodSmokeLessGunpowder': 900, 'HelodBoltAction': 700,
    'HelodTotalSynthesisMedicine': 1100, 'HelodFirstAid': 600, 'HelodChokingAgent': 700,
    # Great War.
    'HelodHighElectronics': 1500, 'HelodWireless': 1000, 'HelodHaberBosch': 1500, 'HelodHighExplosive': 1100,
    'HelodBasicMachineGun': 900, 'HelodHighMachineGun': 1000, 'HelodSamiAutomaticPistol': 800,
    'HelodBasicModernRifle': 1100, 'HelodGreatWarTech': 1300, 'HelodLowGranade': 800,
    'HelodLowLightArtillery': 1100, 'HelodLowHeavyArtillery': 1400, 'HelodBlisterAgent': 900,
    'HelodSniperRifle': 900, 'HelodRespectForPatrioticMartyrs': 800, 'HelodNumericalControl': 1800,
    # Cold War.
    'HelodTransistor': 2200, 'HelodLowAutomaticRifle': 1400, 'HelodAntiAirMachineGun': 1600,
    'HelodMidAutomaticRifle': 1500, 'HelodWireGuidedMissile': 2000, 'HelodColdWarTech': 1800,
    'HelodLessLethalClose': 900, 'HelodLessLethalLowRanged': 1000, 'HelodLessLethalChemical': 1100,
    'HelodSpecialOperationsWeapons': 1500, 'HelodFlakJacket': 1200, 'HelodLowHeadSet': 1000,
    'HelodLowVelocityGrenades': 1100, 'HelodIndustrialAutomation': 2500,
    # Modern.
    'HelodHighAutomaticRifle': 1800, 'HelodPASGT': 1800, 'HelodModernDiversionaryGrenades': 1400,
    'HelodModernOffensiveGrenades': 1500, 'HelodAdvancedWeaponOptics': 2000,
    'HelodLaserTargetDesignators': 2400, 'HelodAdvancedAmmunition': 2000,
    'HelodRemoteWeaponControl': 2400, 'HelodMultiTaskMachining': 3200,
}

snapshot = {x['name']: x for x in json.loads(Path('Docs/Data/StageI/research_before.json').read_text(encoding='utf-8'))}
vanilla = {n: x for n, x in snapshot.items() if x['source'] == 'ludeon.rimworld'}

helod_files = sorted(Path('Defs/Research').glob('*.xml'))
helod = {}
for path in helod_files:
    for d in E.parse(path).getroot().iter('ResearchProjectDef'):
        name = d.findtext('defName')
        if name:
            helod[name] = dict(cost=float(d.findtext('baseCost')), tech=d.findtext('techLevel'),
                               prereqs=[x.text for x in d.findall('prerequisites/li')],
                               tags=[x.text for x in d.findall('tags/li')], path=path)
assert set(values) == set(helod), set(values) ^ set(helod)
assert all(h['tech'] == 'Industrial' for h in helod.values())

prereqs = {n: x['prerequisites'] for n, x in vanilla.items()}
prereqs.update({n: h['prereqs'] for n, h in helod.items()})
links = [([i.text for i in x.findall('leftProjects/li')], [i.text for i in x.findall('rightProjects/li')])
         for x in E.parse('Defs/Research/ResearchLinks_Helod.xml').getroot()]


def ancestors(names):
    out = set()
    def add(n):
        if n in out: return
        out.add(n)
        for p in prereqs[n]: add(p)
    for n in names: add(n)
    return out


def close(known):
    while True:
        before = set(known)
        known |= ancestors(known)
        for left, right in links:
            if set(left) <= known: known |= ancestors(right)
            if set(right) <= known: known |= ancestors(left)
        if known == before: return known


# Start tags are added by Patches/StartingResearch_Helod.xml, so read them from the loaded snapshot.
free = close({n for n, x in snapshot.items() if 'HD_HelodWildWestScenarioStart' in x['tags']})


def spend(targets, cost, known=None):
    known = close(set(free) if known is None else set(known))
    paid = []
    def study(n):
        if n in known: return
        for p in prereqs[n]: study(p)
        if n in known: return
        paid.append(n); known.add(n); close(known)
    for n in targets: study(n)
    return sum(cost[n] for n in paid), paid


# Linked vanilla packages buy the Helod knowledge they grant beyond their own prerequisites.
base = {n: x['baseCost'] for n, x in vanilla.items()}
base.update(values)
budgets = {}
for left, right in links:
    if len(left) != 1: continue
    n = left[0]
    extra, _ = spend(right, base, free | ancestors(prereqs[n]))
    cost = max(vanilla[n]['baseCost'], math.ceil(extra / 50) * 50)
    if cost != vanilla[n]['baseCost']: budgets[n] = cost
base.update(budgets)

old_budget = {d.tag: float(d.text) for d in E.parse('Defs/Scenarios/ResearchBudgets_Helod.xml').getroot()
              .find('Helodrace.HelodScenarioResearchBudgetDef/baseCosts')}
old_base = {n: x['baseCost'] for n, x in vanilla.items()}
old_base.update(old_budget)
old_base.update({n: h['cost'] for n, h in helod.items()})

for left, right in links:
    if len(left) != 1: continue
    a, _ = spend(left, base); b, _ = spend(right, base)
    assert a == b, (left, a, b)


def report():
    print('Helod base total', int(sum(h['cost'] for h in helod.values())), '->', sum(values.values()))
    print('\nMilestone work (start completed excluded), normalized 24 h days at speed 1')
    for n in ['HelodSteamEngine', 'HelodTelegraph', 'HelodBasicOilDevelopment', 'HelodLineShaft', 'HelodBoltAction',
              'HelodBasicPetrochemistry', 'HelodHighElectronics', 'HelodNumericalControl', 'HelodTransistor',
              'HelodIndustrialAutomation', 'HelodMultiTaskMachining', 'HelodLaserTargetDesignators']:
        a, _ = spend([n], old_base); b, _ = spend([n], base)
        print(f'  {n:34} {a*OLD_FACTOR:8.0f} -> {b*FACTOR:8.0f}   {a*OLD_FACTOR/DAY:6.1f} -> {b*FACTOR/DAY:6.1f} days')
    a, _ = spend(list(values), old_base); b, _ = spend(list(values), base)
    print(f'  {"whole Helod tree":34} {a*OLD_FACTOR:8.0f} -> {b*FACTOR:8.0f}   {a*OLD_FACTOR/DAY:6.1f} -> {b*FACTOR/DAY:6.1f} days')
    print('\nVanilla base budgets in Helod scenarios (apparent work = base x factor)')
    for n in sorted(set(old_budget) | set(budgets)):
        o = old_budget.get(n, vanilla[n]['baseCost']); c = budgets.get(n, vanilla[n]['baseCost'])
        tag = 'linked' if n in budgets else 'vanilla'
        print(f'  {n:26} vanilla {vanilla[n]["baseCost"]:5}  budget {o:7.0f} -> {c:6}  work {o*OLD_FACTOR:7.0f} -> {c*FACTOR:7.0f}  {tag}')


def write():
    for path in helod_files:
        text = path.read_text(encoding='utf-8')
        def rep(m):
            name = m.group(2)
            return m.group(0) if name not in values else re.sub(
                r'<baseCost>[^<]*</baseCost>', f'<baseCost>{values[name]}</baseCost>', m.group(0), count=1)
        new = re.sub(r'<ResearchProjectDef>\s*<defName>(?P<n>(\w+))</defName>.*?</ResearchProjectDef>',
                     lambda m: rep(m), text, flags=re.S)
        if new != text: path.write_bytes(new.encode('utf-8'))
    p = Path('Defs/Scenarios/ResearchBudgets_Helod.xml')
    text = p.read_text(encoding='utf-8')
    body = ''.join(f'      <{n}>{c}</{n}>\n' for n, c in sorted(budgets.items()))
    text = re.sub(r'(<baseCosts>\n).*?(\s*</baseCosts>)', lambda m: m.group(1) + body.rstrip('\n') + m.group(2), text, flags=re.S)
    p.write_bytes(text.encode('utf-8'))
    # Policy read by the actual-game checks (Source/Tests/StageI/ResearchChecks.cs).
    changes = [dict(name=n, source='ludeon.rimworld', base_before=vanilla[n]['baseCost'], base_after=c)
               for n, c in sorted(budgets.items())]
    changes += [dict(name=n, source='bluebackedfish.helodrace.main', base_before=helod[n]['cost'], base_after=c)
                for n, c in sorted(values.items())]
    # Every Core project without a linked budget, including the ones restored from the earlier raised budgets.
    unlinked = [dict(name=n, base=vanilla[n]['baseCost']) for n in sorted(vanilla) if n not in budgets]
    out = Path('Docs/Data/ResearchMedieval'); out.mkdir(parents=True, exist_ok=True)
    (out / 'research_policy.json').write_text(json.dumps(dict(factor=FACTOR, changes=changes, restored_vanilla=unlinked),
                                                         ensure_ascii=False, indent=2), encoding='utf-8')


parser = argparse.ArgumentParser()
parser.add_argument('--write', action='store_true')
parser.add_argument('--check', action='store_true')
args = parser.parse_args()
if args.write: write()
if args.check:
    assert {n: h['cost'] for n, h in helod.items()} == values, 'Helod base costs differ from this policy'
    assert old_budget == budgets, 'ResearchBudgets_Helod.xml differs from this policy'
    print('PASS: Helod costs, linked vanilla budgets and equal linked routes')
report()
