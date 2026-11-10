using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;
using Helodrace.Ballistics;

// Phase 2: kinetic small-arms model and the shipped XML data against the protection target table.
internal static class KineticTests
{
    private static Action<bool, string> check;
    private static Action<double, double, double, string> near;

    public static void Run(string root, Action<bool, string> checkFn, Action<double, double, double, string> nearFn)
    {
        check = checkFn;
        near = nearFn;
        Model();
        Coverage();
        if (root != null) ShippedData(root);
    }

    private static KineticMaterial Steelish(float k = 2000f, float scatter = 0.06f) =>
        new KineticMaterial("test", ArmorMaterialFamily.Fiber, 1000f, new[] { k, k, k, k }, null, null, scatter, 0.2f, 0.01f);

    private static void Model()
    {
        KineticMaterial material = Steelish();
        float ad = KineticBulletModel.LosArealDensity(material, 0.01f, 0f, 1f);
        near(10, ad, 1e-4, "Areal density rho t");
        near(20, KineticBulletModel.LosArealDensity(material, 0.01f, MathF.PI / 3f, 1f), 1e-3, "Areal density doubles at 60 deg");
        near(5, KineticBulletModel.LosArealDensity(material, 0.01f, 0f, 0.5f), 1e-4, "Effectiveness scales areal density");
        float v50 = KineticBulletModel.LimitVelocity(material, PenetratorClass.Soft, ad, 0.008f, 0.009f);
        double expected = 2000 * Math.Sqrt(10 * Math.PI * 0.25 * 0.009 * 0.009 / 0.008);
        near(expected, v50, 0.01, "V50 = k sqrt(AD A / m)");
        check(KineticBulletModel.LimitVelocity(material, PenetratorClass.Soft, ad, 0.016f, 0.009f) < v50, "Heavier round has lower limit");
        check(KineticBulletModel.LimitVelocity(material, PenetratorClass.Soft, ad * 2, 0.008f, 0.009f) > v50, "More armour raises the limit");
        check(KineticBulletModel.LimitVelocity(material, PenetratorClass.Soft, ad, 0f, 0.009f) == 0f, "Massless round has no limit");

        near(0.5, KineticBulletModel.PerforationProbability(v50, v50, 0.06f), 1e-4, "Half at V50");
        near(0, KineticBulletModel.PerforationProbability(v50 * 0.8f, v50, 0.06f), 1e-6, "Certain stop below 2.5 sigma");
        near(1, KineticBulletModel.PerforationProbability(v50 * 1.2f, v50, 0.06f), 1e-6, "Certain perforation above 2.5 sigma");
        near(0.8413, KineticBulletModel.PerforationProbability(v50 * 1.06f, v50, 0.06f), 1e-3, "One sigma");
        near(0.97725, KineticBulletModel.NormalCdf(2f), 1e-4, "Normal CDF");

        // Sampling matches the analytic probability.
        var round = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, 0.008f, new Vector3(0, 0, -v50 * 1.03f), 0f, 0.009f);
        int perforated = 0;
        const int samples = 40000;
        for (int i = 0; i < samples; i++)
        {
            var rng = new BallisticRng((ulong)i * 7919UL + 1);
            if (KineticBulletModel.ResolveLayer(round, PenetratorClass.Soft, material, 0.01f, 0f, 1f, ref rng).Perforated) perforated++;
        }
        near(KineticBulletModel.PerforationProbability(v50 * 1.03f, v50, 0.06f), perforated / (double)samples, 0.01,
            "Sampled perforation matches analytic");

