using System;
using RimWorld;
using Verse;

namespace BetterRimAI
{
    /// <summary>
    /// Cheap startup-only diagnostic: identifies the actual WorkGiver implementation behind the
    /// HaulToInventory def in the user's mod list. No Harmony patch and no tick-time work.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HaulToInventoryDiagnostics
    {
        static HaulToInventoryDiagnostics()
        {
            try
            {
                WorkGiverDef def = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulToInventory");
                if (def == null)
                {
                    Log.Message("[BetterRimAI][haul-def] WorkGiverDef HaulToInventory not found.");
                    return;
                }

                WorkGiver worker = def.Worker;
                Type type = worker?.GetType();
                Log.Message(
                    $"[BetterRimAI][haul-def] def={def.defName}, " +
                    $"giverClass={def.giverClass?.FullName ?? "null"}, " +
                    $"workerType={type?.FullName ?? "null"}, " +
                    $"assembly={type?.Assembly.GetName().Name ?? "null"}, " +
                    $"scanThings={def.scanThings}, scanCells={def.scanCells}, priorityInType={def.priorityInType}.");
            }
            catch (Exception ex)
            {
                Log.Error("[BetterRimAI][haul-def] failed: " + ex);
            }
        }
    }
}
