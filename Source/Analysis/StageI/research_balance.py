"""Run from repository root; reproduce budgets, native comparisons and charts.
Use --check to verify current definitions and the captured native after snapshot.
"""
import json, math, xml.etree.ElementTree as E
from pathlib import Path
old={x['name']:x for x in json.loads(Path('Docs/Data/StageI/research_before.json').read_text(encoding='utf-8'))}
# Infrastructure milestones and specialized follow-on projects are budgeted separately.
values={
'HelodFrontierSpirit':100,'HelodMechanicalEngineering':500,'HelodExperimentalAnalysis':500,
'HelodIronMaking':1000,'HelodSteamEngine':1200,'HelodBasicMachineTool':1000,'HelodIngredientExtraction':700,'HelodPrimaryCell':600,'HelodBasicOilDevelopment':900,'HelodLineShaft':1000,'HelodTelegraph':600,'HelodMetalCuttingMachine':1000,'HelodMetalFormingMachine':1000,'HelodBreechLoadingGun':700,'HelodBasicEngine':1400,'HelodLargeSteamEngine':900,'HelodHeatPump':700,'HelodBasicPetrochemistry':1200,'HelodHighOilDevelopment':1100,'HelodTotalSynthesisMedicine':1200,'HelodBasicElectronics':1000,'HelodBasicMotor':1200,'HelodWireless':1000,'HelodLeverAction':600,'HelodSmokeLessGunpowder':1000,'HelodBoltAction':650,'HelodChokingAgent':650,'HelodFirstAid':600,'HelodBasicMachineGun':800,'HelodSamiAutomaticPistol':650,'HelodHighElectronics':1500,'HelodHighMachineGun':800,'HelodHaberBosch':1600,'HelodBasicModernRifle':850,'HelodHighExplosive':1000,'HelodBlisterAgent':700,'HelodSniperRifle':700,'HelodLowGranade':600,'HelodLowLightArtillery':900,'HelodLowHeavyArtillery':1200,'HelodRespectForPatrioticMartyrs':600,'HelodGreatWarTech':1200,'HelodLowAutomaticRifle':900,'HelodTransistor':2200,'HelodAntiAirMachineGun':1200,'HelodMidAutomaticRifle':1000,'HelodHighAutomaticRifle':1100,'HelodWireGuidedMissile':1600,'HelodColdWarTech':1500,'HelodLessLethalLowRanged':650,'HelodLessLethalClose':600,'HelodLessLethalChemical':700,'HelodSpecialOperationsWeapons':1100,'HelodFlakJacket':900,'HelodPASGT':1200,'HelodLowHeadSet':700,'HelodModernDiversionaryGrenades':1000,'HelodModernOffensiveGrenades':1200,'HelodAdvancedWeaponOptics':1600,'HelodLaserTargetDesignators':1800,'HelodLowVelocityGrenades':800,'HelodAdvancedAmmunition':1400,'HelodRemoteWeaponControl':1600,'HelodNumericalControl':1800,'HelodIndustrialAutomation':2200,'HelodMultiTaskMachining':2800}
assert set(values)=={n for n in old if n.startswith('Helod')}
links=[]
for x in E.parse('Defs/Research/ResearchLinks_Helod.xml').getroot():
 links.append(([i.text for i in x.findall('leftProjects/li')],[i.text for i in x.findall('rightProjects/li')]))
def ancestors(names):
 out=set()
 def add(n):
  if n in out:return
  out.add(n)
  for p in old[n]['prerequisites']:add(p)
 for n in names:add(n)
 return out
def close(known):
 while True:
  before=set(known)
  known |= ancestors(known)
  for left,right in links:
   if set(left)<=known: known |= ancestors(right)
   if set(right)<=known: known |= ancestors(left)
  if known==before:return known
free=close({n for n,x in old.items() if 'HD_HelodWildWestScenarioStart' in x['tags']})
new={n:x['baseCost'] for n,x in old.items()};new.update(values)
# Linked vanilla packages buy the additional Helod knowledge granted by the link,
# after their vanilla prerequisites (and their grants) have already been earned.
linked=[]
for left,right in links:
 if len(left)!=1:continue
 n=left[0]; known=close(free | ancestors(old[n]['prerequisites'])); paid=[]
 def study(n):
  if n in known:return
  for p in old[n]['prerequisites']:study(p)
  if n in known:return
  paid.append(n);known.add(n);close(known)
 for r in right:study(r)
 extra=sum(new[p] for p in paid)
 new[n]=max(old[n]['baseCost'], math.ceil(extra/50)*50)
 linked.append(dict(name=n,base_before=old[n]['baseCost'],base_after=new[n],additional_projects=paid,additional_budget=extra))
