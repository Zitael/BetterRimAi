using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Debug log for "did BetterRimAI touch this job, why, and at which layer". Everything is
    /// off unless "Debug threat decisions" is enabled, except invariant violations: changing a
    /// job while no threat is active is always reported as an error.
    /// </summary>
    internal static class ThreatSafetyLog
    {
        private const string Prefix = "[BetterRimAI][threat] ";
        private const int PawnCooldownTicks = 300;
        private const int InvariantCooldownTicks = 2500;

        private static readonly Dictionary<int, int> lastReject = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> suppressedRejects = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> lastSafeSide = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> lastInvariant = new Dictionary<int, int>();
        private static ConditionalWeakTable<Job, string> tags = new ConditionalWeakTable<Job, string>();

        internal static bool Enabled => BetterRimAIMod.Settings?.threatDebugLogging == true;
        private static int Tick => Find.TickManager?.TicksGame ?? 0;

        internal static void Reset()
        {
            lastReject.Clear();
            suppressedRejects.Clear();
            lastSafeSide.Clear();
            lastInvariant.Clear();
            tags = new ConditionalWeakTable<Job, string>();
        }

        internal static void StateChanged(Map map, bool active, int threatCount, Thing example)
        {
            if (!Enabled) return;
            Log.Message(Prefix + "map=" + map?.uniqueID + " THREAT STATE became " + (active
                ? "ACTIVE (" + threatCount + " active threat(s), e.g. " + example?.LabelShort + " at " + example?.Position + ")"
                : "CLEAR"));
        }

        internal static void CandidateRejected(Pawn pawn, WorkGiverDef giver, LocalTargetInfo target, Verdict verdict)
        {
            if (!Enabled) return;
            if (!Cooldown(lastReject, pawn, PawnCooldownTicks))
            {
                suppressedRejects.TryGetValue(pawn.thingIDNumber, out int n);
                suppressedRejects[pawn.thingIDNumber] = n + 1;
                return;
            }
            suppressedRejects.TryGetValue(pawn.thingIDNumber, out int suppressed);
            suppressedRejects[pawn.thingIDNumber] = 0;
            Log.Message(Prefix + "pawn=" + pawn.LabelShort + " giver=" + giver?.defName + " target=" + Describe(target)
                + " BetterRimAI action=REJECT layer=JobGiver_Work candidate reason=" + verdict.Describe()
                + (suppressed > 0 ? " (+" + suppressed + " similar rejections not shown)" : ""));
        }

        internal static void CompletedJobRejected(Pawn pawn, Job job, LocalTargetInfo culprit, Verdict verdict, bool gaveUp)
        {
            if (!Enabled) return;
            Log.Message(Prefix + "pawn=" + pawn.LabelShort + " job=" + job.def?.defName + " giver=" + job.workGiverDef?.defName
                + " target=" + Describe(culprit) + " BetterRimAI action=REJECT layer=JobGiver_Work completed job reason="
                + verdict.Describe() + (gaveUp ? " -> no safe work job; vanilla continues with needs/joy/idle" : " -> rescanning without it"));
        }

        internal static void SafeSide(Pawn pawn, Job job, LocalTargetInfo target, IntVec3 cell)
        {
            if (!Enabled) return;
            Tag(job, "SAFE_SIDE interactionCell=" + cell);
            if (!Cooldown(lastSafeSide, pawn, PawnCooldownTicks)) return;
            Log.Message(Prefix + "pawn=" + pawn.LabelShort + " job=" + job.def?.defName + " target=" + Describe(target)
                + " BetterRimAI action=SAFE_SIDE layer=StartPath interactionCell=" + cell);
        }

        /// <summary>Every BetterRimAI change must happen under an active threat. Anything else is a bug.</summary>
        internal static void Invariant(string action, Pawn pawn, Job job)
        {
            if (pawn != null && !Cooldown(lastInvariant, pawn, InvariantCooldownTicks)) return;
            Log.Error("[BetterRimAI] INVARIANT VIOLATION: threat safety performed " + action + " for " + pawn?.LabelShort
                + " (job=" + job?.def?.defName + ") while no threat was active. Please report this log.");
        }

        /// <summary>Threat safety fails open: on an exception vanilla's decision stands.</summary>
        internal static void Failed(string what, Exception ex)
            => Log.ErrorOnce("[BetterRimAI] Threat safety " + what + " failed; leaving vanilla's decision unchanged: " + ex,
                0x5AFE2000 ^ what.GetHashCode());

        internal static void Tag(Job job, string tag)
        {
            if (job == null || !Enabled) return;
            tags.Remove(job);
            tags.Add(job, tag);
        }

        /// <summary>
        /// Selected-pawn trace: one line per started job showing whether BetterRimAI changed it.
        /// Select the pawn under investigation and watch the log.
        /// </summary>
        internal static void JobStarted(Pawn pawn, Job job, ThinkNode giver)
        {
            if (pawn?.Map == null || job == null || Find.Selector == null || !Find.Selector.IsSelected(pawn)) return;
            string state = BetterRimAIMod.Settings?.threatAwareOutdoorWork != true ? "DISABLED"
                : MapThreatState.For(pawn.Map).Active ? "ACTIVE" : "CLEAR";
            if (!tags.TryGetValue(job, out string action)) action = "NONE";
            Log.Message(Prefix + "pawn=" + pawn.LabelShort + " job=" + job.def?.defName + " A=" + Describe(job.targetA)
                + " B=" + Describe(job.targetB) + " jobGiver=" + giver?.GetType().FullName + " forced=" + job.playerForced
                + " threatState=" + state + " BetterRimAI action=" + action);
        }

        private static bool Cooldown(Dictionary<int, int> last, Pawn pawn, int ticks)
        {
            int tick = Tick, id = pawn.thingIDNumber;
            if (last.TryGetValue(id, out int previous) && tick >= previous && tick - previous < ticks) return false;
            last[id] = tick;
            return true;
        }

        private static string Describe(LocalTargetInfo target)
        {
            if (!target.IsValid) return "none";
            return target.HasThing ? target.Thing.LabelShort + "@" + target.Cell : target.Cell.ToString();
        }
    }

    /// <summary>Diagnostics only: never changes the job.</summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class ThreatSafetyJobTracePatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Pawn ___pawn, Job __0, ThinkNode __2)
        {
            if (ThreatSafetyLog.Enabled) ThreatSafetyLog.JobStarted(___pawn, __0, __2);
        }
    }
}
