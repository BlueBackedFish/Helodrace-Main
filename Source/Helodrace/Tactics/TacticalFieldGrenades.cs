using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalFieldResponse
    {
        public int GrenadeCheckAt, GrenadeMemberCursor, GrenadeContactCursor;
    }

    public static class TacticalFieldGrenadePolicy
    {
        public const int CheckInterval = 15, ThrowInterval = 600, ContactLimit = 4;
        public static bool Lethal(ThingDef item) => item?.projectileWhenLoaded?.projectile?.damageDef?.harmsHealth == true
            && item.projectileWhenLoaded.projectile.explosionRadius > 0
            && item.projectileWhenLoaded.GetModExtension<ModernWar.ModernGrenadeProjectileExtension>()?.gasDef == null
            && item.projectileWhenLoaded.GetModExtension<HelodGasOnExplosionExtension>()?.gasDef == null;
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long FieldGrenadeChecks, FieldGrenadePlans, FieldGrenadeThrows;

        private void PlanFieldGrenade(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalFieldResponse field, int tick)
        {
            if (tick < field.GrenadeCheckAt || active.Count == 0 || field.Screen != null) return;
            field.GrenadeCheckAt = tick + TacticalFieldGrenadePolicy.CheckInterval;
            if (field.GrenadeMemberCursor >= active.Count) field.GrenadeMemberCursor = 0;
            var member = active[field.GrenadeMemberCursor++]; Pawn pawn = member.Pawn;
            // Do not consume a native cover-plan slot for an ineligible carrier.
            // A shooting warmup can be cancelled to start grenade preparation.
            if (!FieldCombatContext(pawn, out _) || pawn.CurJobDef != JobDefOf.Wait_Combat
                || pawn.pather.Moving || tick < member.RetryTick) return;
            Thing grenade = null; float hazard = float.MaxValue;
            if (!InventoryGrenadeUtility.CanUseGrenades(pawn) || pawn.inventory == null) return;
            foreach (Thing item in pawn.inventory.innerContainer)
                if (item.stackCount > 0 && InventoryGrenadeUtility.IsInventoryGrenade(item.def)
                    && TacticalFieldGrenadePolicy.Lethal(item.def)
                    && TacticalGrenadePolicy.AllowsAutomatic(pawn.Faction?.def, item.def))
                {
                    float radius = TacticalGrenadePolicy.HazardRadius(item.def.projectileWhenLoaded, false);
                    if (radius < hazard) { grenade = item; hazard = radius; }
                }
            if (grenade == null) return;
            var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryPlan(tick)) { field.GrenadeCheckAt = tick + 1; return; }
            long started = Stopwatch.GetTimestamp();
            try
            {

                var contacts = command.Contacts.Memory.Entries;
                for (int visit = 0; visit < Math.Min(TacticalFieldGrenadePolicy.ContactLimit, contacts.Count); visit++)
                {
                    if (field.GrenadeContactCursor >= contacts.Count) field.GrenadeContactCursor = 0;
                    TacticalContact contact = contacts[field.GrenadeContactCursor++];
                    IntVec3 target = contact.Position;
                    if (!TacticalContactMemory.Fresh(contact, tick) || !target.InBounds(map) || target.Roofed(map)
                        || pawn.Position.DistanceToSquared(target) > InventoryGrenadeUtility.ThrowRange * InventoryGrenadeUtility.ThrowRange) continue;
                    // Resolve only the remembered cell. An unseen moving enemy
                    // is never followed through a full-map ID lookup.
                    Pawn enemy = null;
                    foreach (Thing thing in target.GetThingList(map))
                        if (thing is Pawn candidate && candidate.thingIDNumber == contact.EnemyId)
                        { enemy = candidate; break; }
                    if (enemy == null || enemy.Dead || enemy.Downed || !enemy.HostileTo(pawn)) continue;
                    FieldGrenadeChecks++;
                    if (!InventoryGrenadeUtility.CanThrowAt(pawn, target, InventoryGrenadeUtility.ThrowRange)) continue;
                    if (!TryFieldGrenadeRetreat(command, pawn, grenade, target, out IntVec3 retreat)) continue;
                    if (!CanIssue(member)) return;
                    Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(InventoryGrenadeUtility.ThrowJobName), target, grenade, retreat);
                    job.canUseRangedWeapon = false;
                    if (!Issue(member, job)) { JobMaker.ReturnToPool(job); return; }
                    field.GrenadeCheckAt = tick + TacticalFieldGrenadePolicy.ThrowInterval;
                    FieldGrenadePlans++; return;
                }
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }

        private bool TryFieldGrenadeRetreat(TacticalSquadCommand command, Pawn pawn, Thing grenade,
            IntVec3 target, out IntVec3 retreat)
        {
            retreat = pawn.Position;
            if (SafeOpeningThrow(pawn, grenade, pawn.Position, target, retreat)) return true;
            IntVec3 delta = pawn.Position - target;
            IntVec3 away = Math.Abs(delta.x) >= Math.Abs(delta.z)
                ? new IntVec3(Math.Sign(delta.x), 0, 0) : new IntVec3(0, 0, Math.Sign(delta.z));
            // The long-range fragment margin can extend past throw range.
            // A short, straight, fuse-safe withdrawal protects the carrier;
            // other allies still veto the throw. No route search is added.
            for (int steps = 2; steps <= 4; steps++)
            {
                IntVec3 candidate = pawn.Position + away * steps;
                if (!candidate.InBounds(map) || !FieldLocusContains(command, pawn, candidate)
                    || !FieldCombatFloor(command, candidate)) continue;
                if (SafeOpeningThrow(pawn, grenade, pawn.Position, target, candidate))
                { retreat = candidate; return true; }
            }
            return false;
        }

        internal void FieldGrenadeLaunched(Pawn pawn)
        {
            FieldGrenadeThrows++; ContactShot(pawn);
        }
    }
}
