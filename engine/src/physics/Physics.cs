//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static class Physics
{
    public static bool OverlapPointTriangle(
        in Vector2 point,
        in Vector2 tri0,
        in Vector2 tri1,
        in Vector2 tri2,
        out Vector2 barycentric)
    {
        barycentric = default;

        var area = (tri1.X - tri0.X) * (tri2.Y - tri0.Y) - (tri2.X - tri0.X) * (tri1.Y - tri0.Y);

        if (MathF.Abs(area) < 1e-6f)
            return false;

        var invArea = 1.0f / area;
        var s = ((tri2.Y - tri0.Y) * (point.X - tri0.X) + (tri0.X - tri2.X) * (point.Y - tri0.Y)) * invArea;
        var t = ((tri0.Y - tri1.Y) * (point.X - tri0.X) + (tri1.X - tri0.X) * (point.Y - tri0.Y)) * invArea;

        if (s >= 0 && t >= 0 && (s + t) <= 1)
        {
            barycentric = new Vector2(s, t);
            return true;
        }

        return false;
    }

    public static bool OverlapLineLine(
        in Vector2 l0Start,
        in Vector2 l0End,
        in Vector2 l1Start,
        in Vector2 l1End,
        out Vector2 intersection)
    {
        intersection = default;

        var d0 = l0End - l0Start;
        var d1 = l1End - l1Start;
        var cross = d0.X * d1.Y - d0.Y * d1.X;
        if (MathF.Abs(cross) < MathEx.Epsilon)
            return false;

        var delta = l1Start - l0Start;
        var t = (delta.X * d1.Y - delta.Y * d1.X) / cross;
        var u = (delta.X * d0.Y - delta.Y * d0.X) / cross;
        if (t >= 0.0f && t <= 1.0f && u >= 0.0f && u <= 1.0f)
        {
            intersection = l0Start + d0 * t;
            return true;
        }

        return false;
    }

    public static bool ClosestPointOnLine(
        in Vector2 lineStart,
        in Vector2 lineEnd,
        in Vector2 point,
        float maxDistance,
        out Vector2 closestPoint)
    {
        var line = lineEnd - lineStart;
        var lengthSq = line.LengthSquared();

        if (lengthSq < MathEx.Epsilon)
        {
            closestPoint = lineStart;
            return Vector2.Distance(point, lineStart) <= maxDistance;
        }

        var t = Math.Clamp(Vector2.Dot(point - lineStart, line) / lengthSq, 0f, 1f);
        closestPoint = lineStart + line * t;

        return Vector2.Distance(point, closestPoint) <= maxDistance;
    }

    public static bool OverlapPoint(ReadOnlySpan<Vector2> points, Vector2 point)
    {
        if (points.Length == 0)
            return false;

        var v1 = points[points.Length - 1];

        for (var i = 0; i < points.Length; i++)
        {
            var v2 = points[i];
            var edge = v2 - v1;
            var toPoint = point - v1;
            var cross = edge.X * toPoint.Y - edge.Y * toPoint.X;

            if (cross < 0)
                return false;

            v1 = v2;
        }

        return true;
    }

    public static bool CastRayPlane(
        Vector2 rayStart,
        Vector2 rayEnd,
        Vector2 planeOrigin,
        Vector2 planeNormal,
        out CastResult result)
    {
        result = default;

        Vector2 segment = rayEnd - rayStart;
        float denom = Vector2.Dot(segment, planeNormal);

        // Segment is parallel to the plane
        if (Math.Abs(denom) < 1e-6f)
            return false;

        // Fraction along the segment [0, 1]
        float fraction = Vector2.Dot(planeOrigin - rayStart, planeNormal) / denom;

        // Intersection is outside the segment
        if (fraction < 0f || fraction > 1f)
            return false;

        Vector2 hitPoint = rayStart + segment * fraction;

        result.Point = hitPoint;
        result.Fraction = fraction;
        result.Distance = Vector2.Distance(rayStart, hitPoint);
        result.Normal = denom < 0f ? planeNormal : -planeNormal;

        return true;
    }

    public static bool CastCirclePlane(
        Vector2 origin,
        Vector2 target,
        float radius,
        Vector2 planeOrigin,
        Vector2 planeNormal,
        out CastResult result)
    {
        result = default;

        Vector2 segment = target - origin;
        float denom = Vector2.Dot(segment, planeNormal);

        if (Math.Abs(denom) < 1e-6f)
            return false;

        // Signed distance from the start center to the original plane.
        // Its sign tells us which side of the plane the circle is on,
        // so we know which way to offset (Minkowski-expand) the plane.
        float startDist = Vector2.Dot(origin - planeOrigin, planeNormal);
        float side = startDist >= 0f ? 1f : -1f;

        // Offset the plane outward by `radius` along the side the circle is on.
        // Now solve a point-vs-plane intersection against this expanded plane.
        Vector2 expandedOrigin = planeOrigin + planeNormal * (radius * side);

        float fraction = Vector2.Dot(expandedOrigin - origin, planeNormal) / denom;

        // Initial overlap → clamp to 0 instead of rejecting
        if (fraction < 0f)
        {
            if (Math.Abs(startDist) > radius)
                return false; // moving away and not overlapping
            fraction = 0f;
        }

        if (fraction > 1f)
            return false;

        Vector2 centerAtHit = origin + segment * fraction;

        result.Point = centerAtHit - planeNormal * (radius * side); // contact point on original plane
        result.Fraction = fraction;
        result.Distance = Vector2.Distance(origin, centerAtHit);
        result.Normal = planeNormal * side;

        return true;
    }

    public static bool CastCircleCircle(
        Vector2 position,
        Vector2 direction,
        float distance,
        Vector2 target,
        float targetRadius,
        out CastResult result)
    {
        const float Epsilon = 1e-6f;
        
        Vector2 m = position - target;       // from target to ray origin
        float r = targetRadius;
        float rSq = r * r;
        
        float mDotDir = Vector2.Dot(m, direction);
        float mSq = Vector2.Dot(m, m);
        float c = mSq - rSq;
        
        // Already inside the target — resolve at t=0 by pushing out along m
        if (c < 0f)
        {
            float mLen = MathF.Sqrt(mSq);
            Vector2 normal = mLen > Epsilon ? m / mLen : Vector2.UnitY;
            result = new CastResult
            {
                Point = target + normal * r,
                Normal = normal,
                Distance = 0f,
                Fraction = 0f,
            };
            return true;
        }
        
        // Origin is outside and ray points away from target — no hit
        if (mDotDir >= 0f)
        {
            result = default;
            return false;
        }
        
        // Discriminant of the quadratic |position + t*direction - target|² = r²
        // With direction normalized, the quadratic coefficient a = 1, so this simplifies.
        float discriminant = mDotDir * mDotDir - c;
        if (discriminant < 0f)
        {
            result = default;
            return false;
        }
        
        float t = -mDotDir - MathF.Sqrt(discriminant);
        
        // Hit happens beyond the cast distance
        if (t > distance)
        {
            result = default;
            return false;
        }
        
        if (t < 0f) t = 0f;
        
        Vector2 hitPoint = position + direction * t;
        Vector2 n = hitPoint - target;
        float nLen = n.Length();
        Vector2 hitNormal = nLen > Epsilon ? n / nLen : Vector2.UnitY;
        
        result = new CastResult
        {
            Point = hitPoint,
            Normal = hitNormal,
            Distance = t,
            Fraction = distance > Epsilon ? t / distance : 0f,
        };
        return true;
    }
}
