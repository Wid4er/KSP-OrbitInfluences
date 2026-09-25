using System;
using UnityEngine;

namespace OrbitInfluences
{
    // Persistent, phase-offset rings at one predicted SOI crossing.
    public sealed class SoiTransitionMarker : MonoBehaviour
    {
        private const int Segments = 96;
        private const float PixelRadius = 144f;
        private const float MinimumSoiFraction = 0.0045f;
        private const float MaximumSoiFraction = 0.675f;
        private static readonly Vector2[] Directions = CreateDirections();
        private static readonly Bounds RippleBounds =
            new Bounds(Vector3.zero, new Vector3(8f, 8f, 8f));

        private CelestialBody body;
        private Transform mapTransform;
        private Vector3 localRadial;
        private float worldRadius;
        private float markerWorldSize;
        private bool hasTransition;
        private bool isEscape;
        private bool positionLogged;
        private Mesh[] meshes;
        private Vector3[][] ringVertices;
        private Material[] materials;
        private MeshRenderer[] renderers;
        private Color orbitColor;

        internal bool Initialize(CelestialBody target)
        {
            body = target;
            if (body == null || body.MapObject == null || body.MapObject.trf == null)
                return false;
            mapTransform = body.MapObject.trf;
            Shader shader = Shader.Find(SoiSettings.ShaderName);
            if (shader == null)
                return false;

            int count = SoiSettings.RippleCount;
            meshes = new Mesh[count];
            ringVertices = new Vector3[count][];
            materials = new Material[count];
            renderers = new MeshRenderer[count];
            transform.SetParent(mapTransform, false);
            for (int i = 0; i < count; i++)
            {
                GameObject child = new GameObject("Ripple " + i);
                child.layer = gameObject.layer;
                child.transform.SetParent(transform, false);
                MeshFilter filter = child.AddComponent<MeshFilter>();
                meshes[i] = CreateRing(out ringVertices[i]);
                filter.sharedMesh = meshes[i];
                Material material = new Material(shader);
                material.name = "OrbitInfluences SOI ripple";
                material.renderQueue = 3001;
                materials[i] = material;
                MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.receiveShadows = false;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.enabled = false;
                renderers[i] = renderer;
            }
            return true;
        }

        internal bool SetTransition(Orbit bodyPatch, double transitionUT,
                                    bool escape, Color patchColor)
        {
            if (bodyPatch == null || bodyPatch.referenceBody != body ||
                !Finite(transitionUT))
                return false;
            Vector3d relative;
            try
            {
                // Orbit uses Y/Z opposite to Unity; the patch is body-relative.
                relative = bodyPatch.getRelativePositionAtUT(transitionUT);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[OrbitInfluences] SOI transition position unavailable: " +
                                 exception.Message);
                return false;
            }
            if (!Finite(relative.x) || !Finite(relative.y) || !Finite(relative.z))
                return false;
            double radius = Math.Sqrt(relative.x * relative.x +
                                      relative.y * relative.y +
                                      relative.z * relative.z);
            double scaledRadius = radius * ScaledSpace.InverseScaleFactor;
            if (!Finite(scaledRadius) || scaledRadius <= 0d ||
                scaledRadius >= float.MaxValue)
                return false;
            Vector3 worldRadial = new Vector3((float)relative.x,
                                              (float)relative.z,
                                              (float)relative.y);
            if (worldRadial.sqrMagnitude < 0.000001f)
                return false;
            localRadial = mapTransform.InverseTransformDirection(worldRadial.normalized);
            localRadial.Normalize();
            worldRadius = (float)scaledRadius;
            isEscape = escape;
            orbitColor = patchColor;
            hasTransition = true;
            if (!positionLogged)
            {
                Debug.Log("[OrbitInfluences] " + (escape ? "Escape" : "Encounter") +
                          " marker: body=" + body.bodyName + ", UT=" + transitionUT +
                          ", radius/SOI=" + (radius / body.sphereOfInfluence));
                positionLogged = true;
            }
            Camera camera = PlanetariumCamera.Camera;
            return camera != null && RefreshPositionAndScale(camera);
        }

        private void LateUpdate()
        {
            Camera camera = PlanetariumCamera.Camera;
            bool visible = hasTransition && mapTransform != null && camera != null &&
                           RefreshPositionAndScale(camera);
            Color edgeColor = SoiSettings.RippleMatchesOrbitColor ? orbitColor : SoiSettings.WaveColor;
            int count = renderers == null ? 0 : renderers.Length;
            for (int i = 0; i < count; i++)
            {
                renderers[i].enabled = visible;
                if (!visible)
                    continue;
                float phase = Mathf.Repeat(Time.unscaledTime * SoiSettings.RippleSpeed +
                                           (float)i / count, 1f);
                float progress = isEscape ? 1f - phase : phase;
                float scale = Mathf.Lerp(SoiSettings.RippleMinScale,
                                         SoiSettings.RippleMaxScale, progress);
                UpdateRingGeometry(i, scale);
                Color color = Color.Lerp(SoiSettings.WaveCenterColor, edgeColor, progress);
                color.a = SoiSettings.RippleAlpha * Mathf.Sin(Mathf.PI * phase);
                if (materials[i].HasProperty("_TintColor"))
                    materials[i].SetColor("_TintColor", color);
                if (materials[i].HasProperty("_Color"))
                    materials[i].SetColor("_Color", color);
            }
        }

