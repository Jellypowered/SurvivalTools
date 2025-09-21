SurvivalTools — Design Theory (Phases 0–7, no animations)
Purpose

Refactor SurvivalTools to a single-brain, single-surface architecture that keeps saves stable, keeps the game booting clean at every step, and replaces scattered logic with one fast, deterministic pipeline. Everything shipped so far preserves gameplay balance unless a difficulty mode or setting explicitly changes behavior.

North-star principles (what guided every decision)

One brain: a single resolver + scoring layer provides all math (what a tool does, how well, when it matters).

One door: a tiny, stable Compat API is the only entry point for integrations (register required stats, aliases, quirks).

One resolver: tools are discovered and interpreted once at startup—no XML sprawl or duplicated inference.

One UI surface: the Gear iTab is the sole place to read tool effects (plus normal Stat Explanation).

Always-Boot: every phase must load and keep prior behavior intact until the new path fully replaces it.

Performance discipline: no LINQ or allocations in hot paths; pooled buffers; explicit cache invalidation.

Save safety: no renames to package id/defNames/public types; only additive changes.

Determinism: same inputs → same outputs (important for explainability, caching, and debugging).

What’s implemented (by phase)
Phase 0 — Safety scaffolding

File dump helper and a dev-only DebugAction that writes status to Desktop.

No behavior changes; all debug output goes to files, not spammy logs.

Phase 1 — Compat & Registry

Minimal Compat API with fast indices:

RegisterWorkGiverRequirement, RegisterJobRequirement, RegisterStatAlias, RegisterToolQuirk(...).

Forwarders in place so old code keeps working; no deletions yet.

Phase 2 — Resolver (single source of truth)

ToolStatResolver catalogs tools once at startup with a strict precedence:

explicit tool tags/properties

statBases that intersect registered work stats

conservative name/verb hints

safe defaults

Per (toolDef, stuffDef, stat) ToolStatInfo is cached; clamping ensures a material never trails toolless baseline on Normal.

Quirk system (predicate + applier) injected after inference, before clamping for lightweight, additive adjustments.

Resolver Version integer increments on rebuild/quirk changes; anything versioned (e.g., scores) auto-invalidates.

Phase 3 — Scoring & caches

ToolScoring: deterministic, zero-alloc scoring; best-tool lookup; top-contributors for UI/explanations.

ScoreCache: struct keys (Pawn, Thing, StatDef, difficultySeed, ResolverVersion); explicit invalidation on:

inventory/equipment change; tool HP/quality change; settings change; resolver version bump.

Bench/debug actions verify performance (10k loop, 0 GC).

Phase 4 — StatPart (single math path)

StatPart_SurvivalTools is now the only way bonuses/penalties enter vanilla stat math.

Same numbers as legacy logic; concise ExplanationPart uses TopContributors.

Harmony patches trigger cache invalidation on inventory/equipment changes.

Phase 5 — Job gating (behavioral change by mode)

JobGate checks WorkGiver/Job required stats against the pawn’s best tool:

Normal: never blocks; penalties only (StatPart).

Hardcore/Nightmare: blocks jobs missing an appropriate tool with a keyed reason string.

WorkGiver_Scanner postfix/prefix patches perform authoritative gating with context-menu reasons.

Alert_ToolGatedWork surfaces why pawns idle (throttled, optional).

GatingEnforcer cancels now-illegal jobs and prunes queues on mode change and save load (quiet by default).

Phase 6 — Assignment (auto-equip before work)

AssignmentSearch.TryUpgradeFor scans inventory/nearby/stocks within radius & path budget, respecting:

min gain threshold; carry limits per difficulty; hysteresis to avoid ping-pong; reservations.

PreWork_AutoEquip prefix tries to equip before a job starts (and before gating blocks), deferring the job cleanly.

Settings expose thresholds; hot paths are allocation-free.

Phase 7 — Gear iTab (single, readable panel)

Right-side panel inside vanilla Gear tab shows:

header (mode, carry, settings),

two-line rows per tool: name+score, then why (top contributors),

tooltips reusing resolver math.

Accurate, alloc-free draw; numbers match Stat Explanation; no virtual-tool noise.

Cross-cutting: PatchGuard, XML, and DefOf correctness

