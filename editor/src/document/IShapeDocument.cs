//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

/// <summary>
/// Interface for documents that support shape editing tools (pen, knife, shape).
/// Implemented by SpriteDocument.
/// </summary>
public interface IShapeDocument
{
    Matrix3x2 Transform { get; }
    void IncrementVersion();
    void UpdateBounds();
    bool IsActiveLayerLocked { get; }
}
