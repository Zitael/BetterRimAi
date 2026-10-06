using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// The restriction for one JobGiver_Work pass. It exists only while a relevant threat is
    /// active on the map and the pawn stands in protected space; otherwise
    /// <see cref="Current"/> stays null and the candidate filter is a plain vanilla call.
    /// </summary>
    internal sealed class ScanRestriction
    {
        private const int MaxReruns = 3;

        internal static ScanRestriction Current;
        private static readonly ScanRestriction Instance = new ScanRestriction();

        internal Pawn Pawn;
        internal MapThreatState State;
        internal int Component;
        internal int Rejected;
        private readonly List<WorkGiverDef> excludedGivers = new List<WorkGiverDef>();
        private readonly List<LocalTargetInfo> excludedTargets = new List<LocalTargetInfo>();
        private readonly List<WorkGiverDef> excludedWholeGivers = new List<WorkGiverDef>();

        internal static ScanRestriction TryBegin(JobGiver_Work giver, Pawn pawn)
        {
            // A nested pass (the rescan below) reuses the outer restriction and its exclusions.
            if (Current != null || !ThreatSafetyPolicy.Eligible(pawn)) return null;
            // The emergency pass first serves the player's "Prioritize" order (a forced job).
            if (giver.emergency && pawn.mindState?.priorityWork != null && pawn.mindState.priorityWork.IsPrioritized)
                return null;
            MapThreatState state = MapThreatState.For(pawn.Map);
            if (!state.Active) return null;
            int component = state.ComponentAt(pawn.Position);
            // Pawns outside protected space are vanilla's business entirely.
            if (component == 0) return null;
            ScanRestriction r = Instance;
            r.Pawn = pawn;
            r.State = state;
            r.Component = component;
            r.Rejected = 0;
            r.excludedGivers.Clear();
            r.excludedTargets.Clear();
            r.excludedWholeGivers.Clear();
            Current = r;
            return r;
        }

        internal static void End(ScanRestriction r)
        {
            if (r == null || Current != r) return;
            Current = null;
            r.Pawn = null;
            r.State = null;
        }

        internal bool Rejects(WorkGiver_Scanner scanner, LocalTargetInfo target)
        {
            WorkGiverDef giver = scanner.def;
            if (excludedWholeGivers.Contains(giver)) return true;
            for (int i = 0; i < excludedTargets.Count; i++)
                if (excludedGivers[i] == giver && excludedTargets[i] == target) return true;

            Verdict verdict;
            try
            {
                verdict = ThreatSafetyPolicy.Classify(State, Pawn, Component, target, scanner.PathEndMode, out _);
            }
            catch (Exception ex)
            {
                ThreatSafetyLog.Failed("candidate check", ex);
                return false;
            }
            if (!verdict.IsReject()) return false;
            if (!State.Active) ThreatSafetyLog.Invariant("candidate rejection", Pawn, null);
            Rejected++;
            ThreatSafetyLog.CandidateRejected(Pawn, giver, target, verdict);
            return true;
        }

        /// <summary>
        /// A completed job may still send the pawn outside through a secondary target. Exclude it
        /// and let JobGiver_Work continue to the next candidate exactly as if it had been invalid.
        /// </summary>
        internal void ValidateCompletedJob(JobGiver_Work giver, JobIssueParams jobParams, ref ThinkResult result)
        {
            for (int rerun = 0; ; rerun++)
            {
                Job job = result.Job;
                if (job == null || job.playerForced || job.workGiverDef == null) return;
                LocalTargetInfo culprit;
                Verdict verdict;
                try
                {
                    if (!ThreatSafetyPolicy.CompletedJobIsUnsafe(State, Pawn, Component, job, out culprit, out verdict)) return;
                }
                catch (Exception ex)
                {
                    ThreatSafetyLog.Failed("completed job check", ex);
                    return;
                }
                if (!State.Active) ThreatSafetyLog.Invariant("completed job rejection", Pawn, job);
                bool gaveUp = rerun == MaxReruns;
                ThreatSafetyLog.CompletedJobRejected(Pawn, job, culprit, verdict, gaveUp);
                if (gaveUp)
                {
                    // Nothing safe found in this work pass. NoJob lets the think tree fall through
                    // to needs, joy and idle, and starts no movement that could loop.
                    result = ThinkResult.NoJob;
                    return;
                }
                if (rerun == MaxReruns - 1) excludedWholeGivers.Add(job.workGiverDef);
                else
                {
                    Exclude(job.workGiverDef, job.targetA);
                    Exclude(job.workGiverDef, job.targetB);
                    Exclude(job.workGiverDef, job.targetC);
                }
                result = giver.TryIssueJobPackage(Pawn, jobParams);
            }
        }

        private void Exclude(WorkGiverDef giver, LocalTargetInfo target)
        {
            if (!target.IsValid) return;
            excludedGivers.Add(giver);
            excludedTargets.Add(target);
        }
    }

    /// <summary>
    /// Replacements for JobGiver_Work's own calls to WorkGiver_Scanner.HasJobOnThing/HasJobOnCell.
    /// Only autonomous candidate selection goes through here; float-menu orders, forced work and
    /// every other caller of the scanners are untouched.
    /// </summary>
    public static class WorkScanFilter
    {
        public static bool HasJobOnThing(WorkGiver_Scanner scanner, Pawn pawn, Thing thing, bool forced)
        {
            if (!forced && thing != null)
            {
                if (RemoteWorkLocality.CandidateIsTooFar(scanner, pawn, thing.Position)) return false;
                ScanRestriction r = ScanRestriction.Current;
                if (r != null && r.Pawn == pawn && r.Rejects(scanner, thing)) return false;
            }
            return scanner.HasJobOnThing(pawn, thing, forced);
        }

        public static bool HasJobOnCell(WorkGiver_Scanner scanner, Pawn pawn, IntVec3 cell, bool forced)
        {
            if (!forced)
            {
                if (RemoteWorkLocality.CandidateIsTooFar(scanner, pawn, cell)) return false;
                ScanRestriction r = ScanRestriction.Current;
                if (r != null && r.Pawn == pawn && r.Rejects(scanner, cell)) return false;
            }
            return scanner.HasJobOnCell(pawn, cell, forced);
        }
    }

    /// <summary>
    /// Redirects the HasJobOnThing/HasJobOnCell call sites inside JobGiver_Work (its candidate
    /// validator and cell processor) to <see cref="WorkScanFilter"/>. This covers every vanilla
    /// and modded WorkGiver_Scanner used for autonomous work, including Pick Up And Haul, with a
    /// couple of patched methods instead of one patch per scanner override.
    /// </summary>
    [HarmonyPatch]
    internal static class WorkScanCallSitePatch
    {
        internal static readonly MethodInfo HasJobOnThing = AccessTools.DeclaredMethod(typeof(WorkGiver_Scanner),
            nameof(WorkGiver_Scanner.HasJobOnThing), new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
        internal static readonly MethodInfo HasJobOnCell = AccessTools.DeclaredMethod(typeof(WorkGiver_Scanner),
            nameof(WorkGiver_Scanner.HasJobOnCell), new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) });
        private static readonly MethodInfo ThingFilter = AccessTools.Method(typeof(WorkScanFilter), nameof(WorkScanFilter.HasJobOnThing));
        private static readonly MethodInfo CellFilter = AccessTools.Method(typeof(WorkScanFilter), nameof(WorkScanFilter.HasJobOnCell));
        private const string PrioritizedWorkMethod = "GiverTryGiveJobPrioritized";
        private static List<MethodBase> targets;

        [HarmonyPrepare]
        internal static bool Prepare()
        {
            if (FindTargets().Count != 0) return true;
            Log.Warning("[BetterRimAI] JobGiver_Work candidate call sites were not found; threat-safe and remote-locality "
                + "candidate filtering are disabled (vanilla behavior).");
            return false;
        }

        [HarmonyTargetMethods]
        internal static IEnumerable<MethodBase> TargetMethods() => FindTargets();

        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (IsCallTo(instruction.opcode, instruction.operand, HasJobOnThing))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = ThingFilter;
                }
                else if (IsCallTo(instruction.opcode, instruction.operand, HasJobOnCell))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = CellFilter;
                }
                yield return instruction;
            }
        }

        /// <summary>Startup only: JobGiver_Work methods (including compiler-generated local functions) that call the scanners.</summary>
        internal static List<MethodBase> FindTargets()
        {
            if (targets != null) return targets;
            targets = new List<MethodBase>();
            if (HasJobOnThing == null || HasJobOnCell == null) return targets;
            var types = new List<Type> { typeof(JobGiver_Work) };
            for (int i = 0; i < types.Count; i++) types.AddRange(types[i].GetNestedTypes(AccessTools.all));
            foreach (Type type in types)
            {
                if (type.ContainsGenericParameters) continue;
                foreach (MethodInfo method in type.GetMethods(AccessTools.allDeclared))
                {
                    if (method.IsAbstract || method.GetMethodBody() == null) continue;
                    // Player "Prioritize" orders (and their compiler-generated lambdas) stay vanilla.
                    if (method.Name.Contains(PrioritizedWorkMethod)) continue;
                    try
                    {
                        foreach (KeyValuePair<OpCode, object> instruction in PatchProcessor.ReadMethodBody(method))
                        {
                            if (!IsCallTo(instruction.Key, instruction.Value, HasJobOnThing)
                                && !IsCallTo(instruction.Key, instruction.Value, HasJobOnCell)) continue;
                            targets.Add(method);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[BetterRimAI] Could not inspect " + type.FullName + "." + method.Name + ": " + ex.Message);
                    }
                }
            }
            return targets;
        }

        internal static bool IsCallTo(OpCode opcode, object operand, MethodInfo target)
        {
            if (opcode != OpCodes.Callvirt && opcode != OpCodes.Call) return false;
            return operand is MethodInfo method && method.Name == target.Name
                   && method.DeclaringType == target.DeclaringType && method.GetParameters().Length == 3;
        }
    }

    /// <summary>
    /// Opens/closes the per-pass <see cref="ScanRestriction"/> and validates the completed job.
    /// With no restriction the postfix is a single null check.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    internal static class WorkScanSelectionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(JobGiver_Work __instance, Pawn __0, out ScanRestriction __state)
            => __state = ScanRestriction.TryBegin(__instance, __0);

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(JobGiver_Work __instance, JobIssueParams __1, ref ThinkResult __result, ScanRestriction __state)
        {
            if (__state == null || ScanRestriction.Current != __state) return;
            __state.ValidateCompletedJob(__instance, __1, ref __result);
            if (__result.Job != null && __state.Rejected > 0)
                ThreatSafetyLog.Tag(__result.Job, "NONE (selected after rejecting " + __state.Rejected + " unsafe exterior candidate(s))");
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, ScanRestriction __state)
        {
            ScanRestriction.End(__state);
            return __exception;
        }
    }
}
