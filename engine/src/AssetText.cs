using System.Globalization;
using System.Numerics;
using System.Text;

namespace NoZ;

/// <summary>Strict, culture-independent values and blocks for tokenizer-based asset sources.</summary>
public ref struct AssetTextReader
{
    private Tokenizer _tokens;
    private readonly string _text;
    private readonly Stack<HashSet<string>> _fields = new();
    public AssetTextReader(string text)
    {
        _text = text;
        _tokens = new(text);
        _fields.Push(new(StringComparer.Ordinal));
    }
    public void Begin()
    {
        if (!_tokens.ExpectDelimiter('{')) throw Error("Expected '{'");
        if (_fields.Count >= 32) throw Error("Too many nested blocks");
        _fields.Push(new(StringComparer.Ordinal));
    }
    public bool Field(out string name, params string[] repeated)
    {
        name = "";
        if (_fields.Count > 1 && _tokens.ExpectDelimiter('}')) { _fields.Pop(); return false; }
        if (_tokens.IsEOF)
        {
            if (_fields.Count > 1) throw Error("Unterminated block");
            return false;
        }
        if (!_tokens.ExpectIdentifier(out name)) throw Error("Expected a field name");
        if (!repeated.Contains(name) && !_fields.Peek().Add(name)) throw Error($"Duplicate field '{name}'");
        return true;
    }
    public int Int()
    {
        if (!_tokens.ExpectToken(out var token) || token.Type != TokenType.Int ||
            !int.TryParse(_tokens.GetSpan(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw Error("Expected an integer");
        return value;
    }
    public float Float()
    {
        if (!_tokens.ExpectToken(out var token) || token.Type is not (TokenType.Int or TokenType.Float) ||
            !float.TryParse(_tokens.GetSpan(token), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
            throw Error("Expected a finite number");
        return value;
    }
    public bool Bool()
    {
        if (!_tokens.ExpectToken(out var token) || token.Type != TokenType.Bool) throw Error("Expected true or false");
        return token.BoolValue;
    }
    public string String()
    {
        if (!_tokens.ExpectToken(out var token) || token.Type != TokenType.String ||
            token.Start + token.Length >= _text.Length || _text[token.Start + token.Length] != _text[token.Start - 1])
            throw Error("Expected a closed quoted string");
        var raw = _tokens.GetSpan(token);
        var result = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\') { result.Append(raw[i]); continue; }
            if (++i >= raw.Length) throw Error("Incomplete string escape");
            if (raw[i] == 'u')
            {
                if (i + 4 >= raw.Length || !ushort.TryParse(raw.Slice(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                    throw Error("Invalid Unicode escape");
                result.Append((char)code); i += 4;
            }
            else result.Append(raw[i] switch { '\\' => '\\', '"' => '"', '\'' => '\'', 'n' => '\n', 'r' => '\r', 't' => '\t', _ => throw Error("Unknown string escape") });
        }
        return result.ToString();
    }
    public T Enum<T>() where T : struct, Enum
    {
        if (!_tokens.ExpectIdentifier(out var name) || !System.Enum.TryParse<T>(name, true, out var value) || !System.Enum.IsDefined(value))
            throw Error($"Expected {typeof(T).Name}");
        return value;
    }
    public Vector3 Vector3() => new(Float(), Float(), Float());
    public void Version(int expected = 1) { if (Int() != expected) throw Error("Unsupported source version"); }
    public InvalidDataException Unknown(string name) => Error($"Unknown field '{name}'");
    private InvalidDataException Error(string message)
    {
        var copy = _tokens;
        return copy.ExpectToken(out var token) ? new($"{message} at line {token.Line}, column {token.Column}.") : new($"{message} at end of asset.");
    }
}

public sealed class AssetTextWriter
{
    private readonly StringBuilder _text = new();
    private int _depth;
    public void Begin(string name) { Line(name + " {"); _depth++; }
    public void End() { _depth--; Line("}"); }
    public void Field(string name, params object[] values) => Line(name + " " + string.Join(" ", values.Select(Format)));
    private void Line(string text) => _text.Append(' ', _depth * 4).Append(text).Append('\n');
    private static string Format(object value) => value switch
    {
        string s => Quote(s), bool b => b ? "true" : "false",
        float f when float.IsFinite(f) => f.ToString("R", CultureInfo.InvariantCulture),
        float => throw new InvalidDataException("Asset numbers must be finite."),
        Vector3 v => $"{Format(v.X)} {Format(v.Y)} {Format(v.Z)}",
        Enum e => e.ToString().ToLowerInvariant(),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => throw new InvalidDataException($"Unsupported asset value: {value.GetType().Name}"),
    };
    public static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var c in value)
            text.Append(c switch { '\\' => "\\\\", '"' => "\\\"", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", _ => char.IsControl(c) ? "\\u" + ((int)c).ToString("x4") : c.ToString() });
        return text.Append('"').ToString();
    }
    public override string ToString() => _text.ToString();
}
