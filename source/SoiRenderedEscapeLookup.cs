using System;
using System.Collections.Generic;

namespace OrbitInfluences
{
    // Fallback for displayed patches not reachable from an unloaded vessel's
    // solver chain (notably Tracking Station paths after a moon flyby).
    internal static class SoiRenderedEscapeLookup
    {
        internal static bool TryGetEscape(Vessel vessel, CelestialBody body,
            double afterUT, bool includeManeuvers, out Orbit escapePatch,
            out double escapeUT)
        {
            escapePatch = null;
            escapeUT = double.NaN;
            if (vessel == null || body == null || vessel.patchedConicRenderer == null)
                return false;
            double now = Planetarium.GetUniversalTime();
            double earliest = Finite(afterUT) ? Math.Max(now, afterUT) : now;
            double firstBurn = double.PositiveInfinity;
            PatchedConicSolver solver = vessel.patchedConicSolver;
            if (solver != null && solver.maneuverNodes != null)
                foreach (ManeuverNode node in solver.maneuverNodes)
                    if (node != null && Finite(node.UT) && node.UT >= now &&
                        node.UT < firstBurn)
                        firstBurn = node.UT;

            double best = double.PositiveInfinity;
            PatchedConicRenderer renderer = vessel.patchedConicRenderer;
            Scan(renderer.patchRenders, body, earliest,
                 includeManeuvers ? firstBurn : double.PositiveInfinity,
                 ref best, ref escapePatch);
            if (includeManeuvers)
                Scan(renderer.flightPlanRenders, body, earliest,
                     double.PositiveInfinity, ref best, ref escapePatch);
            if (escapePatch == null)
                return false;
            escapeUT = best;
            return true;
        }

        private static void Scan(IEnumerable<PatchRendering> renderings,
            CelestialBody body, double earliest, double latest,
            ref double best, ref Orbit bestPatch)
        {
            if (renderings == null)
                return;
            foreach (PatchRendering rendering in renderings)
            {
                Orbit patch = rendering == null ? null : rendering.patch;
                if (patch == null || patch.referenceBody != body ||
                    patch.patchEndTransition != Orbit.PatchTransitionType.ESCAPE ||
                    !Finite(patch.EndUT) || patch.EndUT < earliest ||
                    patch.EndUT > latest || patch.EndUT >= best)
                    continue;
                best = patch.EndUT;
                bestPatch = patch;
            }
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
