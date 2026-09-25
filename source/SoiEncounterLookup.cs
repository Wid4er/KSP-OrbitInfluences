using System;
using System.Collections.Generic;

namespace OrbitInfluences
{
    // Looks for a future SOI encounter in KSP's displayed patched-conic plan.
    internal static class SoiEncounterLookup
    {
        internal static bool TryGetEncounterOrbit(Vessel vessel, CelestialBody body,
                                                  out Orbit encounterOrbit)
        {
            double ignoredUT;
            return TryGetEncounter(vessel, body, out encounterOrbit, out ignoredUT);
        }

        internal static bool TryGetEncounter(Vessel vessel, CelestialBody body,
                                             out Orbit encounterOrbit, out double encounterUT)
        {
            return TryGetEncounter(vessel, body, true, out encounterOrbit,
                                   out encounterUT);
        }

        internal static bool TryGetEncounter(Vessel vessel, CelestialBody body,
                                             bool includeManeuvers,
                                             out Orbit encounterOrbit, out double encounterUT)
        {
            encounterOrbit = null;
            encounterUT = double.NaN;
            if (vessel == null || body == null)
                return false;

            double now = Planetarium.GetUniversalTime();
            double bestUT = double.PositiveInfinity;
            double firstManeuverUT = double.PositiveInfinity;
            PatchedConicSolver solver = vessel.patchedConicSolver;
            if (solver != null)
            {
                IList<ManeuverNode> nodes = solver.maneuverNodes;
                if (nodes != null)
                {
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        ManeuverNode node = nodes[i];
                        if (node != null && IsFinite(node.UT) && node.UT >= now &&
                            node.UT < firstManeuverUT)
                            firstManeuverUT = node.UT;
                    }
                }

                // KSP builds flightPlan when a maneuver changes the predicted
                // path; the ordinary patches describe the unburned orbit.
                // With maneuver nodes, flightPlan can still contain unburned
                // entries. The node branches are the authoritative plan.
                if (double.IsPositiveInfinity(firstManeuverUT))
                    ScanList(solver.flightPlan, body, now, double.PositiveInfinity,
                             ref bestUT, ref encounterOrbit);
                // When planned waves are off, follow the complete unburned
                // patch chain, including transitions after the first node.
                ScanList(solver.patches, body, now,
                         includeManeuvers ? firstManeuverUT : double.PositiveInfinity,
                         ref bestUT, ref encounterOrbit);

                // KSP also exposes each post-burn prediction from the node.
                // Stop at the next planned burn, where this branch changes.
                if (includeManeuvers && nodes != null)
                {
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        ManeuverNode node = nodes[i];
                        if (node == null || !IsFinite(node.UT) || node.UT < now)
                            continue;
                        double nextBurnUT = double.PositiveInfinity;
                        for (int j = 0; j < nodes.Count; j++)
                        {
                            ManeuverNode later = nodes[j];
                            if (later != null && IsFinite(later.UT) &&
                                later.UT > node.UT && later.UT < nextBurnUT)
                                nextBurnUT = later.UT;
                        }
                        ScanChain(node.nextPatch, body, node.UT, nextBurnUT,
                                  ref bestUT, ref encounterOrbit);
                    }
                }
            }

