using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace TheDeep.EditorTools
{
    /// <summary>
    /// Generates small (64x64) gritty placeholder textures as PNGs with point filtering, for the
    /// PS1-ish look. Deterministic seeds, so regenerating produces identical files.
    /// </summary>
    public static class ProceduralTextures
    {
        const string Folder = "Assets/_Project/Textures/Generated";
        const int Size = 64;

        public static Texture2D HullPanel() => Save("T_HullPanel", () =>
        {
            var c = new Pixels(11, new Color(0.30f, 0.32f, 0.33f));
            c.Stains(14, 5, 14, 0.12f);
            c.Noise(0.06f);
            c.Seams(0, 0);
            c.Rivets(3, 8);
            c.RustStreaks(4, new Color(0.36f, 0.2f, 0.1f));
            return c;
        });

        public static Texture2D FloorPlate() => Save("T_FloorPlate", () =>
        {
            var c = new Pixels(21, new Color(0.21f, 0.21f, 0.2f));
            c.Stains(10, 6, 16, 0.15f);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int lx = x % 8, ly = y % 8;
                bool even = (x / 8 + y / 8) % 2 == 0;
                bool bump = lx >= 2 && lx <= 5 && (even ? lx == ly : lx == 7 - ly);
                if (bump)
                {
                    c.Mul(x, y, 1.45f);
                    c.Mul(x + 1, y - 1, 0.7f);
                }
            }
            c.Noise(0.05f);
            c.Seams(0, 0);
            return c;
        });

        public static Texture2D Grating() => Save("T_Grating", () =>
        {
            var c = new Pixels(31, new Color(0.02f, 0.02f, 0.02f));
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                bool bar = x % 8 < 2 || y % 8 < 2;
                if (bar) c.Set(x, y, new Color(0.3f, 0.3f, 0.27f) * (x % 8 == 0 || y % 8 == 0 ? 1.25f : 1f));
            }
            c.Noise(0.12f);
            return c;
        });

        public static Texture2D Rust() => Save("T_RustTrim", () =>
        {
            var c = new Pixels(41, new Color(0.3f, 0.22f, 0.15f));
            c.Stains(18, 3, 10, -0.35f, new Color(0.55f, 0.28f, 0.1f));
            c.Stains(10, 3, 8, 0.2f);
            c.Noise(0.14f);
            return c;
        });

        public static Texture2D Hazard() => Save("T_Hazard", () =>
        {
            var c = new Pixels(51, Color.black);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                c.Set(x, y, (x + y) / 8 % 2 == 0 ? new Color(0.85f, 0.62f, 0.08f) : new Color(0.07f, 0.07f, 0.06f));
            c.Stains(16, 2, 7, 0.35f);
            c.Noise(0.1f);
            return c;
        });

        public static Texture2D PaintedMetal() => Save("T_PaintedMetal", () =>
        {
            var c = new Pixels(61, new Color(0.24f, 0.29f, 0.22f));
            c.Stains(12, 4, 12, 0.12f);
            c.Scratches(26, new Color(0.45f, 0.45f, 0.42f));
            c.Noise(0.05f);
            c.Seams(0, 0);
            return c;
        });

        public static Texture2D Beige() => Save("T_BeigePlastic", () =>
        {
            var c = new Pixels(71, new Color(0.66f, 0.62f, 0.52f));
            c.Stains(8, 4, 12, 0.08f);
            c.Noise(0.03f);
            return c;
        });

        public static Texture2D Wood() => Save("T_DeskWood", () =>
        {
            var c = new Pixels(81, new Color(0.3f, 0.21f, 0.14f));
            const float tau = Mathf.PI * 2f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float grain = Mathf.Sin(y * tau / Size * 13f + 2.2f * Mathf.Sin(x * tau / Size * 2f + y * 0.15f));
                c.Mul(x, y, 1f + 0.1f * grain);
            }
            c.Noise(0.05f);
            return c;
        });

        public static Texture2D Locker() => Save("T_Locker", () =>
        {
            var c = new Pixels(91, new Color(0.25f, 0.3f, 0.36f));
            c.Stains(8, 4, 10, 0.12f);
            c.Scratches(10, new Color(0.5f, 0.5f, 0.5f));
            for (int y = 46; y < 58; y += 3)
            for (int x = 18; x < 46; x++)
            {
                c.Set(x, y, new Color(0.04f, 0.04f, 0.05f));
                c.Mul(x, y + 1, 1.3f);
            }
            c.Noise(0.05f);
            c.Seams(0, 0);
            return c;
        });

        public static Texture2D Cork() => Save("T_Cork", () =>
        {
            var c = new Pixels(101, new Color(0.52f, 0.37f, 0.22f));
            c.Noise(0.25f);
            c.Stains(20, 1, 3, 0.35f);
            return c;
        });

        public static Texture2D HullExterior() => Save("T_HullExterior", () =>
        {
            var c = new Pixels(111, new Color(0.62f, 0.34f, 0.1f));
            c.Stains(16, 5, 16, 0.25f);
            c.Stains(10, 3, 9, -0.5f, new Color(0.3f, 0.16f, 0.07f));
            c.Scratches(30, new Color(0.35f, 0.33f, 0.3f));
            c.Noise(0.07f);
            c.Seams(0, 0);
            c.Rivets(3, 8);
            c.RustStreaks(6, new Color(0.25f, 0.12f, 0.05f));
            return c;
        });

        public static Texture2D Silt() => Save("T_Silt", () =>
        {
            var c = new Pixels(121, new Color(0.26f, 0.27f, 0.24f));
            c.Stains(30, 3, 12, 0.18f);
            c.Stains(20, 1, 3, -0.4f, new Color(0.4f, 0.4f, 0.36f));
            c.Noise(0.12f);
            return c;
        });

        public static Texture2D Rock() => Save("T_Rock", () =>
        {
            var c = new Pixels(131, new Color(0.2f, 0.2f, 0.21f));
            c.Stains(24, 2, 10, 0.3f);
            c.Stains(12, 2, 6, -0.3f, new Color(0.3f, 0.32f, 0.28f));
            c.Scratches(40, new Color(0.08f, 0.08f, 0.08f));
            c.Noise(0.15f);
            return c;
        });

        static Texture2D Save(string name, Func<Pixels> generate)
        {
            Directory.CreateDirectory(Folder);
            string path = $"{Folder}/{name}.png";
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels(generate().Data);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Tiny wrap-around pixel canvas with some grime-painting helpers.</summary>
        class Pixels
        {
            public readonly Color[] Data = new Color[Size * Size];
            readonly Random rng;

            public Pixels(int seed, Color fill)
            {
                rng = new Random(seed);
                for (int i = 0; i < Data.Length; i++) Data[i] = fill;
            }

            float Rand() => (float)rng.NextDouble();
            int RandInt(int min, int max) => rng.Next(min, max);
            static int Wrap(int v) => ((v % Size) + Size) % Size;

            public Color Get(int x, int y) => Data[Wrap(y) * Size + Wrap(x)];

            public void Set(int x, int y, Color c)
            {
                c.a = 1f;
                Data[Wrap(y) * Size + Wrap(x)] = c;
            }

            public void Mul(int x, int y, float k) => Set(x, y, Get(x, y) * k);

            public void Noise(float amount)
            {
                for (int i = 0; i < Data.Length; i++)
                {
                    Color c = Data[i] * (1f + (Rand() - 0.5f) * 2f * amount);
                    c.a = 1f;
                    Data[i] = c;
                }
            }

            /// <summary>Soft round blotches. Positive darken darkens; negative with a tint blends toward the tint.</summary>
            public void Stains(int count, int minRadius, int maxRadius, float darken, Color? tint = null)
            {
                for (int n = 0; n < count; n++)
                {
                    int cx = RandInt(0, Size), cy = RandInt(0, Size), r = RandInt(minRadius, maxRadius + 1);
                    for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                        if (d > 1f) continue;
                        float w = (1f - d) * (1f - d);
                        Color c = Get(cx + dx, cy + dy);
                        c = tint.HasValue
                            ? Color.Lerp(c, tint.Value, w * Mathf.Abs(darken))
                            : c * (1f - darken * w);
                        Set(cx + dx, cy + dy, c);
                    }
                }
            }

            /// <summary>Panel seam along the tile edge: dark groove with a lit lip.</summary>
            public void Seams(int x0, int y0)
            {
                for (int i = 0; i < Size; i++)
                {
                    Mul(x0, i, 0.5f);
                    Mul(x0 + 1, i, 1.2f);
                    Mul(i, y0, 0.5f);
                    Mul(i, y0 - 1, 1.2f);
                }
            }

            public void Rivets(int inset, int spacing)
            {
                for (int i = spacing / 2; i < Size; i += spacing)
                {
                    foreach (var (x, y) in new[] { (inset, i), (i, Size - inset) })
                    {
                        Mul(x, y, 1.45f);
                        Mul(x + 1, y - 1, 0.6f);
                    }
                }
            }

            public void RustStreaks(int count, Color rust)
            {
                for (int n = 0; n < count; n++)
                {
                    int x = RandInt(4, Size - 4);
                    int top = RandInt(Size / 2, Size - 3);
                    int length = RandInt(10, 34);
                    for (int i = 0; i < length; i++)
                    {
                        float k = 0.55f * (1f - (float)i / length);
                        Set(x, top - i, Color.Lerp(Get(x, top - i), rust, k));
                        if (Rand() < 0.5f) Set(x + 1, top - i, Color.Lerp(Get(x + 1, top - i), rust, k * 0.5f));
                    }
                }
            }

            public void Scratches(int count, Color bare)
            {
                for (int n = 0; n < count; n++)
                {
                    int x = RandInt(0, Size), y = RandInt(0, Size), len = RandInt(2, 7);
                    int dx = RandInt(-1, 2), dy = dx == 0 ? 1 : RandInt(-1, 2);
                    for (int i = 0; i < len; i++) Set(x + dx * i, y + dy * i, Color.Lerp(Get(x + dx * i, y + dy * i), bare, 0.6f));
                }
            }
        }
    }
}
