//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;
using NoZ.Platform;

namespace NoZ;

public static class UIRender
{
    private const int MaxUIVertices = 16384;
    private const int MaxUIIndices = 32768;

    private static nuint _mesh;
    private static NativeArray<UIVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static Shader? _shader;
    private static bool _initialized;

    public static void Init(UIConfig config)
    {
        if (_initialized) return;

        _shader = Asset.Get<Shader>(AssetType.Shader, config.UIShader);
        _vertices = new NativeArray<UIVertex>(MaxUIVertices);
        _indices = new NativeArray<ushort>(MaxUIIndices);

        _mesh = Graphics.Driver.CreateMesh<UIVertex>(
            MaxUIVertices,
            MaxUIIndices,
            BufferUsage.Dynamic,
            "UIRender"
        );

        _initialized = true;
    }

    public static void Shutdown()
    {
        if (!_initialized) return;

        _vertices.Dispose();
        _indices.Dispose();

        Graphics.Driver.DestroyMesh(_mesh);
        _initialized = false;
    }

    public static void DrawRect(
        in Rect rect,
        Color color,
        float borderRadius = 0,
        float borderWidth = 0,
        Color borderColor = default)
    {
        if (!_initialized || _shader == null) return;
        if (borderColor.A < 0) return;

        var vertexOffset = _vertices.Length;
        var indexOffset = _indices.Length;

        var x = rect.X;
        var y = rect.Y;
        var w = rect.Width;
        var h = rect.Height;            

        if (borderRadius <= 0)
        {
            if (!_vertices.CheckCapacity(4) || !_indices.CheckCapacity(6))
                return;

            _vertices.Add(new UIVertex { Position = new Vector2(x, y), UV = new Vector2(1, 1), Normal = Vector2.Zero, Color = color, BorderRatio = -1f, BorderColor = borderColor });
            _vertices.Add(new UIVertex { Position = new Vector2(x + w, y), UV = new Vector2(1, 1), Normal = Vector2.Zero, Color = color, BorderRatio = -1f, BorderColor = borderColor });
            _vertices.Add(new UIVertex { Position = new Vector2(x + w, y + h), UV = new Vector2(1, 1), Normal = Vector2.Zero, Color = color, BorderRatio = -1f, BorderColor = borderColor });
            _vertices.Add(new UIVertex { Position = new Vector2(x, y + h), UV = new Vector2(1, 1), Normal = Vector2.Zero, Color = color, BorderRatio = -1f, BorderColor = borderColor });

            _indices.Add((ushort)vertexOffset);
            _indices.Add((ushort)(vertexOffset + 1));
            _indices.Add((ushort)(vertexOffset + 2));
            _indices.Add((ushort)(vertexOffset + 2));
            _indices.Add((ushort)(vertexOffset + 3));
            _indices.Add((ushort)vertexOffset);

            Graphics.SetShader(_shader);
            Graphics.SetMesh(_mesh);
            Graphics.DrawElements(6, indexOffset);
            return;
        }

        // Rounded rectangle
        if (!_vertices.CheckCapacity(16) || !_indices.CheckCapacity(36))
            return;

        var borderRatio = borderWidth / borderRadius;
        var x0 = x;
        var x1 = x + w;
        var y0 = y;
        var y1 = y + h;
        var r = borderRadius;
        
        // 0-3 (top row, inner edge)
        _vertices.Add(new UIVertex { Position = new Vector2(x0, y0), UV = new Vector2(1, 1), Normal = new Vector2(0, 0), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x0, y0), UV = new Vector2(0, 1), Normal = new Vector2(r, 0), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x1, y0), UV = new Vector2(0, 1), Normal = new Vector2(-r, 0), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x1, y0), UV = new Vector2(1, 1), Normal = new Vector2(0, 0), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });

        // 4-7 (top row, outer edge)
        _vertices.Add(new UIVertex { Position = new Vector2(x0, y0), UV = new Vector2(1, 0), Normal = new Vector2(0, r), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x0, y0), UV = new Vector2(0, 0), Normal = new Vector2(r, r), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x1, y0), UV = new Vector2(0, 0), Normal = new Vector2(-r, r), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x1, y0), UV = new Vector2(1, 0), Normal = new Vector2(0, r), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });

        // 8-11 (bottom row, outer edge)
        _vertices.Add(new UIVertex { Position = new Vector2(x0, y1), UV = new Vector2(1, 0), Normal = new Vector2(0, -r), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x0, y1), UV = new Vector2(0, 0), Normal = new Vector2(r, -r), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x1, y1), UV = new Vector2(0, 0), Normal = new Vector2(-r, -r), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x1, y1), UV = new Vector2(1, 0), Normal = new Vector2(0, -r), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });

        // 12-15 (bottom row, inner edge)
        _vertices.Add(new UIVertex { Position = new Vector2(x0, y1), UV = new Vector2(1, 1), Normal = new Vector2(0, 0), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x0, y1), UV = new Vector2(0, 1), Normal = new Vector2(r, 0), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x1, y1), UV = new Vector2(0, 1), Normal = new Vector2(-r, 0), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });
        _vertices.Add(new UIVertex { Position = new Vector2(x1, y1), UV = new Vector2(1, 1), Normal = new Vector2(0, 0), Color = color, BorderRatio = borderRatio, BorderColor = borderColor });

        // top
        AddTriangle(vertexOffset, 0, 1, 4);
        AddTriangle(vertexOffset, 4, 1, 5);
        AddTriangle(vertexOffset, 1, 2, 5);
        AddTriangle(vertexOffset, 5, 2, 6);
        AddTriangle(vertexOffset, 2, 3, 6);
        AddTriangle(vertexOffset, 6, 3, 7);

        // middle   vertexOffset
        AddTriangle(vertexOffset, 4, 5, 8);
        AddTriangle(vertexOffset, 8, 5, 9);
        AddTriangle(vertexOffset, 9, 5, 6);
        AddTriangle(vertexOffset, 9, 6, 10);
        AddTriangle(vertexOffset, 6, 7, 10);
        AddTriangle(vertexOffset, 10, 7, 11);

        // bottom   vertexOffset
        AddTriangle(vertexOffset, 8, 9, 12);
        AddTriangle(vertexOffset, 12, 9, 13);
        AddTriangle(vertexOffset, 9, 10, 13);
        AddTriangle(vertexOffset, 13, 10, 14);
        AddTriangle(vertexOffset, 10, 11, 14);
        AddTriangle(vertexOffset, 14, 11, 15);

        Graphics.SetShader(_shader);
        Graphics.SetMesh(_mesh);
        Graphics.DrawElements(54, indexOffset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddTriangle(int baseVertex, int i0, int i1, int i2)
    {
        _indices.Add((ushort)(baseVertex + i0));
        _indices.Add((ushort)(baseVertex + i1));
        _indices.Add((ushort)(baseVertex + i2));
    }

    public static void Flush()
    {
        if (_indices.Length == 0 || !_initialized || _shader == null)
            return;

        Graphics.Driver.BindMesh(_mesh);
        Graphics.Driver.UpdateMesh(_mesh, _vertices.AsByteSpan(), _indices.AsSpan());
        _vertices.Clear();
        _indices.Clear();
    }
}
