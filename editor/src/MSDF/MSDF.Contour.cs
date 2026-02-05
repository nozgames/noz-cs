//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor
{
    partial class MSDF
    {
        private class Contour
        {
            public Edge[] edges = null!;

            public void Bounds(ref double l, ref double b, ref double r, ref double t)
            {
                foreach (Edge edge in edges)
                    edge.Bounds(ref l, ref b, ref r, ref t);
            }

            public int Winding()
            {
                if (edges.Length == 0)
                    return 0;

                // Sample each edge at multiple points to handle curves correctly
                const int samplesPerEdge = 4;
                double total = 0;
                Vector2Double prev = edges[edges.Length - 1].GetPoint(1.0);

                foreach (var edge in edges)
                {
                    for (int s = 0; s < samplesPerEdge; s++)
                    {
                        double t = (s + 1.0) / samplesPerEdge;
                        var cur = edge.GetPoint(t);
                        total += ShoeLace(prev, cur);
                        prev = cur;
                    }
                }

                return Sign(total);
            }
        }
    }
}