standalone={'Batteries':700,'SolarPanels':1000,'WatermillGenerator':1000,'GeothermalPower':4000,'BiofuelRefining':1000,'Autodoors':800,'Hydroponics':1000,'HospitalBed':1600,'MultiAnalyzer':5000,'Fabrication':5000,'AdvancedFabrication':6000,'DeepDrilling':1800,'GroundPenetratingScanner':2000,'LongRangeMineralScanner':3000,'TransportPod':1500,'GunTurrets':800,'HeavyTurrets':2200,'IEDs':800,'MoisturePump':1500,'ShieldBelt':1500,'ChargedShot':4000,'ReconArmor':7000,'PoweredArmor':8000,'Bionics':3000}
for n,c in standalone.items():
 assert n in old and old[n]['source']=='ludeon.rimworld'
 assert n not in {x['name'] for x in linked}
 new[n]=c
changes=[dict(name=n,label=old[n]['label'],source=old[n]['source'],base_before=old[n]['baseCost'],base_after=new[n],new_helod_apparent=new[n]*old[n]['tribalFactor'],old_helod_apparent=old[n]['helodScenarioCost'],prerequisites=old[n]['prerequisites']) for n in old if new[n]!=old[n]['baseCost']]
Path('Docs/Data/StageI/research_policy.json').write_text(json.dumps(dict(values=values,changes=changes,linked=linked,free=sorted(free)),ensure_ascii=False,indent=2),encoding='utf-8')
print('Helod total',sum(old[n]['baseCost'] for n in values),'->',sum(values.values()))
print('changes',len(changes),'linked',len(linked),'standalone',len(standalone),'free',sorted(free))
for x in linked:print(x['name'],x['base_before'],'->',x['base_after'],'extra',x['additional_budget'])
# Export a complete review table and milestone comparison from immutable before/after snapshots.
import csv,argparse
parser=argparse.ArgumentParser(description='Reproduce the Stage I research budget policy and comparison')
parser.add_argument('--check',action='store_true',help='Also verify current XML and loaded after snapshot')
args=parser.parse_args()
review=[]
for n,x in old.items():
 if x['source'] not in ('ludeon.rimworld','bluebackedfish.helodrace.main'):continue
 review.append(dict(name=n,label=x['label'],source=x['source'],base_before=x['baseCost'],base_after=new[n],
                    helod_work_before=x['helodScenarioCost'],helod_work_after=new[n]*x['tribalFactor'],
                    start_completed=n in free,reason='linked knowledge package' if n in {p['name'] for p in linked} else 'industrial infrastructure' if n in standalone else 'Helod milestone/derivative' if n in values else 'native base retained'))
with Path('Docs/Data/StageI/research_comparison.csv').open('w',encoding='utf-8-sig',newline='') as stream:
 writer=csv.DictWriter(stream,fieldnames=list(review[0]));writer.writeheader();writer.writerows(review)
def spend(targets,cost):
 known=close(set(free));paid=[]
 def study(n):
  if n in known:return
  for p in old[n]['prerequisites']:study(p)
  if n in known:return
  paid.append(n);known.add(n);close(known)
 for n in targets:study(n)
 return sum(cost[n] for n in paid),paid
before={n:x['helodScenarioCost'] for n,x in old.items()}
after={n:new[n]*x['tribalFactor'] for n,x in old.items()}
route_checks=[]
for left,right in links:
 if len(left)!=1:continue
 a,_=spend(left,after);b,_=spend(right,after)
 assert a==b,(left,a,b)
 route_checks.append(dict(vanilla=left[0],helod=right,vanilla_work=a,helod_work=b))
