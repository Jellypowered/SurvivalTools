# Always‑Boot Contract (applies to *every* phase)
- Game must boot clean, no red errors or exceptions.
- Existing behavior remains intact until the slice specifically says it’s refactored.
- PackageId, defNames, public classes, XML roots unchanged. Only add; do not rename/remove until a phase’s **Kill List** says so.
- Harmony signatures match RimWorld 1.6 exactly; one `PatchAll()` in the static ctor.
- No LINQ/allocations in hot paths (per‑tick, draw, `StatPart`); use pooled lists.
- All Dev/debug dumps write **to a Desktop file** (or platform fallback), not to the log. Regular logs stay quiet and deduped via `ST_Logging`.

---

# Phase 0 — Safety scaffolding (no behavior change)
**Goal:** add tiny utilities + a one‑click smoke test while keeping the game identical.

## 0.1 Add file I/O helper (new file)
**Path:** `Source/Helpers/ST_FileIO.cs`
```csharp
// RimWorld 1.6 / C# 7.3
// Source/Helpers/ST_FileIO.cs
using System;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace SurvivalTools
{
    internal static class ST_FileIO
    {
        internal static string DesktopPath()
        {
            try
            {
                var p = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch { /* ignore */ }
            // Fallbacks for platforms without a Desktop
            return Application.persistentDataPath ?? GenFilePaths.SaveDataFolderPath;
        }

        internal static string WriteUtf8Atomic(string fileName, string content)
        {
            var dir = DesktopPath();
            try { Directory.CreateDirectory(dir); } catch { }
            var path = Path.Combine(dir, fileName);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            File.Move(tmp, path);
            return path;
        }
    }
}
```

## 0.2 Add a dev‑only smoke test DebugAction (new file)
**Path:** `Source/DebugTools/DebugAction_DumpStatus.cs`
```csharp
// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_DumpStatus.cs
using System.Text;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    internal static class ST_DebugActions
    {
        [DebugAction("ST", "Dump ST status → Desktop", allowedGameStates: AllowedGameStates.PlayingOnMap)]
        private static void DumpStatus()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("[SurvivalTools] Status dump");
            sb.AppendLine("----------------------------");
            try { sb.AppendLine($"Settings: hardcore={(Settings?.hardcoreMode==true)} extraHardcore={(Settings?.extraHardcoreMode==true)}"); } catch { }
            try { sb.AppendLine($"Tools present: {DefDatabase<ThingDef>.AllDefsListForReading?.FindAll(t=>t!=null && t.IsSurvivalTool()).Count}"); } catch { }
            try { sb.AppendLine($"WorkSpeedGlobal jobs discovered: {Helpers.WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs().Count}"); } catch { }
            try { sb.AppendLine("Active mods carrying ST hooks:"); sb.AppendLine(CompatLine()); } catch { }
            sb.AppendLine();
            sb.AppendLine("Tip: use this after loading a save and mining a tile to verify wear/penalties.");
            var path = ST_FileIO.WriteUtf8Atomic($"ST_Status_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt", sb.ToString());
            try { Messages.Message("Survival Tools: wrote " + path, MessageTypeDefOf.TaskCompletion); } catch { }
        }

        private static string CompatLine()
        {
            try { return string.Join(", ", Helpers.WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs()); } catch { return "(n/a)"; }
        }
    }
}
```
> This does **not** add any new logging framework. It only writes debug dumps to a file. All runtime/event logging continues to go through `ST_Logging`.

## 0.3 Phase 0 acceptance
- Boots clean.
- New `ST → Dump ST status → Desktop` action present and writes a file.
- No functional changes to gameplay.

---

# Strangler Consolidation Track (append to *every* phase)
At the **end** of each phase, run these three Copilot prompts (paste verbatim):

### A) Forwarders (non‑breaking)
```
Find public methods duplicating tool bonuses/gating outside SurvivalToolUtility/StatPart/CompatAPI.
For each, add [Obsolete(false)] forwarders that call the new API with identical behavior.
XML doc: "Forwarder during refactor. Do not extend."
```

### B) Call‑site migration
```
Replace all direct calls to <OldMethod> with <NewMethod> in this slice.
Compile the solution and fix type/using mismatches without changing behavior.
```

### C) Deletion pass (Kill List)
```
Delete the following now‑redundant methods/types. Remove usings, keep build green:
<PHASE_KILL_LIST>
```
> The **Kill List** appears at the end of each phase section with concrete symbols. No “maybe later”.

---

# Phase 1 — Registry & Compat surface tighten‑up (no behavior change)
**Goal:** keep the current `CompatAPI`/helpers but lock down the single entry points: `RegisterWorkGiverRequirement`, `RegisterJobRequirement`, `RegisterStatAlias`, `(rare) RegisterToolQuirk`.

**Copilot work items**
- Add no‑op overloads matching current call sites so existing compat modules build untouched.
- Make registry indices O(1) lookups (Dict keyed by `WorkGiverDef`/`JobDef` and `StatDef`).
- Keep `OnAfterDefsLoaded(Action)` and `IsModActive(packageId)` as is.

**Kill List (Phase 1)**
- Any duplicated WG/job requirement maps in `Helpers`/compat modules once registry answers them.

**Acceptance**
- Boots clean; compat modules still function.
- DebugAction still writes file.

---

# Phase 2 — Resolver hardening (no behavior change)
**Goal:** finish the startup resolver so tool stats are cataloged once, with safe defaults.

**Copilot work items**
- Prefer explicit tool tags/properties; else intersect `statBases` with registered work stats; final fallback: name/verb hints.
- Cache per `(toolDef, stuffDef, stat)` factors and expose a safe row for `SpecialDisplayStats`.
- Clamp material beats “no tool” baseline on Normal.

