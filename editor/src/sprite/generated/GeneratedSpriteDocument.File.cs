//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class GeneratedSpriteDocument
{
    protected override void SaveTypeHeader(StreamWriter writer)
    {
        writer.WriteLine("type generated");
    }

    protected override bool TryLoadContentToken(ref Tokenizer tk)
    {
        if (tk.ExpectIdentifier("generate"))
        {
            ParseGeneration(ref tk);
            return true;
        }
        // Generated sprites may also have vector paths as references
        if (tk.ExpectIdentifier("group"))
        {
            VectorSpriteDocument.ParsePath(ref tk, Root);
            return true;
        }
        if (tk.ExpectIdentifier("path"))
        {
            VectorSpriteDocument.ParsePath(ref tk, Root);
            return true;
        }
        return false;
    }

    protected override void SaveContent(StreamWriter writer)
    {
        // Save vector paths if any exist (as references)
        writer.WriteLine();
        foreach (var child in Root.Children)
        {
            if (child is SpritePath path)
                VectorSpriteDocument.SavePath(writer, path, 0);
            else if (child is SpriteGroup group)
                VectorSpriteDocument.SaveGroup(writer, group, 0);
        }

        SaveGeneration(writer);
    }

    private void ParseGeneration(ref Tokenizer tk)
    {
        var gen = Generation;
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("prompt"))
                gen.Prompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                gen.NegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("seed"))
            {
                if (tk.ExpectQuotedString(out var seedStr))
                    gen.Seed = seedStr;
                else
                    gen.Seed = tk.ExpectInt().ToString();
            }
            else if (tk.ExpectIdentifier("style"))
                gen.Config.Name = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("prompt_hash"))
                tk.ExpectQuotedString(); // Legacy: skip
            else if (tk.ExpectIdentifier("reference"))
                tk.ExpectQuotedString(); // Legacy: skip
            else if (tk.ExpectIdentifier("image"))
            {
                // Legacy migration: extract embedded base64 to companion file
                var base64 = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(base64))
                {
                    gen.Job.ImageData = Convert.FromBase64String(base64);

                    if (ImageFilePath == null)
                    {
                        var dir = System.IO.Path.GetDirectoryName(Path) ?? "";
                        var stem = System.IO.Path.GetFileNameWithoutExtension(Path);
                        ImageFilePath = System.IO.Path.Combine(dir, stem + ".png");
                    }

                    if (!File.Exists(ImageFilePath))
                        File.WriteAllBytes(ImageFilePath, gen.Job.ImageData);
                }
            }
            else
                break;
        }

        if (!gen.Job.HasImageData && ImageFilePath != null && File.Exists(ImageFilePath))
            gen.Job.ImageData = File.ReadAllBytes(ImageFilePath);
    }

    private void SaveGeneration(StreamWriter writer)
    {
        var gen = Generation;
        writer.WriteLine("generate");
        if (!string.IsNullOrEmpty(gen.Prompt))
            writer.WriteLine($"prompt \"{gen.Prompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(gen.NegativePrompt))
            writer.WriteLine($"prompt_neg \"{gen.NegativePrompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(gen.Seed))
            writer.WriteLine($"seed \"{gen.Seed}\"");
        if (!string.IsNullOrEmpty(gen.Config.Name))
            writer.WriteLine($"style \"{gen.Config.Name}\"");
        if (gen.Job.HasImageData && ImageFilePath != null)
            File.WriteAllBytes(ImageFilePath, gen.Job.ImageData!);
    }
}
