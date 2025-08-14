using RimWorld;
using UnityEngine;
using Verse;

namespace SurvivalTools
{
    public static class ActiveToolDrawer
    {
        // Call from your postfix
        public static void DrawStaticTool(Pawn pawn, Vector3 rootLoc, Rot4 facing, float altitude)
        {
            if (!SurvivalToolsSettings.drawToolsDuringWork) return;
            if (pawn == null || pawn.Map == null) return;

            var tool = ActiveToolResolver.TryGetActiveTool(pawn);
            if (tool == null) return;

            var graphic = tool.Graphic;
            if (graphic == null) return;

            // Settings-driven offset (same as preview)
            Vector3 off = SurvivalToolsSettings.OffsetFor(facing).RotatedBy(facing);

            Vector3 pos = rootLoc + off;
            pos.y = altitude;

            // Yaw the mesh by facing so the sprite actually turns
            float angle = AngleForFacing(facing);
            Quaternion rot = Quaternion.AngleAxis(angle, Vector3.up);

            Mesh mesh = MeshPool.plane10;
            Material mat = graphic.MatAt(facing);

            Matrix4x4 m = default;
            m.SetTRS(pos, rot, Vector3.one);
            Graphics.DrawMesh(mesh, m, mat, 0);


        }

        private static float AngleForFacing(Rot4 f)
        {
            if (f == Rot4.East) return 0f;
            if (f == Rot4.South) return 90f;
            if (f == Rot4.West) return 180f;
            return -90f; // North
        }


    }
}