**Kill List (Phase 2)**
- Any ad‑hoc stat inference duplicated in `ToolResolver` or scattered helpers once centralized.

**Acceptance**
- Boots clean; existing tools still behave the same.

---

# Phase 3 — Scoring & caches (partial redirect)
**Goal:** unify scoring in `SurvivalToolUtility` (already central). Expose `GetBestTool(pawn, stat)` and top‑pair data for UI.

**Copilot work items**
- Ensure caches keyed by `(pawn, tool, difficultySeed)`; invalidate on inventory/HP/quality/priorities.
- Respect difficulty multipliers and carry/mass penalties (but *don’t* change numbers yet).

**Kill List (Phase 3)**
- Redundant scoring in `AutoToolPickup_UtilityIntegrated`, `JobGiver_OptimizeSurvivalTools` (leave flow, replace guts).

**Acceptance**
- Boots clean; optimizer still acts the same.

---

# Phase 4 — StatPart as single math path
**Goal:** all bonuses/penalties come from `StatPart_SurvivalTool` for vanilla work stats.

**Copilot work items**
- Confirm it’s the only place multiplying work stats; no LINQ/allocs.
- Keep current penalty values. Hook deterministic wear ticks in here only when the job actually uses the stat.

**Kill List (Phase 4)**
- Any lingering per‑job math outside the StatPart.

**Acceptance**
- Boots clean; vanilla Stat Explanation matches, numbers unchanged.

---

# Phase 5 — Job gating (Nightmare/Hardcore/Normal)
**Goal:** gating checks before work starts. Normal applies baseline penalties only; Hardcore/Nightmare block.

**Copilot work items**
- Keep the existing `WorkGiver_Scanner` prefixes and `MissingRequiredCapacity` postfixes intact; route the decision through `StatGatingHelper`.
- Map difficulty: Normal(allowed, carry 3), Hardcore(gated, carry 2), Nightmare(gated++, carry 1; toolbelt up to 3). Add the migration mapping “Extra Hardcore → Nightmare”.

**Kill List (Phase 5)**
- Reflection fallback hooks in `Patch_Pawn_JobTracker_ExtraHardcore` once WG‑level gating fully covers it.

**Acceptance**
- Boots clean; “Mining on Nightmare without pickaxe → job blocked with clear reason”.

---

# Phase 6 — Assignment (auto‑equip without ping‑pong)
**Goal:** pre‑work hook asks `AssignmentSearch` to equip a clearly better tool within budget/radius; respect carry limits and reservations.

**Copilot work items**
- Pull order: inventory → storage/stockpile → home → nearby world.
- Drop worst surplus when over limit; add hysteresis to avoid ping‑pong.

**Kill List (Phase 6)**
- Duplicated assignment logic in optimizer once the pre‑work hook covers it.

**Acceptance**
- Boots clean; pawn auto‑equips a better pick within radius.

---

# Phase 7 — Gear iTab (single readable panel)
**Goal:** keep vanilla tab, draw our panel inside a child group; two‑line rows (name/score + short why), tooltips match SpecialDisplayStats.

**Copilot work items**
- Widen area (~360–420px). Consume mouse/scroll only inside our rect.
- Show difficulty, carry, gating status, buttons to open settings.

**Kill List (Phase 7)**
- Old label tweaks/transpiler if the new panel supersedes them.

**Acceptance**
- Boots clean; gear panel shows scores + why; numbers match Stat Explanation.

---

# Phase 8 — Animations (mining + melee, overlay‑only)
**Goal:** code‑only sprite transforms; zero allocations during draw.

**Copilot work items**
- Data‑driven frame lists; precompute per facing.
- Renderer postfix draws overlay last via `RenderPawnInternal(PawnDrawParms)`.
- Melee trigger from `Verb_MeleeAttack.TryCastShot` postfix; disable if CE/DualWield active.

**Kill List (Phase 8)**
- Any text motes for penalties/feedback (we’re “no motes”). Replace with subtle overlay cues.

**Acceptance**
- Boots clean; mining gesture plays & stops correctly; CE/DualWield do not double‑draw.

---

# Global: Debug dumps to Desktop (guidance)
- Any `DebugAction` must write files using `ST_FileIO.WriteUtf8Atomic`.
- Keep runtime logs quiet and deduped through **existing** `ST_Logging`.
- For repeated denials, rely on `ST_Logging.LogToolGateEvent(...)` (already deduped) and avoid `Log.Message` spam.

---

# Copilot paste‑blocks (use at the end of each phase)
**Hot‑path cleanup**
```
Search for LINQ/allocations inside draw, per‑tick, and StatPart paths.
Replace with pooled lists and for‑loops. Add comment: // HOT PATH – no LINQ.
```

**Kill List template**
```
// Remove now:
- <Namespace.Class.Method or file path>
// Rationale:
- <why it’s redundant>
```

---

# End‑to‑end acceptance (mining slice)
- Normal/Hardcore/Nightmare behave as specified; carry limits enforced; migration maps Extra Hardcore → Nightmare.
- Wear drains only while working; virtual stacks consume; tools destroy at 0; caches invalidate.
- Assignment upgrades within radius and avoids ping‑pong.
- Gear panel + Stat Explanation agree.
- Optional patches are fully guarded (FindMod + Conditional) and no‑op safely when missing.
- No GC spikes from StatPart, renderer, or assignment scans.

> When you complete Phases 0–2, jump to 4–5 to prove the loop, then 6–8 for the mining slice. After that, clone the pattern to construction/plants/etc.

