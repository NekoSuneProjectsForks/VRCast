using UnityEngine;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// 最新ライティング向けの画面効果（コンタクトソフトシャドウ・SSAO・画面リム・ブルーム・トーンマップ・色調補正など）。
    /// 出力の alpha は入力のまま残し、透過キャプチャを壊さない。仮想カメラ／Spout より先に実行する。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class CameraPostProcess : MonoBehaviour
    {
        private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        private static readonly int BlurDirId = Shader.PropertyToID("_BlurDir");
        private static readonly int BloomTexId = Shader.PropertyToID("_BloomTex");
        private static readonly int BloomIntensityId = Shader.PropertyToID("_BloomIntensity");
        private static readonly int TonemapId = Shader.PropertyToID("_Tonemap");
        private static readonly int TonemapStrengthId = Shader.PropertyToID("_TonemapStrength");
        private static readonly int LightDirId = Shader.PropertyToID("_LightDir");
        private static readonly int ShadowStrengthId = Shader.PropertyToID("_ShadowStrength");
        private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        private static readonly int SsaoIntensityId = Shader.PropertyToID("_SsaoIntensity");
        private static readonly int SsaoRadiusId = Shader.PropertyToID("_SsaoRadius");
        private static readonly int RimIntensityId = Shader.PropertyToID("_RimIntensity");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        private static readonly int TemperatureId = Shader.PropertyToID("_Temperature");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int SharpenId = Shader.PropertyToID("_Sharpen");
        private static readonly int VignetteId = Shader.PropertyToID("_Vignette");

        private AppSettings _settings;
        private Material _bloomMaterial;
        private Material _contactMaterial;
        private Material _screenFxMaterial;
        private bool _enabled = true;
        private Vector3 _keyLightDirection = new Vector3(0.3f, -0.8f, 0.5f);

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
            EnsureMaterials();
            ApplyEnabled();
        }

        public void SetActive(bool active)
        {
            _enabled = active;
            ApplyEnabled();
        }

        public void SetKeyLightDirection(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude > 1e-6f)
            {
                _keyLightDirection = worldDirection.normalized;
            }
        }

        private void OnEnable()
        {
            ApplyEnabled();
        }

        private void OnDisable()
        {
            var cam = GetComponent<UnityEngine.Camera>();
            if (cam != null)
            {
                cam.depthTextureMode &= ~DepthTextureMode.Depth;
            }
        }

        private void OnDestroy()
        {
            DestroyMaterial(ref _bloomMaterial);
            DestroyMaterial(ref _contactMaterial);
            DestroyMaterial(ref _screenFxMaterial);
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!_enabled || _settings == null || _settings.lightingSystem != LightingSystem.Modern)
            {
                Graphics.Blit(source, destination);
                return;
            }

            EnsureMaterials();

            RenderTexture current = source;
            RenderTexture owned = null;

            // 1) PCSS 風コンタクトシャドウ
            if (_settings.softShadows && _contactMaterial != null)
            {
                RenderTexture next = Alloc(source);
                Vector3 viewLight = transform.InverseTransformDirection(_keyLightDirection);
                _contactMaterial.SetVector(LightDirId, new Vector4(viewLight.x, viewLight.y, viewLight.z, 0f));
                _contactMaterial.SetFloat(ShadowStrengthId, _settings.shadowStrength);
                _contactMaterial.SetFloat(SoftnessId, _settings.shadowSoftness);
                Graphics.Blit(current, next, _contactMaterial, 0);
                Swap(ref current, ref owned, next);
            }

            // 2) SSAO
            if (_settings.ssaoEnabled && _settings.ssaoIntensity > 0.001f && _screenFxMaterial != null)
            {
                RenderTexture next = Alloc(source);
                _screenFxMaterial.SetFloat(SsaoIntensityId, _settings.ssaoIntensity);
                _screenFxMaterial.SetFloat(SsaoRadiusId, _settings.ssaoRadius);
                Graphics.Blit(current, next, _screenFxMaterial, 0);
                Swap(ref current, ref owned, next);
            }

            // 3) 画面空間リム
            if (_settings.screenRimEnabled && _settings.screenRimIntensity > 0.001f && _screenFxMaterial != null)
            {
                RenderTexture next = Alloc(source);
                Color rimColor = Color.HSVToRGB(Mathf.Repeat(_settings.screenRimHue, 1f), 0.35f, 1f);
                _screenFxMaterial.SetFloat(RimIntensityId, _settings.screenRimIntensity);
                _screenFxMaterial.SetFloat(RimPowerId, _settings.screenRimPower);
                _screenFxMaterial.SetColor(RimColorId, rimColor);
                Graphics.Blit(current, next, _screenFxMaterial, 1);
                Swap(ref current, ref owned, next);
            }

            bool bloom = _settings.bloomEnabled && _settings.bloomIntensity > 0.001f && _bloomMaterial != null;
            bool tonemap = _settings.tonemapEnabled && _bloomMaterial != null;
            bool grade = NeedsColorGrade() && _screenFxMaterial != null;

            // 4) ブルーム + トーンマップ
            if (bloom || tonemap)
            {
                RenderTexture bloomA = null;
                RenderTexture bloomB = null;
                if (bloom)
                {
                    int bw = Mathf.Max(1, source.width / 2);
                    int bh = Mathf.Max(1, source.height / 2);
                    bloomA = RenderTexture.GetTemporary(bw, bh, 0, source.format);
                    bloomB = RenderTexture.GetTemporary(bw, bh, 0, source.format);
                    _bloomMaterial.SetFloat(ThresholdId, _settings.bloomThreshold);
                    Graphics.Blit(current, bloomA, _bloomMaterial, 0);
                    _bloomMaterial.SetVector(BlurDirId, new Vector4(1f, 0f, 0f, 0f));
                    Graphics.Blit(bloomA, bloomB, _bloomMaterial, 1);
                    _bloomMaterial.SetVector(BlurDirId, new Vector4(0f, 1f, 0f, 0f));
                    Graphics.Blit(bloomB, bloomA, _bloomMaterial, 1);
                    _bloomMaterial.SetVector(BlurDirId, new Vector4(1f, 0f, 0f, 0f));
                    Graphics.Blit(bloomA, bloomB, _bloomMaterial, 1);
                    _bloomMaterial.SetVector(BlurDirId, new Vector4(0f, 1f, 0f, 0f));
                    Graphics.Blit(bloomB, bloomA, _bloomMaterial, 1);
                    _bloomMaterial.SetTexture(BloomTexId, bloomA);
                    _bloomMaterial.SetFloat(BloomIntensityId, _settings.bloomIntensity);
                }
                else
                {
                    _bloomMaterial.SetTexture(BloomTexId, Texture2D.blackTexture);
                    _bloomMaterial.SetFloat(BloomIntensityId, 0f);
                }

                _bloomMaterial.SetFloat(TonemapId, tonemap ? 1f : 0f);
                _bloomMaterial.SetFloat(TonemapStrengthId, _settings.tonemapStrength);

                if (grade)
                {
                    RenderTexture next = Alloc(source);
                    Graphics.Blit(current, next, _bloomMaterial, 2);
                    Swap(ref current, ref owned, next);
                }
                else
                {
                    Graphics.Blit(current, destination, _bloomMaterial, 2);
                    ReleaseTemp(ref bloomA);
                    ReleaseTemp(ref bloomB);
                    ReleaseTemp(ref owned);
                    return;
                }

                ReleaseTemp(ref bloomA);
                ReleaseTemp(ref bloomB);
            }

            // 5) 色調補正 / シャープ / ビネット
            if (grade)
            {
                ApplyGradeMaterial();
                Graphics.Blit(current, destination, _screenFxMaterial, 2);
            }
            else
            {
                Graphics.Blit(current, destination);
            }

            ReleaseTemp(ref owned);
        }

        private static RenderTexture Alloc(RenderTexture source)
        {
            return RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
        }

        private static void Swap(ref RenderTexture current, ref RenderTexture owned, RenderTexture next)
        {
            if (owned != null)
            {
                RenderTexture.ReleaseTemporary(owned);
            }

            owned = next;
            current = next;
        }

        private bool NeedsColorGrade()
        {
            if (_settings.sharpenEnabled && _settings.sharpenAmount > 0.001f)
            {
                return true;
            }

            if (_settings.vignetteEnabled && _settings.vignetteIntensity > 0.001f)
            {
                return true;
            }

            if (!_settings.colorGradeEnabled)
            {
                return false;
            }

            return !Mathf.Approximately(_settings.exposure, 0f)
                || !Mathf.Approximately(_settings.contrast, 1f)
                || !Mathf.Approximately(_settings.saturation, 1f)
                || !Mathf.Approximately(_settings.colorTemperature, 0f)
                || !Mathf.Approximately(_settings.colorTint, 0f);
        }

        private void ApplyGradeMaterial()
        {
            bool grade = _settings.colorGradeEnabled;
            _screenFxMaterial.SetFloat(ExposureId, grade ? _settings.exposure : 0f);
            _screenFxMaterial.SetFloat(ContrastId, grade ? _settings.contrast : 1f);
            _screenFxMaterial.SetFloat(SaturationId, grade ? _settings.saturation : 1f);
            _screenFxMaterial.SetFloat(TemperatureId, grade ? _settings.colorTemperature : 0f);
            _screenFxMaterial.SetFloat(TintId, grade ? _settings.colorTint : 0f);
            _screenFxMaterial.SetFloat(SharpenId, _settings.sharpenEnabled ? _settings.sharpenAmount : 0f);
            _screenFxMaterial.SetFloat(VignetteId, _settings.vignetteEnabled ? _settings.vignetteIntensity : 0f);
        }

        private void ApplyEnabled()
        {
            enabled = _enabled && _settings != null && _settings.lightingSystem == LightingSystem.Modern;
            var cam = GetComponent<UnityEngine.Camera>();
            if (cam == null)
            {
                return;
            }

            bool needDepth = enabled && _settings != null
                && (_settings.softShadows || _settings.ssaoEnabled || _settings.screenRimEnabled);
            if (needDepth)
            {
                cam.depthTextureMode |= DepthTextureMode.Depth;
            }
            else
            {
                cam.depthTextureMode &= ~DepthTextureMode.Depth;
            }
        }

        private void EnsureMaterials()
        {
            if (_bloomMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/VRCast/BloomTonemap");
                if (shader != null)
                {
                    _bloomMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
            }

            if (_contactMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/VRCast/ContactShadow");
                if (shader != null)
                {
                    _contactMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
            }

            if (_screenFxMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/VRCast/ScreenFx");
                if (shader != null)
                {
                    _screenFxMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
            }
        }

        private static void ReleaseTemp(ref RenderTexture texture)
        {
            if (texture != null)
            {
                RenderTexture.ReleaseTemporary(texture);
                texture = null;
            }
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material != null)
            {
                Destroy(material);
                material = null;
            }
        }
    }
}
