using UnityEngine;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// アンチエイリアス（MSAA / FXAA / 高解像度描画）。方式ごとに処理を分け、MSAA 時に FXAA でぼかさない。
    /// 仮想カメラ等の OnRenderImage でも効くよう、必要なら一時 RT へ描いてから渡す。
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class CameraAntiAliasing : MonoBehaviour
    {
        private AppSettings _settings;
        private Material _fxaaMaterial;
        private RenderTexture _sceneRt;
        private UnityEngine.Camera _camera;

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
            _camera = GetComponent<UnityEngine.Camera>();
            Apply();
        }

        /// <summary>
        /// 設定を品質・一時 RT・コンポーネントの有効状態へ反映する。
        /// </summary>
        public void Apply()
        {
            if (_settings == null)
            {
                return;
            }

            AntiAliasingMode mode = EffectiveMode();
            bool active = mode != AntiAliasingMode.Off && mode != AntiAliasingMode.Unspecified;
            enabled = active;

            // バックバッファ側の MSAA（一時 RT を使うときは RT 側で掛ける）
            if (mode == AntiAliasingMode.Msaa)
            {
                QualitySettings.antiAliasing = MsaaSamples();
            }
            else
            {
                QualitySettings.antiAliasing = 0;
            }

            if (_camera != null)
            {
                _camera.allowMSAA = mode == AntiAliasingMode.Msaa;
            }

            if (!active)
            {
                ReleaseSceneRt();
            }
            else if (mode == AntiAliasingMode.Fxaa)
            {
                EnsureMaterial();
            }
        }

        private void OnDisable()
        {
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }

            ReleaseSceneRt();
        }

        private void OnDestroy()
        {
            if (_fxaaMaterial != null)
            {
                Destroy(_fxaaMaterial);
                _fxaaMaterial = null;
            }

            ReleaseSceneRt();
        }

        private void OnPreCull()
        {
            if (_settings == null || _camera == null)
            {
                return;
            }

            AntiAliasingMode mode = EffectiveMode();
            if (mode != AntiAliasingMode.Msaa && mode != AntiAliasingMode.Supersample)
            {
                _camera.targetTexture = null;
                return;
            }

            float scale = mode == AntiAliasingMode.Supersample ? SupersampleScale() : 1f;
            int samples = mode == AntiAliasingMode.Msaa ? MsaaSamples() : 1;
            int width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale));
            EnsureSceneRt(width, height, samples);
            _camera.targetTexture = _sceneRt;
        }

        private void OnPostRender()
        {
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            AntiAliasingMode mode = EffectiveMode();
            if (mode == AntiAliasingMode.Fxaa && EnsureMaterial())
            {
                Graphics.Blit(source, destination, _fxaaMaterial, 0);
                return;
            }

            // MSAA の resolve・高解像度からの縮小は Blit で行う（ぼかす FXAA は掛けない）
            Graphics.Blit(source, destination);
        }

        /// <summary>
        /// アバター描画の内部解像度倍率（高解像度描画以外は 1）。
        /// </summary>
        public static float RenderScaleOf(AppSettings settings)
        {
            if (settings == null || EffectiveModeOf(settings) != AntiAliasingMode.Supersample)
            {
                return 1f;
            }

            switch (settings.antiAliasing)
            {
                case AntiAliasingQuality.x2: return 1.25f;
                case AntiAliasingQuality.x8: return 2f;
                default: return 1.5f;
            }
        }

        /// <summary>
        /// アバター描画の内部解像度（ウィンドウサイズ × 倍率）。
        /// </summary>
        public static void RenderSizeOf(AppSettings settings, out int width, out int height)
        {
            float scale = RenderScaleOf(settings);
            width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale));
        }

        private static AntiAliasingMode EffectiveModeOf(AppSettings settings)
        {
            return settings.antiAliasingMode == AntiAliasingMode.Unspecified
                ? AntiAliasingMode.Msaa
                : settings.antiAliasingMode;
        }

        private AntiAliasingMode EffectiveMode()
        {
            return _settings == null ? AntiAliasingMode.Off : EffectiveModeOf(_settings);
        }

        private int MsaaSamples()
        {
            int samples = (int)_settings.antiAliasing;
            return samples == 2 || samples == 4 || samples == 8 ? samples : 4;
        }

        private float SupersampleScale()
        {
            return RenderScaleOf(_settings);
        }

        private void EnsureSceneRt(int width, int height, int samples)
        {
            int aa = Mathf.Max(1, samples);
            if (_sceneRt != null && _sceneRt.width == width && _sceneRt.height == height
                && _sceneRt.antiAliasing == aa)
            {
                return;
            }

            ReleaseSceneRt();
            _sceneRt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "VRCast AntiAliasing",
                antiAliasing = aa,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _sceneRt.Create();
        }

        private void ReleaseSceneRt()
        {
            if (_sceneRt == null)
            {
                return;
            }

            _sceneRt.Release();
            Destroy(_sceneRt);
            _sceneRt = null;
        }

        private bool EnsureMaterial()
        {
            if (_fxaaMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find("Hidden/VRCast/FXAA");
            if (shader == null)
            {
                return false;
            }

            _fxaaMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }
    }
}
