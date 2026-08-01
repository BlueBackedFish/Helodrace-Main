using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed class OilFieldRecord : IExposable
    {
        public IntVec3 cell;
        public float quality = 1f;
        public float pressure = 1f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref quality, "quality", 1f);
            Scribe_Values.Look(ref pressure, "pressure", 1f);
        }
    }

    public sealed class MapComponent_OilFields : MapComponent
    {
        public const string OilFieldTerrainDefName = "HD_OilFieldFloor";
        public const string LegacyOilFieldTerrainDefName = "HD_LowQualityOilRigFloor";

        private List<OilFieldRecord> oilFields = new List<OilFieldRecord>();
        private Dictionary<IntVec3, OilFieldRecord> oilFieldsByCell =
            new Dictionary<IntVec3, OilFieldRecord>();

        public MapComponent_OilFields(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref oilFields, "oilFields", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && oilFields == null)
            {
                oilFields = new List<OilFieldRecord>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildLookup();
            }
        }

        public OilFieldRecord CreateOrReplace(IntVec3 cell, float quality, float pressure)
        {
            OilFieldRecord record = FieldAt(cell);
            if (record == null)
            {
                record = new OilFieldRecord { cell = cell };
                oilFields.Add(record);
                oilFieldsByCell[cell] = record;
            }

            record.quality = Mathf.Max(0.01f, quality);
            record.pressure = Mathf.Clamp01(pressure);
            return record;
        }

        public OilFieldRecord FieldAt(IntVec3 cell)
        {
            OilFieldRecord record;
            return oilFieldsByCell.TryGetValue(cell, out record) ? record : null;
        }

        public OilFieldRecord FieldAtOrCreateLegacy(IntVec3 cell)
        {
            OilFieldRecord record = FieldAt(cell);
            if (record != null)
            {
                return record;
            }

            return IsOilFieldTerrain(cell)
                ? CreateOrReplace(cell, 1f, 1f)
                : null;
        }

        public void ReducePressure(OilFieldRecord record, float amount, float minimumPressure)
        {
            if (record == null)
            {
                return;
            }

            float pressureFloor = Mathf.Clamp01(minimumPressure);
            record.pressure = Mathf.Max(pressureFloor, record.pressure - Mathf.Max(0f, amount));
        }

        public bool IsOilFieldTerrain(IntVec3 cell)
        {
            if (!cell.InBounds(map))
            {
                return false;
            }

            string defName = cell.GetTerrain(map)?.defName;
            return defName == OilFieldTerrainDefName || defName == LegacyOilFieldTerrainDefName;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (Find.TickManager.TicksGame % GenTicks.TickRareInterval != 0)
            {
                return;
            }

            // A layerable oil-field floor can be removed with the normal remove-floor
            // designation. Its simulation data disappears with the floor.
            for (int i = oilFields.Count - 1; i >= 0; i--)
            {
                OilFieldRecord record = oilFields[i];
                if (record == null || !IsOilFieldTerrain(record.cell))
                {
                    if (record != null)
                    {
                        oilFieldsByCell.Remove(record.cell);
                    }

                    int lastIndex = oilFields.Count - 1;
                    oilFields[i] = oilFields[lastIndex];
                    oilFields.RemoveAt(lastIndex);
                }
            }
        }

        private void RebuildLookup()
        {
            oilFieldsByCell.Clear();
            for (int i = 0; i < oilFields.Count; i++)
            {
                OilFieldRecord record = oilFields[i];
                if (record != null)
                {
                    oilFieldsByCell[record.cell] = record;
                }
            }
        }
    }
}
