using RimWorld;
using Verse;

namespace Helodrace;

public sealed class ConditionalStatAffecter_PolarField : ConditionalStatAffecter
{
    public const float MaximumTemperature=10f;
    public override string Label=>"HD_PolarField_Condition".Translate();
    public override bool Applies(StatRequest req)
    {
        var pawn=req.Thing as Pawn;
        return pawn?.Spawned==true&&!pawn.Position.Roofed(pawn.Map)&&pawn.AmbientTemperature<=MaximumTemperature;
    }
}

public sealed class ConditionalStatAffecter_HelodComrades : ConditionalStatAffecter
{
    public const int Radius=8;
    public override string Label=>"HD_HelodComrades_Condition".Translate();
    public override bool Applies(StatRequest req)
    {
        var pawn=req.Thing as Pawn;
        if(pawn?.Spawned!=true||pawn.Dead||pawn.Downed||pawn.Faction==null)return false;
        foreach(var other in pawn.Map.mapPawns.AllPawnsSpawned)
            if(other!=pawn&&!other.Dead&&!other.Downed&&other.Faction==pawn.Faction&&HelodRace.IsHelod(other)
                &&other.Position.DistanceToSquared(pawn.Position)<=Radius*Radius)return true;
        return false;
    }
}
