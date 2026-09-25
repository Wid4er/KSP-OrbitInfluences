using System;
using System.Globalization;
using KSP.IO;
using UnityEngine;

namespace OrbitInfluences
{
    internal enum SoiBoundaryStyle { Ring, Grid }

    internal static class SoiSettings
    {
        internal const string ShaderName = "Legacy Shaders/Particles/Alpha Blended";
        internal const int RingSegments = 128;

        internal static bool RingEnabled = true;
        internal static SoiBoundaryStyle BoundaryStyle = SoiBoundaryStyle.Ring;
        internal static bool ShowCurrentBody = true;
        internal static bool ShowEncounterBody = true;
        internal static bool PreviewRippleEnabled = false;
        internal static bool RippleMatchesOrbitColor = true;
        internal static bool ShowManeuverWaves = true;
        internal static Color WaveColor = new Color(2f / 255f, 253f / 255f, 252f / 255f, 1f);
        internal static Color WaveCenterColor = new Color(2f / 255f, 253f / 255f, 252f / 255f, 1f);
        internal static int RippleCount = 3;
        internal static float RippleSpeed = 0.35f;
        internal static float RippleWidth = 0.05f;
        internal static float RippleAlpha = 0.36f;
        internal static float RippleMinScale = 0f;
        internal static float RippleMaxScale = 1f;
        internal static float RingWidthFraction = 0.004f;
        internal static Color RingColor = new Color(0.65f, 0.83f, 1f, 0.16f);

        private static bool loaded;
        private static bool dirty;
        private static float saveAt;
        private static bool saveErrorLogged;
        private static PluginConfiguration preferences;

        internal static void EnsureLoaded()
        {
            if (loaded || GameDatabase.Instance == null)
                return;
            loaded = true;

            // GameData config supplies defaults; the in-game panel writes
            // persistent overrides under PluginData without editing this file.
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("ORBIT_INFLUENCES");
            if (nodes != null && nodes.Length > 0)
                LoadDefaults(nodes[0]);

            try
            {
                preferences = PluginConfiguration.CreateForType<SoiVisualizer>();
                preferences.load();
                RingEnabled = preferences.GetValue("RingEnabled", RingEnabled);
                BoundaryStyle = ParseBoundaryStyle(
                    preferences.GetValue("BoundaryStyle", BoundaryStyle.ToString()),
                    BoundaryStyle);
                ShowCurrentBody = preferences.GetValue("ShowCurrentBody", ShowCurrentBody);
                ShowEncounterBody = preferences.GetValue("ShowEncounterBody", ShowEncounterBody);
                PreviewRippleEnabled = preferences.GetValue("PreviewRippleEnabled", PreviewRippleEnabled);
                RippleMatchesOrbitColor = preferences.GetValue("RippleMatchesOrbitColor", RippleMatchesOrbitColor);
                ShowManeuverWaves = preferences.GetValue("ShowManeuverWaves", ShowManeuverWaves);
                RippleAlpha = UnitValue(preferences.GetValue("RippleAlpha", RippleAlpha), RippleAlpha);
                WaveColor = new Color(
                    UnitValue(preferences.GetValue("WaveRed", WaveColor.r), WaveColor.r),
                    UnitValue(preferences.GetValue("WaveGreen", WaveColor.g), WaveColor.g),
                    UnitValue(preferences.GetValue("WaveBlue", WaveColor.b), WaveColor.b), 1f);
                WaveCenterColor = new Color(
                    UnitValue(preferences.GetValue("WaveCenterRed", WaveCenterColor.r), WaveCenterColor.r),
                    UnitValue(preferences.GetValue("WaveCenterGreen", WaveCenterColor.g), WaveCenterColor.g),
                    UnitValue(preferences.GetValue("WaveCenterBlue", WaveCenterColor.b), WaveCenterColor.b), 1f);
                RingWidthFraction = ValidWidth(preferences.GetValue("RingWidthFraction", RingWidthFraction));
                RingColor = new Color(
                    UnitValue(preferences.GetValue("RingRed", RingColor.r), RingColor.r),
                    UnitValue(preferences.GetValue("RingGreen", RingColor.g), RingColor.g),
                    UnitValue(preferences.GetValue("RingBlue", RingColor.b), RingColor.b),
                    UnitValue(preferences.GetValue("RingAlpha", RingColor.a), RingColor.a));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[OrbitInfluences] Preferences could not be loaded: " + exception.Message);
            }

            Debug.Log("[OrbitInfluences] Settings: ringEnabled=" + RingEnabled +
                      ", current=" + ShowCurrentBody +
                      ", encounters=" + ShowEncounterBody +
                      ", preview=" + PreviewRippleEnabled +
                      ", ringWidthFraction=" + RingWidthFraction +
                      ", ringColor=" + RingColor);
        }

