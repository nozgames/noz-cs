//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor
{
    internal class AtlasDocument : Document
    {
        private struct SpriteRect
        {
            public string Name;
            public RectInt Rect;
            public int FrameCount;
        }

        private readonly List<SpriteRect> _rects = new(128);
        private int _dpi;

        public static void RegisterDef()
        {
            DocumentManager.RegisterDef(new DocumentDef(
                AssetType.Shader,
                ".atlas",
                () => new AtlasDocument()
            ));
        }

        private void Load(ref Tokenizer tk)
        {
            var size = new Vector2Int(EditorApplication.Config!.AtlasSize, EditorApplication.Config!.AtlasSize);
            while (!tk.IsEOF)
            {
                if (tk.ExpectIdentifier("w"))
                {
                    size.X = tk.ExpectInt();
                }
                else if (tk.ExpectIdentifier("h"))
                {
                    size.Y = tk.ExpectInt();
                }
                else if (tk.ExpectIdentifier("d"))
                {
                    _dpi = tk.ExpectInt();
                }
                else if (tk.ExpectIdentifier("r"))
                {
                    var name = tk.ExpectQuotedString();

                    RectInt rect;
                    rect.X = tk.ExpectInt();
                    rect.Y = tk.ExpectInt();
                    rect.Width = tk.ExpectInt();
                    rect.Height = tk.ExpectInt();

                    int frameCount = tk.ExpectInt(1);

                    if (!string.IsNullOrEmpty(name))
                        _rects.Add(new SpriteRect { Name = name, Rect = rect, FrameCount = frameCount });
                }
                else
                {
                    throw new Exception();
                }
            }
        }

        public override void Load()
        {
            var contents = File.ReadAllText(Path);
            var tk = new Tokenizer(contents);
            Load(ref tk);
        }

        public override void Save(string path)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine($"w {EditorApplication.Config!.AtlasSize}");
            sw.WriteLine($"h {EditorApplication.Config!.AtlasSize}");
            sw.WriteLine();
            foreach (var rect in _rects)
                sw.WriteLine($"rect \"{rect.Name}\" {rect.Rect.X} {rect.Rect.Y} {rect.Rect.Width} {rect.Rect.Height}");
        }
    }
}
