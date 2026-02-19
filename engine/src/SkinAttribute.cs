//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class SkinAttribute : Attribute
{
    public string SkinName { get; }
    public string PropertyName { get; }
    public string Value { get; }

    public SkinAttribute(string skinName, string propertyName, string value)
    {
        SkinName = skinName;
        PropertyName = propertyName;
        Value = value;
    }

    public SkinAttribute(string skinName, string value)
    {
        SkinName = skinName;
        PropertyName = "";
        Value = value;
    }
}
