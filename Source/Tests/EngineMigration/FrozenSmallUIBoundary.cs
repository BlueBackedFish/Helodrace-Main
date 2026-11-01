using System;
using System.Reflection;
using Verse;
namespace Helodrace.Tests.FrozenSmallUI
{
    internal static class HelodGasStore
    {
        private static readonly Func<IntVec3,Map,HelodGasDef,float> Density = (Func<IntVec3,Map,HelodGasDef,float>)typeof(HelodGasDef).Assembly.GetType("Helodrace.HelodGasStore",true).GetMethod("DensityPercentAt",BindingFlags.Static|BindingFlags.Public).CreateDelegate(typeof(Func<IntVec3,Map,HelodGasDef,float>));
        internal static float DensityPercentAt(IntVec3 cell,Map map,HelodGasDef gas)=>Density(cell,map,gas);
    }
}
