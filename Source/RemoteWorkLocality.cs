using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>Temporarily favor the last successful remote WorkGiver at the same site.</summary>
    internal static class RemoteWorkLocality
    {
        private const int SiteRadiusSquared = 24 * 24;
        private const int MaxSessionTicks = 2500;
        private const float CriticalNeed = 0.20f;
        private sealed class Entry
        {
            internal Map map;
            internal Job pendingJob;
            internal IntVec3 pendingSite;
            internal WorkGiverDef pendingGiver;
            internal IntVec3 site;
            internal WorkGiverDef giver;
            internal int expiryTick;
        }
        private static ConditionalWeakTable<Pawn, Entry> entries = new ConditionalWeakTable<Pawn, Entry>();
        [ThreadStatic] private static Pawn activePawn;
        internal static void Reset()
        {
            entries = new ConditionalWeakTable<Pawn, Entry>();
            activePawn = null;
        }

        internal static void Started(Pawn pawn, Job job)
        {
            if (pawn?.Map == null || !pawn.IsColonist || BetterRimAIMod.Settings?.remoteWorkLocality != true
                || job == null || job.playerForced || job.workGiverDef == null) return;
            if (!(job.workGiverDef.Worker is WorkGiver_Scanner)) return;
            Map map = pawn.Map;
            Area_Home home = map.areaManager?.Home;
            if (home == null || !job.targetA.IsValid) return;
            IntVec3 destination = job.targetA.Cell;
            if (!destination.InBounds(map) || ThreatAwareHomeSafety.IsSafeCell(map, home, destination)) return;
            bool startedAtBase = ThreatAwareHomeSafety.IsSafeCell(map, home, pawn.Position)
                && (destination - pawn.Position).LengthHorizontalSquared >= LongTripNeedsPatch.LongTripDistanceSquared;
            bool continuing = entries.TryGetValue(pawn, out Entry previous) && previous.map == map
                && previous.giver == job.workGiverDef && (pawn.Position - previous.site).LengthHorizontalSquared <= SiteRadiusSquared
                && (destination - previous.site).LengthHorizontalSquared <= SiteRadiusSquared;
            if (!startedAtBase && !continuing) return;
            Entry entry = entries.GetOrCreateValue(pawn);
            entry.map = map;
            entry.pendingJob = job;
            entry.pendingSite = destination;
            entry.pendingGiver = job.workGiverDef;
        }

        internal static void Finished(Pawn pawn, Job job, JobCondition condition)
        {
            if (pawn == null || !entries.TryGetValue(pawn, out Entry entry)
                || !ReferenceEquals(job, entry.pendingJob)) return;
            entry.pendingJob = null;
            if (condition != JobCondition.Succeeded || pawn.Map != entry.map
                || (pawn.Position - entry.pendingSite).LengthHorizontalSquared > SiteRadiusSquared) return;
            entry.site = pawn.Position;
            entry.giver = entry.pendingGiver;
            entry.expiryTick = (Find.TickManager?.TicksGame ?? 0) + MaxSessionTicks;
        }

        internal static bool TryActivate(Pawn pawn)
        {
            activePawn = null;
            if (pawn == null || !pawn.IsColonist || BetterRimAIMod.Settings?.remoteWorkLocality != true
                || pawn.Drafted || pawn.Map == null || pawn.needs == null
                || (pawn.playerSettings != null && pawn.playerSettings.UsesConfigurableHostilityResponse
                    && pawn.playerSettings.hostilityResponse == HostilityResponseMode.Attack)
                || !NeedsAllowLocality(pawn.needs.food?.CurLevelPercentage, pawn.needs.rest?.CurLevelPercentage)
                || !entries.TryGetValue(pawn, out Entry entry) || entry.giver == null
                || entry.map != pawn.Map || (Find.TickManager?.TicksGame ?? 0) > entry.expiryTick
                || (pawn.Position - entry.site).LengthHorizontalSquared > SiteRadiusSquared) return false;
            activePawn = pawn;
            return true;
        }

        internal static bool NeedsAllowLocality(float? food, float? rest)
            => !(food < CriticalNeed || rest < CriticalNeed);

        internal static void Deactivate() => activePawn = null;

        internal static bool CandidateIsTooFar(WorkGiver giver, Pawn pawn, IntVec3 cell)
        {
            if (activePawn != pawn || !entries.TryGetValue(pawn, out Entry entry)
                || !ReferenceEquals(giver?.def, entry.giver)) return false;
            return (cell - entry.site).LengthHorizontalSquared > SiteRadiusSquared;
        }

        internal static void Prefer(Pawn_WorkSettings settings, ref List<WorkGiver> order)
        {
            Pawn pawn = activePawn;
            if (pawn == null || settings == null || order == null
                || pawn.workSettings != settings || !entries.TryGetValue(pawn, out Entry entry)) return;
            WorkGiver preferred = entry.giver?.Worker;
            if (preferred == null || preferred.def?.workType == null) return;
            int oldIndex = order.IndexOf(preferred);
            if (oldIndex <= 0) return;
            int priority = settings.GetPriority(preferred.def.workType);
            if (priority <= 0) return;
            int insertion = 0;
            while (insertion < oldIndex)
            {
                WorkTypeDef workType = order[insertion].def?.workType;
                if (workType != null && settings.GetPriority(workType) >= priority) break;
                insertion++;
            }
            if (insertion >= oldIndex) return;
            var reordered = new List<WorkGiver>(order);
            reordered.RemoveAt(oldIndex);
            reordered.Insert(insertion, preferred);
            order = reordered;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class RemoteWorkStartPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Pawn ___pawn, Job __0) => RemoteWorkLocality.Started(___pawn, __0);
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    internal static class RemoteWorkFinishPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Pawn ___pawn, Job ___curJob, JobCondition __0)
            => RemoteWorkLocality.Finished(___pawn, ___curJob, __0);
    }

    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    internal static class RemoteWorkSelectionPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(JobGiver_Work __instance, Pawn __0)
        {
            if (!__instance.emergency) RemoteWorkLocality.TryActivate(__0);
        }
        [HarmonyPostfix]
        internal static void Postfix() => RemoteWorkLocality.Deactivate();
        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception)
        {
            RemoteWorkLocality.Deactivate();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Pawn_WorkSettings), "get_WorkGiversInOrderNormal")]
    internal static class RemoteWorkOrderPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Pawn_WorkSettings __instance, ref List<WorkGiver> __result)
            => RemoteWorkLocality.Prefer(__instance, ref __result);
    }

    [HarmonyPatch]
    internal static class RemoteWorkThingCandidatePatch
    {
        [HarmonyTargetMethods]
        internal static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            => ThreatAwareScannerTargets.Find(nameof(WorkGiver_Scanner.HasJobOnThing), typeof(Thing));
        [HarmonyPrefix]
        internal static bool Prefix(WorkGiver_Scanner __instance, Pawn __0, Thing __1, ref bool __result)
        {
            if (!RemoteWorkLocality.CandidateIsTooFar(__instance, __0, __1.Position)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class RemoteWorkCellCandidatePatch
    {
        [HarmonyTargetMethods]
        internal static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            => ThreatAwareScannerTargets.Find(nameof(WorkGiver_Scanner.HasJobOnCell), typeof(IntVec3));
        [HarmonyPrefix]
        internal static bool Prefix(WorkGiver_Scanner __instance, Pawn __0, IntVec3 __1, ref bool __result)
        {
            if (!RemoteWorkLocality.CandidateIsTooFar(__instance, __0, __1)) return true;
            __result = false;
            return false;
        }
    }
}
