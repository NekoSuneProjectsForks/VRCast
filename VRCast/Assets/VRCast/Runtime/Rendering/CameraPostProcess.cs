using UnityEngine;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// 最新ライティング向けの画面効果（コンタクトソフトシャドウ・ブルーム・トーンマップ）。
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

        private AppSettings _settings;
        private Material _bloomMaterial;
        private Material _contactMaterial;
        private bool _enabled = true;
        private Vector3 _keyLightDirection = new Vector3(0.3f, -0.8f, 0.5f);

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
            EnsureMaterials();
            ApplyEnabled();
        }

        /// <summary>
        /// 最新ライティングのときだけ効果を掛ける。
        /// </summary>
        public void SetActive(bool active)
        {
            _enabled = active;
            ApplyEnabled();
        }

        /// <summary>
        /// コンタクトシャドウの光源方向（ワールド、光源から面へ向かう向き）。
        /// </summary>
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
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!_enabled || _settings == null || _settings.lightingSystem != LightingSystem.Modern)
            {
                Graphics.Blit(source, destination);
                return;
            }

            EnsureMaterials();
            if (_bloomMaterial == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            RenderTexture current = source;
            RenderTexture shadowTemp = null;

            // PCSS 風の画面空間ソフトシャドウ（セルフシャドウの柔らかさ）
            bool softShadow = _settings.softShadows && _contactMaterial != null;
            if (softShadow)
            {
                shadowTemp = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
                Vector3 viewLight = transform.InverseTransformDirection(_keyLightDirection);
                _contactMaterial.SetVector(LightDirId, new Vector4(viewLight.x, viewLight.y, viewLight.z, 0f));
                _contactMaterial.SetFloat(ShadowStrengthId, _settings.shadowStrength);
                _contactMaterial.SetFloat(SoftnessId, _settings.shadowSoftness);
                Graphics.Blit(current, shadowTemp, _contactMaterial, 0);
                current = shadowTemp;
            }

            bool bloom = _settings.bloomEnabled && _settings.bloomIntensity > 0.001f;
            bool tonemap = _settings.tonemapEnabled;

            if (!bloom && !tonemap)
            {
                Graphics.Blit(current, destination);
                ReleaseTemp(ref shadowTemp);
                return;
            }

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
            Graphics.Blit(current, destination, _bloomMaterial, 2);

            ReleaseTemp(ref bloomA);
            ReleaseTemp(ref bloomB);
            ReleaseTemp(ref shadowTemp);
        }

        private void ApplyEnabled()
        {
            enabled = _enabled && _settings != null && _settings.lightingSystem == LightingSystem.Modern;
            var cam = GetComponent<UnityEngine.Camera>();
            if (cam == null)
            {
                return;
            }

            if (enabled && _settings != null && _settings.softShadows)
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
