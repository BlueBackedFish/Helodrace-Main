using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Verse;

namespace Helodrace.Ballistics
{
    /// <summary>One value per <see cref="PenetratorClass"/>.</summary>
    public sealed class PenetratorClassValues
    {
        public float soft;
        public float mildSteel;
        public float hardSteel;
        public float tungsten;

        public float[] ToArray() => new[] { soft, mildSteel, hardSteel, tungsten };
    }

    /// <summary>
    /// Armour material. <c>densityKgM3</c> is a measured property [physical]. The kinetic response block is a
    /// calibrated game approximation [game]; <c>empiricalSource</c> names the test data once a value is sourced.
    /// </summary>
    public sealed class ArmorMaterialDef : Def
    {
        public ArmorMaterialFamily family;
        public float densityKgM3;
        public PenetratorClassValues ballisticLimitCoefficient;
        public PenetratorClassValues coreErosion;
        public PenetratorClassValues coreMushroom;
        public float limitScatter = 0.06f;
        public float traumaTransfer = 0.2f;
        public float wearPerJoule = 1f / 300f;
        // Strike-face ricochet on helmet shells and metal faces [game]; 0 = this face never glances rounds off.
        public float ricochetCriticalDegrees;
        public float ricochetWidthDegrees = 4f;
        public float ricochetTangentialRetention = 0.8f;
        public float ricochetNormalRestitution = 0.1f;
        public string empiricalSource;
        // Vehicle armour and penetrator bodies (design doc 4.2, phase 5). Hardness and yield strength are
        // measured-range properties [physical]; KE mass efficiency is an RHA-equivalence factor [game].
        public float brinellHardness;
        public float yieldStrengthMPa;
        public float massEfficiencyVsKinetic = 1f;
        /// <summary>Steels: the long-rod fit uses this target's own density and hardness instead of RHA equivalence.</summary>
        public bool steelTarget;
        /// <summary>Lanz-Odermatt perforation fit when this material is a rod. [experimental, with source]</summary>
        public LongRodFit longRodPerforation;

        private KineticMaterial model;
        private VehicleArmorMaterial vehicleModel;
        private PenetratorMaterial penetratorModel;

        public KineticMaterial Model => model ?? (model = Build());
        public VehicleArmorMaterial VehicleModel => vehicleModel ??
            (vehicleModel = new VehicleArmorMaterial(Model, brinellHardness, massEfficiencyVsKinetic, steelTarget));
        public PenetratorMaterial PenetratorModel => penetratorModel ?? (penetratorModel = new PenetratorMaterial(defName,
            densityKgM3, brinellHardness, yieldStrengthMPa * 1e6f, longRodPerforation?.Model));