            // The current orbit chain remains useful for unloaded vessels.
            // A burn cuts it off only when planned predictions are included.
            ScanChain(vessel.orbit, body, now,
                      includeManeuvers ? firstManeuverUT : double.PositiveInfinity,
                      ref bestUT, ref encounterOrbit);
            if (encounterOrbit == null)
                return false;
            encounterUT = bestUT;
            return true;
        }

        // Search all predicted patches. After a Mun flyby, a Kerbin escape
        // belongs to a later Kerbin patch, not the vessel's current patch.
        internal static bool TryGetFutureEscape(Vessel vessel, CelestialBody body,
                                                double afterUT, bool includeManeuvers,
                                                out Orbit escapeOrbit, out double escapeUT)
        {
            escapeOrbit = null;
            escapeUT = double.NaN;
            if (vessel == null || body == null)
                return false;
            double now = Planetarium.GetUniversalTime();
            double earliestUT = IsFinite(afterUT) ? Math.Max(now, afterUT) : now;
            double bestUT = double.PositiveInfinity;
            double firstManeuverUT = double.PositiveInfinity;
            PatchedConicSolver solver = vessel.patchedConicSolver;
            if (solver != null)
            {
                IList<ManeuverNode> nodes = solver.maneuverNodes;
                if (nodes != null)
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        ManeuverNode node = nodes[i];
                        if (node != null && IsFinite(node.UT) && node.UT >= now &&
                            node.UT < firstManeuverUT)
                            firstManeuverUT = node.UT;
                    }
                if (double.IsPositiveInfinity(firstManeuverUT))
                    ScanEscapeList(solver.flightPlan, body, earliestUT,
                                   double.PositiveInfinity, ref bestUT,
                                   ref escapeOrbit);
                ScanEscapeList(solver.patches, body, earliestUT,
                               includeManeuvers ? firstManeuverUT : double.PositiveInfinity,
                               ref bestUT, ref escapeOrbit);
                if (includeManeuvers && nodes != null)
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        ManeuverNode node = nodes[i];
                        if (node == null || !IsFinite(node.UT) || node.UT < now)
                            continue;
                        double nextBurnUT = double.PositiveInfinity;
                        for (int j = 0; j < nodes.Count; j++)
                        {
                            ManeuverNode later = nodes[j];
                            if (later != null && IsFinite(later.UT) &&
                                later.UT > node.UT && later.UT < nextBurnUT)
                                nextBurnUT = later.UT;
                        }
                        ScanEscapeChain(node.nextPatch, body,
                                        Math.Max(earliestUT, node.UT), nextBurnUT,
                                        ref bestUT, ref escapeOrbit);
                    }
            }
            ScanEscapeChain(vessel.orbit, body, earliestUT,
                            includeManeuvers ? firstManeuverUT : double.PositiveInfinity,
                            ref bestUT, ref escapeOrbit);
            if (escapeOrbit == null)
                return false;
            escapeUT = bestUT;
            return true;
        }

        private static void ScanEscapeList(IList<Orbit> patches, CelestialBody body,
                                           double earliestUT, double latestUT,
                                           ref double bestUT, ref Orbit bestOrbit)
        {
            if (patches == null)
                return;
            for (int i = 0; i < patches.Count; i++)
                ConsiderEscape(patches[i], body, earliestUT, latestUT,
                               ref bestUT, ref bestOrbit);
        }

        private static void ScanEscapeChain(Orbit patch, CelestialBody body,
                                            double earliestUT, double latestUT,
                                            ref double bestUT, ref Orbit bestOrbit)
        {
            for (int i = 0; patch != null && i < 64; i++)
            {
                ConsiderEscape(patch, body, earliestUT, latestUT,
                               ref bestUT, ref bestOrbit);
                Orbit next = patch.nextPatch;
                if (next == patch)
                    break;
                patch = next;
            }
        }

        private static void ConsiderEscape(Orbit patch, CelestialBody body,
                                           double earliestUT, double latestUT,
                                           ref double bestUT, ref Orbit bestOrbit)
        {
            if (patch == null || patch.referenceBody != body ||
                patch.patchEndTransition != Orbit.PatchTransitionType.ESCAPE ||
                !IsFinite(patch.EndUT) || patch.EndUT < earliestUT ||
                patch.EndUT > latestUT || patch.EndUT >= bestUT)
                return;
            bestUT = patch.EndUT;
            bestOrbit = patch;
        }

        private static void ScanList(IList<Orbit> patches, CelestialBody body,
                                     double earliestUT, double latestUT,
                                     ref double bestUT, ref Orbit bestOrbit)
        {
            if (patches == null)
                return;
            for (int i = 0; i < patches.Count; i++)
            {
                Orbit patch = patches[i];
                Orbit next = patch == null ? null : patch.nextPatch;
                if (next == null && i + 1 < patches.Count)
                    next = patches[i + 1];
                Consider(patch, next, body, earliestUT, latestUT,
                         ref bestUT, ref bestOrbit);
            }
        }

        private static void ScanChain(Orbit patch, CelestialBody body,
                                      double earliestUT, double latestUT,
                                      ref double bestUT, ref Orbit bestOrbit)
        {
            for (int i = 0; patch != null && i < 64; i++)
            {
                Orbit next = patch.nextPatch;
                Consider(patch, next, body, earliestUT, latestUT,
                         ref bestUT, ref bestOrbit);
                if (next == patch)
                    break;
                patch = next;
            }
        }

        private static void Consider(Orbit patch, Orbit next, CelestialBody body,
                                     double earliestUT, double latestUT,
                                     ref double bestUT, ref Orbit bestOrbit)
        {
            if (patch == null || next == null ||
                patch.patchEndTransition != Orbit.PatchTransitionType.ENCOUNTER ||
                next.referenceBody != body || !IsFinite(patch.EndUT) ||
                patch.EndUT < earliestUT || patch.EndUT > latestUT ||
                patch.EndUT >= bestUT)
                return;
            bestUT = patch.EndUT;
            bestOrbit = next;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