milestones=[]
for n in ['HelodBasicOilDevelopment','HelodHighOilDevelopment','HelodBasicPetrochemistry','HelodHighElectronics','HelodNumericalControl','HelodTransistor','HelodIndustrialAutomation','HelodMultiTaskMachining']:
 a,_=spend([n],before);b,paid=spend([n],after)
 milestones.append(dict(name=n,before_work=a,after_work=b,normalized_full_days_before=a/495,normalized_full_days_after=b/495,paid_projects=paid))
Path('Docs/Data/StageI/research_routes.json').write_text(json.dumps(dict(routes=route_checks,milestones=milestones),ensure_ascii=False,indent=2),encoding='utf-8')
if args.check:
 current={}
 for p in Path('Defs/Research').rglob('*.xml'):
  for d in E.parse(p).getroot().findall('ResearchProjectDef'):
   if d.findtext('defName') in values:current[d.findtext('defName')]=float(d.findtext('baseCost'))
 assert current==values
 patched={}
 for d in E.parse('Patches/ResearchCosts_Helod.xml').getroot().findall('Operation/operations/li'):
  name=d.findtext('xpath').split('defName="')[1].split('"')[0]
  patched[name]=float(d.findtext('value/baseCost'))
 assert patched=={n:new[n] for n in old if old[n]['source']=='ludeon.rimworld' and new[n]!=old[n]['baseCost']}
 loaded={x['name']:x for x in json.loads(Path('Docs/Data/StageI/research_after.json').read_text(encoding='utf-8'))}
 for row in review:
  actual=loaded[row['name']]
  assert actual['baseCost']==row['base_after'] and actual['helodScenarioApparent']==row['helod_work_after'],(row,actual)
 print('PASS current XML, 143 native before/after rows, 14 equal milestone routes.')
# Standalone scientific chart; all plotted points come from the exported comparison.
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib import font_manager
font=Path('C:/Windows/Fonts/malgun.ttf')
if font.exists():font_manager.fontManager.addfont(str(font));plt.rcParams['font.family']='Malgun Gothic'
plt.rcParams['axes.unicode_minus']=False
fig,axes=plt.subplots(1,2,figsize=(14,7),layout='constrained')
selected=['Electricity','Machining','MicroelectronicsBasics','MedicineProduction','PrecisionRifling','MultibarrelWeapons','GeothermalPower','Fabrication']
labels=['전기','기계 가공','마이크로일렉트로닉스','의약품 생산','정밀 강선','다총열 무기','지열 발전','부품 제작']
for side,key,color in [(0,'before','#a76a26'),(1,'after','#197d92')]:
 axes[0].barh([i+side*.36 for i in range(len(selected))],[old[n]['baseCost'] if key=='before' else new[n] for n in selected],height=.36,color=color,label=['기존 기본값','변경 기본값'][side])
axes[0].set(yticks=[i+.18 for i in range(len(labels))],yticklabels=labels,title='바닐라 대표 연구 · 개별 기본량',xlabel='연구점');axes[0].invert_yaxis();axes[0].legend()
mlabels=['목제 펌프 해금','철제 펌프 해금','석유화학','진공관 / 전기','수치제어 / 기계 가공','트랜지스터 / 마이크로','산업 자동화','복합 가공']
for side,key,color in [(0,'before_work','#a76a26'),(1,'after_work','#197d92')]:
 axes[1].barh([i+side*.36 for i in range(len(milestones))],[x[key] for x in milestones],height=.36,color=color,label=['기존 시나리오','변경 시나리오'][side])
axes[1].set(yticks=[i+.18 for i in range(len(mlabels))],yticklabels=mlabels,title='헤로드 경로 · 시작 완료 제외 누적 실효 작업량',xlabel='연구점 상당 작업량');axes[1].invert_yaxis();axes[1].legend()
for ax in axes:ax.grid(axis='x',alpha=.2);ax.set_axisbelow(True)
fig.suptitle('산업 연구 예산과 부족민식 기술 보정',fontsize=19)
fig.savefig('Docs/Data/StageI/research_comparison.png',dpi=160,bbox_inches='tight');plt.close(fig)
print('Core total base',sum(x['baseCost'] for x in old.values() if x['source']=='ludeon.rimworld'),'->',sum(new[n] for n,x in old.items() if x['source']=='ludeon.rimworld'))
for x in milestones:print(x['name'],x['before_work'],'->',x['after_work'])
