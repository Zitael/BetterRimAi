using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    // Stores a few references/primitive facts while jobs are selected. Only a pawn that
    // reaches the exceptional path guard causes a bounded sequence of Player.log lines.
    internal static class ThreatAwareRuntimeTrace
    {
        private sealed class State
        {
            internal Job lastProposed;
            internal Type producer;
            internal string stage;
            internal string reason;
            internal bool safeTouch;
            internal Job lastStarted;
            internal Type startNode;
            internal int focusUntil;
            internal int guardCount;
            internal int linesLeft;
            internal bool uiLogged;
        }
        private static ConditionalWeakTable<Pawn, State> states = new ConditionalWeakTable<Pawn, State>();
        internal static void Reset() => states = new ConditionalWeakTable<Pawn, State>();
        private static bool Enabled => BetterRimAIMod.Settings?.threatDebugLogging == true;
        private static int Tick => Find.TickManager?.TicksGame ?? 0;

        internal static void Proposed(Pawn pawn, Job job, Type producer, string stage, bool rejected,
            string reason, bool safeTouch)
        {
            if (!Enabled || pawn == null || job == null) return;
            State state = states.GetOrCreateValue(pawn);
            if (!rejected)
            {
                state.lastProposed = job;
                state.producer = producer;
                state.stage = stage;
                state.reason = reason;
                state.safeTouch = safeTouch;
            }
            if (Focused(state)) Emit(pawn, state, "policy " + stage + " producer=" + producer?.FullName
                + " job=" + Describe(pawn, job) + " safeTouch=" + safeTouch + " reject=" + rejected
                + " reason=" + reason);
        }

        internal static void Started(Pawn pawn, Job job, ThinkNode sourceNode)
        {
            if (!Enabled || pawn == null || job == null) return;
            State state = states.GetOrCreateValue(pawn);
            state.lastStarted = job;
            state.startNode = sourceNode?.GetType();
            if (Focused(state)) Emit(pawn, state, "next job started node=" + state.startNode?.FullName
                + " job=" + Describe(pawn, job));
        }

        internal static void CandidateRejected(Pawn pawn, string stage, LocalTargetInfo target,
            string giver, bool safeTouch, string reason)
        {
            if (!Enabled || pawn == null || !states.TryGetValue(pawn, out State state) || !Focused(state)) return;
            Emit(pawn, state, "candidate rejected stage=" + stage + " giver=" + giver
                + " target=" + target.Cell + " safeTouch=" + safeTouch + " reason=" + reason);
        }

        internal static void Guard(Pawn pawn, Job job, LocalTargetInfo destination, IntVec3 dangerCell,
            Thing threat, string evidence)
        {
            if (!Enabled || pawn == null) return;
            State state = states.GetOrCreateValue(pawn);
            int tick = Tick;
            if (tick > state.focusUntil)
            {
                state.focusUntil = tick + 360;
                state.guardCount = 0;
                state.linesLeft = 24;
            }
            if (++state.guardCount > 3) return;
            Emit(pawn, state, "cycle start; selected producer=" + state.producer?.FullName
                + " stage=" + state.stage + " safeTouch=" + state.safeTouch
                + " policy=" + state.reason + " proposed=" + Describe(pawn, state.lastProposed));
            Emit(pawn, state, "StartJob node=" + state.startNode?.FullName
                + " started=" + Describe(pawn, state.lastStarted));
            Emit(pawn, state, "path guard job=" + Describe(pawn, job) + " pathDestination=" + destination.Cell
                + " dangerCell=" + dangerCell + " threat=" + threat?.LabelShort
                + " routeEvidence=" + evidence);
        }

        internal static void Cancelled(Pawn pawn, Job job, bool sameJob)
        {
            if (!Enabled || pawn == null || !states.TryGetValue(pawn, out State state) || !Focused(state)) return;
            Emit(pawn, state, "deferred cancellation sameJob=" + sameJob + " job=" + Describe(pawn, job));
        }

        internal static void CancelSkipped(Pawn pawn, Job job, string reason)
        {
            if (!Enabled || pawn == null || !states.TryGetValue(pawn, out State state) || !Focused(state)) return;
            Emit(pawn, state, "deferred cancellation skipped reason=" + reason + " job=" + Describe(pawn, job));
        }

        internal static void UiRendered(Pawn pawn)
        {
            if (!Enabled || pawn == null) return;
            State state = states.GetOrCreateValue(pawn);
            if (state.uiLogged) return;
            state.uiLogged = true;
            Log.Message("[BetterRimAI][trace] pawn=" + pawn.thingIDNumber
                + " selected-pawn inspect status rendered at tick=" + Tick);
        }

        private static bool Focused(State state) => Tick <= state.focusUntil && state.linesLeft > 0;
        private static void Emit(Pawn pawn, State state, string message)
        {
            if (state.linesLeft-- <= 0) return;
            Log.Message("[BetterRimAI][trace] tick=" + Tick + " pawn=" + pawn.thingIDNumber + " " + message);
        }
        private static string Describe(Pawn pawn, Job job)
        {
            if (job == null) return "null";
            return (job.def?.defName ?? "null") + " giver=" + job.workGiverDef?.defName
                + " A=" + Classify(pawn, job.targetA)
                + " B=" + Classify(pawn, job.targetB)
                + " C=" + Classify(pawn, job.targetC)
                + " forced=" + job.playerForced;
        }
        private static string Classify(Pawn pawn, LocalTargetInfo target)
        {
            if (!target.IsValid) return "invalid";
            Map map = pawn?.Map;
            Area_Home home = map?.areaManager?.Home;
            if (home == null || !target.Cell.InBounds(map)) return target.Cell + "/unknown";
            return target.Cell + (ThreatAwareHomeSafety.IsSafeCell(map, home, target.Cell) ? "/protected" : "/outside");
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class ThreatAwareStartJobTracePatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Pawn ___pawn, Job __0, ThinkNode __2)
            => ThreatAwareRuntimeTrace.Started(___pawn, __0, __2);
    }
}
