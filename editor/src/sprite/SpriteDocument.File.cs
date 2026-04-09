//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract partial class SpriteDocument
{
    private void Load(ref Tokenizer tk)
    {
        Root.Clear();

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("type"))
            {
                // Type line is consumed during factory creation; skip here
                tk.ExpectIdentifier();
            }
            else if (tk.ExpectIdentifier("edges"))
            {
                if (tk.ExpectVec4(out var edgesVec))
                    Edges = new EdgeInsets(edgesVec.X, edgesVec.Y, edgesVec.Z, edgesVec.W);
            }
            else if (tk.ExpectIdentifier("skeleton"))
                Skeleton.Name = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("bone"))
                BoneName = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("sort"))
                SortOrderId = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("animated"))
                IsAnimated = true;
            else if (!TryLoadContentToken(ref tk))
            {
                tk.ExpectToken(out var badToken);
                ReportError(badToken.Line, $"Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }
    }

    protected virtual bool TryLoadContentToken(ref Tokenizer tk) => false;

    private static void SkipOldAnimFrame(ref Tokenizer tk)
    {
        tk.ExpectDelimiter('{');
        var depth = 1;
        while (!tk.IsEOF && depth > 0)
        {
            if (tk.ExpectDelimiter('{'))
                depth++;
            else if (tk.ExpectDelimiter('}'))
                depth--;
            else
                tk.ExpectToken(out _);
        }
    }

    public override void Save(StreamWriter writer)
    {
        SaveTypeHeader(writer);

        if (!Edges.IsZero)
            writer.WriteLine($"edges ({Edges.T},{Edges.L},{Edges.B},{Edges.R})");

        if (Skeleton.HasValue)
            writer.WriteLine($"skeleton \"{Skeleton.Name}\"");

        if (BoneName != null)
            writer.WriteLine($"bone \"{BoneName}\"");

        if (SortOrderId != null)
            writer.WriteLine($"sort \"{SortOrderId}\"");

        if (IsAnimated)
            writer.WriteLine("animated");

        SaveContent(writer);
    }

    protected virtual void SaveTypeHeader(StreamWriter writer) { }

    protected static string FormatColor(Color32 c)
    {
        if (c.A < 255)
            return $"rgba({c.R},{c.G},{c.B},{c.A / 255f:G})";
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
