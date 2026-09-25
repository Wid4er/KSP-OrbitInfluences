using System;

namespace OrbitInfluences
{
    // Arc distance from the crossing along the SOI sphere, expressed in
    // tangent-plane radial and inward offsets from that crossing.
    internal static class SoiSurfaceProjection
    {
        internal static void GetOffsets(double sphereRadius, double arcDistance,
                                        out double tangent, out double inward)
        {
            double angle = arcDistance / sphereRadius;
            tangent = sphereRadius * Math.Sin(angle);
            inward = sphereRadius * (Math.Cos(angle) - 1d);
        }
    }
}
