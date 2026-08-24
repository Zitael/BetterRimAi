using System.Reflection;
using HarmonyLib;
using Verse;

namespace BetterRimAI
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            var harmony = new Harmony("zitael.betterrimai");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[BetterRimAI] v0.1 loaded: long-trip need guard enabled.");
        }
    }
}
