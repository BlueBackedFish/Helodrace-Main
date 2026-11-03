"""Render native T2-32 benchmark data. Does not edit game balance definitions."""
from pathlib import Path
import argparse
import csv
import json
import math
import statistics as st
import shutil
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager
from matplotlib.lines import Line2D

ROOT=Path(__file__).resolve().parents[3]
GUNS=["HD_Gun_WinM1887_Weapon","HD_Gun_M1897_Weapon"]
NAMES={GUNS[0]:"M1887",GUNS[1]:"M1897"}
ARMORS=["비장갑","방탄조끼 + 강철 간이투구","해병갑옷 + 해병투구"]
COLORS=["#217b91","#c98b2c","#785aa3"]
MODES=["일반","명사수 속사"]

def mean_ci(values):
    mean=st.mean(values)
    half=1.96*st.stdev(values)/math.sqrt(len(values)) if len(values)>1 else 0
    return mean,max(0,mean-half),mean+half

def wilson(values):
    n=len(values);p=sum(values)/n;z=1.96
    mid=(p+z*z/(2*n))/(1+z*z/n)
    half=z*math.sqrt(p*(1-p)/n+z*z/(4*n*n))/(1+z*z/n)
    return p,max(0,mid-half),min(1,mid+half)

def main():
    parser=argparse.ArgumentParser();parser.add_argument("--evidence",type=Path,required=True)
    parser.add_argument("--out",type=Path,default=ROOT/"Docs/Data/T2_32");args=parser.parse_args()
    status=(args.evidence/"status.txt").read_text(encoding="utf-8-sig")
    assert status.rstrip().endswith("COMPLETE") and "FAIL " not in status
    specs=json.loads((args.evidence/"specs.json").read_text(encoding="utf-8-sig"))
    raw=list(csv.DictReader((args.evidence/"shots.csv").open(encoding="utf-8-sig")))
    assert len(specs)==36 and sum(s["available"] for s in specs)==27
    result=[]
    for spec in specs:
        rows=[r for r in raw if int(r["case"])==spec["index"]]
        out={k:spec[k] for k in ["index","weapon","alt","armor","distance","available","range","warmup_seconds","cooldown_seconds","cycle_seconds","aim_chance","pellets","spread_sigma_degrees","base_damage","base_AP"]}
        out["samples"]=len(rows)
        if spec["available"]:
            assert len(rows)>=8 and len({int(r["repeat"]) for r in rows})==len(rows)
            assert all(int(r["launched_pellets"])==9 and 0<=int(r["damaging_pellets"])<=int(r["hits"])<=9 for r in rows)
            out["damage_budget_overruns"]=sum(float(r["damage"])>float(r["damage_budget"])+.05 for r in rows)
            for key in ["damage","hits","damaging_pellets"]:
                avg,lo,hi=mean_ci([float(r[key]) for r in rows]);out[key+"_mean"]=avg;out[key+"_ci_low"]=lo;out[key+"_ci_high"]=hi
            for key in ["down_or_dead","dead","vital_injury"]:
                avg,lo,hi=wilson([r[key].lower()=="true" for r in rows]);out[key+"_rate"]=avg;out[key+"_ci_low"]=lo;out[key+"_ci_high"]=hi
            out["duplicate_damage_pellets"]=sum(int(r["duplicate_damage_pellets"]) for r in rows)
            out["damage_budget"]=float(rows[0]["damage_budget"])
            for name in ["mean","ci_low","ci_high"]:out["modeled_DPS_"+name]=out["damage_"+name]/out["cycle_seconds"]
        else:assert len(rows)==0
        result.append(out)
    valid=[r for r in result if r["available"]];counts={r["samples"] for r in valid};assert len(counts)==1
    samples=counts.pop();assert len(raw)==27*samples
    outdir=args.out;outdir.mkdir(parents=True,exist_ok=True)
    for name in ["shots.csv","specs.json","status.txt"]:shutil.copyfile(args.evidence/name,outdir/name)
    (outdir/"summary.json").write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    fields=list(dict.fromkeys(k for row in result for k in row))
    with (outdir/"summary.csv").open("w",encoding="utf-8-sig",newline="") as stream:
        writer=csv.DictWriter(stream,fieldnames=fields);writer.writeheader();writer.writerows(result)
    font=Path("C:/Windows/Fonts/malgun.ttf")
    if font.exists():font_manager.fontManager.addfont(str(font));plt.rcParams["font.family"]="Malgun Gothic"
    plt.rcParams["axes.unicode_minus"]=False
    fig,axes=plt.subplots(2,3,figsize=(14,8),layout="constrained")
    for i,gun in enumerate(GUNS):
        for armor,ax in enumerate(axes[i]):
            for alt,color in [(False,"#217b91"),(True,"#dc8736")]:
                for x,distance in enumerate([3,7,15]):
                    r=next(r for r in result if (r["weapon"],r["armor"],r["alt"],r["distance"])==(gun,armor,alt,distance))
                    xpos=x+(.18 if alt else -.18)
                    if r["available"]:
                        avg=r["damage_mean"];ax.bar(xpos,avg,width=.34,color=color,label=MODES[alt] if x==0 else None)
                        ax.errorbar(xpos,avg,yerr=[[avg-r["damage_ci_low"]],[r["damage_ci_high"]-avg]],fmt="none",color="#26363b",capsize=3,linewidth=1)
                        ax.text(xpos,r["damage_ci_high"]+1,f"{avg:.1f}",ha="center",fontsize=8)
                    else:ax.text(xpos,2,"사거리 밖",ha="center",va="bottom",rotation=90,fontsize=8,color="#777777")
            ax.set(title=f"{NAMES[gun]} · {ARMORS[armor]}",xticks=range(3),xticklabels=["3칸","7칸","15칸"],ylim=(0,52),xlim=(-.6,2.6),ylabel="한 발의 실제 피해 합계")
            ax.grid(axis="y",alpha=.18);ax.set_axisbelow(True)
    axes[0,0].legend(fontsize=9)
    fig.suptitle("T2-32 · 발당 데미지 비교",fontsize=19)
    fig.text(.5,-.018,f"실제 1.7 · 조건당 {samples}회 · 동일 사수 사격 10 / 보통 품질 / 정지 표적 · 막대=평균, 오차막대=평균의 근사 95% CI",ha="center",fontsize=10)
    fig.savefig(outdir/"damage.png",dpi=160,bbox_inches="tight");plt.close(fig)
    fig,axes=plt.subplots(2,3,figsize=(14,8))
    fig.subplots_adjust(left=.065,right=.99,top=.9,bottom=.19,wspace=.28,hspace=.37)
    columns=[("modeled_DPS","mean","조준·쿨다운 환산 DPS","실제 평균 피해 / 사격 주기"),("down_or_dead","rate","1발 즉시 무력화율 · 사망 포함","비율 (%)"),("dead","rate","1발 즉시 사망률","비율 (%)")]
    for i,gun in enumerate(GUNS):
        for col,(key,mean,title,ylabel) in enumerate(columns):
            ax=axes[i,col]
            for armor,color in enumerate(COLORS):
                for alt,style in [(False,"-"),(True,"--")]:
                    rr=[r for r in valid if (r["weapon"],r["armor"],r["alt"])==(gun,armor,alt)]
                    scale=100 if key!="modeled_DPS" else 1
                    xx=[r["distance"]+.11*(armor-1)+(.035 if alt else -.035) for r in rr];yy=[r[key+"_"+mean]*scale for r in rr]
                    err=[[r[key+"_"+mean]*scale-r[key+"_ci_low"]*scale for r in rr],[r[key+"_ci_high"]*scale-r[key+"_"+mean]*scale for r in rr]]
                    ax.errorbar(xx,yy,yerr=err,color=color,linestyle=style,marker="s" if alt else "o",markersize=5,capsize=2,linewidth=1.7,alpha=.9)
            ax.set(title=f"{NAMES[gun]} · {title}",xlabel="거리 (칸)",ylabel=ylabel,xticks=[3,7,15],xlim=(2,16))
            ax.set_ylim(bottom=0)
            if key!="modeled_DPS":
                upper=max(10,math.ceil(max(r[key+"_ci_high"] for r in valid)*10)*10)
                ax.set_ylim(0,upper)
            ax.grid(alpha=.18)
    handles=[Line2D([0],[0],color=c,lw=2,label=a) for a,c in zip(ARMORS,COLORS)]+[Line2D([0],[0],color="#333333",linestyle=s,marker=m,label=n) for s,m,n in [("-","o","일반"),("--","s","명사수 속사")]]
    fig.legend(handles=handles,loc="lower center",bbox_to_anchor=(.5,.055),ncol=5,fontsize=9)
    fig.suptitle("T2-32 · 성능 비교",fontsize=19)
    fig.text(.5,.015,"DPS는 발당 실측 피해와 엔진의 조준+쿨다운으로 환산 · 재장전/장기 교전 실측 아님 · 비율 오차막대=Wilson 95% CI · 사거리 밖은 점 없음",ha="center",fontsize=9)
    fig.savefig(outdir/"performance.png",dpi=160,bbox_inches="tight");plt.close(fig)
    print(f"PASS: {len(raw)} native shots, 27 valid / 9 out-of-range conditions, {samples} repeats; damage budgets audited.")
    print("Shell damage-budget overruns:",sum(r["damage_budget_overruns"] for r in valid))
    print("Duplicate positive damage calls per pellet:",sum(r["duplicate_damage_pellets"] for r in valid))
    for gun in GUNS:
        for alt in [False,True]:
            r=next(r for r in valid if r["weapon"]==gun and r["alt"]==alt and r["armor"]==0 and r["distance"]==3)
            print(NAMES[gun],MODES[alt],"warmup",r["warmup_seconds"],"cooldown",r["cooldown_seconds"],"damage",r["damage_mean"],"DPS",r["modeled_DPS_mean"])

if __name__=="__main__":main()
