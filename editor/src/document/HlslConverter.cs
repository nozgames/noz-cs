//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;
using System.Text.RegularExpressions;

namespace NoZ.Editor;

public static class HlslConverter
{
    private record struct Variable(string Type, string Name, int Location, bool Flat);
    private record struct UniformBlock(string Name, string InstanceName, int Binding, string Body, bool RowMajor);
    private record struct Sampler(string Type, string Name, int Binding);

    public static string ConvertVertex(string glsl)
    {
        var inputs = ParseLayoutInputs(glsl);
        var outputs = ParseOutputs(glsl);
        var uniformBlocks = ParseUniformBlocks(glsl);
        var standaloneUniforms = ParseStandaloneUniforms(glsl);
        var samplers = ParseSamplers(glsl);
        var mainBody = ExtractMainBody(glsl);

        var sb = new StringBuilder();

        // Constant buffers from uniform blocks
        var cbufferIndex = 0;
        foreach (var block in uniformBlocks)
        {
            sb.AppendLine($"cbuffer {block.Name} : register(b{cbufferIndex++}) {{");
            var convertedBody = ConvertUniformBlockBody(block.Body, block.RowMajor);
            sb.AppendLine(convertedBody);
            sb.AppendLine("};");
            sb.AppendLine();
        }

        // Standalone uniforms as cbuffer
        if (standaloneUniforms.Count > 0)
        {
            sb.AppendLine($"cbuffer Globals : register(b{cbufferIndex++}) {{");
            foreach (var u in standaloneUniforms)
            {
                sb.AppendLine($"    {ConvertType(u.Type)} {u.Name};");
            }
            sb.AppendLine("};");
            sb.AppendLine();
        }

        // Textures and samplers
        var textureIndex = 0;
        foreach (var sampler in samplers)
        {
            var hlslType = sampler.Type switch
            {
                "sampler2D" => "Texture2D",
                "sampler2DArray" => "Texture2DArray",
                _ => "Texture2D"
            };
            sb.AppendLine($"{hlslType} {sampler.Name} : register(t{textureIndex});");
            sb.AppendLine($"SamplerState {sampler.Name}Sampler : register(s{textureIndex});");
            textureIndex++;
        }
        if (samplers.Count > 0) sb.AppendLine();

        // VSInput struct
        sb.AppendLine("struct VSInput {");
        foreach (var input in inputs)
        {
            var semantic = GetInputSemantic(input.Name, input.Location);
            sb.AppendLine($"    {ConvertType(input.Type)} {input.Name} : {semantic};");
        }
        sb.AppendLine("};");
        sb.AppendLine();

        // VSOutput struct
        sb.AppendLine("struct VSOutput {");
        sb.AppendLine("    float4 position : SV_Position;");
        foreach (var output in outputs)
        {
            var semantic = GetOutputSemantic(output.Name, output.Location);
            var interp = output.Flat ? "nointerpolation " : "";
            sb.AppendLine($"    {interp}{ConvertType(output.Type)} {output.Name} : {semantic};");
        }
        sb.AppendLine("};");
        sb.AppendLine();

        // Main function
        sb.AppendLine("VSOutput main(VSInput input) {");
        sb.AppendLine("    VSOutput output;");

        var convertedMain = ConvertMainBody(mainBody, inputs, outputs, uniformBlocks, samplers, true);
        sb.AppendLine(convertedMain);

        sb.AppendLine("    return output;");
        sb.AppendLine("}");

        return sb.ToString();
    }