        private bool RefreshPositionAndScale(Camera camera)
        {
            double scaledRadius = body.sphereOfInfluence * ScaledSpace.InverseScaleFactor;
            if (!Finite(scaledRadius) || scaledRadius <= 0d ||
                scaledRadius >= float.MaxValue)
                return false;
            float mapRadius = (float)scaledRadius;
            Vector3 scaledRadial = mapTransform.TransformVector(localRadial);
            float unitsPerLocalUnit = scaledRadial.magnitude;
            Vector3 parentScale = mapTransform.lossyScale;
            float scaleX = Mathf.Abs(parentScale.x);
            float scaleY = Mathf.Abs(parentScale.y);
            float scaleZ = Mathf.Abs(parentScale.z);
            if (unitsPerLocalUnit < 0.000001f || scaleX < 0.000001f ||
                scaleY < 0.000001f || scaleZ < 0.000001f || camera.pixelHeight <= 0)
                return false;
            transform.localPosition = localRadial * (worldRadius / unitsPerLocalUnit);
            Vector3 outward = scaledRadial / unitsPerLocalUnit;
            transform.rotation = Quaternion.FromToRotation(Vector3.forward, outward);
            float distance = Vector3.Distance(camera.transform.position, transform.position);
            float unitsPerPixel = camera.orthographic
                ? 2f * camera.orthographicSize / camera.pixelHeight
                : 2f * distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) /
                  camera.pixelHeight;
            if (float.IsNaN(unitsPerPixel) || float.IsInfinity(unitsPerPixel) ||
                unitsPerPixel <= 0f)
                return false;
            float worldSize = Mathf.Clamp(unitsPerPixel * PixelRadius,
                mapRadius * MinimumSoiFraction, mapRadius * MaximumSoiFraction);
            markerWorldSize = worldSize;
            transform.localScale = new Vector3(worldSize / scaleX,
                                               worldSize / scaleY,
                                               worldSize / scaleZ);
            return true;
        }

        private void UpdateRingGeometry(int index, float phaseScale)
        {
            double halfWidth = SoiSettings.RippleWidth * 0.5d;
            double innerArc = markerWorldSize * phaseScale * (1d - halfWidth);
            double outerArc = markerWorldSize * phaseScale * (1d + halfWidth);
            double innerTangent, innerInward, outerTangent, outerInward;
            SoiSurfaceProjection.GetOffsets(worldRadius, innerArc,
                out innerTangent, out innerInward);
            SoiSurfaceProjection.GetOffsets(worldRadius, outerArc,
                out outerTangent, out outerInward);
            float innerXY = (float)(innerTangent / markerWorldSize);
            float outerXY = (float)(outerTangent / markerWorldSize);
            float innerZ = (float)(innerInward / markerWorldSize);
            float outerZ = (float)(outerInward / markerWorldSize);
            Vector3[] vertices = ringVertices[index];
            for (int i = 0; i <= Segments; i++)
            {
                Vector2 direction = Directions[i];
                int v = i * 2;
                vertices[v] = new Vector3(direction.x * innerXY,
                                          direction.y * innerXY, innerZ);
                vertices[v + 1] = new Vector3(direction.x * outerXY,
                                              direction.y * outerXY, outerZ);
            }
            // The mesh, topology, and vertex array are reused every frame.
            meshes[index].vertices = vertices;
            meshes[index].bounds = RippleBounds;
        }

        private void OnDestroy()
        {
            if (materials != null)
                foreach (Material material in materials)
                    if (material != null) Destroy(material);
            if (meshes != null)
                foreach (Mesh mesh in meshes)
                    if (mesh != null) Destroy(mesh);
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static Vector2[] CreateDirections()
        {
            Vector2[] directions = new Vector2[Segments + 1];
            for (int i = 0; i <= Segments; i++)
            {
                float angle = 2f * Mathf.PI * i / Segments;
                directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return directions;
        }

        private static Mesh CreateRing(out Vector3[] vertices)
        {
            vertices = new Vector3[(Segments + 1) * 2];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[Segments * 6];
            for (int i = 0; i <= Segments; i++)
            {
                int v = i * 2;
                normals[v] = Vector3.forward;
                normals[v + 1] = Vector3.forward;
                uvs[v] = new Vector2((float)i / Segments, 0f);
                uvs[v + 1] = new Vector2((float)i / Segments, 1f);
                if (i == Segments) continue;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }
            Mesh result = new Mesh();
            result.name = "OrbitInfluences curved ripple ring";
            result.MarkDynamic();
            result.vertices = vertices;
            result.normals = normals;
            result.uv = uvs;
            result.triangles = triangles;
            result.bounds = RippleBounds;
            return result;
        }
    }
}
