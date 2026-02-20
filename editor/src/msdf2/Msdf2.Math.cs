//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;

namespace NoZ.Editor.Msdf2;

/// <summary>
/// Core math utilities ported from msdfgen's arithmetics.hpp and equation-solver.cpp
/// </summary>
internal static class MsdfMath
{
    public static double Median(double a, double b, double c)
    {
        return Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
    }

    public static T Mix<T>(T a, T b, double weight) where T : struct
    {
        // Specialized for double
        if (a is double da && b is double db)
            return (T)(object)((1.0 - weight) * da + weight * db);
        throw new NotSupportedException();
    }

    public static double Clamp(double n, double a, double b)
    {
        return n >= a && n <= b ? n : n < a ? a : b;
    }

    public static int Sign(double n) => (0.0 < n ? 1 : 0) - (n < 0.0 ? 1 : 0);

    public static int NonZeroSign(double n) => 2 * (n > 0.0 ? 1 : 0) - 1;

    // --- Vector math helpers ---

    public static double Dot(Vector2Double a, Vector2Double b) => a.x * b.x + a.y * b.y;
    public static double Cross(Vector2Double a, Vector2Double b) => a.x * b.y - a.y * b.x;

    public static Vector2Double VecMix(Vector2Double a, Vector2Double b, double weight)
    {
        return new Vector2Double(
            (1.0 - weight) * a.x + weight * b.x,
            (1.0 - weight) * a.y + weight * b.y);
    }

    public static double SquaredLength(Vector2Double v) => v.x * v.x + v.y * v.y;

    public static double Length(Vector2Double v) => Math.Sqrt(v.x * v.x + v.y * v.y);

    /// <summary>
    /// Normalize with allowZero=false: returns (0, 1) when zero-length.
    /// Matches msdfgen's Vector2::normalize(false).
    /// </summary>
    public static Vector2Double Normalize(Vector2Double v)
    {
        double len = Length(v);
        if (len != 0)
            return new Vector2Double(v.x / len, v.y / len);
        return new Vector2Double(0, 1);
    }

    /// <summary>
    /// Normalize with allowZero=true: returns (0, 0) when zero-length.
    /// Matches msdfgen's Vector2::normalize(true).
    /// </summary>
    public static Vector2Double NormalizeAllowZero(Vector2Double v)
    {
        double len = Length(v);
        if (len != 0)
            return new Vector2Double(v.x / len, v.y / len);
        return new Vector2Double(0, 0);
    }

    /// <summary>
    /// Returns a vector with unit length orthogonal to this one.
    /// Matches msdfgen's Vector2::getOrthonormal(polarity, allowZero=true).
    /// </summary>
    public static Vector2Double GetOrthonormal(Vector2Double v, bool polarity = true, bool allowZero = false)
    {
        double len = Length(v);
        if (len != 0)
            return polarity
                ? new Vector2Double(-v.y / len, v.x / len)
                : new Vector2Double(v.y / len, -v.x / len);
        return polarity
            ? new Vector2Double(0, allowZero ? 0 : 1)
            : new Vector2Double(0, allowZero ? 0 : -1);
    }

    // --- Equation solvers ---

    /// <summary>
    /// Solves ax^2 + bx + c = 0. Returns number of solutions.
    /// </summary>
    public static int SolveQuadratic(Span<double> x, double a, double b, double c)
    {
        if (a == 0 || Math.Abs(b) > 1e12 * Math.Abs(a))
        {
            if (b == 0)
            {
                if (c == 0) return -1; // 0 == 0
                return 0;
            }
            x[0] = -c / b;
            return 1;
        }
        double dscr = b * b - 4 * a * c;
        if (dscr > 0)
        {
            dscr = Math.Sqrt(dscr);
            x[0] = (-b + dscr) / (2 * a);
            x[1] = (-b - dscr) / (2 * a);
            return 2;
        }
        else if (dscr == 0)
        {
            x[0] = -b / (2 * a);
            return 1;
        }
        else
            return 0;
    }

    private static int SolveCubicNormed(Span<double> x, double a, double b, double c)
    {
        double a2 = a * a;
        double q = 1.0 / 9.0 * (a2 - 3 * b);
        double r = 1.0 / 54.0 * (a * (2 * a2 - 9 * b) + 27 * c);
        double r2 = r * r;
        double q3 = q * q * q;
        double aDiv3 = a * (1.0 / 3.0);
        if (r2 < q3)
        {
            double t = r / Math.Sqrt(q3);
            if (t < -1) t = -1;
            if (t > 1) t = 1;
            t = Math.Acos(t);
            q = -2 * Math.Sqrt(q);
            x[0] = q * Math.Cos(1.0 / 3.0 * t) - aDiv3;
            x[1] = q * Math.Cos(1.0 / 3.0 * (t + 2 * Math.PI)) - aDiv3;
            x[2] = q * Math.Cos(1.0 / 3.0 * (t - 2 * Math.PI)) - aDiv3;
            return 3;
        }
        else
        {
            double u = (r < 0 ? 1 : -1) * Math.Pow(Math.Abs(r) + Math.Sqrt(r2 - q3), 1.0 / 3.0);
            double v = u == 0 ? 0 : q / u;
            x[0] = (u + v) - aDiv3;
            if (u == v || Math.Abs(u - v) < 1e-12 * Math.Abs(u + v))
            {
                x[1] = -0.5 * (u + v) - aDiv3;
                return 2;
            }
            return 1;
        }
    }

    /// <summary>
    /// Solves ax^3 + bx^2 + cx + d = 0. Returns number of solutions.
    /// </summary>
    public static int SolveCubic(Span<double> x, double a, double b, double c, double d)
    {
        if (a != 0)
        {
            double bn = b / a;
            if (Math.Abs(bn) < 1e6)
                return SolveCubicNormed(x, bn, c / a, d / a);
        }
        return SolveQuadratic(x, b, c, d);
    }
}