ST_PatchGuard: at runtime, inspects Harmony patches on hotspots and removes legacy ST prefixes/postfixes not on an allowlist (without touching third-party mods). Prevents collisions with the new PreWork/Gear iTab paths.

XML repairs and DefOf elimination: fixed malformed XML; removed all early DefOf access by switching to OnAfterDefsLoaded/lazy initialization. Boots clean with no “Uninitialized DefOf” warnings.

Desktop-only debugging: all dumps and benches write to files; runtime logs stay minimal and use existing ST_Logging.

Architecture (how it fits together)

Startup

Compat registry builds the required-stat maps.

Resolver scans tools → applies quirks → clamps → caches rows; bumps Version.

During play

StatPart uses resolver data for every supported work stat; explanations show the same contributors as the UI.

ToolScoring & ScoreCache provide O(1) best-tool selection and per-tool scores, invalidating on the explicit triggers.

JobGate enforces difficulty policy at WorkGiver level.

AssignmentSearch equips a better/required tool just-in-time to avoid blocks.

Gear iTab visualizes the same math for players.

Alerts/Enforcer explain and enforce mode shifts without spam.

Design rationale (why this shape)

Determinism > cleverness: deterministic math enables explainable UI, stable caching, and reproducible debugging.

Strangler pattern: forwarders first, call-site migration second, deletions last—the game stays playable every commit.

Single source of truth: resolver + scoring remove drift between UI, stat math, and gating.

Small Harmony surface: patch only where value is highest (StatPart, WorkGiver gates, pre-work hook, gear tab draw). Fewer hooks = fewer conflicts.

User clarity: gating reasons, alerts, and the Gear iTab reduce “pawn won’t work” confusion.

Performance discipline: zero-alloc steady-state loops and explicit invalidation prevent GC spikes in renderer/job/scoring paths.

Interop safety: Compat API provides stable, tiny hooks; guarded PatchOps no-op when targets/mods are missing.

Invariants & guarantees

Always-Boot: no red errors; behavior changes are gated strictly by difficulty setting.

Save compatibility: package id, defNames, and public types unchanged; XML stays stable.

Localization: every .Translate() has a key under 1.6/Languages/English/Keyed/.

No motes: visual feedback (today: UI/alerts; later: overlays) never replaces vanilla weapon draw.

Hot paths: StatPart, pre-work hook, WorkGiver gates, Gear iTab draw are allocation-free.

Current user-visible impact

Normal: identical gameplay feel, but better explanations/UI.

Hardcore/Nightmare: tool-gated work is blocked with clear reasons; auto-equip often resolves blocks automatically.

Gear iTab: players can see scores and top contributors per tool, consistent with Stat Explanation.

Alerts: concise, throttled warning when pawns are gated (configurable).

Mode flips / save loads: invalid current/queued jobs cancel quietly; pawns won’t keep doing illegal work.

What’s intentionally not done yet

Animations/overlays (Phase 8).

Deleting all legacy files—PatchGuard isolates them; removal is staged after extended play-testing.

Powered tools & batteries; virtual resource tiers (later milestones).

Deep compat patches beyond the small guarded surface in Patches/.

Open follow-ups (post-Phase 7)

Kill-List deletions: remove legacy auto-equip/gating/UI guts now that the new paths are stable (keep [Obsolete(false)] shims if needed).

UI polish: optional highlight/sort by focused stat; grey out tools at/below baseline.

Perf audit: short profiler passes on large colonies to validate no GC in StatPart/WorkGiver/PreWork/GearTab steady state.

Telemetry (dev-only): count cache hit/miss; single-file desktop dump to spot pathological maps.

Acceptance snapshot (what we verify regularly)

Mining on Nightmare without a pickaxe → job blocked; alert line explains the missing stat/tool.

Normal/Hardcore: Stat Explanation and Gear iTab numbers match for Mining/Construction/Plants.

Auto-equip: before starting mining, a pawn near a clearly better pick equips it (within radius/budget); no ping-pong.

Debug actions produce clean Desktop files; no runtime log spam.

No GC during repeated scoring, pre-work checks, and gear tab scrolling.

This is the foundation the rest of the refactor will sit on: one resolver, one scoring & cache brain, one UI surface, and a minimal Harmony perimeter—deterministic, explainable, and safe to extend.
