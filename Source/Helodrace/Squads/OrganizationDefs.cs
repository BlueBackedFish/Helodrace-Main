using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Squads
{
    public class RoleDef : Def
    {
        public bool isCommandRole;
        public int commandAuthority;
        public int successionPriority = 100;
        public List<string> canCommandUnitLevels = new List<string>();
        public string combatFunction;
        public List<ThingDef> requiredEquipment = new List<ThingDef>();
        public SkillDef preferredSkill;
        public int minimumSkillLevel;
        public List<string> doctrineTags = new List<string>();

        public bool CanCommand(string level)
        {
            return isCommandRole && canCommandUnitLevels.Contains(level);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            if (isCommandRole && canCommandUnitLevels.Count == 0)
                yield return "Command role requires a supported unit level.";
            if (minimumSkillLevel < 0 || requiredEquipment.Any(def => def == null))
                yield return "Role has an invalid skill threshold or equipment reference.";
        }
    }

    public class FormationRoleSlot
    {
        public PawnKindDef pawnKind;
        public RoleDef combatRole;
        public RoleDef commandRole;
        public List<RoleDef> commandQualifications = new List<RoleDef>();
        public int count = 1;
        public int explicitSuccessionOrder = -1;
        public int rankPriority;
        public List<ThingDef> grenadeLoadout = new List<ThingDef>();
        public List<ThingDef> inventoryLoadout = new List<ThingDef>();
        public List<ThingDef> apparelLoadout = new List<ThingDef>();
        public Helodrace.ModernWar.ModularWeaponPresetDef weaponPreset;
        // combatPower already includes the usual equipment. These are optional extra costs.
        public float equipmentPointCost;
        public float specialistPointCost;
    }

    public class ChildFormationSlot
    {
        public FormationDef formation;
        public int count = 1;
        public int successionPriority = 100;
    }

    public class FormationDef : Def
    {
        public string unitLevel;
        public int minimumPersonnel;
        public int idealPersonnel;
        public int maximumPersonnel;
        public int selectionPriority;
        public List<FormationRoleSlot> requiredRoles = new List<FormationRoleSlot>();
        // Optional templates are still complete fixed formations when enabled in this Def.
        public List<FormationRoleSlot> optionalRoles = new List<FormationRoleSlot>();
        public List<ChildFormationSlot> childFormations = new List<ChildFormationSlot>();
        public RoleDef commanderRole;
        public List<RoleDef> successionRoles = new List<RoleDef>();
        public bool allowSubordinateCommanders = true;
        public DoctrineDef doctrine;

        public IEnumerable<FormationRoleSlot> Slots => requiredRoles.Concat(optionalRoles);
        public int StandardPersonnel => CountPersonnel(new HashSet<FormationDef>());
        public float FormationCost => CalculateCost(new HashSet<FormationDef>());

        private int CountPersonnel(HashSet<FormationDef> path)
        {
            if (!path.Add(this)) throw new InvalidOperationException("Cyclic formation: " + defName);
            int count = Slots.Sum(slot => slot.count);
            foreach (ChildFormationSlot child in childFormations)
                count += child.count * child.formation.CountPersonnel(path);
            path.Remove(this);
            return count;
        }

        private float CalculateCost(HashSet<FormationDef> path)
        {
            if (!path.Add(this)) throw new InvalidOperationException("Cyclic formation: " + defName);
            float cost = Slots.Sum(slot => slot.count * (slot.pawnKind.combatPower
                + slot.equipmentPointCost + slot.specialistPointCost));
            foreach (ChildFormationSlot child in childFormations)
                cost += child.count * child.formation.CalculateCost(path);
            path.Remove(this);
            return cost;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            if (string.IsNullOrEmpty(unitLevel)) yield return "Formation requires a unitLevel.";
            if (commanderRole == null || !commanderRole.CanCommand(unitLevel))
                yield return "Formation requires a commander role qualified for its unit level.";
            if (Slots.Any(slot => slot == null || slot.pawnKind == null || slot.combatRole == null
                || slot.combatRole.isCommandRole || slot.count <= 0
                || slot.equipmentPointCost < 0 || slot.specialistPointCost < 0
                || slot.grenadeLoadout == null || slot.grenadeLoadout.Any(def => def == null)
                || slot.inventoryLoadout == null || slot.inventoryLoadout.Any(def => def == null)
                || slot.apparelLoadout == null || slot.apparelLoadout.Any(def => def == null
                    || !def.IsApparel)))
            {
                yield return "Formation has an invalid role slot.";
                yield break;
            }
            if (Slots.Any(slot => slot.commandRole != null && !slot.commandRole.isCommandRole))
                yield return "A command slot must reference a command RoleDef.";
            if (childFormations.Any(child => child == null || child.formation == null || child.count <= 0))
            {
                yield return "Formation has an invalid child slot.";
                yield break;
            }
            int personnel = 0;
            float cost = 0;
            string error = null;
            try { personnel = StandardPersonnel; cost = FormationCost; }
            catch (Exception exception) { error = exception.Message; }
            if (error != null) { yield return error; yield break; }
            if (personnel <= 0 || cost <= 0 || float.IsNaN(cost) || float.IsInfinity(cost))
                yield return "Formation must have positive personnel and finite positive cost.";
            if (minimumPersonnel != personnel || idealPersonnel != personnel || maximumPersonnel != personnel)
                yield return "Only complete formations are supported: all personnel limits must equal slot count.";
            if (!Slots.Any(slot => slot.commandRole == commanderRole) && childFormations.Count == 0)
                yield return "Leaf formation has no commander slot.";
            foreach (ChildFormationSlot child in childFormations)
                foreach (string childError in child.formation.ConfigErrors())
                    yield return child.formation.defName + ": " + childError;
        }
    }

    public class DoctrineDef : Def
    {
        public List<FormationDef> availableFormations = new List<FormationDef>();
        public float formationPointTolerance = 0.10f;
        public int commandLossDelayTicks = 90;
        public int commandRecoveryTicks = 300;
        public float actingCommandEfficiency = 0.70f;
        // Metadata for later systems; these values do not control pawn AI.
        public float directControl;
        public float fireteamIndependence;
        public float squadCohesion;
        public float distributedLeadership;
        public int movementNodeSpan = 16;
        public int movementArrivalRadius = 3;
        public int movementPortalRadius = 2;
        public int movementArrivalRefreshTicks = 60;
        public int movementDestinationRetryTicks = 120;

        public override IEnumerable<string> ConfigErrors()
        {
            if (availableFormations.Count == 0 || availableFormations.Any(def => def == null))
                yield return "Doctrine requires resolved formation candidates.";
            if (formationPointTolerance < 0 || formationPointTolerance > 1)
                yield return "Formation point tolerance must be between 0 and 1.";
            if (commandLossDelayTicks < 0 || commandRecoveryTicks < 0)
                yield return "Command delays cannot be negative.";
            if (actingCommandEfficiency < 0 || actingCommandEfficiency > 1)
                yield return "Command efficiency must be between 0 and 1.";
            if (movementNodeSpan < 1 || movementNodeSpan > 64
                || movementArrivalRadius < 1 || movementArrivalRadius > 6
                || movementPortalRadius < 1 || movementPortalRadius > 6
                || movementArrivalRefreshTicks < 1 || movementDestinationRetryTicks < 1)
                yield return "Movement node settings exceed their bounded work limits.";
        }
    }

    public class FactionOrganizationExtension : DefModExtension
    {
        public DoctrineDef doctrine;
        public override IEnumerable<string> ConfigErrors()
        {
            if (doctrine == null) yield return "Faction organization requires a doctrine.";
        }
    }
}
