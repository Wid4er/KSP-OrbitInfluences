using UnityEngine;

namespace OrbitInfluences
{
    // A body-fixed latitude/longitude grid on the actual SOI sphere.
    public sealed class SoiGridRenderer : MonoBehaviour
    {
        private const int Meridians = 12;
        private const int MeridianSegments = 64;
        private const int ParallelSegments = 96;
        private const float PoleMargin = 0.02f;
        private static readonly float[] ParallelLatitudes =
            { -60f, -30f, 0f, 30f, 60f };

        private CelestialBody body;
        private Transform mapTransform;
        private Mesh mesh;
        private Material material;
        private MeshRenderer meshRenderer;
        private float mapRadius;
        private float appliedWidth;
        private Color appliedColor;

        internal bool Initialize(CelestialBody target)
        {
            body = target;
            if (body == null || body.MapObject == null || body.MapObject.trf == null)
                return false;
            mapTransform = body.MapObject.trf;
            Shader shader = Shader.Find(SoiSettings.ShaderName);
            if (shader == null)
                return false;

            appliedWidth = SoiSettings.RingWidthFraction;
            appliedColor = SoiSettings.RingColor;
            mesh = CreateGridMesh(appliedWidth);
            material = new Material(shader);
            material.name = "OrbitInfluences body-fixed SOI grid";
            material.renderQueue = 3000;
            ApplyColor();
            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.receiveShadows = false;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Identity local orientation follows the body map transform. The
            // vessel orbit is never consulted or applied to this renderer.
            transform.SetParent(mapTransform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            if (!RefreshRadius())
                return false;
            Debug.Log("[OrbitInfluences] Body-fixed SOI grid for " + body.bodyName +
                      ": mapRadius=" + mapRadius +
                      ", parentScale=" + mapTransform.lossyScale);
            return true;
        }

        internal bool RefreshRadius()
        {
            if (body == null || mapTransform == null)
                return false;
            double scaledRadius = body.sphereOfInfluence * ScaledSpace.InverseScaleFactor;
            if (double.IsNaN(scaledRadius) || double.IsInfinity(scaledRadius) ||
                scaledRadius <= 0d || scaledRadius >= float.MaxValue)
                return false;
            mapRadius = (float)scaledRadius;
            return ApplyWorldRadius();
        }

        internal void RefreshAppearance()
        {
            if (meshRenderer == null)
                return;
            if (appliedColor != SoiSettings.RingColor)
            {
                appliedColor = SoiSettings.RingColor;
                ApplyColor();
            }
            if (appliedWidth != SoiSettings.RingWidthFraction)
            {
                appliedWidth = SoiSettings.RingWidthFraction;
                Mesh oldMesh = mesh;
                mesh = CreateGridMesh(appliedWidth);
                GetComponent<MeshFilter>().sharedMesh = mesh;
                Destroy(oldMesh);
            }
        }

        internal void SetVisible(bool visible)
        {
            if (meshRenderer != null)
                meshRenderer.enabled = visible;
        }

        private void ApplyColor()
        {
            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", appliedColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", appliedColor);
        }

        private void LateUpdate()
        {
            if (meshRenderer != null && meshRenderer.enabled)
                ApplyWorldRadius();
        }

        private bool ApplyWorldRadius()
        {
            if (mapTransform == null)
                return false;
            Vector3 parentScale = mapTransform.lossyScale;
            float x = Mathf.Abs(parentScale.x);
            float y = Mathf.Abs(parentScale.y);
            float z = Mathf.Abs(parentScale.z);
            if (x < 0.000001f || y < 0.000001f || z < 0.000001f)
                return false;
            transform.localScale = new Vector3(mapRadius / x, mapRadius / y,
                                               mapRadius / z);
            return true;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (mesh != null) Destroy(mesh);
        }

