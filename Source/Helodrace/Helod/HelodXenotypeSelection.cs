using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace;

public static class HelodXenotypeSelection
{
    public static XenotypeDef Choose(PawnKindDef kind,Faction faction)
    {
        var choices=PawnGenerator.XenotypesAvailableFor(kind,faction?.def??kind.defaultFactionDef,faction);
        if(choices.Where(entry=>entry.Value>0&&HelodRace.XenotypeSet.Contains(entry.Key))
            .TryRandomElementByWeight(entry=>entry.Value,out var selected))return selected.Key;
        return HelodRace.HelodXenotypes.RandomElement();
    }
}