        private KineticMaterial Build() => new KineticMaterial(defName, family, densityKgM3,
            ballisticLimitCoefficient?.ToArray(), coreErosion?.ToArray(), coreMushroom?.ToArray(),
            limitScatter, traumaTransfer, wearPerJoule, ricochetCriticalDegrees, ricochetWidthDegrees,
            ricochetTangentialRetention, ricochetNormalRestitution);

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            // Penetrator-only bodies (rod alloys) carry no armour response.
            bool penetratorOnly = ballisticLimitCoefficient == null && longRodPerforation != null;
            if (!penetratorOnly)
            {
                if (ballisticLimitCoefficient == null) yield return defName + " has no ballisticLimitCoefficient.";
                if (family == ArmorMaterialFamily.Ceramic && (coreErosion == null || coreMushroom == null))
                    yield return defName + " is ceramic but has no coreErosion/coreMushroom.";
                foreach (string error in Build().Validate())
                    yield return error;
            }
            else if (!(densityKgM3 > 0f)) yield return defName + " needs a density.";
            if (brinellHardness < 0f || yieldStrengthMPa < 0f) yield return defName + " has negative hardness or yield strength.";
            if (!(massEfficiencyVsKinetic > 0f)) yield return defName + " needs a positive massEfficiencyVsKinetic.";
            if (longRodPerforation != null)
                foreach (string error in longRodPerforation.Model.Validate(defName)) yield return error;
        }
    }

    /// <summary>Lanz-Odermatt perforation coefficients with their source (design doc 4.2).</summary>
    public sealed class LongRodFit
    {
        public float a;
        public float c0;
        public float c1;
        public float scatter;
        public bool steelForm;
        public float k;
        public float n;
        public string source;

        private LongRodCoefficients model;

        public LongRodCoefficients Model => model ?? (model = new LongRodCoefficients(a, c0, c1, scatter, source, steelForm, k, n));
    }

    public sealed class ArmorLayupLayer
    {
        public ArmorMaterialDef material;
        public float thicknessMillimeters;
    }

    /// <summary>Ordered layers of a protective item, strike face first.</summary>
    public sealed class ArmorLayupDef : Def
    {
        public List<ArmorLayupLayer> layers;
        /// <summary>Plays the metal deflection effect when the layup stops a round.</summary>
        public bool metallicEffect = true;

        private KineticLayer[] compiled;

        public IReadOnlyList<KineticLayer> KineticLayers => compiled ?? (compiled = (layers ?? new List<ArmorLayupLayer>())
            .Where(layer => layer?.material != null)
            .Select(layer => new KineticLayer(layer.material.Model, layer.thicknessMillimeters * 0.001f))
            .ToArray());

        /// <summary>Areal density at normal incidence, kg/m^2.</summary>
        public float ArealDensity => KineticLayers.Sum(layer => layer.Material.DensityKgM3 * layer.ThicknessM);

        /// <summary>Trauma transfer of the stopping layer (last layer when unknown).</summary>
        public float TraumaTransferOf(int layerIndex)
        {
            IReadOnlyList<KineticLayer> stack = KineticLayers;
            if (stack.Count == 0) return 0f;
            int index = layerIndex < 0 || layerIndex >= stack.Count ? stack.Count - 1 : layerIndex;
            return stack[index].Material.TraumaTransfer;
        }

        public float WearPerJouleOf(int layerIndex)
        {
            IReadOnlyList<KineticLayer> stack = KineticLayers;
            if (stack.Count == 0) return 0f;
            int index = layerIndex < 0 || layerIndex >= stack.Count ? 0 : layerIndex;
            return stack[index].Material.WearPerJoule;
        }

        /// <summary>"6.5 mm boron carbide + 7.5 mm UHMWPE (23.7 kg/m²)".</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            for (int i = 0; i < (layers?.Count ?? 0); i++)
            {
                ArmorLayupLayer layer = layers[i];
                if (layer?.material == null) continue;
                if (text.Length > 0) text.Append(" + ");
                text.Append(layer.thicknessMillimeters.ToString("0.#", CultureInfo.InvariantCulture))
                    .Append(" mm ").Append(layer.material.label);
            }
            return "HD_Ballistics_LayupSummary".Translate(text.ToString(),
                ArealDensity.ToString("0.#", CultureInfo.InvariantCulture)).Resolve();
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (layers.NullOrEmpty())
            {
                yield return defName + " has no layers.";
                yield break;
            }
            foreach (ArmorLayupLayer layer in layers)
            {
                if (layer?.material == null) yield return defName + " has a layer without material.";
                else if (!(layer.thicknessMillimeters > 0f)) yield return defName + " has a layer without thickness.";
            }
        }
    }

    /// <summary>Helmet shell on the head: covered above heightMin, except the open face window. [game]</summary>
    public sealed class HelmetShellCoverage
    {
        public float heightMin = -1f;
        public float faceHalfWidthDegrees = 45f;
        public float faceTopHeight = 0.35f;

        public ShellCoverage Model => new ShellCoverage(heightMin, faceHalfWidthDegrees, faceTopHeight);
    }

    /// <summary>Attaches a ballistic layup to a plate, a worn armour piece or a shield.</summary>
    public sealed class BallisticLayupExtension : DefModExtension
    {
        public ArmorLayupDef layup;
        /// <summary>Set for helmets: curved shell (ricochet at the strike face) with partial head coverage.</summary>
        public HelmetShellCoverage shell;

        public override IEnumerable<string> ConfigErrors()
        {
            if (layup == null) yield return "BallisticLayupExtension has no layup.";
        }
    }
}
