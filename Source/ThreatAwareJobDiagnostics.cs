using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Temporary high-detail diagnostics for every HaulToInventory start and for every job whose
    /// target BetterRimAI already considers unsafe. This is intentionally noisy when debug logging
    /// is enabled; remove/reduce it once the source of the hauling job is identified.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class ThreatAwareStartJobDiagnostics
    {
        [HarmonyPrefix]
        public static void Prefix(
            Pawn ___pawn,
            Job newJob,
            ThinkNode jobGiver,
            bool fromQueue,
            ThinkTreeDef thinkTree)
        {
            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatDebugLogging || ___pawn == null || newJob == null)
                return;

            bool isInteresting = string.Equals(newJob.def?.defName, "HaulToInventory", StringComparison.Ordinal)
                                 || ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(___pawn, newJob);
            if (!isInteresting)
                return;

            string workGiver = newJob.workGiverDef?.defName ?? "null";
            string sourceNode = jobGiver?.GetType().AssemblyQualifiedName ?? "null";
            string tree = thinkTree?.defName ?? "null";
            string current = DescribeJob(___pawn.CurJob);
            string targetA = DescribeTarget(newJob.targetA);
            string targetB = DescribeTarget(newJob.targetB);
            string targetC = DescribeTarget(newJob.targetC);
            string queue = DescribeQueue(___pawn);
            string driver = newJob.GetCachedDriver(___pawn)?.GetType().AssemblyQualifiedName ?? "null";
            string giverAssembly = jobGiver?.GetType().Assembly.GetName().Name ?? "null";
            string driverAssembly = newJob.GetCachedDriver(___pawn)?.GetType().Assembly.GetName().Name ?? "null";

            Log.Message(
                "[BetterRimAI][diag-start]\n" +
                $"pawn={___pawn.LabelShort} id={___pawn.thingIDNumber} pos={___pawn.Position}\n" +
                $"newJob={DescribeJob(newJob)}\n" +
                $"playerForced={newJob.playerForced}, fromQueue={fromQueue}, workGiverDef={workGiver}\n" +
                $"sourceNode={sourceNode}\nsourceAssembly={giverAssembly}\nthinkTree={tree}\n" +
                $"driver={driver}\ndriverAssembly={driverAssembly}\n" +
                $"targetA={targetA}\ntargetB={targetB}\ntargetC={targetC}\n" +
                $"currentBeforeStart={current}\nqueueBeforeStart={queue}\n" +
                $"stack={ShortStack()}");
        }

        private static string DescribeJob(Job job)
        {
            if (job == null) return "null";
            return $"{job.def?.defName ?? "null"}#obj{job.GetHashCode()} forced={job.playerForced} " +
                   $"workGiver={job.workGiverDef?.defName ?? "null"}";
        }

        private static string DescribeTarget(LocalTargetInfo target)
        {
            if (!target.IsValid) return "invalid";
            if (target.HasThing && target.Thing != null)
            {
                Thing t = target.Thing;
                return $"Thing({t.LabelCap}, def={t.def?.defName ?? "null"}, id={t.thingIDNumber}, " +
                       $"type={t.GetType().AssemblyQualifiedName}, pos={t.Position})";
            }
            return $"Cell({target.Cell})";
        }

        private static string DescribeQueue(Pawn pawn)
        {
            if (pawn?.jobs?.jobQueue == null) return "null";
            try
            {
                return "[" + string.Join(" | ", pawn.jobs.jobQueue.Select(q =>
                    q?.job == null ? "null" : DescribeJob(q.job))) + "]";
            }
            catch (Exception ex)
            {
                return "<queue read failed: " + ex.GetType().Name + ">";
            }
        }

        private static string ShortStack()
        {
            try
            {
                return string.Join(" <- ", new System.Diagnostics.StackTrace(2, false)
                    .GetFrames()
                    ?.Take(12)
                    .Select(f =>
                    {
                        MethodBase m = f.GetMethod();
                        return (m?.DeclaringType?.FullName ?? "?") + "." + (m?.Name ?? "?");
                    }) ?? Enumerable.Empty<string>());
            }
            catch
            {
                return "<stack unavailable>";
            }
        }
    }
}