        // Residual velocity and energy accounting. [physical approximation, p = 2]
        var noScatter = Steelish(2000f, 0f);
        var fast = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, 0.008f, new Vector3(0, 0, -2f * v50), 0f, 0.009f);
        var r = new BallisticRng(1);
        KineticLayerResult through = KineticBulletModel.ResolveLayer(fast, PenetratorClass.Soft, noScatter, 0.01f, 0f, 1f, ref r);
        check(through.Perforated, "Twice V50 perforates");
        near(Math.Sqrt(3) * v50, through.VelocityOut, 0.05, "Residual sqrt(v^2 - V50^2)");
        near(fast.KineticEnergyJ, through.AbsorbedJ + through.Out.KineticEnergyJ, 0.01, "Energy is conserved across the layer");
        NearVector(Vector3.Normalize(fast.Velocity), through.Out.Direction, "Residual keeps direction");
        var slow = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, 0.008f, new Vector3(0, 0, -0.5f * v50), 0f, 0.009f);
        KineticLayerResult stop = KineticBulletModel.ResolveLayer(slow, PenetratorClass.Soft, noScatter, 0.01f, 0f, 1f, ref r);
        check(!stop.Perforated && stop.Out.IsSpent && stop.Out.Speed == 0f, "Slow round is stopped and spent");
        near(slow.KineticEnergyJ, stop.AbsorbedJ, 0.01, "Stopped round deposits all energy");

        // Ceramic erodes and flattens the core; fibre does not.
        var ceramic = new KineticMaterial("c", ArmorMaterialFamily.Ceramic, 2500f, new[] { 1000f, 1000f, 1000f, 1000f },
            new[] { 0.5f, 0.4f, 0.3f, 0.2f }, new[] { 1.5f, 1.3f, 1.1f, 1.05f }, 0f, 0.1f, 0.01f);
        KineticLayerResult eroded = KineticBulletModel.ResolveLayer(fast, PenetratorClass.HardSteel, ceramic, 0.001f, 0f, 1f, ref r);
        check(eroded.Perforated, "Thin ceramic perforated");
        near(0.008 * 0.7, eroded.Out.MassKg, 1e-6, "Hard core erosion");
        near(0.009 * 1.1, eroded.Out.DiameterM, 1e-6, "Hard core mushroom");
        check(eroded.Out.Condition == PenetratorCondition.Eroded, "Hard core eroded");
        check(KineticBulletModel.ResolveLayer(fast, PenetratorClass.Soft, ceramic, 0.001f, 0f, 1f, ref r).Out.Condition
            == PenetratorCondition.Deformed, "Soft core deformed");
        check(through.Out.MassKg == fast.MassKg, "Fibre keeps mass");

        // Layups: stops end the stack; per-layer streams do not depend on earlier layers.
        var layers = new[] { new KineticLayer(noScatter, 0.002f), new KineticLayer(noScatter, 0.05f) };
        KineticLayupResult stack = KineticBulletModel.ResolveLayup(fast, PenetratorClass.Soft, layers, 0f, 1f, 5);
        check(!stack.Perforated && stack.StoppingLayer == 1 && stack.EnergyOut == 0f, "Thick backing stops the round");
        near(fast.KineticEnergyJ, stack.AbsorbedJ, 0.05, "Layup absorbs the whole energy when stopped");
        var trace = new ImpactTrace();
        trace.Begin(new ImpactEvent(5, 0, 0, 0, "t", PenetrationMechanism.KineticBullet, Vector3.Zero, fast));
        KineticLayupResult traced = KineticBulletModel.ResolveLayup(fast, PenetratorClass.Soft, layers, 0f, 1f, 5, trace, "plate");
        check(traced.StoppingLayer == stack.StoppingLayer, "Tracing does not change the result");
        KineticLayupResult again = KineticBulletModel.ResolveLayup(fast, PenetratorClass.Soft,
            new[] { new KineticLayer(Steelish(2000f, 0.06f), 0.01f) }, 0f, 1f, 77, null, "x");
        KineticLayupResult again2 = KineticBulletModel.ResolveLayup(fast, PenetratorClass.Soft,
            new[] { new KineticLayer(Steelish(2000f, 0.06f), 0.01f) }, 0f, 1f, 77, null, "x");
        near(again.EnergyOut, again2.EnergyOut, 0, "Layup reproducible from seed");

        near(1, KineticBulletModel.WoundFactor(100, 100), 1e-6, "Unarmoured wound unchanged (anchor)");
        near(0.5, KineticBulletModel.WoundFactor(100, 25), 1e-6, "Wound factor sqrt(E ratio)");
        near(0, KineticBulletModel.WoundFactor(100, 0), 1e-6, "Stopped round has no penetrating wound");
        near(KineticBulletModel.WoundFactor(100, 50) * KineticBulletModel.WoundFactor(50, 10),
            KineticBulletModel.WoundFactor(100, 10), 1e-5, "Wound factor composes over layers");
        near(1, KineticBulletModel.VanillaPenetrationFactor(900, 900), 1e-6, "Unslowed vanilla penetration");
        near(Math.Pow(0.5, 1.2), KineticBulletModel.VanillaPenetrationFactor(900, 450), 1e-5, "Vanilla penetration v^1.2");

        check(Steelish().Validate().Count() == 0, "Valid material");
        check(new KineticMaterial("bad", ArmorMaterialFamily.Ceramic, 0f, null, null, null, 1f, 2f, -1f).Validate().Count() >= 4,
            "Invalid material reported");
    }

    private static void NearVector(Vector3 expected, Vector3 actual, string message) =>
        check(Vector3.Distance(expected, actual) < 1e-4f, message);

    private static void Coverage()
    {
        var frontBack = new CoverageRegion(new[] { 0f, 180f }, 100f, -0.25f, 0.85f);
        check(frontBack.Contains(10f, 0.2f, out int index) && index == 0, "Front plate covers centre chest");
        check(frontBack.Contains(-175f, 0.2f, out index) && index == 1, "Back plate covers centre back");
        check(!frontBack.Contains(90f, 0.2f, out _), "Front/back plates miss the side");
        check(!frontBack.Contains(0f, -0.6f, out _), "Plate misses the lower abdomen");
        check(!frontBack.Contains(0f, 0.95f, out _), "Plate misses the top of the shoulders");
        var full = new CoverageRegion(null, 360f, -1f, 1f);
        check(full.Contains(123f, -0.99f, out index) && index == -1, "Non-directional coverage covers everything");

        // A frontal flat shot on a front-facing torso: about the share of hits that land on the plate.
        var rng = new BallisticRng(4);
        int onPlate = 0;
        const int samples = 20000;
        for (int i = 0; i < samples; i++)
        {
            BodyHit hit = BodyHitGeometry.Sample(BodyHitGeometry.Torso, -Vector3.UnitZ, ref rng);
            check(hit.AzimuthDegrees > -90.001f && hit.AzimuthDegrees < 90.001f, "Frontal shot lands on the front half");
            if (frontBack.Contains(hit.AzimuthDegrees, hit.HeightFraction, out index))
            {
                onPlate++;
                check(index == 0, "Frontal shot meets the front plate");
                check(hit.PlateObliquity(0f) < 1e-3f, "Frontal shot meets the flat plate head-on");
            }
        }
        double share = onPlate / (double)samples;
        check(share > 0.35 && share < 0.75, "Plate covers part of the frontal silhouette: " + share);

        // A shot from the right side meets the right of the torso and the side plate at a slant.
        BodyHit side = BodyHitGeometry.Sample(BodyHitGeometry.Torso, -Vector3.UnitX, ref rng);
        check(side.AzimuthDegrees > 0f, "Shot from the right lands on the right side");
        check(side.PlateObliquity(0f) > 1.5f, "Front plate is edge-on to a side shot");
    }

    private sealed class Round
    {
        public string Name;
        public float Mass;
        public float Velocity;
        public float Diameter;
        public PenetratorClass Class;
    }

    private static void ShippedData(string root)
    {
        var materials = new Dictionary<string, KineticMaterial>();
        foreach (XElement def in XDocument.Load(Path.Combine(root, "Defs/Ballistics/ArmorMaterials.xml")).Root.Elements())
        {
            float[] Values(string name) => def.Element(name) == null ? null : new[]
            {
                F(def.Element(name).Element("soft")), F(def.Element(name).Element("mildSteel")),
                F(def.Element(name).Element("hardSteel")), F(def.Element(name).Element("tungsten"))
            };
            if (def.Element("ballisticLimitCoefficient") == null && def.Element("longRodPerforation") != null) continue; // rod alloy
            var material = new KineticMaterial((string)def.Element("defName"),
                Enum.Parse<ArmorMaterialFamily>((string)def.Element("family")), F(def.Element("densityKgM3")),
                Values("ballisticLimitCoefficient"), Values("coreErosion"), Values("coreMushroom"),
                F(def.Element("limitScatter")), F(def.Element("traumaTransfer")), F(def.Element("wearPerJoule")),
                F(def.Element("ricochetCriticalDegrees")), def.Element("ricochetWidthDegrees") == null ? 4f : F(def.Element("ricochetWidthDegrees")),
                def.Element("ricochetTangentialRetention") == null ? 0.8f : F(def.Element("ricochetTangentialRetention")),
                def.Element("ricochetNormalRestitution") == null ? 0.1f : F(def.Element("ricochetNormalRestitution")));
            check(!material.Validate().Any(), "Shipped material valid: " + material.Id);
            materials[material.Id] = material;
        }
        var layups = XDocument.Load(Path.Combine(root, "Defs/Ballistics/ArmorLayups.xml")).Root.Elements()
            .ToDictionary(d => (string)d.Element("defName"), d => d.Element("layers").Elements()
                .Select(l => new KineticLayer(materials[(string)l.Element("material")], F(l.Element("thicknessMillimeters")) * 0.001f))
                .ToArray());

        var rounds = new Dictionary<string, Round>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "Defs"), "*.xml", SearchOption.AllDirectories))
        {
            XDocument doc;
            try { doc = XDocument.Load(file); } catch { continue; }
            foreach (XElement thing in doc.Root.Elements("ThingDef"))
            {
                XElement ext = thing.Element("modExtensions")?.Elements("li")
                    .FirstOrDefault(n => (string)n.Attribute("Class") == "Helodrace.BulletBallisticsExtension");
                if (ext?.Element("diameterMillimeters") == null) continue;
                rounds[(string)thing.Element("defName")] = new Round
                {
                    Name = (string)thing.Element("defName"),
                    Mass = F(ext.Element("projectileMassGrams")) * 0.001f,
                    Velocity = F(ext.Element("referenceVelocityMetersPerSecond")),
                    Diameter = F(ext.Element("diameterMillimeters")) * 0.001f,
                    Class = ext.Element("penetratorClass") == null ? PenetratorClass.Soft
                        : Enum.Parse<PenetratorClass>((string)ext.Element("penetratorClass"))
                };
            }
        }
        check(rounds.Count >= 30, "Ballistic data on the Helod rounds: " + rounds.Count);

        Round R(string name) => rounds["HD_Bullet_" + name + "_Proj"];
        Round At(Round round, float velocity) => new Round
        { Name = round.Name + "@" + velocity, Mass = round.Mass, Velocity = velocity, Diameter = round.Diameter, Class = round.Class };
        Round pistol = R("919M882"), acp = R("45ACP"), carbine = R("30Carbine"), blk = R("300BLKTACTX"), pedersen = R("3018Auto");
        Round m2ball = R("3006M2Ball"), m1ball = R("3006M1Ball"), m193 = R("556M193"), m855 = R("556M855"), mk262 = R("556MK262");
        Round mk318 = R("556MK318"), a1 = R("556M855A1"), m995 = R("556M995"), m80 = R("76251M80"), m80a1 = R("76251M80A1");
        Round ball50 = R("12799M2Ball"), api50 = R("12799M8"), api20 = rounds["HD_Projectile_20mmAPI"];
        Round[] sapiStops = { pistol, acp, carbine, blk, pedersen, m2ball, m1ball, m193, m855, mk262, mk318 };
        Round[] esapiStops = sapiStops.Concat(new[] { a1, m80a1, m80 }).ToArray();

        // Target table (Docs/Ballistic_Protection_System.md phase 2): stop <= 1 %, perforate >= 60 %,
        // reference velocity, normal incidence, undamaged.
        // Plates are worn in the IBTV carrier over its HG2 soft panel: evaluate the worn stack.
        foreach (string plate in new[] { "HD_Layup_SAPI", "HD_Layup_ESAPI", "HD_Layup_RAMPART4800" })
            layups[plate + "+IBTV"] = layups[plate].Concat(layups["HD_Layup_SoftHG2"]).ToArray();
        Expect("HD_Layup_SAPI+IBTV", layups, sapiStops, new[] { a1, m995, m80a1, ball50, api50, api20 });
        Expect("HD_Layup_ESAPI+IBTV", layups, esapiStops, new[] { m995, ball50, api50, api20 });
        Expect("HD_Layup_RAMPART4800+IBTV", layups, esapiStops, new[] { m995, ball50, api50, api20 });
        Expect("HD_Layup_SoftHG2", layups, new[] { At(pistol, 448f), acp }, new[] { carbine, m193, m855, m2ball });
        Expect("HD_Layup_SoftHG1", layups, new[] { At(pistol, 398f), acp }, new[] { carbine, blk, m193, m855, m2ball });
        Expect("HD_Layup_ShieldHG2", layups, new[] { At(pistol, 448f), acp }, new[] { m193, m855, a1 });
        Expect("HD_Layup_ShieldRF3", layups, esapiStops.Concat(new[] { m995 }).ToArray(), new[] { api20 });

        // Helmets and visor at normal incidence (phase 4).
        Round acp244 = At(acp, 244f);
        Expect("HD_Layup_HelmetHG2", layups, new[] { At(pistol, 448f), acp }, new[] { carbine, m193, a1 });
        Expect("HD_Layup_VisorHG2", layups, new[] { At(pistol, 448f), acp }, new[] { carbine, m193, a1 });
        Expect("HD_Layup_HelmetM1", layups, new[] { acp244, acp }, new[] { pistol, m193 });
        check(materials["HD_Armor_ManganeseSteel"].RicochetCriticalDegrees > 0f
            && materials["HD_Armor_Aramid"].RicochetCriticalDegrees > 0f
            && materials["HD_Armor_BoronCarbide"].RicochetCriticalDegrees == 0f,
            "Helmet faces glance rounds off; plate ceramics keep the phase 2 calibration");

        // Damage on a plate makes it weaker; obliquity makes it stronger.
        double fresh = Perforation(layups["HD_Layup_ESAPI+IBTV"], m80a1, 0f, 1f);
        double cracked = Perforation(layups["HD_Layup_ESAPI+IBTV"], m80a1, 0f, 0.4f + 0.6f * 0.2f);
        check(cracked > fresh + 0.5, "A badly worn plate lets M80A1 through: " + cracked);
        double sloped = Perforation(layups["HD_Layup_SAPI+IBTV"], a1, 45f * BallisticGeometry.DegToRad, 1f);
        check(sloped < Perforation(layups["HD_Layup_SAPI+IBTV"], a1, 0f, 1f), "Obliquity reduces perforation");
    }

    private static void Expect(string layupName, Dictionary<string, KineticLayer[]> layups, Round[] stops, Round[] perforates)
    {
        KineticLayer[] layup = layups[layupName];
        foreach (Round round in stops)
        {
            double p = Perforation(layup, round, 0f, 1f);
            check(p <= 0.01, layupName + " stops " + round.Name + " (p=" + p.ToString("0.000", CultureInfo.InvariantCulture) + ")");
        }
        foreach (Round round in perforates)
        {
            double p = Perforation(layup, round, 0f, 1f);
            check(p >= 0.6, layupName + " is perforated by " + round.Name + " (p=" + p.ToString("0.000", CultureInfo.InvariantCulture) + ")");
        }
    }

    private static double Perforation(KineticLayer[] layup, Round round, float obliquity, float effectiveness)
    {
        var state = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, round.Mass,
            new Vector3(0, 0, -round.Velocity), 0f, round.Diameter);
        const int samples = 3000;
        int through = 0;
        for (int i = 0; i < samples; i++)
        {
            ulong seed = BallisticSeed.ForImpact(i, 1, 2);
            if (KineticBulletModel.ResolveLayup(state, round.Class, layup, obliquity, effectiveness, seed).Perforated) through++;
        }
        return through / (double)samples;
    }

    private static float F(XElement element) => element == null ? 0f : float.Parse(element.Value, CultureInfo.InvariantCulture);
}
