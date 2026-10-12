using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using Helodrace;
using Helodrace.ModernWar;
using Helodrace.Tactics;
using RimWorld;
using Verse;

internal static class TacticalGrenadeTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        XElement organization = XDocument.Load(Path.Combine(root, "Defs/Organization/Organization_Modern.xml")).Root;
        XElement squad = organization.Elements().Single(def => (string)def.Element("defName") == "HD_Formation_MW_RifleSquad");
        XElement team = organization.Elements().Single(def => (string)def.Element("defName") == "HD_Formation_MW_Fireteam");
        Check(!organization.Descendants("grenadeLoadout").Elements("li").Any(li => li.Value == "HD_Grenade_M7A2_Item"),
            "High formations must not issue CS.");
        Check((int)squad.Element("minimumPersonnel") == 13 && (int)team.Element("minimumPersonnel") == 4
            && (int)squad.Element("childFormations").Element("li").Element("count") == 3
            && squad.Element("requiredRoles").Elements("li").Count() == 1,
            "Removing CS must preserve the 13-person squad with three real teams.");
        var leader = squad.Element("requiredRoles").Element("li").Element("grenadeLoadout").Elements("li").Select(li => li.Value).ToArray();
        Check(leader.Count(name => name == "HD_C4_Charge") == 3 && leader.Count(name => name == "HD_M81Igniter") == 1,
            "C4 and ignition gear must remain issued.");
        Check(team.Descendants("grenadeLoadout").Elements("li").Select(li => li.Value).SequenceEqual(new[] {
            "HD_Grenade_M84_Item", "HD_Grenade_M8_Item", "HD_Grenade_M67_Item", "HD_Grenade_M67_Item" }),
            "Team flashbang, HC and frag issuance must remain unchanged.");
        var coldWar = XDocument.Load(Path.Combine(root, "Defs/ColdWar/Items/Grenades_ColdWar.xml")).Root;
        var itemXml = coldWar.Elements().Single(def => (string)def.Element("defName") == "HD_Grenade_M7A2_Item");
        var projectileXml = coldWar.Elements().Single(def => (string)def.Element("defName") == (string)itemXml.Element("projectileWhenLoaded"));
        var gas = new HelodGasDef { defName = projectileXml.Descendants("gasDef").Single().Value };
        ThingDef Definition(string name)
        {
            var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            def.defName = name; def.modExtensions = new List<DefModExtension>(); return def;
        }
        var cs = Definition("HD_Grenade_M7A2_Item");
        cs.projectileWhenLoaded = Definition("HD_Projectile_M7A2_Round");
        cs.projectileWhenLoaded.modExtensions.Add(new ModernGrenadeProjectileExtension { gasDef = gas });
        var high = new FactionDef { defName = "HD_HelodCivilHighFaction" };
        var low = new FactionDef { defName = "HD_HelodCivilLowFaction" };
        Check(gas.defName == "HD_CSGasGrid" && !TacticalGrenadePolicy.AllowsAutomatic(high, cs),
            "High must reject CS that was already in inventory.");
        Check(TacticalGrenadePolicy.AllowsAutomatic(low, cs) && TacticalGrenadePolicy.AllowsAutomatic(null, cs),
            "Other factions and unassigned manual inventory must retain CS.");
        cs.defName = "renamed-CS";
        Check(!TacticalGrenadePolicy.AllowsAutomatic(high, cs), "CS gas identity must also exclude renamed grenades.");
        cs.projectileWhenLoaded.modExtensions.Clear();
        cs.projectileWhenLoaded.modExtensions.Add(new HelodGasOnExplosionExtension { gasDef = gas });
        Check(!TacticalGrenadePolicy.AllowsAutomatic(high, cs), "Explosion gas extension cannot bypass High's CS restriction.");
        foreach (string name in new[] { "HD_Grenade_M8_Item", "HD_Grenade_M84_Item", "HD_Grenade_M67_Item", "HD_C4_Charge" })
            Check(TacticalGrenadePolicy.AllowsAutomatic(high, Definition(name)), "High's non-CS equipment must remain usable: " + name);
        var explosive = Definition("test-frag");
        explosive.projectile = new ProjectileProperties { explosionRadius = 3 };
        explosive.modExtensions.Add(new FragmentationGrenadeExtension { radius = 8, longRangeRadius = 12, longRangeFragmentFraction = .15f });
        Check(TacticalGrenadePolicy.HazardRadius(explosive, false) == 12, "Fragment safety must include long-range fragments.");
        var flash = Definition("test-flash"); flash.projectile = new ProjectileProperties { explosionRadius = .1f };
        flash.modExtensions.Add(new FlashbangProjectileExtension { effectRadius = 12, indoorRadiusFactor = 1.5f });
        Check(TacticalGrenadePolicy.HazardRadius(flash, true) == 18 && TacticalGrenadePolicy.HazardRadius(flash, false) == 12,
            "Flashbang friendly safety must use the real indoor effect radius, not its small explosion radius.");
        flash.modExtensions.Add(new ModernGrenadeProjectileExtension { projectileBounceTravelDistance = 2 });
        Check(TacticalGrenadePolicy.HazardRadius(flash, true) == 20, "Projectile bounce must not move damage outside the safety margin.");
        var gasProjectile = Definition("test-gas"); gasProjectile.projectile = new ProjectileProperties();
        gasProjectile.modExtensions.Add(new HelodGasOnExplosionExtension { gasDef = gas, radius = 9 });
        Check(TacticalGrenadePolicy.HazardRadius(gasProjectile, true) == 9, "Non-screening gas must retain its emission danger radius.");
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var otherRoom = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        Room RoomAt(IntVec3 cell) => cell.x >= 1 ? room : otherRoom;
        Check(TacticalGrenadePolicy.FriendlyInRoom(room, new IntVec3(10, 0, 10), false, RoomAt),
            "A friendly in the target room is protected even beyond nominal blast range or behind local cover.");
        Check(TacticalGrenadePolicy.FriendlyInRoom(room, IntVec3.Zero, true, RoomAt),
            "An adjacent door occupant must prevent a throw into the connected room.");
        Check(!TacticalGrenadePolicy.FriendlyInRoom(room, IntVec3.Zero, false, RoomAt)
            && !TacticalGrenadePolicy.FriendlyInRoom(null, IntVec3.Zero, true, RoomAt),
            "A separate room or unavailable room identity must not match by null equality.");
        foreach (IntVec3 forward in GenAdj.CardinalDirections)
        {
            var opening = new TacticalLocalPlan {Opening=IntVec3.Zero,Inward=forward,Depth=2};
            IntVec3 tangent=new IntVec3(-forward.z,0,forward.x);
            Room Merged(IntVec3 cell)=>room;
            Check(!TacticalGrenadePolicy.FriendlyInRoom(room,-forward+tangent*2,false,Merged,opening),
                "A wall-merged approach-side stack is not an ally inside the target room.");
            Check(TacticalGrenadePolicy.FriendlyInRoom(room,forward*3+tangent*2,false,Merged,opening)
                &&TacticalGrenadePolicy.FriendlyInRoom(room,forward,true,Merged,opening),
                "Far-side allies and channel/door occupants remain protected after room merging.");
        }
        var greatWar=XDocument.Load(Path.Combine(root,"Defs/GreatWar/Items/Grenades_GreatWar.xml")).Root;
        foreach(string name in new[]{"HD_Projectile_MKII","HD_Projectile_MKIII"})
        {
            var xml=greatWar.Elements().Single(d=>(string)d.Element("defName")==name);
            var shockXml=xml.Element("modExtensions").Elements("li").Single(d=>(string)d.Attribute("Class")==typeof(FlashbangProjectileExtension).FullName);
            float Value(string field)=>float.Parse((string)shockXml.Element(field),System.Globalization.CultureInfo.InvariantCulture);
            var shock=new FlashbangProjectileExtension {severityAtCenter=Value("severityAtCenter"),blockedPathFactor=Value("blockedPathFactor"),
                smallRoomFactor=Value("smallRoomFactor"),largeRoomFactor=Value("largeRoomFactor"),outdoorFactor=Value("outdoorFactor"),
                stunThreshold=Value("stunThreshold"),damageThreshold=shockXml.Element("damageThreshold")==null?float.MaxValue:Value("damageThreshold")};
            var definition=Definition(name);definition.modExtensions.Add(shock);
            Check(!TacticalGrenadePolicy.DangerousThroughCover(definition),"Weak blocked shock must not veto wall-covered LOW throws: "+name);
            shock.stunThreshold=.01f;
            Check(TacticalGrenadePolicy.DangerousThroughCover(definition),"A custom through-wall shock strong enough to stun must remain unsafe.");
        }
        Check(TacticalGrenadePolicy.DangerousThroughCover(gasProjectile),"Harmful gas must remain unsafe through cover.");
        var timed=Definition("timed-CQB-grenade");timed.thingClass=typeof(Projectile_ModernGrenade);
        timed.modExtensions.Add(new ModernGrenadeProjectileExtension {fuseTicks=270,fuseVarianceTicks=30});
        IntVec3 from=new IntVec3(10,0,10),covered=new IntVec3(9,0,11);
        Check(TacticalGrenadePolicy.CanRetreatBeforeFuse(timed,from,covered,15,_=>true),
            "An exposed thrower with a short clear return and ample fuse time may be assessed at its covered return post.");
        Check(!TacticalGrenadePolicy.CanRetreatBeforeFuse(timed,from,covered,15,c=>c!=covered)
            &&!TacticalGrenadePolicy.CanRetreatBeforeFuse(timed,from,covered,60,_=>true),
            "An occupied return post or wounded slow thrower must not borrow unsafe future cover.");
        Check(!TacticalGrenadePolicy.CanRetreatBeforeFuse(timed,from,from+IntVec3.East*5,15,_=>true)
            &&!TacticalGrenadePolicy.CanRetreatBeforeFuse(explosive,from,covered,15,_=>true),
            "Long or unknown non-fuse retreats must keep current-position safety.");
        timed.modExtensions[0]=new ModernGrenadeProjectileExtension {fuseTicks=50,fuseVarianceTicks=10};
        Check(!TacticalGrenadePolicy.CanRetreatBeforeFuse(timed,from,covered,15,_=>true),
            "A short-fuse grenade must not allow a planned return after detonation.");
        CheckInventorySelection(greatWar,Check);
        Console.WriteLine($"PASS: {checks} grenade issuance, extended hazard radius, friendly room and adjacent door safety checks.");
    }
    private static void CheckInventorySelection(XElement xml,Action<bool,string> check)
    {
        var oldCategories=DefDatabase<ThingCategoryDef>.AllDefsListForReading.ToArray();
        var category=DefDatabase<ThingCategoryDef>.GetNamedSilentFail("HD_InventoryGrenades");
        bool registered=category==null;
        if(registered)
        {
            category=(ThingCategoryDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingCategoryDef));
            category.defName="HD_InventoryGrenades";DefDatabase<ThingCategoryDef>.Add(category);
        }
        try
        {
            var pawn=new Pawn {kindDef=new PawnKindDef()};
            pawn.inventory=new Pawn_InventoryTracker(pawn);
            var faction=new Faction {def=new FactionDef {defName="HD_HelodCivilLowFaction"}};
            AccessTools.Field(typeof(Thing),"factionInt").SetValue(pawn,faction);
            Thing Item(string name)
            {
                var itemXml=xml.Elements().Single(e=>(string)e.Element("defName")==name);
                var def=(ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
                def.defName=name;def.thingCategories=new List<ThingCategoryDef>{category};
                var projectile=(ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
                projectile.defName=(string)itemXml.Element("projectileWhenLoaded");
                projectile.projectile=new ProjectileProperties {damageDef=new DamageDef {defName=name=="HD_Grenade_M8_Item"?"Smoke":"Bomb"}};
                projectile.modExtensions=new List<DefModExtension>();
                if(name=="HD_Grenade_M8_Item") projectile.modExtensions.Add(new HelodGasOnExplosionExtension {gasDef=new HelodGasDef {defName="HD_HCSmokeGrid"}});
                def.projectileWhenLoaded=projectile;
                var item=new Thing {def=def,stackCount=1};pawn.inventory.innerContainer.InnerListForReading.Add(item);return item;
            }
            var smoke=Item("HD_Grenade_M8_Item");var frag=Item("HD_Grenade_MKII");var offensive=Item("HD_Grenade_MKIII");
            var select=AccessTools.Method(typeof(MapComponent_TacticalCommands),"SelectSupportGrenade");
            Thing Select(bool outdoors)=>(Thing)select.Invoke(null,new object[]{pawn.inventory.innerContainer,faction.def,outdoors,true});
            check(InventoryGrenadeUtility.IsInventoryGrenade(frag.def)&&Select(false)==frag,
                "Actual LOW inventory/category selection must accept an MK II as indoor antipersonnel equipment, not smoke.");
            pawn.inventory.innerContainer.InnerListForReading.Remove(frag);
            check(Select(false)==offensive&&Select(true)==smoke,
                "Actual LOW selection must use MK III indoors and preserve HC outdoors.");
        }
        finally
        {
            if(registered) {DefDatabase<ThingCategoryDef>.Clear();DefDatabase<ThingCategoryDef>.Add(oldCategories);}
        }
    }
}
