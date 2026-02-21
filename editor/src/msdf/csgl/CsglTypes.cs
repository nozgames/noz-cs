// Minimal type stubs for CSGL dependencies used by MSDFGen.
// These replace CSGL.Math and CSGL.Graphics types.

using System;
using System.Numerics;

namespace CSGL.Math {
    public struct Rectangle {
        public float X, Y, Width, Height;

        public Rectangle(float x, float y, float width, float height) {
            X = x; Y = y; Width = width; Height = height;
        }

        public float Left => X;
        public float Top => Y;
        public float Right => X + Width;
        public float Bottom => Y + Height;
        public Vector2 Position => new Vector2(X, Y);
    }

    public struct Rectanglei {
        public int X, Y, Width, Height;

        public Rectanglei(int x, int y, int width, int height) {
            X = x; Y = y; Width = width; Height = height;
        }

        public int Left => X;
        public int Top => Y;
        public int Right => X + Width;
        public int Bottom => Y + Height;
    }
}

namespace CSGL.Graphics {
    public class Bitmap<T> where T : struct {
        public int Width { get; }
        public int Height { get; }
        public T[] Data { get; }

        public Bitmap(int width, int height) {
            Width = width;
            Height = height;
            Data = new T[width * height];
        }

        public T this[int x, int y] {
            get => Data[y * Width + x];
            set => Data[y * Width + x] = value;
        }
    }

    public struct Color3 {
        public float r, g, b;

        public Color3(float r, float g, float b) {
            this.r = r; this.g = g; this.b = b;
        }
    }

    public struct Color3b {
        public byte r, g, b;

        public Color3b(Color3 c) {
            r = (byte)System.Math.Clamp(c.r * 255f, 0f, 255f);
            g = (byte)System.Math.Clamp(c.g * 255f, 0f, 255f);
            b = (byte)System.Math.Clamp(c.b * 255f, 0f, 255f);
        }
    }

    public struct Color4 {
        public float r, g, b, a;

        public Color4(float r, float g, float b, float a) {
            this.r = r; this.g = g; this.b = b; this.a = a;
        }

        public Color4(Color4b c) {
            r = c.r / 255f; g = c.g / 255f; b = c.b / 255f; a = c.a / 255f;
        }
    }

    public struct Color4b {
        public byte r, g, b, a;

        public Color4b(Color3 c, byte a) {
            r = (byte)System.Math.Clamp(c.r * 255f, 0f, 255f);
            g = (byte)System.Math.Clamp(c.g * 255f, 0f, 255f);
            b = (byte)System.Math.Clamp(c.b * 255f, 0f, 255f);
            this.a = a;
        }
    }
}
