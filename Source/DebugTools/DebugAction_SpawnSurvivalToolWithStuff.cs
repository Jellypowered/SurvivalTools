using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using LudeonTK;

namespace SurvivalTools.DebugTools
{
    public static class DebugAction_SpawnSurvivalToolWithStuff
    {
        [LudeonTK.DebugAction("Survival Tools", "Spawn tool with stuff...", false, false)]
        public static void SpawnToolWithStuff()
        {
            var toolDefs = DefDatabase<ThingDef>.AllDefs
                .Where(def => def.IsSurvivalTool())
                .OrderBy(def => def.modContentPack?.Name ?? "Unknown")
                .ThenBy(def => def.label)
                .ToList();

            if (!toolDefs.Any())
            {
                Messages.Message("No survival tool definitions found.", MessageTypeDefOf.RejectInput);
                return;
            }

            var toolOptions = new List<DebugMenuOption>();
            foreach (var toolDef in toolDefs)
            {
                var modName = toolDef.modContentPack?.Name ?? "Unknown";
                var displayName = $"{toolDef.LabelCap} ({modName})";

                toolOptions.Add(new DebugMenuOption(displayName, DebugMenuOptionMode.Action, () =>
                {
                    // Check if this tool can be made from stuff
                    if (toolDef.MadeFromStuff)
                    {
                        ShowStuffPicker(toolDef);
                    }
                    else
                    {
                        // For things that can't be made from stuff (like cloth, raw materials, etc.)
                        // spawn them directly
                        PrepareToolSpawn(toolDef, null);
                    }
                }));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(toolOptions));
        }

        private static void ShowStuffPicker(ThingDef toolDef)
        {
            var stuffDefs = DefDatabase<ThingDef>.AllDefs
                .Where(def => def.IsStuff && def.stuffProps != null && toolDef.MadeFromStuff)
                .OrderBy(def => def.label)
                .ToList();

            if (!stuffDefs.Any())
            {
                Messages.Message($"{toolDef.LabelCap} cannot be made from stuff.", MessageTypeDefOf.RejectInput);
                return;
            }

            var stuffOptions = new List<DebugMenuOption>();

            // Add a separator for materials with StuffPropsTool
            var stuffPropsStuff = stuffDefs.Where(def => def.GetModExtension<StuffPropsTool>() != null).ToList();
            if (stuffPropsStuff.Any())
            {
                stuffOptions.Add(new DebugMenuOption("=== Materials with StuffPropsTool ===", DebugMenuOptionMode.Action, () => { }));
                foreach (var stuffDef in stuffPropsStuff)
                {
                    var ext = stuffDef.GetModExtension<StuffPropsTool>();
                    var statCount = ext?.toolStatFactors?.Count ?? 0;
                    stuffOptions.Add(new DebugMenuOption($"{stuffDef.LabelCap} ({statCount} tool stats)", DebugMenuOptionMode.Action, () =>
                    {
                        PrepareToolSpawn(toolDef, stuffDef);
                    }));
                }
                stuffOptions.Add(new DebugMenuOption("", DebugMenuOptionMode.Action, () => { }));
            }

            // Add materials with SurvivalToolProperties (virtual tools)
            var virtualToolStuff = stuffDefs.Where(def => def.GetModExtension<SurvivalToolProperties>() != null).ToList();
            if (virtualToolStuff.Any())
            {
                stuffOptions.Add(new DebugMenuOption("=== Materials with SurvivalToolProperties ===", DebugMenuOptionMode.Action, () => { }));
                foreach (var stuffDef in virtualToolStuff)
                {
                    var ext = stuffDef.GetModExtension<SurvivalToolProperties>();
                    var statCount = ext?.baseWorkStatFactors?.Count ?? 0;
                    stuffOptions.Add(new DebugMenuOption($"{stuffDef.LabelCap} ({statCount} tool stats)", DebugMenuOptionMode.Action, () =>
                    {
                        PrepareToolSpawn(toolDef, stuffDef);
                    }));
                }
                stuffOptions.Add(new DebugMenuOption("", DebugMenuOptionMode.Action, () => { }));
            }

            // Add regular materials
            var regularStuff = stuffDefs.Where(def =>
                def.GetModExtension<StuffPropsTool>() == null &&
                def.GetModExtension<SurvivalToolProperties>() == null)
                .ToList();

            if (regularStuff.Any())
            {
                stuffOptions.Add(new DebugMenuOption("=== Regular Materials ===", DebugMenuOptionMode.Action, () => { }));
                foreach (var stuffDef in regularStuff)
                {
                    stuffOptions.Add(new DebugMenuOption(stuffDef.LabelCap, DebugMenuOptionMode.Action, () =>
                    {
                        PrepareToolSpawn(toolDef, stuffDef);
                    }));
                }
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(stuffOptions));
        }

        private static void PrepareToolSpawn(ThingDef toolDef, ThingDef stuffDef)
        {
            var tool = ThingMaker.MakeThing(toolDef, stuffDef);
            if (tool == null)
            {
                var errorMessage = stuffDef != null
                    ? $"Failed to create {toolDef.LabelCap} from {stuffDef.LabelCap}."
                    : $"Failed to create {toolDef.LabelCap}.";
                Messages.Message(errorMessage, MessageTypeDefOf.RejectInput);
                return;
            }

            // Show info about what we're about to spawn
            string infoMessage = $"Click to place {tool.LabelCap}";

            if (stuffDef != null)
            {
                var stuffPropsExt = stuffDef.GetModExtension<StuffPropsTool>();
                var virtualExt = stuffDef.GetModExtension<SurvivalToolProperties>();

                if (stuffPropsExt != null)
                {
                    infoMessage += $" (StuffPropsTool: {stuffPropsExt.toolStatFactors?.Count ?? 0} stats)";
                }
                else if (virtualExt != null)
                {
                    infoMessage += $" (SurvivalToolProperties: {virtualExt.baseWorkStatFactors?.Count ?? 0} stats)";
                }
                else
                {
                    infoMessage += " (No tool stat modifiers)";
                }
            }
            else
            {
                // Check if the tool itself has SurvivalToolProperties (virtual tool)
                var toolExt = toolDef.GetModExtension<SurvivalToolProperties>();
                if (toolExt != null)
                {
                    infoMessage += $" (Virtual tool: {toolExt.baseWorkStatFactors?.Count ?? 0} stats)";
                }
                else
                {
                    infoMessage += " (Raw material - virtual tool capabilities)";
                }
            }

            Messages.Message(infoMessage, MessageTypeDefOf.NeutralEvent);

            // Set up the placement tool
            DebugTool placementTool = new DebugTool($"Place {tool.LabelCap}", delegate
            {
                var map = Find.CurrentMap;
                if (map == null)
                {
                    Messages.Message("No current map.", MessageTypeDefOf.RejectInput);
                    return;
                }

                IntVec3 pos = UI.MouseCell();
                if (pos.InBounds(map))
                {
                    GenSpawn.Spawn(tool, pos, map);
                    Messages.Message($"Spawned {tool.LabelCap}", MessageTypeDefOf.TaskCompletion);
                }
            });

            LudeonTK.DebugTools.curTool = placementTool;
        }
    }
}
