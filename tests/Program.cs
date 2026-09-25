using OrbitInfluences;

static class Program
{
    static int passed;

    static void Main()
    {
        var mun = new CelestialBody();
        var minmus = new CelestialBody();

        var natural = Encounter(mun, 150);
        Check("natural encounter without a solver",
            new Vessel { orbit = natural }, mun, true);

        var postBurn = Encounter(mun, 200);
        var planned = new ManeuverNode { UT = 120, nextPatch = postBurn };
        var vessel = new Vessel
        {
            orbit = new Orbit(),
            patchedConicSolver = new PatchedConicSolver()
        };
        vessel.patchedConicSolver.patches.Add(vessel.orbit);
        vessel.patchedConicSolver.maneuverNodes.Add(planned);
        Check("encounter produced only by a maneuver node", vessel, mun, true);
        Orbit naturalOnlyPatch;
        double naturalOnlyUT;
        if (SoiEncounterLookup.TryGetEncounter(vessel, mun, false,
                                               out naturalOnlyPatch, out naturalOnlyUT))
            throw new Exception("Failed: maneuver encounter shown with planned waves off");
        passed++;

        CheckUT("maneuver encounter UT", vessel, mun, 200);
        Check("other body stays hidden", vessel, minmus, false);

        planned.nextPatch = new Orbit();
        Check("editing node removes old encounter", vessel, mun, false);
        planned.nextPatch = Encounter(minmus, 210);
        Check("editing node changes encounter body", vessel, minmus, true);
        vessel.patchedConicSolver.maneuverNodes.Clear();
        Check("removing node removes planned encounter", vessel, minmus, false);

        var obsolete = Encounter(mun, 180);
        var diverted = new Vessel
        {
            orbit = obsolete,
            patchedConicSolver = new PatchedConicSolver()
        };
        diverted.patchedConicSolver.patches.Add(obsolete);
        diverted.patchedConicSolver.maneuverNodes.Add(
            new ManeuverNode { UT = 120, nextPatch = new Orbit() });
        Check("unburned encounter after node is ignored", diverted, mun, false);
        Orbit naturalAfterBurn;
        double naturalAfterBurnUT;
        if (!SoiEncounterLookup.TryGetEncounter(diverted, mun, false,
                out naturalAfterBurn, out naturalAfterBurnUT) ||
            naturalAfterBurn != obsolete.nextPatch || naturalAfterBurnUT != 180)
            throw new Exception("Failed: original encounter disappears when maneuver plans are off");
        passed++;
        obsolete.EndUT = 110;
        Check("encounter before node remains valid", diverted, mun, true);
        CheckUT("pre-burn encounter UT", diverted, mun, 110);

        var planOnly = new Vessel { orbit = new Orbit(), patchedConicSolver = new PatchedConicSolver() };
        planOnly.patchedConicSolver.flightPlan.Add(Encounter(mun, 175));
        Check("encounter in solver flight plan", planOnly, mun, true);

        var firstNode = new ManeuverNode { UT = 120, nextPatch = Encounter(mun, 180) };
        var secondNode = new ManeuverNode { UT = 150, nextPatch = Encounter(minmus, 200) };
        var twoBurns = new Vessel { orbit = new Orbit(), patchedConicSolver = new PatchedConicSolver() };
        twoBurns.patchedConicSolver.maneuverNodes.Add(firstNode);
        twoBurns.patchedConicSolver.maneuverNodes.Add(secondNode);
        Check("encounter after superseded first burn is ignored", twoBurns, mun, false);
        Check("second burn encounter is used", twoBurns, minmus, true);

        var munPass = Encounter(mun, 180);
        var insideMun = munPass.nextPatch;
        insideMun.EndUT = 240;
        insideMun.patchEndTransition = Orbit.PatchTransitionType.ESCAPE;
        insideMun.nextPatch = new Orbit { referenceBody = new CelestialBody() };
        CheckEscape("escape after encounter", insideMun, mun, true, 240);
        CheckEscape("no escape for other body", insideMun, minmus, false, 0);
        insideMun.patchEndTransition = Orbit.PatchTransitionType.INITIAL;
        CheckEscape("captured encounter has no escape", insideMun, mun, false, 0);
        insideMun.patchEndTransition = Orbit.PatchTransitionType.ESCAPE;
        insideMun.EndUT = 90;
        CheckEscape("past escape is hidden", insideMun, mun, false, 0);
        insideMun.EndUT = 240;
        CheckEscape("maneuver prediction includes escape", munPass.nextPatch, mun,
                    true, 240);

        var listedEntry = new Orbit { referenceBody = mun };
        var listedExit = new Orbit
        {
            referenceBody = mun,
            EndUT = 260,
            patchEndTransition = Orbit.PatchTransitionType.ESCAPE
        };
        var listedVessel = new Vessel { patchedConicSolver = new PatchedConicSolver() };
        listedVessel.patchedConicSolver.flightPlan.Add(listedEntry);
        listedVessel.patchedConicSolver.flightPlan.Add(listedExit);
        Orbit foundExit;
        double foundExitUT;
        if (!SoiEncounterLookup.TryGetFutureEscape(listedVessel, mun, double.NaN,
                                                   true, out foundExit, out foundExitUT) ||
            foundExit != listedExit || foundExitUT != 260)
            throw new Exception("Failed: flight-plan exit without nextPatch link");
        passed++;

        var kerbin = new CelestialBody();
        var kerbinBeforeMun = Encounter(mun, 180);
        kerbinBeforeMun.referenceBody = kerbin;
        var munFlyby = kerbinBeforeMun.nextPatch;
        munFlyby.patchEndTransition = Orbit.PatchTransitionType.ESCAPE;
        munFlyby.EndUT = 220;
        var kerbinAfterMun = new Orbit
        {
            referenceBody = kerbin,
            patchEndTransition = Orbit.PatchTransitionType.ESCAPE,
            EndUT = 300
        };
        munFlyby.nextPatch = kerbinAfterMun;
        CheckFutureEscape("Kerbin escape after Mun flyby",
            new Vessel { orbit = kerbinBeforeMun }, kerbin, 300, true);
        CheckFutureEscape("Mun exit on the same plan",
            new Vessel { orbit = kerbinBeforeMun }, mun, 220, true);

        var plannedKerbinEscape = new Orbit
        {
            referenceBody = kerbin,
            patchEndTransition = Orbit.PatchTransitionType.ESCAPE,
            EndUT = 250
        };
        var maneuverEscapeVessel = new Vessel
        {
            orbit = new Orbit { referenceBody = kerbin },
            patchedConicSolver = new PatchedConicSolver()
        };
        maneuverEscapeVessel.patchedConicSolver.maneuverNodes.Add(
            new ManeuverNode { UT = 120, nextPatch = plannedKerbinEscape });
        CheckFutureEscape("maneuver creates Kerbin escape", maneuverEscapeVessel,
                          kerbin, 250, true);
        CheckFutureEscape("maneuver waves disabled", maneuverEscapeVessel,
                          kerbin, 0, false);
        var staleEscape = new Orbit
        {
            referenceBody = kerbin,
            patchEndTransition = Orbit.PatchTransitionType.ESCAPE,
            EndUT = 250
        };
        maneuverEscapeVessel.orbit = staleEscape;
        maneuverEscapeVessel.patchedConicSolver.patches.Add(staleEscape);
        maneuverEscapeVessel.patchedConicSolver.maneuverNodes[0].nextPatch =
            new Orbit { referenceBody = kerbin };
        CheckFutureEscape("unburned escape after maneuver is ignored",
                          maneuverEscapeVessel, kerbin, 0, true);
        CheckFutureEscape("original escape survives with maneuver plans off",
                          maneuverEscapeVessel, kerbin, 250, false);

        var firstBurnEscape = new Vessel
        {
            orbit = new Orbit { referenceBody = kerbin },
            patchedConicSolver = new PatchedConicSolver()
        };
        firstBurnEscape.patchedConicSolver.maneuverNodes.Add(
            new ManeuverNode { UT = 120, nextPatch =
                new Orbit { referenceBody = kerbin,
                            patchEndTransition = Orbit.PatchTransitionType.ESCAPE,
                            EndUT = 200 } });
        firstBurnEscape.patchedConicSolver.maneuverNodes.Add(
            new ManeuverNode { UT = 150, nextPatch = new Orbit { referenceBody = kerbin } });
        CheckFutureEscape("superseded first-burn escape is ignored",
                          firstBurnEscape, kerbin, 0, true);
        firstBurnEscape.patchedConicSolver.flightPlan.Add(
            new Orbit { referenceBody = kerbin,
                        patchEndTransition = Orbit.PatchTransitionType.ESCAPE,
                        EndUT = 275 });
        CheckFutureEscape("unmatched flight plan is ignored while nodes exist",
                          firstBurnEscape, kerbin, 0, true);
        firstBurnEscape.patchedConicSolver.flightPlan.Clear();
        firstBurnEscape.orbit = new Orbit
        {
            referenceBody = kerbin,
            patchEndTransition = Orbit.PatchTransitionType.ESCAPE,
            EndUT = 110
        };
        CheckFutureEscape("pre-burn escape remains without planned waves",
                          firstBurnEscape, kerbin, 110, false);
        Orbit afterPatch;
        double afterUT;
        if (SoiEncounterLookup.TryGetFutureEscape(firstBurnEscape, kerbin, 120,
                                                   false, out afterPatch, out afterUT))
            throw new Exception("Failed: escape before selected entry");
        passed++;

        var oldEntry = Encounter(mun, 190);
        var rerouted = new Vessel
        {
            orbit = oldEntry,
            patchedConicSolver = new PatchedConicSolver()
        };
        rerouted.patchedConicSolver.flightPlan.Add(oldEntry);
        rerouted.patchedConicSolver.patches.Add(oldEntry);
        rerouted.patchedConicSolver.maneuverNodes.Add(
            new ManeuverNode { UT = 120, nextPatch = Encounter(mun, 230) });
        CheckUT("planned entry replaces stale unburned entry",
                rerouted, mun, 230);
        rerouted.patchedConicSolver.maneuverNodes[0].nextPatch =
            new Orbit { referenceBody = kerbin };
        Check("planned path removes stale unburned entry", rerouted, mun, false);

        var oldKerbinExit = new Orbit
        {
            referenceBody = kerbin,
            patchEndTransition = Orbit.PatchTransitionType.ESCAPE,
            EndUT = 190
        };
        var changedEscape = new Vessel
        {
            orbit = oldKerbinExit,
            patchedConicSolver = new PatchedConicSolver()
        };
        changedEscape.patchedConicSolver.flightPlan.Add(oldKerbinExit);
        changedEscape.patchedConicSolver.maneuverNodes.Add(
            new ManeuverNode { UT = 120, nextPatch =
                new Orbit { referenceBody = kerbin,
                            patchEndTransition = Orbit.PatchTransitionType.ESCAPE,
                            EndUT = 240 } });
        CheckFutureEscape("planned exit replaces stale flight plan exit",
                          changedEscape, kerbin, 240, true);

        var renderedVessel = new Vessel
        {
            orbit = new Orbit { referenceBody = kerbin },
            patchedConicRenderer = new PatchedConicRenderer()
        };
        renderedVessel.patchedConicRenderer.patchRenders.Add(
            new PatchRendering { patch = kerbinAfterMun });
        Orbit renderedEscape;
        double renderedUT;
        if (!SoiRenderedEscapeLookup.TryGetEscape(renderedVessel, kerbin,
                double.NaN, true, out renderedEscape, out renderedUT) ||
            renderedEscape != kerbinAfterMun || renderedUT != 300)
            throw new Exception("Failed: rendered post-Mun Kerbin escape fallback");
        passed++;
        renderedVessel.patchedConicSolver = new PatchedConicSolver();
        renderedVessel.patchedConicSolver.maneuverNodes.Add(
            new ManeuverNode { UT = 120, nextPatch = new Orbit() });
        if (SoiRenderedEscapeLookup.TryGetEscape(renderedVessel, kerbin,
                double.NaN, true, out renderedEscape, out renderedUT))
            throw new Exception("Failed: stale rendered base escape after node");
        passed++;
        if (!SoiRenderedEscapeLookup.TryGetEscape(renderedVessel, kerbin,
                double.NaN, false, out renderedEscape, out renderedUT) ||
            renderedEscape != kerbinAfterMun || renderedUT != 300)
            throw new Exception("Failed: original rendered escape disappears when maneuver plans are off");
        passed++;
        renderedVessel.patchedConicRenderer.flightPlanRenders.Add(
            new PatchRendering { patch = kerbinAfterMun });
        if (!SoiRenderedEscapeLookup.TryGetEscape(renderedVessel, kerbin,
                double.NaN, true, out renderedEscape, out renderedUT) ||
            renderedEscape != kerbinAfterMun)
            throw new Exception("Failed: rendered maneuver escape fallback");
        passed++;

        foreach (double radius in new[] { 404.9265, 14026.55 })
            foreach (double arc in new[] { 0d, 0.1d, radius * 0.25d,
                                             radius * 0.675d })
            {
                double tangent, inward;
                SoiSurfaceProjection.GetOffsets(radius, arc,
                                                out tangent, out inward);
                double resultRadius = Math.Sqrt(tangent * tangent +
                                                (radius + inward) *
                                                (radius + inward));
                if (Math.Abs(resultRadius - radius) > radius * 1e-12)
                    throw new Exception("Failed: wave vertex leaves SOI surface");
                passed++;
            }

        Console.WriteLine($"{passed} lookup and geometry scenarios passed");
    }

