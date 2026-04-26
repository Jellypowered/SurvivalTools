// RimWorld 1.6 / C# 7.3
// Pure mapping functions: raw setting values → effective values under global difficulty scale.
// All methods are side-effect-free. Hard clamps prevent any subsystem from receiving unsafe values.
// At scale == 1.0, every function returns baseSetting unchanged (neutral parity guarantee).
using UnityEngine;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Maps the global difficulty scale slider to per-subsystem effective values.
    /// Call these only through the computed properties on <see cref="SurvivalToolsSettings"/>;
    /// never call them directly from gameplay code.
    /// </summary>
    public static class DifficultyScaling
    {
        public const float MinScale = 0.5f;
        public const float MaxScale = 2.0f;
        public const float NeutralScale = 1.0f;

        // -----------------------------------------------------------------------
        // Formula 2/3 — No-tool penalty factor
        // Harder = lower factor = steeper throughput penalty when unequipped.
        // effective = Clamp(base / scale, 0.05, 1.0)
        // scale 1.0 → unchanged  |  scale 2.0 → 0.4 becomes 0.20  |  scale 0.5 → 0.80
        // -----------------------------------------------------------------------
        public static float ScaleNoToolFactor(float baseFactor, float scale) =>
            Mathf.Clamp(baseFactor / scale, 0.05f, 1.0f);

        // -----------------------------------------------------------------------
        // Formula 7/8/9 — HC/NM tool effectiveness
        // Harder = higher effectiveness = quality and condition gaps matter more.
        // Uses sqrt to dampen the curve so max 2.0 scale doesn't trivially hit 1.5 clamp.
        // effective = Clamp(base * sqrt(scale), 0.5, 1.5)
        // scale 1.0 → unchanged  |  scale 2.0 → ×1.414  |  scale 0.5 → ×0.707
        // -----------------------------------------------------------------------
        public static float ScaleHardcoreEffectiveness(float baseEff, float scale) =>
            Mathf.Clamp(baseEff * Mathf.Sqrt(scale), 0.5f, 1.5f);

        // -----------------------------------------------------------------------
        // Formula 10/11 — Assignment min-gain threshold
        // Harder = higher threshold = only clearly superior tools trigger reassignment.
        // effective = Clamp(base * scale, 0.01, 0.50)
        // scale 1.0 → unchanged  |  scale 2.0 → 0.10 becomes 0.20  |  scale 0.5 → 0.05
        // -----------------------------------------------------------------------
        public static float ScaleAssignMinGainPct(float baseGain, float scale) =>
            Mathf.Clamp(baseGain * scale, 0.01f, 0.50f);

        // -----------------------------------------------------------------------
        // Formula 13 — Assignment search radius
        // Harder = shorter radius = fewer candidates considered.
        // effective = Clamp(base / scale, 5, 200)
        // scale 1.0 → unchanged  |  scale 2.0 → 25 becomes 12.5  |  scale 0.5 → 50
        // -----------------------------------------------------------------------
        public static float ScaleSearchRadius(float baseRadius, float scale) =>
            Mathf.Clamp(baseRadius / scale, 5f, 200f);

        // -----------------------------------------------------------------------
        // Formula 13 — Assignment path-cost budget
        // Harder = tighter budget = fewer candidate paths evaluated.
        // effective = RoundToInt(Clamp(base / scale, 50, 5000))
        // scale 1.0 → unchanged  |  scale 2.0 → 500 becomes 250  |  scale 0.5 → 1000
        // -----------------------------------------------------------------------
        public static int ScalePathBudget(int baseBudget, float scale) =>
            Mathf.RoundToInt(Mathf.Clamp(baseBudget / scale, 50f, 5000f));

        // -----------------------------------------------------------------------
        // Formulas 15/16/17 — ToolResolver tech-level multipliers
        // Harder = lower multiplier = auto-enhanced modded tools are weaker.
        // Uses sqrt to stay comfortably within the existing per-tier bounds.
        // effective = Clamp(base / sqrt(scale), min, max)
        // scale 1.0 → unchanged  |  scale 2.0 → ÷1.414  |  scale 0.5 → ÷0.707
        // -----------------------------------------------------------------------
        public static float ScaleResolverMult(float baseMult, float scale, float min, float max) =>
            Mathf.Clamp(baseMult / Mathf.Sqrt(scale), min, max);

        // -----------------------------------------------------------------------
        // Formulas 19/20 — Tool degradation
        // Returns a pure multiplier applied on top of the base degradation factor.
        // Harder = faster wear.
        // effective multiplier = Clamp(scale, 0.1, 5.0)
        // scale 1.0 → ×1.0 (unchanged)  |  scale 2.0 → ×2.0  |  scale 0.5 → ×0.5
        // -----------------------------------------------------------------------
        public static float DegradationMultiplier(float scale) =>
            Mathf.Clamp(scale, 0.1f, 5.0f);
    }
}
