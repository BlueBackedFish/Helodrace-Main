using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI.Group;
using LudeonTK;

namespace Helodrace.Squads
{
    public enum RaidSupportKind { Smoke, Sniper, Jamming }
    public sealed class RaidSupportRequest : IExposable
    {
        public int Reservation, Due, EnemyId;
        public RaidSupportKind Kind;
        public Pawn Caller;
        public Map Map;
        public IntVec3 Aim, Source;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Reservation, "reservation"); Scribe_Values.Look(ref Due, "due");
            Scribe_Values.Look(ref EnemyId, "enemyId"); Scribe_Values.Look(ref Kind, "kind");
            Scribe_References.Look(ref Caller, "caller"); Scribe_References.Look(ref Map, "map");
            Scribe_Values.Look(ref Aim, "aim"); Scribe_Values.Look(ref Source, "source");
        }
    }
    public sealed partial class CombatOrganization
    {
        public List<RaidSupportRequest> SupportRequests = new List<RaidSupportRequest>();
        public int NextSupportAt, SupportCheckAt, SupportSequence;
        public bool Supplied;
        partial void ExposeRaidSupport()
        {
            Scribe_Collections.Look(ref SupportRequests, "raidSupportRequests", LookMode.Deep);
            Scribe_Values.Look(ref NextSupportAt, "nextSupportAt"); Scribe_Values.Look(ref SupportSequence, "supportSequence");
            Scribe_Values.Look(ref Supplied, "raidSupplied");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) SupportRequests = SupportRequests ?? new List<RaidSupportRequest>();
        }
    }
    public static class RaidSupport
    {
        public static int Cost(RaidSupportKind kind) => kind == RaidSupportKind.Smoke ? 100 : kind == RaidSupportKind.Sniper ? 180 : 150;
        public static void Equip(CombatOrganization organization)
        {
            if (organization.Supplied) return;
            organization.Supplied = true;
            if (organization.faction.def.defName != "HD_HelodCivilLowFaction") return;
            Pawn[] candidates = organization.AllGroups.SelectMany(g => g.roleAssignments)
                .Where(a => !a.medicalDuty && a.combatRole?.combatFunction == "Rifleman").Select(a => a.pawn).Take(2).ToArray();
            if (candidates.Length == 0) return;
            ThingDef armor = DefDatabase<ThingDef>.GetNamedSilentFail("HD_Apparel_M1952AFlakJacket");
            int reservation = armor == null ? 0 : organization.ReserveSupport(120);
            if (reservation != 0)
            {
                bool success = false;
                try { candidates[0].apparel.Wear((Apparel)ThingMaker.MakeThing(armor), false); success = candidates[0].apparel.WornApparel.Any(a => a.def == armor); }
                finally { organization.FinishSupport(reservation, success); }
            }
            var preset = DefDatabase<ModernWar.ModularWeaponPresetDef>.GetNamedSilentFail("HD_WeaponPreset_ModularM14_Default");
            reservation = candidates.Length < 2 || preset == null ? 0 : organization.ReserveSupport(220);
            if (reservation != 0)
            {
                bool success = false;
                try { ModernWar.Patch_PawnGenerator_ModularLoadout.ApplyWeapon(candidates[1], new ModernWar.ModularPawnKindLoadout { weaponPreset = preset }); success = candidates[1].equipment.Primary != null; }
                finally { organization.FinishSupport(reservation, success); }
            }
        }
        public static bool Request(CombatOrganization organization, RaidSupportKind kind, Pawn caller, IntVec3 aim,
            IntVec3 source, int enemyId, int tick)
        {
            if (organization.SupportRequests.Count >= 4 || caller?.Spawned != true || caller.Faction != organization.faction
                || !aim.InBounds(caller.Map)) return false;
            int reservation = organization.ReserveSupport(Cost(kind));
            if (reservation == 0) return false;
            organization.SupportRequests.Add(new RaidSupportRequest { Reservation = reservation, Kind = kind, Caller = caller,
                Map = caller.Map, Aim = aim, Source = source, EnemyId = enemyId, Due = tick + 120 });
            return true;
        }
        public static void Advance(TacticalSquadCommand command, int tick)
        {
            CombatOrganization organization = command.Link.Unit.Organization;
            if (tick < organization.SupportCheckAt) return;
            organization.SupportCheckAt = tick + 90;
            for (int i = organization.SupportRequests.Count - 1; i >= 0; i--)
            {
                var request = organization.SupportRequests[i];
                if (tick < request.Due) continue;
                bool valid = request.Map != null && !request.Map.Disposed && request.Caller?.Spawned == true
                    && request.Caller.Map == request.Map && !request.Caller.Dead && !request.Caller.Downed
                    && GameComponent_TacticalCommands.IsAssaultLord(request.Caller.GetLord());
                bool applied = false;
                try { if (valid) applied = Apply(request, command); }
                finally { organization.FinishSupport(request.Reservation, applied); organization.SupportRequests.RemoveAt(i); }
            }
            var field = command.FieldResponse;
            if (tick < organization.NextSupportAt || field == null || field.High && !RaidTacticalRadioUtility.SignalAvailable(command.Owner.map)
                || tick - field.LastEngaged > 300 || organization.SupportRequests.Count > 0) return;
            Pawn caller = command.Link.Unit.Commander;
            if (caller?.Spawned != true) return;
            bool high = organization.faction.def.defName == "HD_HelodCivilHighFaction";
            if (high && !RaidTacticalRadioUtility.Radios(caller).Any()) return;
            var contact = command.Contacts.Memory.Entries.FirstOrDefault(c => TacticalContactMemory.Fresh(c, tick) && c.RangedThreat);
            RaidSupportKind kind = organization.SupportSequence % 2 == 0 ? RaidSupportKind.Smoke
                : high ? RaidSupportKind.Jamming : RaidSupportKind.Sniper;
            if (kind != RaidSupportKind.Smoke && contact == null) return;
            if (kind == RaidSupportKind.Jamming && !contact.PlayerControlled) kind = RaidSupportKind.Smoke;
            IntVec3 aim = kind == RaidSupportKind.Smoke
                ? field.Anchor + TacticalFieldDoctrine.Offset(field.Focus - field.Anchor, 0, Math.Min(18, Math.Sqrt(field.Focus.DistanceToSquared(field.Anchor)) * .5))
                : contact.Position;
            IntVec3 source = caller.Position;
            int edge = Math.Min(Math.Min(source.x, caller.Map.Size.x - 1 - source.x), Math.Min(source.z, caller.Map.Size.z - 1 - source.z));
            if (source.x == edge) source.x = 0; else if (caller.Map.Size.x - 1 - source.x == edge) source.x = caller.Map.Size.x - 1;
            else if (source.z == edge) source.z = 0; else source.z = caller.Map.Size.z - 1;
            if (Request(organization, kind, caller, aim, source, contact?.EnemyId ?? -1, tick))
            { organization.NextSupportAt = tick + 1800; organization.SupportSequence++; }
            else organization.NextSupportAt = tick + 600;
        }
        private static bool Apply(RaidSupportRequest request, TacticalSquadCommand command)
        {
            if (request.Kind == RaidSupportKind.Smoke)
            {
                if (!request.Source.InBounds(request.Map)) return false;
                Projectile smoke = (Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("HD_RaidHCSmokeShell"), request.Source, request.Map);
                smoke.Launch(request.Caller, request.Source.ToVector3Shifted(), request.Aim, request.Aim, ProjectileHitFlags.All);
                return true;
            }
            if (request.Kind == RaidSupportKind.Jamming)
            {
                var owner = request.Map.GetComponent<MapComponent_TacticalCommands>();
                if (owner == null) return false;
                owner.PlayerRadioJammedUntil = Math.Max(owner.PlayerRadioJammedUntil, GenTicks.TicksGame + 1800); return true;
            }
            Pawn target = request.Map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == request.EnemyId);
            if (target?.Spawned != true || target.Downed || !target.HostileTo(request.Caller)
                || target.Position.DistanceToSquared(request.Aim) > 16 || !request.Source.InBounds(request.Map)
                || !GenSight.LineOfSight(request.Source, target.Position, request.Map, true)
                || !TacticalContactSight.CanSee(request.Map, request.Caller.Position, target, command.Contacts, 120)) return false;
            Projectile projectile = (Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("Bullet_SniperRifle"), request.Source, request.Map);
            projectile.Launch(request.Caller, request.Source.ToVector3Shifted(), target, target, ProjectileHitFlags.IntendedTarget);
            return true;
        }
        public static void Cancel(CombatOrganization organization)
        {
            foreach (var request in organization.SupportRequests) organization.FinishSupport(request.Reservation, false);
            organization.SupportRequests.Clear();
        }
        [DebugAction("Helodrace/New tactical AI", "Inspect raid support", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Inspect()
        {
            var organizations = OrganizationAPI.Registry.Organizations.Where(o => o.AllMembers.Any(p => p.Spawned && p.Map == Find.CurrentMap));
            Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n", organizations.Select(o =>
                $"{o.id}: initial={o.initialRaidPoints} formation={o.formationPointsSpent} available={o.SupportPointsRemaining} reserved={o.SupportPointsReserved} requests={o.SupportRequests.Count} next={o.NextSupportAt}"))));
        }
    }
}
