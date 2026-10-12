using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FogHost;

namespace FogReconstruction
{
    /// <summary>Development-only geometric queries over verified DSR MSB region facts.</summary>
    public static class FogDsrSpatialIndex
    {
        public static IReadOnlyList<FogDsrRegionFact> ContainingRegions(
            FogDsrFacts.Map map,
            FogDsrTransform point,
            float tolerance = 0.001f)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return Array.AsReadOnly(map.RegionDetails
                .Where(region => Contains(region, point, tolerance))
                .OrderBy(region => region.Name, StringComparer.Ordinal)
                .ThenBy(region => region.EntityId)
                .ToArray());
        }

        public static bool Contains(FogDsrRegionFact region, FogDsrTransform point, float tolerance = 0.001f)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            if (point == null) throw new ArgumentNullException(nameof(point));
            if (!float.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                throw new ArgumentException("A finite point transform is required.", nameof(point));

            double dx = point.X - region.Transform.X;
            double dy = point.Y - region.Transform.Y;
            double dz = point.Z - region.Transform.Z;
            switch (region.Shape.Kind)
            {
                case FogDsrRegionShapeKind.Point:
                    return dx * dx + dy * dy + dz * dz <= tolerance * tolerance;
                case FogDsrRegionShapeKind.Box:
                {
                    double radians = region.Transform.Yaw * Math.PI / 180.0;
                    double cosine = Math.Cos(radians);
                    double sine = Math.Sin(radians);
                    double localX = dx * cosine - dz * sine;
                    double localZ = dx * sine + dz * cosine;
                    return Math.Abs(localX) <= Half(region.Shape.Width, "box width") + tolerance
                        && Math.Abs(dy) <= Half(region.Shape.Height, "box height") + tolerance
                        && Math.Abs(localZ) <= Half(region.Shape.Depth, "box depth") + tolerance;
                }
                case FogDsrRegionShapeKind.Cylinder:
                    return dx * dx + dz * dz <= Square(Required(region.Shape.Radius, "cylinder radius") + tolerance)
                        && Math.Abs(dy) <= Half(region.Shape.Height, "cylinder height") + tolerance;
                case FogDsrRegionShapeKind.Sphere:
                    return dx * dx + dy * dy + dz * dz <= Square(Required(region.Shape.Radius, "sphere radius") + tolerance);
                default:
                    throw new InvalidDataException($"Unknown DSR region shape kind {region.Shape.Kind}.");
            }
        }

        private static double Half(float? dimension, string name) => Required(dimension, name) / 2.0;

        private static double Required(float? dimension, string name)
        {
            if (!dimension.HasValue || !float.IsFinite(dimension.Value) || dimension.Value < 0)
                throw new InvalidDataException($"A finite non-negative {name} is required for DSR region containment.");
            return dimension.Value;
        }

        private static double Square(double value) => value * value;
    }

    internal static class FogDsrSpatialIndexSelfTests
    {
        public static void Run()
        {
            var box = new FogDsrRegionFact("rotated box", 1, new FogDsrTransform(0, 0, 0, 90),
                new FogDsrRegionShape(FogDsrRegionShapeKind.Box, 2, 2, 4, null));
            if (!FogDsrSpatialIndex.Contains(box, new FogDsrTransform(1.5f, 0, 0, 0))
                || FogDsrSpatialIndex.Contains(box, new FogDsrTransform(0, 0, 1.5f, 0)))
                throw new InvalidOperationException("Rotated DSR box containment did not respect yaw and independent dimensions.");

            var cylinder = new FogDsrRegionFact("cylinder", 2, new FogDsrTransform(0, 0, 0, 0),
                new FogDsrRegionShape(FogDsrRegionShapeKind.Cylinder, null, 4, null, 2));
            if (!FogDsrSpatialIndex.Contains(cylinder, new FogDsrTransform(1, 1.9f, 1, 0))
                || FogDsrSpatialIndex.Contains(cylinder, new FogDsrTransform(2.01f, 0, 0, 0))
                || FogDsrSpatialIndex.Contains(cylinder, new FogDsrTransform(0, 2.1f, 0, 0)))
                throw new InvalidOperationException("DSR cylinder containment did not respect its radius and height.");

            var sphere = new FogDsrRegionFact("sphere", 3, new FogDsrTransform(0, 0, 0, 0),
                new FogDsrRegionShape(FogDsrRegionShapeKind.Sphere, null, null, null, 2));
            if (!FogDsrSpatialIndex.Contains(sphere, new FogDsrTransform(1, 1, 1, 0))
                || FogDsrSpatialIndex.Contains(sphere, new FogDsrTransform(2, 1, 0, 0)))
                throw new InvalidOperationException("DSR sphere containment did not respect its three-dimensional radius.");

            var point = new FogDsrRegionFact("point", 4, new FogDsrTransform(1, 2, 3, 0),
                new FogDsrRegionShape(FogDsrRegionShapeKind.Point, null, null, null, null));
            if (!FogDsrSpatialIndex.Contains(point, new FogDsrTransform(1, 2, 3, 0))
                || FogDsrSpatialIndex.Contains(point, new FogDsrTransform(1.01f, 2, 3, 0)))
                throw new InvalidOperationException("DSR point-region containment did not respect its tolerance.");
        }
    }
}