        internal static void Changed()
        {
            dirty = true;
            saveAt = Time.unscaledTime + 0.5f;
        }

        internal static void SaveIfDue()
        {
            if (dirty && Time.unscaledTime >= saveAt)
                SaveNow();
        }

        internal static void SaveNow()
        {
            if (!dirty)
                return;
            try
            {
                if (preferences == null)
                    preferences = PluginConfiguration.CreateForType<SoiVisualizer>();
                preferences.SetValue("RingEnabled", RingEnabled);
                preferences.SetValue("BoundaryStyle", BoundaryStyle.ToString());
                preferences.SetValue("ShowCurrentBody", ShowCurrentBody);
                preferences.SetValue("ShowEncounterBody", ShowEncounterBody);
                preferences.SetValue("PreviewRippleEnabled", PreviewRippleEnabled);
                preferences.SetValue("RippleMatchesOrbitColor", RippleMatchesOrbitColor);
                preferences.SetValue("ShowManeuverWaves", ShowManeuverWaves);
                preferences.SetValue("RippleAlpha", RippleAlpha);
                preferences.SetValue("WaveRed", WaveColor.r);
                preferences.SetValue("WaveGreen", WaveColor.g);
                preferences.SetValue("WaveBlue", WaveColor.b);
                preferences.SetValue("WaveCenterRed", WaveCenterColor.r);
                preferences.SetValue("WaveCenterGreen", WaveCenterColor.g);
                preferences.SetValue("WaveCenterBlue", WaveCenterColor.b);
                preferences.SetValue("RingWidthFraction", RingWidthFraction);
                preferences.SetValue("RingRed", RingColor.r);
                preferences.SetValue("RingGreen", RingColor.g);
                preferences.SetValue("RingBlue", RingColor.b);
                preferences.SetValue("RingAlpha", RingColor.a);
                preferences.save();
                dirty = false;
                saveErrorLogged = false;
            }
            catch (Exception exception)
            {
                saveAt = Time.unscaledTime + 10f;
                if (!saveErrorLogged)
                {
                    Debug.LogWarning("[OrbitInfluences] Preferences could not be saved: " + exception.Message);
                    saveErrorLogged = true;
                }
            }
        }

