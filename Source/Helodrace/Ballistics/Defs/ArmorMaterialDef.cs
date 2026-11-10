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
        public string empiricalSource;

        private KineticMaterial model;

        public KineticMaterial Model => model ?? (model = new KineticMaterial(defName, family, densityKgM3,
            ballisticLimitCoefficient?.ToArray(), coreErosion?.ToArray(), coreMushroom?.ToArray(),
            limitScatter, traumaTransfer, wearPerJoule));

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (ballisticLimitCoefficient == null) yield return defName + " has no ballisticLimitCoefficient.";
            if (family == ArmorMaterialFamily.Ceramic && (coreErosion == null || coreMushroom == null))
                yield return defName + " is ceramic but has no coreErosion/coreMushroom.";
            foreach (string error in new KineticMaterial(defName, family, densityKgM3,
                ballisticLimitCoefficient?.ToArray(), coreErosion?.ToArray(), coreMushroom?.ToArray(),
                limitScatter, traumaTransfer, wearPerJoule).Validate())
                yield return error;
        }
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

    /// <summary>Attaches a ballistic layup to a plate, a worn armour piece or a shield.</summary>
    public sealed class BallisticLayupExtension : DefModExtension
    {
        public ArmorLayupDef layup;

        public override IEnumerable<string> ConfigErrors()
        {
            if (layup == null) yield return "BallisticLayupExtension has no layup.";
        }
    }
}