    static void CheckFutureEscape(string scenario, Vessel vessel,
                                  CelestialBody body, double expectedUT,
                                  bool includeManeuvers)
    {
        Orbit patch;
        double ut;
        bool found = SoiEncounterLookup.TryGetFutureEscape(vessel, body, 100,
            includeManeuvers, out patch, out ut);
        if (found != (expectedUT > 0) || found && ut != expectedUT)
            throw new Exception($"Failed: {scenario}; UT={ut}");
        passed++;
    }

    static void CheckEscape(string scenario, Orbit start, CelestialBody body,
                            bool expected, double expectedUT)
    {
        Orbit actual;
        double actualUT;
        bool found = SoiEncounterLookup.TryGetFutureEscape(
            new Vessel { orbit = start }, body, double.NaN, true,
            out actual, out actualUT);
        if (found != expected || found && (actual != start || actualUT != expectedUT))
            throw new Exception($"Failed: {scenario}; UT={actualUT}");
        passed++;
    }

    static void CheckUT(string scenario, Vessel vessel, CelestialBody body,
                        double expectedUT)
    {
        Orbit orbit;
        double actualUT;
        if (!SoiEncounterLookup.TryGetEncounter(vessel, body, out orbit,
                                                out actualUT) || actualUT != expectedUT)
            throw new Exception($"Failed: {scenario}; UT={actualUT}");
        passed++;
    }

