using UnityEngine;
using UnityEngine.UI;

namespace ProjectLEA.Manuel.UI
{
    /// <summary>
    /// Procedural sprites for the hand-rolled UI.
    ///
    /// Every panel, button, row and switch in the lobby and the settings panel is drawn with
    /// an Image that needs a sprite, and importing sprite assets by hand is not possible here
    /// (Unity cannot be launched from the shell to import them). So they are drawn pixel by
    /// pixel into a Texture2D at runtime instead.
    ///
    /// <see cref="Rounded"/> is a white rounded square with a 9-slice border, so one tiny
    /// texture scales to any size and <see cref="Image.color"/> still tints it. <see cref="Gradient"/>
    /// is a vertical gradient stretched over the backdrop, which is what stops the menus from
    /// looking like a flat grey slab.
    /// </summary>
    public static class RoundedSpriteFactory
    {
        private const int RoundedSize = 64;
        private const int SmallSize = 32;
        private const int GradientHeight = 128;

        private static Sprite _rounded;
        private static Sprite _small;
        private static Sprite _gradient;

        /// <summary>Big soft corners for panels and buttons. White, so Image.color tints it.</summary>
        public static Sprite Rounded => _rounded ??= MakeRounded(RoundedSize, 20, 100f);

        /// <summary>Tighter corners for rows, pills and switches.</summary>
        public static Sprite Small => _small ??= MakeRounded(SmallSize, 10, 100f);

        /// <summary>A vertical gradient for full-screen backdrops. Stretches to fit.</summary>
        public static Sprite Gradient => _gradient ??= MakeGradient();

        /// <summary>
        /// Draws a rounded square via a signed-distance field. The inner rectangle is the
        /// texture minus the corner radius; a pixel is inside if its distance to that inner
        /// rect, clamped to the outside quadrants, is under the radius.
        /// </summary>
        private static Sprite MakeRounded(int size, int radius, float pixelsPerUnit)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[size * size];
            float centre = (size - 1) * 0.5f;
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance from the inner rectangle, clamped so corners go circular and
                    // straight edges read as a constant negative distance.
                    float qx = Mathf.Abs(x - centre) - (half - radius);
                    float qy = Mathf.Abs(y - centre) - (half - radius);

                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                               Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float distance = outside + inside - radius;

                    // One-pixel hard edge: bilinear filtering softens it for free.
                    float alpha = Mathf.Clamp01(1f - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            // The border is the corner radius, which is what keeps corners round at any size.
            var border = new Vector4(radius, radius, radius, radius);
            var rect = new Rect(0f, 0f, size, size);

            return Sprite.Create(texture, rect, Vector2.one * 0.5f, pixelsPerUnit, 0u,
                                 SpriteMeshType.FullRect, border);
        }

        /// <summary>Top is darker than the bottom, which is what gives the menus depth.</summary>
        private static Sprite MakeGradient()
        {
            const int width = 4;

            var texture = new Texture2D(width, GradientHeight, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            var top = new Color(0.015f, 0.022f, 0.035f, 1f);
            var bottom = new Color(0.055f, 0.075f, 0.10f, 1f);

            var pixels = new Color32[width * GradientHeight];
            for (int y = 0; y < GradientHeight; y++)
            {
                // Texture2D rows run bottom to top.
                Color c = Color.Lerp(bottom, top, y / (float)(GradientHeight - 1));
                for (int x = 0; x < width; x++) pixels[y * width + x] = c;
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, width, GradientHeight),
                                 Vector2.one * 0.5f, 100f);
        }
    }
}
