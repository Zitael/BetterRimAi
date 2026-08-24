using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// RimWorld 1.6 JobGiver_Work validates scanner candidates through HasJobOnThing/HasJobOnCell
    /// before it asks the scanner to build the final Job. A target that our runtime path guard has
    /// already proven unsafe must fail here, so vanilla simply continues scanning other targets and
    /// other WorkGivers instead of starting/cancelling the same job in a tight loop.
    /// </summary>
    [HarmonyPatch]
    public static class ThreatAwareBlockedCandidatePatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type scannerType = typeof(WorkGiver_Scanner);
            HashSet<MethodBase> seen = new HashSet<MethodBase>();

            foreach (Type type in GenTypes.AllTypes)
            {
                if (type == null || type.IsAbstract || !scannerType.IsAssignableFrom(type))
                    continue;

                Add(type, nameof(WorkGiver_Scanner.HasJobOnThing),
                    new[] { typeof(Pawn), typeof(Thing), typeof(bool) }, seen, out MethodInfo thing);
                if (thing != null) yield return thing;

                Add(type, nameof(WorkGiver_Scanner.HasJobOnCell),
                    new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) }, seen, out MethodInfo cell);
                if (cell != null) yield return cell;
            }

            Add(scannerType, nameof(WorkGiver_Scanner.HasJobOnThing),
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) }, seen, out MethodInfo baseThing);
            if (baseThing != null) yield return baseThing;

            Add(scannerType, nameof(WorkGiver_Scanner.HasJobOnCell),
                new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) }, seen, out MethodInfo baseCell);
            if (baseCell != null) yield return baseCell;
        }

        private static void Add(Type type, string name, Type[] args, HashSet<MethodBase> seen, out MethodInfo result)
        {
            result = AccessTools.DeclaredMethod(type, name, args);
            if (result == null || result.ReturnType != typeof(bool) || !seen.Add(result))
                result = null;
        }

        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, MethodBase __originalMethod, object[] __args, ref bool __result)
        {
            if (pawn == null || __args == null || __args.Length < 2)
                return true;

            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatAwareOutdoorWork)
                return true;

            if (__args[1] is Thing thing && ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkTarget(pawn, thing))
            {
                __result = false;
                return false;
            }

            if (__args[1] is IntVec3 cell && ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkTarget(pawn, cell))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
