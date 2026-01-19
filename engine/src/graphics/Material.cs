//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public class Material(Shader shader)
{
    public Shader Shader { get; } = shader;
    
    
    public void SetFloat(int location, float value)
    {
        
    }

    public void SetVector2(int location, in Vector2 value)
    {
    }
}