        private static void LoadDefaults(ConfigNode node)
        {
            bool flag;
            if (bool.TryParse(node.GetValue("ringEnabled"), out flag))
                RingEnabled = flag;
            BoundaryStyle = ParseBoundaryStyle(node.GetValue("boundaryStyle"),
                                               BoundaryStyle);
            if (bool.TryParse(node.GetValue("showCurrentBody"), out flag))
                ShowCurrentBody = flag;
            if (bool.TryParse(node.GetValue("showEncounterBody"), out flag))
                ShowEncounterBody = flag;
            if (bool.TryParse(node.GetValue("previewRippleEnabled"), out flag))
                PreviewRippleEnabled = flag;
            if (bool.TryParse(node.GetValue("wavesEnabled"), out flag))
                PreviewRippleEnabled = flag;
            if (bool.TryParse(node.GetValue("showManeuverWaves"), out flag))
                ShowManeuverWaves = flag;
            if (bool.TryParse(node.GetValue("rippleMatchesOrbitColor"), out flag))
                RippleMatchesOrbitColor = flag;
            int count;
            if (int.TryParse(node.GetValue("rippleCount"), out count) && count >= 1 && count <= 8)
                RippleCount = count;
            float ripple;
            if (TryFloat(node.GetValue("rippleSpeed"), out ripple) && ripple > 0f && ripple <= 5f)
                RippleSpeed = ripple;
            if (TryFloat(node.GetValue("rippleWidth"), out ripple) && ripple > 0f && ripple <= 0.3f)
                RippleWidth = ripple;
            if (TryFloat(node.GetValue("rippleAlpha"), out ripple))
                RippleAlpha = UnitValue(ripple, RippleAlpha);
            if (TryFloat(node.GetValue("rippleMinScale"), out ripple) && ripple >= 0f && ripple < 1f)
                RippleMinScale = ripple;
            if (TryFloat(node.GetValue("rippleMaxScale"), out ripple) && ripple > RippleMinScale && ripple <= 3f)
                RippleMaxScale = ripple;

            float width;
            if (TryFloat(node.GetValue("ringWidthFraction"), out width))
                RingWidthFraction = ValidWidth(width);
            float alpha;
            if (TryFloat(node.GetValue("ringAlpha"), out alpha))
                RingColor.a = UnitValue(alpha, RingColor.a);

            Color parsed;
            if (TryColor(node.GetValue("ringColor"), out parsed))
            {
                parsed.a = RingColor.a;
                RingColor = parsed;
            }
            if (TryColor(node.GetValue("waveColor"), out parsed))
                WaveColor = parsed;
            if (TryColor(node.GetValue("waveCenterColor"), out parsed))
                WaveCenterColor = parsed;
        }

        private static SoiBoundaryStyle ParseBoundaryStyle(
            string value, SoiBoundaryStyle fallback)
        {
            if (string.Equals(value, "grid", StringComparison.OrdinalIgnoreCase))
                return SoiBoundaryStyle.Grid;
            if (string.Equals(value, "ring", StringComparison.OrdinalIgnoreCase))
                return SoiBoundaryStyle.Ring;
            return fallback;
        }

        private static bool TryColor(string value, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(value))
                return false;
            value = value.Trim();
            if (value.Length == 7 && value[0] == '#')
            {
                byte redByte, greenByte, blueByte;
                if (!byte.TryParse(value.Substring(1, 2), NumberStyles.HexNumber,
                                   CultureInfo.InvariantCulture, out redByte) ||
                    !byte.TryParse(value.Substring(3, 2), NumberStyles.HexNumber,
                                   CultureInfo.InvariantCulture, out greenByte) ||
                    !byte.TryParse(value.Substring(5, 2), NumberStyles.HexNumber,
                                   CultureInfo.InvariantCulture, out blueByte))
                    return false;
                color = new Color32(redByte, greenByte, blueByte, 255);
                return true;
            }
            string[] parts = value.Split(',');
            float red, green, blue;
            if (parts.Length != 3 || !TryFloat(parts[0], out red) ||
                !TryFloat(parts[1], out green) || !TryFloat(parts[2], out blue) ||
                !InUnitRange(red) || !InUnitRange(green) || !InUnitRange(blue))
                return false;
            color = new Color(red, green, blue, 1f);
            return true;
        }

        private static float ValidWidth(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                   value >= 0.0005f && value <= 0.05f ? value : 0.004f;
        }

        private static float UnitValue(float value, float fallback)
        {
            return InUnitRange(value) ? value : fallback;
        }

        private static bool TryFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float,
                                  CultureInfo.InvariantCulture, out result) &&
                   !float.IsNaN(result) && !float.IsInfinity(result);
        }

        private static bool InUnitRange(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                   value >= 0f && value <= 1f;
        }
    }
}
