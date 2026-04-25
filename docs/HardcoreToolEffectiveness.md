# Hardcore / Nightmare Tool Effectiveness Slider

## Background

Normal mode has a "bare-hands penalty" slider (`noToolStatFactorNormal`, default 0.4) that controls how slow a pawn works without any tool. This gives players a dial for how punishing the mode feels.

Hardcore and Nightmare have no equivalent. They gate on a binary pass/fail (tool score > 0), so once a pawn has the right type of tool, there is no setting that controls how much tool quality and condition matter to their output speed.

This feature adds that dial.

---

## Setting

**Field:** `hardcoreToolEffectiveness` (float)  
**Default:** 1.0  
**Range:** 0.5 – 1.5 (UI snaps to 5% steps)  
**Modes:** Only applied in Hardcore and Nightmare. Ignored in Normal mode.  
**Saved:** Yes, via `ExposeData`

---

## What It Does

The multiplier is applied in `ToolScoring.CalculateScore` **after** the quality curve and condition factor have already been applied:

```
score = (toolFactor - baseline)
score *= qualityCurve(quality)   // e.g. 0.7 awful → 1.6 legendary
score *= conditionFactor         // 0.5 + 0.5 * (HP / MaxHP)
score *= hardcoreToolEffectiveness   // ← new, HC/NM only
```

The gate check (`score > 0`) happens before this multiplier and is **not affected**. A wrong-type tool or a tool with score 0 still hard-blocks the job regardless of this setting.

---

## Effect on Gameplay

| Multiplier | Effect |
|---|---|
| 0.5× | Compressed range. An awful sickle and a legendary one have nearly identical speed. |
| 1.0× | Default. Quality and condition affect speed at their normal rates. |
| 1.5× | Exaggerated range. Masterwork/legendary tools are noticeably faster than normal-quality ones. |

This lets players soften or sharpen the reward curve for tool quality without changing the fundamental requirement to have the right tool type.

---

## UI

The slider appears in **Settings → Normal Mode section**, but is only visible when Hardcore or Nightmare mode is active. It uses an orange section header to distinguish it from the Normal-mode penalty controls.

The helper text updates contextually:
- Below 95%: *"Quality and condition matter less — an awful sickle performs closer to a legendary one."*
- Above 105%: *"Quality and condition matter more — there is a larger gap between poor and masterwork tools."*
- Near 100%: *"Default: quality and condition affect speed at their normal rates."*

---

## Relationship to the Normal Mode Slider

| Setting | Mode | Controls |
|---|---|---|
| `noToolStatFactorNormal` | Normal only | How slow a pawn is with **no tool at all** |
| `hardcoreToolEffectiveness` | HC/NM only | How much **tool quality/condition** affects speed once a valid tool is equipped |

They are independent. The Normal slider has no effect in HC/NM; the effectiveness slider has no effect in Normal.
