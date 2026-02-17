//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;
using System.Text.RegularExpressions;

namespace NoZ.Editor;

public class ShaderDocument : Document
{
    public bool Blend { get; set; }
    public bool Depth { get; set; }
    public bool DepthLess { get; set; }
    public bool Postprocess { get; set; }
    public bool UiComposite { get; set; }
    public bool PremultipliedAlpha { get; set; }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef 
        {
            Type = AssetType.Shader,
            Extension = ".wgsl",
            Factory = () => new ShaderDocument()
        });
    }

    public override void LoadMetadata(PropertySet meta)
    {
        Blend = meta.GetBool("shader", "blend", false);
        Depth = meta.GetBool("shader", "depth", false);
        DepthLess = meta.GetBool("shader", "depth_less", false);
        Postprocess = meta.GetBool("shader", "postproc", false);
        UiComposite = meta.GetBool("shader", "composite", false);
        PremultipliedAlpha = meta.GetBool("shader", "premultiplied", false);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        if (Blend) meta.SetBool("shader", "blend", true);
        if (Depth) meta.SetBool("shader", "depth", true);
        if (DepthLess) meta.SetBool("shader", "depth_less", true);
        if (Postprocess) meta.SetBool("shader", "postproc", true);
        if (UiComposite) meta.SetBool("shader", "composite", true);
        if (PremultipliedAlpha) meta.SetBool("shader", "premultiplied", true);
    }

    public override void Import(string outputPath, PropertySet meta)
    {
        ImportWgsl(outputPath, GetShaderFlags());
    }

    private void ImportWgsl(string outputPath, ShaderFlags flags)
    {
        var wgslSource = File.ReadAllText(Path);
        var bindings = ParseWgslBindings(wgslSource);
        var vertexHash = ComputeVertexInputHash(wgslSource);

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Shader, Shader.Version);

        var sourceBytes = Encoding.UTF8.GetBytes(wgslSource);
        writer.Write((uint)sourceBytes.Length);
        writer.Write(sourceBytes);
        writer.Write((byte)flags);
        writer.Write((byte)bindings.Count);
        foreach (var binding in bindings)
        {
            writer.Write((byte)binding.Binding);
            writer.Write((byte)binding.Type);
            writer.Write(binding.Name);
        }
        writer.Write(vertexHash);

        Log.Info($"Imported WGSL shader {Name} with {bindings.Count} bindings, vertexHash=0x{vertexHash:X8}");
    }

    private List<ShaderBinding> ParseWgslBindings(string wgslSource)
    {
        var bindings = new List<ShaderBinding>();
        var bindingDict = new Dictionary<uint, ShaderBinding>();

        // Pattern: @group(N) @binding(M) var<uniform> name: Type;
        // Pattern: @group(N) @binding(M) var name: texture_2d<f32>;
        // Pattern: @group(N) @binding(M) var name: texture_2d_array<f32>;
        // Pattern: @group(N) @binding(M) var name: sampler;

        var bindingPattern = @"@group\s*\(\s*(\d+)\s*\)\s*@binding\s*\(\s*(\d+)\s*\)\s*var(?:<(\w+)>)?\s+(\w+)\s*:\s*([^;]+);";
        var matches = Regex.Matches(wgslSource, bindingPattern);

        foreach (Match match in matches)
        {
            var group = uint.Parse(match.Groups[1].Value);
            var binding = uint.Parse(match.Groups[2].Value);
            var storageClass = match.Groups[3].Value; // uniform, storage, etc.
            var name = match.Groups[4].Value;
            var type = match.Groups[5].Value.Trim();

            // Determine binding type from WGSL type
            ShaderBindingType bindingType;
            if (storageClass == "uniform" || type.Contains("uniform"))
            {
                bindingType = ShaderBindingType.UniformBuffer;
            }
            else if (type.Contains("texture_2d_array"))
            {
                bindingType = ShaderBindingType.Texture2DArray;
            }
            else if (type.Contains("texture_2d") || type.Contains("texture_cube"))
            {
                bindingType = ShaderBindingType.Texture2D;
            }
            else if (type.Contains("sampler"))
            {
                bindingType = ShaderBindingType.Sampler;
            }
            else
            {
                Log.Warning($"Unknown WGSL binding type: {type} for {name}, assuming uniform buffer");
                bindingType = ShaderBindingType.UniformBuffer;
            }

            // Only support group 0 for now
            if (group == 0)
            {
                bindingDict[binding] = new ShaderBinding
                {
                    Binding = binding,
                    Type = bindingType,
                    Name = name
                };
            }
        }

        return bindingDict.Values.OrderBy(b => b.Binding).ToList();
    }

    private static uint ComputeVertexInputHash(string wgslSource)
    {
        // Find the VertexInput struct and parse its @location attributes
        var structMatch = Regex.Match(wgslSource, @"struct\s+VertexInput\s*\{([^}]+)\}");
        if (!structMatch.Success)
            return 0;

        var body = structMatch.Groups[1].Value;
        var locationPattern = @"@location\s*\(\s*(\d+)\s*\)\s+\w+\s*:\s*(\w+)";
        var matches = Regex.Matches(body, locationPattern);

        Span<(int location, int components, VertexAttribType type)> attrs =
            stackalloc (int, int, VertexAttribType)[matches.Count];

        for (int i = 0; i < matches.Count; i++)
        {
            var location = int.Parse(matches[i].Groups[1].Value);
            var wgslType = matches[i].Groups[2].Value;
            var (components, attribType) = MapWgslType(wgslType);
            attrs[i] = (location, components, attribType);
        }

        return VertexFormatHash.Compute(attrs);
    }

    private static (int components, VertexAttribType type) MapWgslType(string wgslType) => wgslType switch
    {
        "f32" => (1, VertexAttribType.Float),
        "i32" => (1, VertexAttribType.Int),
        "u32" => (1, VertexAttribType.Int),
        _ when wgslType.StartsWith("vec2") && wgslType.Contains("f32") => (2, VertexAttribType.Float),
        _ when wgslType.StartsWith("vec2") && wgslType.Contains("i32") => (2, VertexAttribType.Int),
        _ when wgslType.StartsWith("vec3") && wgslType.Contains("f32") => (3, VertexAttribType.Float),
        _ when wgslType.StartsWith("vec3") && wgslType.Contains("i32") => (3, VertexAttribType.Int),
        _ when wgslType.StartsWith("vec4") && wgslType.Contains("f32") => (4, VertexAttribType.Float),
        _ when wgslType.StartsWith("vec4") && wgslType.Contains("i32") => (4, VertexAttribType.Int),
        _ when wgslType.StartsWith("vec2") => (2, VertexAttribType.Float),
        _ when wgslType.StartsWith("vec3") => (3, VertexAttribType.Float),
        _ when wgslType.StartsWith("vec4") => (4, VertexAttribType.Float),
        _ => (1, VertexAttribType.Float),
    };

    private ShaderFlags GetShaderFlags()
    {
        var flags = ShaderFlags.None;
        if (Blend) flags |= ShaderFlags.Blend;
        if (Depth) flags |= ShaderFlags.Depth;
        if (DepthLess) flags |= ShaderFlags.DepthLess;
        if (PremultipliedAlpha) flags |= ShaderFlags.PremultipliedAlpha;
        return flags;
    }

    private static string ProcessIncludes(string source, string baseDir)
    {
        var result = new StringBuilder();
        var lines = source.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#include"))
            {
                var quote1 = trimmed.IndexOf('"');
                var quote2 = trimmed.LastIndexOf('"');
                if (quote1 >= 0 && quote2 > quote1)
                {
                    var filename = trimmed.Substring(quote1 + 1, quote2 - quote1 - 1);
                    var includePath = System.IO.Path.Combine(baseDir, filename);

                    if (File.Exists(includePath))
                    {
                        var includeContent = File.ReadAllText(includePath);
                        var includeDir = System.IO.Path.GetDirectoryName(includePath) ?? baseDir;
                        result.AppendLine(ProcessIncludes(includeContent, includeDir));
                    }
                    else
                    {
                        Log.Error($"Could not open include file: {includePath}");
                    }
                    continue;
                }
            }
            result.AppendLine(line);
        }

        return result.ToString();
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconShader);
        }
    }
}
