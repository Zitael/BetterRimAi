using System.Linq;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class ThreatAwareEndJobDiagnostics
    {
        private sealed class State
        {
            public Pawn pawn;
            public Job job;
            public int thingId = -1;
            public bool blocked;
        }

        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn, ref State __state)
        {
            __state = new State
            {
                pawn = ___pawn,
                job = ___pawn?.CurJob
            };

            if (__state.job == null || ___pawn == null)
            {
                return;
            }

            __state.blocked = ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(___pawn, __state.job);
            if (__state.job.targetA.HasThing && __state.job.targetA.Thing != null)
            {
                __state.thingId = __state.job.targetA.Thing.thingIDNumber;
            }
            else if (__state.job.targetB.HasThing && __state.job.targetB.Thing != null)
            {
                __state.thingId = __state.job.targetB.Thing.thingIDNumber;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(State __state)
        {
            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (__state == null || !__state.blocked || settings == null || !settings.threatDebugLogging)
            {
                return;
            }

            Pawn pawn = __state.pawn;
            Job before = __state.job;
            Job after = pawn?.CurJob;
            int reservations = 0;

            if (pawn?.Map?.reservationManager != null)
            {
                reservations = pawn.Map.reservationManager.ReservationsReadOnly.Count(r =>
                    r.Claimant == pawn && r.Job == before);
            }

            Log.Message(
                $"[BetterRimAI][diag-end] pawn={pawn?.LabelShort ?? "null"}, " +
                $"before={before?.def?.defName ?? "null"}, beforeForced={before?.playerForced ?? false}, " +
                $"after={after?.def?.defName ?? "null"}, sameJob={ReferenceEquals(before, after)}, " +
                $"remainingReservationsForOldJob={reservations}, thingId={__state.thingId}.");
        }
    }
}
