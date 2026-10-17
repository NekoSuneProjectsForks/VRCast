using UnityEngine;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// MSAA を品質設定へ反映し、仮想カメラ等の OnRenderImage で MSAA が効かないとき用に α 保持の FXAA を掛ける。
    /// 仮想カメラ／Spout／最新ライティングのポストより先に実行する。
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class CameraAntiAliasing : MonoBehaviour
    {
        private AppSettings _settings;
        private Material _fxaaMaterial;

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
            Apply();
        }

        /// <summary>
        /// 設定のアンチエイリアスを MSAA と FXAA の有無へ反映する。
        /// </summary>
        public void Apply()
        {
            if (_settings == null)
            {
                return;
            }

            int samples = (int)_settings.antiAliasing;
            QualitySettings.antiAliasing = samples;

            var cam = GetComponent<UnityEngine.Camera>();
            if (cam != null)
            {
                cam.allowMSAA = samples > 0;
            }

            enabled = samples > 0;
            if (enabled)
            {
                EnsureMaterial();
            }
        }

        private void OnDestroy()
        {
            if (_fxaaMaterial != null)
            {
                Destroy(_fxaaMaterial);
                _fxaaMaterial = null;
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_settings == null || _settings.antiAliasing == AntiAliasingQuality.Off || !EnsureMaterial())
            {
                Graphics.Blit(source, destination);
                return;
            }

            Graphics.Blit(source, destination, _fxaaMaterial, 0);
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
