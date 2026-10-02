using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI.Tests
{
    [TestFixture, NonParallelizable]
    public class RuntimeContractTests
    {
        [Test]
        public void RequiredGameMethodsHaveExpectedSignatures()
        {
            Assert.That(AccessTools.Method(typeof(Pawn_PathFollower), "TryEnterNextPathCell"), Is.Not.Null);
            Assert.That(AccessTools.Method(typeof(Pawn_PathFollower), "StartPath"), Is.Not.Null);
            Assert.That(AccessTools.Method(typeof(Pawn_PathFollower), "StartPath", new[] { typeof(LocalTargetInfo), typeof(PathEndMode) }), Is.Not.Null);
            Assert.That(AccessTools.Method(typeof(Pawn_WorkSettings), "get_WorkGiversInOrderNormal"), Is.Not.Null);
            Assert.That(AccessTools.Method(typeof(Pawn_JobTracker), "EndCurrentJob"), Is.Not.Null);
            foreach (Type type in new[] { typeof(ThinkNode_JobGiver), typeof(JobGiver_Work) })
                Assert.That(AccessTools.DeclaredMethod(type, "TryIssueJobPackage", new[] { typeof(Pawn), typeof(JobIssueParams) }), Is.Not.Null, type.FullName);
            foreach (string name in new[] { "HasJobOnThing", "JobOnThing", "HasJobOnCell", "JobOnCell" })
                Assert.That(AccessTools.DeclaredMethod(typeof(WorkGiver_Scanner), name,
                    new[] { typeof(Pawn), name.EndsWith("Thing") ? typeof(Thing) : typeof(IntVec3), typeof(bool) }), Is.Not.Null, name);
            Assert.That(ThreatAwarePendingCancellation.TargetMethods().Count(), Is.EqualTo(2));
        }

        [Test]
        public void DiscoveredHarmonyTargetsAreConcreteAndTypeCompatible()
        {
            // GenTypes normally gets its assembly list from the running Unity/mod host.
            // Supply the real game types explicitly in the headless runner.
            FieldInfo cache = AccessTools.Field(typeof(GenTypes), "allTypesCached");
            Assert.That(cache, Is.Not.Null);
            object old = cache.GetValue(null);
            Type[] types;
            try { types = typeof(Pawn).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            cache.SetValue(null, types.ToList());
            try
            {
                var groups = new[]
                {
                    ThreatAwareBlockedThingCandidatePatch.TargetMethods().ToArray(),
                    ThreatAwareBlockedCellCandidatePatch.TargetMethods().ToArray(),
                    ThreatAwareThingJobPatch.TargetMethods().ToArray(),
                    ThreatAwareCellJobPatch.TargetMethods().ToArray(),
                    ThreatAwareNonScanJobPatch.TargetMethods().ToArray(),
                    ThreatAwareThinkNodePatch.TargetMethods().ToArray(),
                    RemoteWorkThingCandidatePatch.TargetMethods().ToArray(),
                    RemoteWorkCellCandidatePatch.TargetMethods().ToArray()
                };
                for (int g = 0; g < groups.Length; g++)
                {
                    Assert.That(groups[g], Is.Not.Empty, "target group " + g);
                    Assert.That(groups[g].Distinct().Count(), Is.EqualTo(groups[g].Length));
                    TestContext.WriteLine("Patch group " + g + ": " + groups[g].Length + " methods");
                    foreach (MethodInfo method in groups[g])
                    {
                        Assert.That(method.IsAbstract, Is.False);
                        Assert.That(method.ContainsGenericParameters, Is.False);
                        Assert.That(method.GetParameters()[0].ParameterType, Is.EqualTo(typeof(Pawn)));
                        Assert.That(method.ReturnType, Is.EqualTo(g < 2 || g >= 6 ? typeof(bool) : g == 5 ? typeof(ThinkResult) : typeof(Job)));
                    }
                }
                Assert.That(groups[5].Any(m => m.DeclaringType == typeof(JobGiver_Work)), Is.True);
                Assert.That(groups[5].Any(m => m.DeclaringType == typeof(ThinkNode_JobGiver)), Is.True);
            }
            finally { cache.SetValue(null, old); }
        }
    }
}
