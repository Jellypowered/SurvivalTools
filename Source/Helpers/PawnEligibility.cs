// RimWorld 1.6 / C# 7.3
// Shared pawn eligibility helpers for Survival Tools gating / prework / rescue / assignment.
using RimWorld;
using Verse;

namespace SurvivalTools.Helpers
{
    internal static class PawnEligibility
    {
        internal static bool IsEligibleColonistHuman(Pawn p)
        {
            if (p == null) return false;
            if (p.Dead || p.Destroyed) return false;
            // Exclude player-owned mechanoids entirely (colony mechs). Comprehensive check catches modded mechanoids.
            if (PawnToolValidator.IsMechanoidOrInherited(p) && p.Faction == Faction.OfPlayer) return false;
            if (!(p.RaceProps?.Humanlike ?? false)) return false;
            return p.IsColonistPlayerControlled; // includes slaves / recruited guests under player control
        }
    }
}
