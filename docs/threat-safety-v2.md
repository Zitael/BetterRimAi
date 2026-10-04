# Threat safety v2 decision

The policy is directional. Autonomous player colonists, mechs, and drones are filtered only while leaving the protected Home envelope. Indoor work and movement by pawns already outside remain with vanilla. Drafted, forced, and Attack-response pawns bypass the filter.

Retained: `ThreatAwareHomeSafety` (including enclosed unpainted pockets), `ThreatAwareSafeWorkCell` (safe-side Touch work), WorkGiver/ThinkNode/PUAH adapters, and deferred cancellation for the path safety net. `ThreatAwareOutdoorWorkPatch` is now only a boundary-crossing guard. `ThreatAwareThreat` retains vanilla potential-threat and verb geometry.

Deleted: global job/target blocks, target keys, retry cooldown and restriction state, per-pawn hostile lists, path state machine, path-start invalidation, and per-turret firing arrays. These represented the same restriction in several places and could strand outside pawns.

`ThreatAwareOutdoorPolicy` owns candidate decisions. `ThreatAwareOutdoorSafetyMap` owns one lazy snapshot per map. It scans active hostile pawns and registered attack structures once per 180 game ticks when outdoor candidates exist. Mobile danger spreads within the configured radius only through standable cells and open doors; turret danger uses `Verb.CanHitTargetFrom`. A single flood from protected cells marks exterior cells reachable without entering danger. The normal candidate cost is Home-envelope checks and one indexed lookup. No hostile enumeration, pathfinding, reflection, or map flood occurs in a warm candidate. A boundary crossing refreshes a snapshot older than 10 ticks and inspects the actual remaining path once. The game component resets transient state on a new game; expiry clears dead, removed, dormant, or disabled threats and changed firing geometry.

When vanilla chooses a hazardous route despite a safe alternative being present in the map flood, the boundary guard records that exact destination and exposed route cell in the same map snapshot. The next candidate for that destination is rejected before movement while that cell remains dangerous. This avoids an exit retry loop without a job-wide ban or time-only cooldown. Indoor work and unrelated exterior targets remain eligible.

`ThreatAwareDecision` retains a small weak-keyed per-pawn record and formats it only in `Pawn.GetInspectString`. Optional debug logs distinguish candidate rejection, path safety-net cancellation, and clearing a map's last threat. No Messages or Letters are emitted.

Limitations: mobile danger uses local passable connectivity rather than a tactical attack simulation. It does not model wall destruction, special abilities, diagonal corner movement, or every modded traversal rule. The protected envelope and threat snapshot update on coarse expiry, so immediate topology changes can take up to 180 ticks to affect candidate selection. The boundary guard revalidates more promptly. Manual RimWorld playtesting is required before merge.
