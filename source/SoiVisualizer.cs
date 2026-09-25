using KSP.UI.Screens;
using UnityEngine;

namespace OrbitInfluences
{
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public sealed class SoiVisualizer : MonoBehaviour
    {
        private CelestialBody currentBody;
        private CelestialBody markerBody;
        private SoiBoundaryRenderer ring;
        private SoiGridRenderer grid;
        private SoiBoundaryStyle currentStyle;
        private SoiTransitionMarker entryMarker;
        private SoiTransitionMarker exitMarker;
        private SoiTransitionMarker currentBodyExitMarker;
        private float nextRefresh;
        private float nextCreationAttempt;
        private float nextMarkerCreationAttempt;
        private Vessel lastSelectedVessel;
        private MapObject lastFocusTarget;
        private bool lastResolved;
        private Vessel lastTrackingVessel;

        private void Awake()
        {
            gameObject.AddComponent<SoiControlPanel>();
        }

        private void Update()
        {
            bool tracking = HighLogic.LoadedScene == GameScenes.TRACKSTATION;
            bool flightMap = HighLogic.LoadedScene == GameScenes.FLIGHT && MapView.MapIsEnabled;
            if (!tracking && !flightMap)
            {
                RemoveBoundary();
                markerBody = null;
                lastSelectedVessel = null;
                lastFocusTarget = null;
                lastResolved = false;
                lastTrackingVessel = null;
                return;
            }

            SoiSettings.EnsureLoaded();
            SoiSettings.SaveIfDue();
            if (!SoiSettings.RingEnabled)
                RemoveBoundaryVisual();

            if (ring != null)
                ring.RefreshAppearance();
            if (grid != null)
                grid.RefreshAppearance();

            if (Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + 0.25f;

            MapObject focus = PlanetariumCamera.fetch == null ? null : PlanetariumCamera.fetch.target;
            Vessel vessel;
            if (tracking)
            {
                Vessel selected = SpaceTracking.Instance == null
                    ? null : SpaceTracking.Instance.SelectedVessel;
                if (selected != null)
                    lastTrackingVessel = selected;
                // Stock Tracking Station clears SelectedVessel on body focus.
                vessel = selected ?? (focus != null ? focus.vessel : null) ??
                    lastTrackingVessel;
            }
            else
            {
                lastTrackingVessel = null;
                vessel = focus != null && focus.vessel != null
                    ? focus.vessel : FlightGlobals.ActiveVessel;
            }
            CelestialBody body;
            Orbit orbit;
            double encounterUT;
            bool resolved = TryResolveFocus(vessel, focus, out body, out orbit,
                                            out encounterUT);
            if (vessel != lastSelectedVessel || focus != lastFocusTarget ||
                resolved != lastResolved)
            {
                Debug.Log("[OrbitInfluences] Selection: scene=" + HighLogic.LoadedScene +
                          ", vessel=" + (vessel == null ? "none" : vessel.vesselName) +
                          ", focus=" + (focus == null ? "none" : focus.name) +
                          ", boundary=" + (resolved ? body.bodyName : "none"));
                lastSelectedVessel = vessel;
                lastFocusTarget = focus;
                lastResolved = resolved;
            }
            if (!resolved)
            {
                RemoveBoundary();
                markerBody = null;
                return;
            }
            if (body != markerBody)
            {
                RemoveMarkers();
                markerBody = body;
            }

            // Boundary objects and transition markers have separate lifetimes.
            // Turning off the boundary must not hide an active trajectory wave.
            if (SoiSettings.RingEnabled)
            {
                SoiBoundaryStyle style = SoiSettings.BoundaryStyle;
                if (body != currentBody || currentStyle != style ||
                    ring == null && grid == null)
                {
                    RemoveBoundaryVisual();
                    if (Time.unscaledTime >= nextCreationAttempt)
                    {
                        GameObject holder = new GameObject("OrbitInfluences SOI: " + body.bodyName);
                        holder.layer = tracking ? 10 : 24;
                        bool created;
                        if (style == SoiBoundaryStyle.Grid)
                        {
                            grid = holder.AddComponent<SoiGridRenderer>();
                            created = grid.Initialize(body);
                        }
                        else
                        {
                            ring = holder.AddComponent<SoiBoundaryRenderer>();
                            created = ring.Initialize(body);
                        }
                        if (!created)
                        {
                            Debug.LogWarning("[OrbitInfluences] SOI boundary unavailable for " +
                                             body.bodyName);
                            RemoveBoundaryVisual();
                            nextCreationAttempt = Time.unscaledTime + 1f;
                        }
                        else
                        {
                            currentBody = body;
                            currentStyle = style;
                            Debug.Log("[OrbitInfluences] SOI " + style + " created for " +
                                      body.bodyName);
                        }
                    }
                }

                if (style == SoiBoundaryStyle.Grid && grid != null)
                    grid.SetVisible(grid.RefreshRadius());
                else if (style == SoiBoundaryStyle.Ring && ring != null)
                    ring.SetVisible(ring.RefreshRadius() && ring.SetOrbitPlane(orbit));
            }

            bool trajectoryEnabled = vessel != null &&
                (body == vessel.mainBody ? SoiSettings.ShowCurrentBody
                                         : SoiSettings.ShowEncounterBody);
            if (!SoiSettings.PreviewRippleEnabled || !trajectoryEnabled)
            {
                RemoveMarkers();
                return;
            }

            bool showEntry = !double.IsNaN(encounterUT);
            if (showEntry)
                SetMarker(ref entryMarker, body, orbit, encounterUT, false,
                          vessel, tracking);
            else
                RemoveMarker(ref entryMarker);

            Orbit escapePatch;
            double escapeUT;
            if (SoiEncounterLookup.TryGetFutureEscape(vessel, body, encounterUT,
                    SoiSettings.ShowManeuverWaves, out escapePatch, out escapeUT) ||
                SoiRenderedEscapeLookup.TryGetEscape(vessel, body, encounterUT,
                    SoiSettings.ShowManeuverWaves, out escapePatch, out escapeUT))
                SetMarker(ref exitMarker, body, escapePatch, escapeUT, true,
                          vessel, tracking);
            else
                RemoveMarker(ref exitMarker);

            // Keep the selected vessel's later home-body escape visible while
            // an encounter body such as Mun is focused.
            if (body != vessel.mainBody && SoiSettings.ShowCurrentBody &&
                !double.IsNaN(encounterUT))
            {
                Orbit homeEscape;
                double homeEscapeUT;
                if (SoiEncounterLookup.TryGetFutureEscape(vessel,
                        vessel.mainBody, double.NaN,
                        SoiSettings.ShowManeuverWaves, out homeEscape,
                        out homeEscapeUT) ||
                    SoiRenderedEscapeLookup.TryGetEscape(vessel,
                        vessel.mainBody, double.NaN,
                        SoiSettings.ShowManeuverWaves, out homeEscape,
                        out homeEscapeUT))
                    SetMarker(ref currentBodyExitMarker, vessel.mainBody,
                              homeEscape, homeEscapeUT, true, vessel, tracking);
                else
                    RemoveMarker(ref currentBodyExitMarker);
            }
            else
                RemoveMarker(ref currentBodyExitMarker);
        }

        private void SetMarker(ref SoiTransitionMarker marker, CelestialBody body,
                               Orbit patch, double ut, bool escape, Vessel vessel,
                               bool tracking)
        {
            if (marker == null && Time.unscaledTime >= nextMarkerCreationAttempt)
            {
                GameObject holder = new GameObject("OrbitInfluences " +
                    (escape ? "escape" : "encounter") + " marker: " + body.bodyName);
                holder.layer = tracking ? 10 : 24;
                marker = holder.AddComponent<SoiTransitionMarker>();
                if (!marker.Initialize(body))
                {
                    Destroy(holder);
                    marker = null;
                    nextMarkerCreationAttempt = Time.unscaledTime + 1f;
                    Debug.LogWarning("[OrbitInfluences] SOI ripple unavailable for " +
                                     body.bodyName);
                    return;
                }
            }
            if (marker != null && !marker.SetTransition(patch, ut, escape,
                                                        GetPatchColor(vessel, patch)))
            {
                RemoveMarker(ref marker);
                nextMarkerCreationAttempt = Time.unscaledTime + 1f;
                Debug.LogWarning("[OrbitInfluences] SOI ripple could not be positioned for " +
                                 body.bodyName);
            }
        }

        private static Color GetPatchColor(Vessel vessel, Orbit patch)
        {
            if (SoiSettings.RippleMatchesOrbitColor && vessel != null &&
                vessel.patchedConicRenderer != null)
            {
                PatchRendering rendering =
                    vessel.patchedConicRenderer.FindRenderingForPatch(patch);
                if (rendering != null)
                    return rendering.patchColor;
            }
            return SoiSettings.WaveColor;
        }

        private static bool TryResolveFocus(Vessel vessel, MapObject focus,
                                            out CelestialBody body, out Orbit orbit,
                                            out double encounterUT)
        {
            body = null;
            orbit = null;
            encounterUT = double.NaN;
            if (focus == null)
                return false;

            if (focus.vessel != null)
            {
                // A focused vessel still uses its own trajectory and the
                // existing vessel-focus visibility setting.
                if (vessel != focus.vessel || !SoiSettings.ShowCurrentBody ||
                    vessel.mainBody == null)
                    return false;
                body = vessel.mainBody;
                orbit = vessel.orbit;
                return true;
            }

            CelestialBody focusedBody = focus.celestialBody;
            if (focusedBody == null)
                return false;
            body = focusedBody;

            // A focused body's boundary is useful even without a selected
            // vessel or predicted encounter. Trajectory data only chooses
            // the ring plane and enables transition waves when available.
            if (vessel != null && vessel.mainBody == focusedBody &&
                SoiSettings.ShowCurrentBody && vessel.orbit != null)
            {
                orbit = vessel.orbit;
                return true;
            }
            if (vessel != null && SoiSettings.ShowEncounterBody &&
                SoiEncounterLookup.TryGetEncounter(vessel, focusedBody,
                                                   SoiSettings.ShowManeuverWaves,
                                                   out orbit, out encounterUT))
                return true;

            orbit = focusedBody.orbit;
            return true;
        }

        private void OnDestroy()
        {
            RemoveBoundary();
        }

        private void RemoveBoundary()
        {
            RemoveMarkers();
            RemoveBoundaryVisual();
        }

        private void RemoveBoundaryVisual()
        {
            if (ring != null)
            {
                ring.SetVisible(false);
                Destroy(ring.gameObject);
            }
            if (grid != null)
            {
                grid.SetVisible(false);
                Destroy(grid.gameObject);
            }
            ring = null;
            grid = null;
            currentBody = null;
        }

        private void RemoveMarkers()
        {
            RemoveMarker(ref entryMarker);
            RemoveMarker(ref exitMarker);
            RemoveMarker(ref currentBodyExitMarker);
        }

        private void RemoveMarker(ref SoiTransitionMarker marker)
        {
            if (marker != null)
                Destroy(marker.gameObject);
            marker = null;
        }
    }
}
