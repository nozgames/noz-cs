//
//  Adapter that bridges our NoZ.Editor.Msdf types to the CSGL.MSDFGen library.
//  Converts our Shape → MSDFGen.Shape, calls their GenerateMSDF, copies result back.
//

using System;
using System.Numerics;
using CSGL.Graphics;

namespace NoZ.Editor.Msdf;

internal static class MsdfGeneratorRemora
{
    /// <summary>
    /// Convert our Shape to a MSDFGen.Shape, generate MSDF using their code,
    /// and write the result into our MsdfBitmap.
    /// </summary>
    public static void GenerateMSDF(
        MsdfBitmap output,
        Shape ourShape,
        double rangeValue,
        Vector2Double scale,
        Vector2Double translate,
        bool invertWinding = false)
    {
        // Convert our shape to CSGL's shape
        var csglShape = ConvertShape(ourShape);

        // Apply edge coloring using their implementation
        MSDFGen.MSDF.EdgeColoringSimple(csglShape, 3.0, 0);

        int w = output.width;
        int h = output.height;

        // Create their bitmap
        var csglBitmap = new Bitmap<Color3>(w, h);

        // Their coordinate mapping inside GenerateMSDF:
        //   Vector2 p = (new Vector2(x, y) - region.Position - translate) / scale
        //   then EvaluateMSDF adds +0.5 to p
        //   So effective: p = (new Vector2(x + 0.5, y + 0.5) - translate) / scale
        //
        // Our coordinate mapping:
        //   p = (new Vector2(x + 0.5, y + 0.5)) / scale - translate
        //   = (x + 0.5 - translate * scale) / scale
        //
        // Theirs: (x + 0.5 - translate) / scale
        //
        // So: their_translate = our_translate * scale
        var csglTranslate = new Vector2(
            (float)(translate.x * scale.x),
            (float)(translate.y * scale.y));
        var csglScale = new Vector2((float)scale.x, (float)scale.y);

        MSDFGen.MSDF.GenerateMSDF(csglBitmap, csglShape,
            new CSGL.Math.Rectangle(0, 0, w, h),
            rangeValue,
            csglScale,
            csglTranslate,
            1.001);

        // Copy results back to our bitmap.
        // CSGL handles inverseYAxis internally in GenerateMSDF, so the bitmap
        // is already flipped. We just copy straight through.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = csglBitmap[x, y];
                var pixel = output[x, y];
                pixel[0] = c.r;
                pixel[1] = c.g;
                pixel[2] = c.b;
            }
        }
    }

    /// <summary>
    /// Simple error correction using CSGL's PixelClash detection.
    /// </summary>
    public static void CorrectErrors(MsdfBitmap sdf, int w, int h, double rangeValue)
    {
        // Convert to CSGL bitmap for their error correction
        var csglBitmap = new Bitmap<Color4>(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var p = sdf[x, y];
                csglBitmap[x, y] = new Color4(p[0], p[1], p[2], 1f);
            }
        }

        var threshold = new Vector2((float)(1.0 / rangeValue), (float)(1.0 / rangeValue));
        MSDFGen.MSDF.CorrectErrors(csglBitmap, new CSGL.Math.Rectanglei(0, 0, w, h), threshold);

        // Copy back
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = csglBitmap[x, y];
                var p = sdf[x, y];
                p[0] = c.r;
                p[1] = c.g;
                p[2] = c.b;
            }
        }
    }

    /// <summary>
    /// Convert our NoZ Shape to a MSDFGen.Shape.
    /// </summary>
    private static MSDFGen.Shape ConvertShape(Shape ourShape)
    {
        var csglShape = new MSDFGen.Shape();
        csglShape.InverseYAxis = ourShape.inverseYAxis;

        foreach (var ourContour in ourShape.contours)
        {
            var csglContour = new MSDFGen.Contour();
            csglShape.Contours.Add(csglContour);

            foreach (var ourEdge in ourContour.edges)
            {
                MSDFGen.EdgeSegment csglEdge;

                if (ourEdge is LinearSegment lin)
                {
                    csglEdge = new MSDFGen.LinearSegment(
                        ToVec2(lin.p[0]),
                        ToVec2(lin.p[1]),
                        ConvertColor(ourEdge.color));
                }
                else if (ourEdge is QuadraticSegment quad)
                {
                    csglEdge = new MSDFGen.QuadraticSegment(
                        ToVec2(quad.p[0]),
                        ToVec2(quad.p[1]),
                        ToVec2(quad.p[2]),
                        ConvertColor(ourEdge.color));
                }
                else if (ourEdge is CubicSegment cub)
                {
                    csglEdge = new MSDFGen.CubicSegment(
                        ToVec2(cub.p[0]),
                        ToVec2(cub.p[1]),
                        ToVec2(cub.p[2]),
                        ToVec2(cub.p[3]),
                        ConvertColor(ourEdge.color));
                }
                else
                {
                    continue;
                }

                csglContour.Edges.Add(csglEdge);
            }
        }

        // Let CSGL do its own normalize and edge coloring
        csglShape.Normalize();

        return csglShape;
    }

    private static Vector2 ToVec2(Vector2Double v) => new Vector2((float)v.x, (float)v.y);

    private static MSDFGen.EdgeColor ConvertColor(EdgeColor c) => (MSDFGen.EdgeColor)(int)c;
}