    static Orbit Encounter(CelestialBody body, double ut)
    {
        return new Orbit
        {
            EndUT = ut,
            patchEndTransition = Orbit.PatchTransitionType.ENCOUNTER,
            nextPatch = new Orbit { referenceBody = body }
        };
    }

    static void Check(string scenario, Vessel vessel, CelestialBody body, bool expected)
    {
        Orbit actual;
        bool found = SoiEncounterLookup.TryGetEncounterOrbit(vessel, body, out actual);
        if (found != expected || found && actual.referenceBody != body)
            throw new Exception($"Failed: {scenario}");
        passed++;
    }
}

class CelestialBody { }
class Vessel
{
    public Orbit orbit;
    public PatchedConicSolver patchedConicSolver;
    public PatchedConicRenderer patchedConicRenderer;
}
class PatchedConicSolver
{
    public List<Orbit> flightPlan = new();
    public List<Orbit> patches = new();
    public List<ManeuverNode> maneuverNodes = new();
}
class PatchedConicRenderer
{
    public List<PatchRendering> patchRenders = new();
    public List<PatchRendering> flightPlanRenders = new();
}
class PatchRendering
{
    public Orbit patch;
}
class ManeuverNode
{
    public double UT;
    public Orbit nextPatch;
}
class Orbit
{
    public enum PatchTransitionType { INITIAL, ENCOUNTER, ESCAPE }
    public PatchTransitionType patchEndTransition;
    public Orbit nextPatch;
    public CelestialBody referenceBody;
    public double EndUT;
}
static class Planetarium
{
    public static double GetUniversalTime() => 100;
}