    public static string ConvertFragment(string glsl)
    {
        var inputs = ParseFragmentInputs(glsl);
        var outputs = ParseFragmentOutputs(glsl);
        var uniformBlocks = ParseUniformBlocks(glsl);
        var standaloneUniforms = ParseStandaloneUniforms(glsl);
        var samplers = ParseSamplers(glsl);
        var mainBody = ExtractMainBody(glsl);

        var sb = new StringBuilder();

        // Constant buffers from uniform blocks
        var cbufferIndex = 0;
        foreach (var block in uniformBlocks)
        {
            sb.AppendLine($"cbuffer {block.Name} : register(b{cbufferIndex++}) {{");
            var convertedBody = ConvertUniformBlockBody(block.Body, block.RowMajor);
            sb.AppendLine(convertedBody);
            sb.AppendLine("};");
            sb.AppendLine();
        }

        // Standalone uniforms as cbuffer
        if (standaloneUniforms.Count > 0)
        {
            sb.AppendLine($"cbuffer Globals : register(b{cbufferIndex++}) {{");
            foreach (var u in standaloneUniforms)
            {
                sb.AppendLine($"    {ConvertType(u.Type)} {u.Name};");
            }
            sb.AppendLine("};");
            sb.AppendLine();
        }

        // Textures and samplers
        var textureIndex = 0;
        foreach (var sampler in samplers)
        {
            var hlslType = sampler.Type switch
            {
                "sampler2D" => "Texture2D",
                "sampler2DArray" => "Texture2DArray",
                _ => "Texture2D"
            };
            sb.AppendLine($"{hlslType} {sampler.Name} : register(t{textureIndex});");
            sb.AppendLine($"SamplerState {sampler.Name}Sampler : register(s{textureIndex});");
            textureIndex++;
        }
        if (samplers.Count > 0) sb.AppendLine();

        // PSInput struct (matches VSOutput)
        sb.AppendLine("struct PSInput {");
        sb.AppendLine("    float4 position : SV_Position;");
        foreach (var input in inputs)
        {
            var semantic = GetOutputSemantic(input.Name, input.Location);
            var interp = input.Flat ? "nointerpolation " : "";
            sb.AppendLine($"    {interp}{ConvertType(input.Type)} {input.Name} : {semantic};");
        }
        sb.AppendLine("};");
        sb.AppendLine();

        // Main function
        sb.AppendLine("float4 main(PSInput input) : SV_Target {");

        // Declare output variable(s)
        foreach (var output in outputs)
        {
            sb.AppendLine($"    {ConvertType(output.Type)} {output.Name};");
        }

        var convertedMain = ConvertMainBody(mainBody, inputs, outputs, uniformBlocks, samplers, false);
        sb.AppendLine(convertedMain);

        // Return the first output (typically f_color or fragColor)
        if (outputs.Count > 0)
        {
            sb.AppendLine($"    return {outputs[0].Name};");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static List<Variable> ParseLayoutInputs(string glsl)
    {
        var inputs = new List<Variable>();
        var pattern = new Regex(@"layout\s*\([^)]*location\s*=\s*(\d+)[^)]*\)\s*in\s+(\w+)\s+(\w+)\s*;");

        foreach (Match match in pattern.Matches(glsl))
        {
            var location = int.Parse(match.Groups[1].Value);
            var type = match.Groups[2].Value;
            var name = match.Groups[3].Value;
            inputs.Add(new Variable(type, name, location, false));
        }

        return inputs.OrderBy(i => i.Location).ToList();
    }

    private static List<Variable> ParseOutputs(string glsl)
    {
        var outputs = new List<Variable>();

        // With layout location
        var layoutPattern = new Regex(@"layout\s*\([^)]*location\s*=\s*(\d+)[^)]*\)\s*(flat\s+)?out\s+(\w+)\s+(\w+)\s*;");
        foreach (Match match in layoutPattern.Matches(glsl))
        {
            var location = int.Parse(match.Groups[1].Value);
            var flat = !string.IsNullOrEmpty(match.Groups[2].Value);
            var type = match.Groups[3].Value;
            var name = match.Groups[4].Value;
            outputs.Add(new Variable(type, name, location, flat));
        }

        // Without layout location
        var simplePattern = new Regex(@"(?<!layout[^;]*)(flat\s+)?out\s+(\w+)\s+(\w+)\s*;");
        var locationCounter = outputs.Count > 0 ? outputs.Max(o => o.Location) + 1 : 0;
        foreach (Match match in simplePattern.Matches(glsl))
        {
            var flat = !string.IsNullOrEmpty(match.Groups[1].Value);
            var type = match.Groups[2].Value;
            var name = match.Groups[3].Value;
            if (!outputs.Any(o => o.Name == name))
            {
                outputs.Add(new Variable(type, name, locationCounter++, flat));
            }
        }

        return outputs.OrderBy(o => o.Location).ToList();
    }

    private static List<Variable> ParseFragmentInputs(string glsl)
    {
        var inputs = new List<Variable>();

        // With layout location
        var layoutPattern = new Regex(@"layout\s*\([^)]*location\s*=\s*(\d+)[^)]*\)\s*(flat\s+)?in\s+(\w+)\s+(\w+)\s*;");
        foreach (Match match in layoutPattern.Matches(glsl))
        {
            var location = int.Parse(match.Groups[1].Value);
            var flat = !string.IsNullOrEmpty(match.Groups[2].Value);
            var type = match.Groups[3].Value;
            var name = match.Groups[4].Value;
            inputs.Add(new Variable(type, name, location, flat));
        }

        // Without layout location
        var simplePattern = new Regex(@"(?<!layout[^;]*)(flat\s+)?in\s+(\w+)\s+(\w+)\s*;");
        var locationCounter = inputs.Count > 0 ? inputs.Max(i => i.Location) + 1 : 0;
        foreach (Match match in simplePattern.Matches(glsl))
        {
            var flat = !string.IsNullOrEmpty(match.Groups[1].Value);
            var type = match.Groups[2].Value;
            var name = match.Groups[3].Value;
            if (!inputs.Any(i => i.Name == name))
            {
                inputs.Add(new Variable(type, name, locationCounter++, flat));
            }
        }

        return inputs.OrderBy(i => i.Location).ToList();
    }

    private static List<Variable> ParseFragmentOutputs(string glsl)
    {
        var outputs = new List<Variable>();
        var pattern = new Regex(@"(?:layout\s*\([^)]*\)\s*)?out\s+(\w+)\s+(\w+)\s*;");

        var location = 0;
        foreach (Match match in pattern.Matches(glsl))
        {
            var type = match.Groups[1].Value;
            var name = match.Groups[2].Value;
            outputs.Add(new Variable(type, name, location++, false));
        }

        return outputs;
    }

    private static List<UniformBlock> ParseUniformBlocks(string glsl)
    {
        var blocks = new List<UniformBlock>();
        var pattern = new Regex(
            @"layout\s*\(([^)]*)\)\s*uniform\s+(\w+)\s*\{([^}]*)\}\s*(\w*)\s*;",
            RegexOptions.Singleline);

        foreach (Match match in pattern.Matches(glsl))
        {
            var layoutParams = match.Groups[1].Value;
            var blockName = match.Groups[2].Value;
            var body = match.Groups[3].Value;
            var instanceName = match.Groups[4].Value;

            var bindingMatch = Regex.Match(layoutParams, @"binding\s*=\s*(\d+)");
            var binding = bindingMatch.Success ? int.Parse(bindingMatch.Groups[1].Value) : blocks.Count;

            var rowMajor = layoutParams.Contains("row_major");

            blocks.Add(new UniformBlock(blockName, instanceName, binding, body, rowMajor));
        }

        return blocks;
    }

    private static List<Variable> ParseStandaloneUniforms(string glsl)
    {
        var uniforms = new List<Variable>();
        var pattern = new Regex(@"(?<!layout[^;]*)uniform\s+(\w+)\s+(\w+)\s*;");

        foreach (Match match in pattern.Matches(glsl))
        {
            var type = match.Groups[1].Value;
            var name = match.Groups[2].Value;
            // Skip samplers
            if (!type.Contains("sampler"))
            {
                uniforms.Add(new Variable(type, name, 0, false));
            }
        }

        return uniforms;
    }

    private static List<Sampler> ParseSamplers(string glsl)
    {
        var samplers = new List<Sampler>();

        // With layout
        var layoutPattern = new Regex(@"layout\s*\([^)]*(?:binding\s*=\s*(\d+))?[^)]*\)\s*uniform\s+(sampler\w+)\s+(\w+)\s*;");
        foreach (Match match in layoutPattern.Matches(glsl))
        {
            var binding = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : samplers.Count;
            var type = match.Groups[2].Value;
            var name = match.Groups[3].Value;
            samplers.Add(new Sampler(type, name, binding));
        }

        // Without layout
        var simplePattern = new Regex(@"(?<!layout[^;]*)uniform\s+(sampler\w+)\s+(\w+)\s*;");
        foreach (Match match in simplePattern.Matches(glsl))
        {
            var type = match.Groups[1].Value;
            var name = match.Groups[2].Value;
            if (!samplers.Any(s => s.Name == name))
            {
                samplers.Add(new Sampler(type, name, samplers.Count));
            }
        }

        return samplers;
    }

    private static string ExtractMainBody(string glsl)
    {
        var pattern = new Regex(@"void\s+main\s*\(\s*\)\s*\{([\s\S]*)\}", RegexOptions.RightToLeft);
        var match = pattern.Match(glsl);
        if (!match.Success) return "";

        var body = match.Groups[1].Value;

        // Find matching brace
        var depth = 1;
        var endIndex = 0;
        for (var i = 0; i < body.Length && depth > 0; i++)
        {
            if (body[i] == '{') depth++;
            else if (body[i] == '}') depth--;
            if (depth > 0) endIndex = i;
        }

        return body.Substring(0, endIndex + 1);
    }

    private static string ConvertUniformBlockBody(string body, bool rowMajor)
    {
        var lines = body.Split('\n');
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var converted = ConvertTypes(trimmed);
            if (rowMajor && (converted.Contains("float3x3") || converted.Contains("float4x4")))
            {
                converted = "    row_major " + converted.TrimStart();
            }
            else
            {
                converted = "    " + converted.TrimStart();
            }
            sb.AppendLine(converted);
        }

        return sb.ToString().TrimEnd();
    }

    private static string ConvertMainBody(string body, List<Variable> inputs, List<Variable> outputs,
        List<UniformBlock> uniformBlocks, List<Sampler> samplers, bool isVertex)
    {
        var result = body;

        // Convert types
        result = ConvertTypes(result);

        // Convert functions
        result = ConvertFunctions(result, samplers);

        // Replace gl_Position with output.position
        result = Regex.Replace(result, @"\bgl_Position\b", "output.position");

        // Replace input variable references (but not output names)
        var outputNames = outputs.Select(o => o.Name).ToHashSet();
        foreach (var input in inputs)
        {
            if (!outputNames.Contains(input.Name))
            {
                result = Regex.Replace(result, $@"\b{input.Name}\b", $"input.{input.Name}");
            }
        }

        // Replace output variable references (vertex shader only)
        if (isVertex)
        {
            foreach (var output in outputs)
            {
                result = Regex.Replace(result, $@"\b{output.Name}\b", $"output.{output.Name}");
            }
        }
        // Fragment shader: output variables are declared as locals, return added at end

        // Replace uniform block instance references
        foreach (var block in uniformBlocks)
        {
            if (!string.IsNullOrEmpty(block.InstanceName))
            {
                result = Regex.Replace(result, $@"\b{block.InstanceName}\.", "");
            }
        }

        // For fragment shaders, convert bare "return;" to "return outputVar;"
        if (!isVertex && outputs.Count > 0)
        {
            result = Regex.Replace(result, @"\breturn\s*;", $"return {outputs[0].Name};");
        }

        // Indent
        var lines = result.Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                sb.AppendLine("    " + trimmed);
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string ConvertTypes(string code)
    {
        var result = code;

        // Vector types
        result = Regex.Replace(result, @"\bvec2\b", "float2");
        result = Regex.Replace(result, @"\bvec3\b", "float3");
        result = Regex.Replace(result, @"\bvec4\b", "float4");
        result = Regex.Replace(result, @"\bivec2\b", "int2");
        result = Regex.Replace(result, @"\bivec3\b", "int3");
        result = Regex.Replace(result, @"\bivec4\b", "int4");
        result = Regex.Replace(result, @"\buvec2\b", "uint2");
        result = Regex.Replace(result, @"\buvec3\b", "uint3");
        result = Regex.Replace(result, @"\buvec4\b", "uint4");
        result = Regex.Replace(result, @"\bbvec2\b", "bool2");
        result = Regex.Replace(result, @"\bbvec3\b", "bool3");
        result = Regex.Replace(result, @"\bbvec4\b", "bool4");

        // Matrix types
        result = Regex.Replace(result, @"\bmat2\b", "float2x2");
        result = Regex.Replace(result, @"\bmat3\b", "float3x3");
        result = Regex.Replace(result, @"\bmat4\b", "float4x4");

        return result;
    }

    private static string ConvertFunctions(string code, List<Sampler> samplers)
    {
        var result = code;

        // texture() calls - need to match sampler name and replace with .Sample
        foreach (var sampler in samplers)
        {
            var pattern = $@"texture\s*\(\s*{sampler.Name}\s*,\s*([^)]+)\)";
            result = Regex.Replace(result, pattern, $"{sampler.Name}.Sample({sampler.Name}Sampler, $1)");
        }

        // Other function mappings
        result = Regex.Replace(result, @"\bmix\s*\(", "lerp(");
        result = Regex.Replace(result, @"\bfract\s*\(", "frac(");
        result = Regex.Replace(result, @"\bdFdx\s*\(", "ddx(");
        result = Regex.Replace(result, @"\bdFdy\s*\(", "ddy(");

        // mod() needs special handling - GLSL mod vs HLSL fmod have different behavior
        // GLSL: mod(x, y) = x - y * floor(x/y)
        // We'll use a simple replacement for now
        result = Regex.Replace(result, @"\bmod\s*\(\s*([^,]+)\s*,\s*([^)]+)\s*\)",
            "($1 - $2 * floor($1 / $2))");

        // Matrix multiplication: in GLSL with row_major, vec * mat order
        // This is a simplified conversion - may need refinement for complex cases
        // For now, convert mat4 * vec4 to mul(mat4, vec4)
        result = Regex.Replace(result,
            @"(\w+)\s*\*\s*(float4\s*\([^)]+\))",
            "mul($1, $2)");

        return result;
    }

    private static string ConvertType(string glslType)
    {
        return glslType switch
        {
            "vec2" => "float2",
            "vec3" => "float3",
            "vec4" => "float4",
            "ivec2" => "int2",
            "ivec3" => "int3",
            "ivec4" => "int4",
            "uvec2" => "uint2",
            "uvec3" => "uint3",
            "uvec4" => "uint4",
            "mat2" => "float2x2",
            "mat3" => "float3x3",
            "mat4" => "float4x4",
            "int" => "int",
            "uint" => "uint",
            "float" => "float",
            "bool" => "bool",
            _ => glslType
        };
    }

    private static string GetInputSemantic(string name, int location)
    {
        var lowerName = name.ToLower();

        if (lowerName.Contains("position") || lowerName.Contains("pos"))
            return "POSITION";
        if (lowerName.Contains("normal"))
            return "NORMAL";
        if (lowerName.Contains("color") || lowerName.Contains("colour"))
            return "COLOR";
        if (lowerName.Contains("bone") && lowerName.Contains("weight"))
            return "BLENDWEIGHT";
        if (lowerName.Contains("bone") || lowerName.Contains("blend"))
            return "BLENDINDICES";
        if (lowerName.Contains("uv") || lowerName.Contains("texcoord") || lowerName.Contains("tex"))
            return $"TEXCOORD{location}";

        return $"TEXCOORD{location}";
    }

    private static string GetOutputSemantic(string name, int location)
    {
        // For inter-stage varyings (vertex output / fragment input), use TEXCOORD semantics
        // COLOR semantic is only for vertex input attributes, not for varyings
        return $"TEXCOORD{location}";
    }
}
