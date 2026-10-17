using UnityEngine;
using UnityEngine.Rendering;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// スタジオ用 Cubemap をランタイム生成し、空を描かずに環境光（ambient probe）だけ効かせる。
    /// </summary>
    public static class StudioIbl
    {
        private const int FaceSize = 32;

        private static Cubemap _studio;
        private static Cubemap _warm;
        private static Cubemap _cool;
        private static Material _skybox;

        /// <summary>
        /// 最新ライティング用の環境光を適用する。カメラのクリアは呼び出し側が SolidColor のままにする。
        /// </summary>
        public static void Apply(IblPreset preset, float intensity, float ambientFlat)
        {
            Cubemap cubemap = CubemapOf(preset);
            EnsureSkybox(cubemap);

            RenderSettings.skybox = _skybox;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = Mathf.Max(0f, intensity);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cubemap;
            RenderSettings.reflectionIntensity = Mathf.Clamp01(intensity * 0.5f);

            // Flat の環境光スライダー分を SH に足して、トゥーンでも最低限の明るさを残す
            DynamicGI.UpdateEnvironment();
            if (ambientFlat > 0.001f)
            {
                SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
                Color flat = new Color(0.5f, 0.5f, 0.5f) * ambientFlat;
                probe.AddAmbientLight(flat);
                RenderSettings.ambientProbe = probe;
            }
        }

        /// <summary>
        /// レガシー用にスカイボックス参照を外し、反射を弱める。
        /// </summary>
        public static void Clear()
        {
            RenderSettings.skybox = null;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        }

        private static Cubemap CubemapOf(IblPreset preset)
        {
            switch (preset)
            {
                case IblPreset.Warm:
                    // 天・地の差を抑え、影側が茶色く沈みすぎないようにする
                    return _warm ??= BuildCubemap(new Color(1f, 0.78f, 0.55f), new Color(0.7f, 0.55f, 0.42f),
                        new Color(1f, 0.9f, 0.78f));
                case IblPreset.Cool:
                    return _cool ??= BuildCubemap(new Color(0.65f, 0.78f, 1f), new Color(0.4f, 0.45f, 0.55f),
                        new Color(0.82f, 0.9f, 1f));
                default:
                    // 床方向を明るめにして白い服のしわが灰色にくすまないようにする
                    return _studio ??= BuildCubemap(new Color(0.95f, 0.96f, 1f), new Color(0.58f, 0.57f, 0.6f),
                        new Color(1f, 0.99f, 0.98f));
            }
        }

        private static void EnsureSkybox(Cubemap cubemap)
        {
            if (_skybox == null)
            {
                Shader shader = Shader.Find("Skybox/Cubemap");
                _skybox = shader != null ? new Material(shader) : null;
            }

            if (_skybox != null)
            {
                _skybox.SetTexture("_Tex", cubemap);
            }
        }

        private static Cubemap BuildCubemap(Color zenith, Color nadir, Color horizon)
        {
            var cubemap = new Cubemap(FaceSize, TextureFormat.RGB24, false)
            {
                name = "VRCastStudioIbl",
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (int face = 0; face < 6; face++)
            {
                var colors = new Color[FaceSize * FaceSize];
                for (int y = 0; y < FaceSize; y++)
                {
                    for (int x = 0; x < FaceSize; x++)
                    {
                        Vector3 dir = DirectionOf((CubemapFace)face, x, y, FaceSize);
                        float t = dir.y * 0.5f + 0.5f;
                        Color color = t > 0.5f
                            ? Color.Lerp(horizon, zenith, (t - 0.5f) * 2f)
                            : Color.Lerp(nadir, horizon, t * 2f);
                        // 正面寄りのソフトキーを足してスタジオっぽくする（やや強めにラップ）
                        float front = Mathf.Max(0f, Vector3.Dot(dir.normalized, new Vector3(0f, 0.25f, -1f)));
                        color += horizon * (front * front * 0.45f);
                        colors[y * FaceSize + x] = color;
                    }
                }

                cubemap.SetPixels(colors, (CubemapFace)face);
            }

            cubemap.Apply(false, true);
            return cubemap;
        }

        private static Vector3 DirectionOf(CubemapFace face, int x, int y, int size)
        {
            float u = (x + 0.5f) / size * 2f - 1f;
            float v = (y + 0.5f) / size * 2f - 1f;
            switch (face)
            {
                case CubemapFace.PositiveX: return new Vector3(1f, -v, -u);
                case CubemapFace.NegativeX: return new Vector3(-1f, -v, u);
                case CubemapFace.PositiveY: return new Vector3(u, 1f, v);
                case CubemapFace.NegativeY: return new Vector3(u, -1f, -v);
                case CubemapFace.PositiveZ: return new Vector3(u, -v, 1f);
                default: return new Vector3(-u, -v, -1f);
            }
        }
    }
}
