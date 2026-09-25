using UnityEngine;

namespace OrbitInfluences
{
    // One SOI annulus aligned with the selected vessel orbit plane.
    public sealed class SoiBoundaryRenderer : MonoBehaviour
    {
        private CelestialBody body;
        private Transform mapTransform;
        private Mesh mesh;
        private Material material;
        private MeshRenderer meshRenderer;
        private float mapRadius;
        private float appliedWidth;
        private Color appliedColor;
        private Quaternion orbitRotation = Quaternion.identity;

        internal bool Initialize(CelestialBody target)
        {
            body = target;
            if (body == null || body.MapObject == null || body.MapObject.trf == null)
                return false;
            mapTransform = body.MapObject.trf;

            Shader shader = Shader.Find(SoiSettings.ShaderName);
            if (shader == null)
            {
                Debug.LogError("[OrbitInfluences] Boundary shader unavailable: " + SoiSettings.ShaderName);
                return false;
            }

            appliedWidth = SoiSettings.RingWidthFraction;
            appliedColor = SoiSettings.RingColor;
            mesh = CreateRingMesh(SoiSettings.RingSegments, appliedWidth);
            material = new Material(shader);
            material.name = "OrbitInfluences SOI boundary";
            material.renderQueue = 3000;
            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", SoiSettings.RingColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", SoiSettings.RingColor);

            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.receiveShadows = false;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Follow KSP's map body transform, as OrbitPOInts does. Its
            // non-unit scale is compensated in RefreshRadius so the world
            // radius still equals SOI * ScaledSpace.InverseScaleFactor.
            transform.SetParent(mapTransform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            if (!RefreshRadius())
                return false;

            Debug.Log("[OrbitInfluences] Boundary for " + body.bodyName +
                      ": SOI(m)=" + body.sphereOfInfluence +
                      ", inverseScale=" + ScaledSpace.InverseScaleFactor +
                      ", mapRadius=" + mapRadius +
                      ", parentScale=" + mapTransform.lossyScale +
                      ", localRadius=" + transform.localScale.x);
            LogNearestSatelliteDistance();
            return true;
        }

        internal bool RefreshRadius()
        {
            if (body == null || mapTransform == null)
                return false;

            double scaledRadius = body.sphereOfInfluence * ScaledSpace.InverseScaleFactor;
            if (double.IsNaN(scaledRadius) || double.IsInfinity(scaledRadius) ||
                scaledRadius <= 0 || scaledRadius >= float.MaxValue)
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
                if (material.HasProperty("_TintColor"))
                    material.SetColor("_TintColor", appliedColor);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", appliedColor);
            }
            if (appliedWidth != SoiSettings.RingWidthFraction)
            {
                appliedWidth = SoiSettings.RingWidthFraction;
                Mesh oldMesh = mesh;
                mesh = CreateRingMesh(SoiSettings.RingSegments, appliedWidth);
                GetComponent<MeshFilter>().sharedMesh = mesh;
                Destroy(oldMesh);
            }
        }

        internal void SetVisible(bool visible)
        {
            if (meshRenderer != null)
                meshRenderer.enabled = visible;
        }

        internal bool SetOrbitPlane(Orbit orbit)
        {
            if (orbit == null)
            {
                // A central body can have no parent orbit. Use a stable
                // reference plane instead of hiding its SOI boundary.
                orbitRotation = Quaternion.identity;
                transform.rotation = orbitRotation;
                return true;
            }

            // KSP Orbit vectors use x,z,y relative to Unity world vectors.
            Vector3d orbital = orbit.GetOrbitNormal();
            if (double.IsNaN(orbital.x) || double.IsNaN(orbital.y) ||
                double.IsNaN(orbital.z) || double.IsInfinity(orbital.x) ||
                double.IsInfinity(orbital.y) || double.IsInfinity(orbital.z))
                return false;

            Vector3 normal = new Vector3((float)orbital.x, (float)orbital.z,
                                         (float)orbital.y);
            if (normal.sqrMagnitude < 0.000001f)
                return false;

            // The annulus lies in local XY, so its normal is local +Z.
            orbitRotation = Quaternion.FromToRotation(Vector3.forward, normal.normalized);
            transform.rotation = orbitRotation;
            return true;
        }

        private void LateUpdate()
        {
            if (meshRenderer == null || !meshRenderer.enabled || mapTransform == null)
                return;

            // KSP updates ScaledSpace while the camera moves. Parenting keeps
            // the center in the same frame; only compensate scale and rotation.
            ApplyWorldRadius();
            transform.rotation = orbitRotation;
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

            // MapObject.trf is uniformly scaled in the observed KSP system.
            // Component compensation also avoids a silent 10x shrink if its
            // scale differs on another body.
            transform.localScale = new Vector3(mapRadius / x, mapRadius / y,
                                               mapRadius / z);
            return true;
        }

        private void LogNearestSatelliteDistance()
        {
            if (body.orbitingBodies == null)
                return;

            CelestialBody nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (CelestialBody satellite in body.orbitingBodies)
            {
                if (satellite == null || satellite.MapObject == null ||
                    satellite.MapObject.trf == null)
                    continue;

                float distance = Vector3.Distance(mapTransform.position,
                                                  satellite.MapObject.trf.position);
                if (distance > 0f && distance < nearestDistance)
                {
                    nearest = satellite;
                    nearestDistance = distance;
                }
            }

            if (nearest != null)
                Debug.Log("[OrbitInfluences] Nearest satellite of " + body.bodyName +
                          ": " + nearest.bodyName +
                          ", mapDistance=" + nearestDistance +
                          ", SOI/distance=" + (mapRadius / nearestDistance));
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (mesh != null) Destroy(mesh);
        }

        private static Mesh CreateRingMesh(int segments, float widthFraction)
        {
            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[segments * 6];
            float inner = 1f - widthFraction * 0.5f;
            float outer = 1f + widthFraction * 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                int v = i * 2;
                vertices[v] = direction * inner;
                vertices[v + 1] = direction * outer;
                normals[v] = Vector3.forward;
                normals[v + 1] = Vector3.forward;
                uvs[v] = new Vector2((float)i / segments, 0f);
                uvs[v + 1] = new Vector2((float)i / segments, 1f);

                if (i == segments) continue;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            Mesh result = new Mesh();
            result.name = "OrbitInfluences unit SOI ring";
            result.vertices = vertices;
            result.normals = normals;
            result.uv = uvs;
            result.triangles = triangles;
            result.RecalculateBounds();
            return result;
        }
    }
}
