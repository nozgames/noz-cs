//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public interface ISkeletonBound
{
    SpriteDocument.SkeletonBinding Binding { get; }
    bool ShowInSkeleton { get; }
    void DrawSkinned(ReadOnlySpan<Matrix3x2> bindPose, ReadOnlySpan<Matrix3x2> animatedPose, in Matrix3x2 baseTransform);
}