        private static Mesh CreateGridMesh(float widthFraction)
        {
            int vertexCount = Meridians * (MeridianSegments + 1) * 2 +
                              ParallelLatitudes.Length * (ParallelSegments + 1) * 2;
            int triangleCount = (Meridians * MeridianSegments +
                                 ParallelLatitudes.Length * ParallelSegments) * 6;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[triangleCount];
            int vertexCursor = 0;
            int triangleCursor = 0;
            float halfWidth = widthFraction * 0.5f;
            float cosWidth = Mathf.Cos(halfWidth);
            float sinWidth = Mathf.Sin(halfWidth);

            for (int meridian = 0; meridian < Meridians; meridian++)
            {
                float longitude = 2f * Mathf.PI * meridian / Meridians;
                float cosLongitude = Mathf.Cos(longitude);
                float sinLongitude = Mathf.Sin(longitude);
                Vector3 side = new Vector3(-sinLongitude, 0f, cosLongitude);
                int first = vertexCursor;
                for (int step = 0; step <= MeridianSegments; step++)
                {
                    float latitude = Mathf.Lerp(-Mathf.PI * 0.5f + PoleMargin,
                                                Mathf.PI * 0.5f - PoleMargin,
                                                (float)step / MeridianSegments);
                    float cosLatitude = Mathf.Cos(latitude);
                    Vector3 center = new Vector3(cosLatitude * cosLongitude,
                        Mathf.Sin(latitude), cosLatitude * sinLongitude);
                    WritePair(center, side, cosWidth, sinWidth,
                        (float)step / MeridianSegments, vertices, normals, uvs,
                        ref vertexCursor);
                }
                WriteTriangles(first, MeridianSegments, triangles,
                               ref triangleCursor);
            }

            for (int parallel = 0; parallel < ParallelLatitudes.Length; parallel++)
            {
                float latitude = ParallelLatitudes[parallel] * Mathf.Deg2Rad;
                float cosLatitude = Mathf.Cos(latitude);
                float sinLatitude = Mathf.Sin(latitude);
                int first = vertexCursor;
                for (int step = 0; step <= ParallelSegments; step++)
                {
                    float longitude = 2f * Mathf.PI * step / ParallelSegments;
                    float cosLongitude = Mathf.Cos(longitude);
                    float sinLongitude = Mathf.Sin(longitude);
                    Vector3 center = new Vector3(cosLatitude * cosLongitude,
                        sinLatitude, cosLatitude * sinLongitude);
                    Vector3 south = new Vector3(sinLatitude * cosLongitude,
                        -cosLatitude, sinLatitude * sinLongitude);
                    WritePair(center, south, cosWidth, sinWidth,
                        (float)step / ParallelSegments, vertices, normals, uvs,
                        ref vertexCursor);
                }
                WriteTriangles(first, ParallelSegments, triangles,
                               ref triangleCursor);
            }

            Mesh result = new Mesh();
            result.name = "OrbitInfluences unit body-fixed SOI grid";
            result.vertices = vertices;
            result.normals = normals;
            result.uv = uvs;
            result.triangles = triangles;
            result.RecalculateBounds();
            return result;
        }

        private static void WritePair(Vector3 center, Vector3 side,
            float cosWidth, float sinWidth, float u, Vector3[] vertices,
            Vector3[] normals, Vector2[] uvs, ref int cursor)
        {
            Vector3 first = center * cosWidth + side * sinWidth;
            Vector3 second = center * cosWidth - side * sinWidth;
            vertices[cursor] = first;
            vertices[cursor + 1] = second;
            normals[cursor] = first;
            normals[cursor + 1] = second;
            uvs[cursor] = new Vector2(u, 0f);
            uvs[cursor + 1] = new Vector2(u, 1f);
            cursor += 2;
        }

        private static void WriteTriangles(int first, int segments,
                                            int[] triangles, ref int cursor)
        {
            for (int step = 0; step < segments; step++)
            {
                int v = first + step * 2;
                triangles[cursor++] = v;
                triangles[cursor++] = v + 1;
                triangles[cursor++] = v + 2;
                triangles[cursor++] = v + 2;
                triangles[cursor++] = v + 1;
                triangles[cursor++] = v + 3;
            }
        }
    }
}
