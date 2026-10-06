using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace BetterRimAI.RimWorldTests
{
    /// <summary>
    /// Verse.Log hands every message to Unity's native logger, which only exists inside the game
    /// process; under an external NUnit runner the call throws a SecurityException ("ECall methods
    /// must be packaged into a system module") and aborts the test before its assertions run.
    /// This captures the messages instead of forwarding them to Unity. It does not hide them:
    /// tests call <see cref="AssertNoErrors"/>, so any error the code logs fails the test with its
    /// full text.
    /// </summary>
    [SetUpFixture]
    public sealed class LogCapture
    {
        private const string HarmonyId = "BetterRimAI.tests.log-capture";
        private static readonly List<string> errors = new List<string>();
        private static readonly List<string> messages = new List<string>();

        internal static IReadOnlyList<string> Errors => errors;

        [OneTimeSetUp]
        public void Install()
        {
            var harmony = new Harmony(HarmonyId);
            Patch(harmony, nameof(Log.Message), new[] { typeof(string) }, nameof(Message));
            Patch(harmony, nameof(Log.Warning), new[] { typeof(string) }, nameof(Warning));
            Patch(harmony, nameof(Log.WarningOnce), new[] { typeof(string), typeof(int) }, nameof(Warning));
            Patch(harmony, nameof(Log.Error), new[] { typeof(string) }, nameof(Error));
            Patch(harmony, nameof(Log.ErrorOnce), new[] { typeof(string), typeof(int) }, nameof(Error));
        }

        [OneTimeTearDown]
        public void Uninstall() => new Harmony(HarmonyId).UnpatchAll(HarmonyId);

        private static void Patch(Harmony harmony, string method, System.Type[] args, string prefix) =>
            harmony.Patch(AccessTools.Method(typeof(Log), method, args),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(LogCapture), prefix)));

        private static bool Message(string text) { messages.Add("Message: " + text); return false; }
        private static bool Warning(string text) { messages.Add("Warning: " + text); return false; }
        private static bool Error(string text) { errors.Add(text); return false; }

        internal static void Clear()
        {
            errors.Clear();
            messages.Clear();
        }

        /// <summary>Fails the test if anything was logged as an error since the last <see cref="Clear"/>.</summary>
        internal static void AssertNoErrors(string during)
        {
            foreach (string m in messages) TestContext.WriteLine(m);
            Assert.That(errors, Is.Empty, "errors logged during " + during + ":\n" + string.Join("\n---\n", errors.ToArray()));
        }
    }
}
