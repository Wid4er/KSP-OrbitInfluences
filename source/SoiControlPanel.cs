using System.Globalization;
using KSP.UI.Screens;
using UnityEngine;

namespace OrbitInfluences
{
    // Compact stock launcher panel for settings that take effect immediately.
    public sealed class SoiControlPanel : MonoBehaviour
    {
        private const int WindowId = 47820101;
        private static readonly string[] BoundaryOptions =
            { "Orbit Ring", "Orbit Grid" };
        private static readonly Color[] Palette =
        {
            new Color(1f, 1f, 1f), new Color(0.65f, 0.83f, 1f),
            new Color(0.30f, 0.70f, 1f), new Color(2f / 255f, 253f / 255f, 252f / 255f),
            new Color(0.35f, 0.90f, 0.45f), new Color(0.85f, 0.90f, 0.25f),
            new Color(1f, 0.75f, 0.25f), new Color(1f, 0.40f, 0.25f),
            new Color(1f, 0.25f, 0.40f), new Color(0.95f, 0.35f, 0.80f),
            new Color(0.70f, 0.45f, 1f), new Color(0.42f, 0.48f, 1f)
        };

        private ApplicationLauncherButton button;
        private Texture2D icon;
        private bool windowOpen;
        private int openPalette;
        private string ringHex;
        private string waveCenterHex;
        private string waveHex;
        private Rect windowRect = new Rect(80f, 80f, 340f, 320f);

        private void Update()
        {
            SoiSettings.SaveIfDue();
            if (!Available())
            {
                RemoveButton();
                return;
            }
            SoiSettings.EnsureLoaded();
            if (button == null && ApplicationLauncher.Instance != null)
                CreateButton();
        }

        private static bool Available()
        {
            return HighLogic.LoadedScene == GameScenes.TRACKSTATION ||
                   HighLogic.LoadedScene == GameScenes.FLIGHT && MapView.MapIsEnabled;
        }

        private void CreateButton()
        {
            icon = CreateIcon();
            button = ApplicationLauncher.Instance.AddModApplication(
                Open, Close, null, null, null, null,
                ApplicationLauncher.AppScenes.MAPVIEW |
                ApplicationLauncher.AppScenes.TRACKSTATION, icon);
            if (button == null)
            {
                Destroy(icon);
                icon = null;
            }
        }

        private void RemoveButton()
        {
            if (windowOpen)
                SoiSettings.SaveNow();
            windowOpen = false;
            if (button != null)
            {
                if (ApplicationLauncher.Instance != null)
                    ApplicationLauncher.Instance.RemoveModApplication(button);
                button = null;
            }
            if (icon != null)
            {
                Destroy(icon);
                icon = null;
            }
        }

        private void Open()
        {
            windowOpen = true;
            ringHex = ToHex(SoiSettings.RingColor);
            waveCenterHex = ToHex(SoiSettings.WaveCenterColor);
            waveHex = ToHex(SoiSettings.WaveColor);
            windowRect.x = Mathf.Max(0f, (Screen.width - windowRect.width) * 0.5f);
            windowRect.y = Mathf.Max(0f, (Screen.height - windowRect.height) * 0.5f);
        }

        private void Close()
        {
            windowOpen = false;
            openPalette = 0;
            SoiSettings.SaveNow();
        }

        private void OnGUI()
        {
            if (!windowOpen || !Available())
                return;
            GUISkin previous = GUI.skin;
            if (HighLogic.Skin != null)
                GUI.skin = HighLogic.Skin;
            try
            {
                windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow,
                                              "OrbitInfluences", GUILayout.Width(340f));
            }
            finally
            {
                GUI.skin = previous;
            }
        }

        private void DrawWindow(int id)
        {
            bool changed = false;
            GUILayout.Label("SOI display");
            changed |= Toggle(ref SoiSettings.RingEnabled, "Show SOI boundary");
            int style = GUILayout.Toolbar((int)SoiSettings.BoundaryStyle,
                                         BoundaryOptions);
            if (style != (int)SoiSettings.BoundaryStyle)
            {
                SoiSettings.BoundaryStyle = (SoiBoundaryStyle)style;
                changed = true;
            }
            changed |= Toggle(ref SoiSettings.ShowCurrentBody, "Current vessel trajectory");
            changed |= Toggle(ref SoiSettings.ShowEncounterBody, "Future encounter trajectory");
            GUILayout.Space(5f);
            GUILayout.Label("Transition waves");
            changed |= Toggle(ref SoiSettings.PreviewRippleEnabled, "Show entry and exit waves");
            changed |= Toggle(ref SoiSettings.ShowManeuverWaves, "Include maneuver plans");
            changed |= Toggle(ref SoiSettings.RippleMatchesOrbitColor,
                              "Match orbit edge color");
            GUILayout.Space(5f);
            changed |= DrawColorRow("Boundary color", ref SoiSettings.RingColor,
                                    ref ringHex, 1);
            changed |= DrawPercentRow("Boundary opacity", ref SoiSettings.RingColor.a);
            changed |= DrawColorRow("Wave center", ref SoiSettings.WaveCenterColor,
                                    ref waveCenterHex, 2);
            if (!SoiSettings.RippleMatchesOrbitColor)
            {
                changed |= DrawColorRow("Wave edge", ref SoiSettings.WaveColor,
                                        ref waveHex, 3);
            }
            changed |= DrawPercentRow("Wave opacity", ref SoiSettings.RippleAlpha);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Line width  " +
                (SoiSettings.RingWidthFraction * 100f).ToString("F2") + "%",
                GUILayout.Width(220f));
            if (GUILayout.Button("-", GUILayout.Width(32f)))
            {
                SoiSettings.RingWidthFraction = Mathf.Max(0.0005f,
                    SoiSettings.RingWidthFraction - 0.001f);
                changed = true;
            }
            if (GUILayout.Button("+", GUILayout.Width(32f)))
            {
                SoiSettings.RingWidthFraction = Mathf.Min(0.05f,
                    SoiSettings.RingWidthFraction + 0.001f);
                changed = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            if (GUILayout.Button("Close"))
            {
                if (button != null)
                    button.SetFalse();
                Close();
            }
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
            if (changed)
                SoiSettings.Changed();
        }

        private static bool Toggle(ref bool value, string label)
        {
            bool next = GUILayout.Toggle(value, label);
            if (next == value)
                return false;
            value = next;
            return true;
        }

        private static bool DrawPercentRow(string label, ref float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(135f));
            float next = GUILayout.HorizontalSlider(value, 0f, 1f,
                                                     GUILayout.Width(110f));
            GUILayout.Label(Mathf.RoundToInt(next * 100f) + "%",
                            GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            if (next == value)
                return false;
            value = next;
            return true;
        }

        private bool DrawColorRow(string label, ref Color color,
                                  ref string hex, int paletteId)
        {
            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(135f));
            Color previous = GUI.backgroundColor;
            Color swatch = color;
            swatch.a = 1f;
            GUI.backgroundColor = swatch;
            if (GUILayout.Button(" ", GUILayout.Width(34f), GUILayout.Height(20f)))
            {
                openPalette = openPalette == paletteId ? 0 : paletteId;
            }
            GUI.backgroundColor = previous;
            GUILayout.Label("#", GUILayout.Width(12f));
            string input = GUILayout.TextField(hex ?? ToHex(color),
                                                6, GUILayout.Width(68f));
            GUILayout.EndHorizontal();
            if (input != hex)
            {
                hex = input.ToUpperInvariant();
                Color parsed;
                if (TryHex(hex, out parsed))
                {
                    color.r = parsed.r;
                    color.g = parsed.g;
                    color.b = parsed.b;
                    changed = true;
                }
            }
            if (openPalette != paletteId)
                return changed;
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(135f);
                for (int col = 0; col < 6; col++)
                {
                    Color choice = Palette[row * 6 + col];
                    GUI.backgroundColor = choice;
                    if (GUILayout.Button(" ", GUILayout.Width(24f),
                                         GUILayout.Height(20f)))
                    {
                        color.r = choice.r;
                        color.g = choice.g;
                        color.b = choice.b;
                        hex = ToHex(color);
                        changed = true;
                        openPalette = 0;
                    }
                }
                GUI.backgroundColor = previous;
                GUILayout.EndHorizontal();
            }
            return changed;
        }

        private static string ToHex(Color color)
        {
            return Mathf.RoundToInt(color.r * 255f).ToString("X2") +
                   Mathf.RoundToInt(color.g * 255f).ToString("X2") +
                   Mathf.RoundToInt(color.b * 255f).ToString("X2");
        }

        private static bool TryHex(string text, out Color color)
        {
            color = Color.white;
            if (text == null || text.Length != 6)
                return false;
            byte red, green, blue;
            if (!byte.TryParse(text.Substring(0, 2), NumberStyles.HexNumber,
                               CultureInfo.InvariantCulture, out red) ||
                !byte.TryParse(text.Substring(2, 2), NumberStyles.HexNumber,
                               CultureInfo.InvariantCulture, out green) ||
                !byte.TryParse(text.Substring(4, 2), NumberStyles.HexNumber,
                               CultureInfo.InvariantCulture, out blue))
                return false;
            color = new Color32(red, green, blue, 255);
            return true;
        }

        private static Texture2D CreateIcon()
        {
            const int size = 38;
            Texture2D result = new Texture2D(size, size, TextureFormat.ARGB32, false);
            result.name = "OrbitInfluences launcher icon";
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - 18.5f;
                    float dy = y - 18.5f;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    result.SetPixel(x, y, radius >= 12f && radius <= 15f
                        ? new Color(0.65f, 0.83f, 1f, 1f) : Color.clear);
                }
            result.Apply();
            return result;
        }

        private void OnDestroy()
        {
            SoiSettings.SaveNow();
            RemoveButton();
        }
    }
}
