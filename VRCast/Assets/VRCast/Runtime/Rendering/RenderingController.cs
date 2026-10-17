using UnityEngine;
using UnityEngine.Rendering;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// ライティングのプリセット（太陽光の強さ・色温度・向きと環境光の組。最新では三点・IBL・映えも含む）。
    /// </summary>
    public enum LightingPreset
    {
        Default,
        Sunny,
        Soft,
        Studio,
    }

    /// <summary>
    /// 背景（透過 / 単色）、解像度、ライティング（レガシーまたは最新）を設定値に従って適用する。
    /// 値の保持は AppSettings に委ね、変更は即座に反映する。
    /// </summary>
    public class RenderingController : MonoBehaviour
    {
        // 環境光の明るさ 1 のときの色（全方向から均一に当たる灰色）— レガシー用
        private const float AmbientBase = 0.5f;

        // カメラ正面（アバターの正面）から当てるときのワールドの向き。カメラは +Z 側から -Z を向く
        private const float FrontYaw = 180f;

        // 描画のフレームレートの上限（通常 / 軽量モード）。上限が無いと GPU を使い切ってゲーム・OBS を圧迫する
        public const int NormalFrameRate = 60;
        public const int LowLoadFrameRate = 30;

        // アバター向けの影距離（短くしてセルフシャドウの解像度を稼ぐ）
        private const float ModernShadowDistance = 12f;
        private const int ModernShadowResolution = 2048;

        private readonly AvatarMaterials _avatarMaterials = new AvatarMaterials();
        private UnityEngine.Camera _camera;
        private Light _keyLight;
        private Light _fillLight;
        private Light _rimLight;
        private CameraPostProcess _postProcess;
        private CameraAntiAliasing _antiAliasing;
        private AppSettings _settings;
        private bool _forceTransparent;
        private float _savedShadowDistance = 150f;

        /// <summary>
        /// 設定に関係なく背景を透過させる（パネルを隠している間）。保存はしない。
        /// </summary>
        public bool ForceTransparent
        {
            get => _forceTransparent;
            set
            {
                _forceTransparent = value;
                ApplyBackground();
            }
        }

        public bool TransparentBackground
        {
            get => _settings.transparentBackground;
            set
            {
                _settings.transparentBackground = value;
                ApplyBackground();
            }
        }

        public Color BackgroundColor
        {
            get => _settings.backgroundColor;
            set
            {
                // 単色背景は常に不透明で扱う
                value.a = 1f;
                _settings.backgroundColor = value;
                ApplyBackground();
            }
        }

        /// <summary>
        /// 操作パネルのダークモード。切り替え時、背景色が切り替え前のテーマの既定色のままなら新しいテーマの既定色にする
        /// （利用者が選んだ色は残す）。パネルの配色は MainPanel が設定を見て作り直す。
        /// </summary>
        public bool DarkMode
        {
            get => _settings.darkMode;
            set
            {
                // 変化が無ければ何もしない
                if (value == _settings.darkMode)
                {
                    return;
                }

                bool defaultBackground = _settings.backgroundColor == DefaultBackgroundColor;
                _settings.darkMode = value;
                if (defaultBackground)
                {
                    BackgroundColor = DefaultBackgroundColor;
                }
            }
        }

        /// <summary>
        /// 現在のテーマに合った背景色の既定値。
        /// </summary>
        public Color DefaultBackgroundColor => AppSettings.DefaultBackgroundOf(_settings.darkMode);

        public LightingSystem LightingSystem
        {
            get => _settings.lightingSystem;
            set
            {
                _settings.lightingSystem = value;
                ApplyAll();
            }
        }

        public bool IsModern => _settings.lightingSystem == LightingSystem.Modern;

        public AntiAliasingMode AntiAliasingMode
        {
            get => _settings.antiAliasingMode;
            set
            {
                _settings.antiAliasingMode = value;
                _antiAliasing?.Apply();
            }
        }

        public AntiAliasingQuality AntiAliasing
        {
            get => _settings.antiAliasing;
            set
            {
                _settings.antiAliasing = value;
                _antiAliasing?.Apply();
            }
        }

        public AntiAliasingQuality Supersample
        {
            get => _settings.supersample;
            set
            {
                _settings.supersample = value;
                _antiAliasing?.Apply();
            }
        }

        public float LightIntensity
        {
            get => _settings.lightIntensity;
            set
            {
                _settings.lightIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxLightIntensity);
                ApplyLights();
            }
        }

        public float LightYaw
        {
            get => _settings.lightYaw;
            set
            {
                _settings.lightYaw = value;
                ApplyLights();
            }
        }

        public float LightPitch
        {
            get => _settings.lightPitch;
            set
            {
                _settings.lightPitch = Mathf.Clamp(value, -90f, 90f);
                ApplyLights();
            }
        }

        public float LightTemperature
        {
            get => _settings.lightTemperature;
            set
            {
                _settings.lightTemperature = Mathf.Clamp(
                    value, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
                ApplyLights();
            }
        }

        public float AmbientIntensity
        {
            get => _settings.ambientIntensity;
            set
            {
                _settings.ambientIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxAmbientIntensity);
                ApplyAmbient();
            }
        }

        public float FillIntensity
        {
            get => _settings.fillIntensity;
            set
            {
                _settings.fillIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxLightIntensity);
                ApplyLights();
            }
        }

        public float FillYaw
        {
            get => _settings.fillYaw;
            set
            {
                _settings.fillYaw = value;
                ApplyLights();
            }
        }

        public float FillPitch
        {
            get => _settings.fillPitch;
            set
            {
                _settings.fillPitch = Mathf.Clamp(value, -90f, 90f);
                ApplyLights();
            }
        }

        public float FillTemperature
        {
            get => _settings.fillTemperature;
            set
            {
                _settings.fillTemperature = Mathf.Clamp(
                    value, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
                ApplyLights();
            }
        }

        public float RimIntensity
        {
            get => _settings.rimIntensity;
            set
            {
                _settings.rimIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxLightIntensity);
                ApplyLights();
            }
        }

        public float RimYaw
        {
            get => _settings.rimYaw;
            set
            {
                _settings.rimYaw = value;
                ApplyLights();
            }
        }

        public float RimPitch
        {
            get => _settings.rimPitch;
            set
            {
                _settings.rimPitch = Mathf.Clamp(value, -90f, 90f);
                ApplyLights();
            }
        }

        public float RimTemperature
        {
            get => _settings.rimTemperature;
            set
            {
                _settings.rimTemperature = Mathf.Clamp(
                    value, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
                ApplyLights();
            }
        }

        public float IblIntensity
        {
            get => _settings.iblIntensity;
            set
            {
                _settings.iblIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxIblIntensity);
                ApplyAmbient();
            }
        }

        public IblPreset IblPreset
        {
            get => _settings.iblPreset;
            set
            {
                _settings.iblPreset = value;
                ApplyAmbient();
            }
        }

        public bool SoftShadows
        {
            get => _settings.softShadows;
            set
            {
                _settings.softShadows = value;
                ApplyLights();
                _postProcess?.SetActive(IsModern);
            }
        }

        public float ShadowStrength
        {
            get => _settings.shadowStrength;
            set
            {
                _settings.shadowStrength = Mathf.Clamp(value, 0f, AppSettings.MaxShadowStrength);
                ApplyLights();
            }
        }

        public float ShadowSoftness
        {
            get => _settings.shadowSoftness;
            set
            {
                _settings.shadowSoftness = Mathf.Clamp(value, 0f, AppSettings.MaxShadowSoftness);
                ApplyLights();
            }
        }

        public bool BloomEnabled
        {
            get => _settings.bloomEnabled;
            set
            {
                _settings.bloomEnabled = value;
                _postProcess?.SetActive(IsModern);
            }
        }

        public float BloomIntensity
        {
            get => _settings.bloomIntensity;
            set => _settings.bloomIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxBloomIntensity);
        }

        public float BloomThreshold
        {
            get => _settings.bloomThreshold;
            set => _settings.bloomThreshold = Mathf.Clamp(value, 0f, AppSettings.MaxBloomThreshold);
        }

        public bool TonemapEnabled
        {
            get => _settings.tonemapEnabled;
            set
            {
                _settings.tonemapEnabled = value;
                _postProcess?.SetActive(IsModern);
            }
        }

        public float TonemapStrength
        {
            get => _settings.tonemapStrength;
            set => _settings.tonemapStrength = Mathf.Clamp(value, 0f, AppSettings.MaxTonemapStrength);
        }

        public float AvatarBrightness
        {
            get => _settings.avatarBrightness;
            set
            {
                _settings.avatarBrightness = Mathf.Clamp(
                    value, AppSettings.MinAvatarBrightness, AppSettings.MaxAvatarBrightness);
                _avatarMaterials.Apply(_settings.avatarBrightness);
            }
        }

        /// <summary>
        /// アバターの輪郭線の太さ（マテリアルの太さの倍率。1 = そのまま、0 = 輪郭線なし）。
        /// </summary>
        public float OutlineWidth
        {
            get => _settings.outlineWidth;
            set
            {
                _settings.outlineWidth = Mathf.Clamp(value, 0f, AppSettings.MaxOutlineWidth);
                _avatarMaterials.ApplyOutline(_settings.outlineWidth);
            }
        }

        /// <summary>
        /// 表示中のアバターに太さを変えられる輪郭線があるか。
        /// </summary>
        public bool HasAvatarOutline => _avatarMaterials.HasOutline;

        public bool LowLoadMode
        {
            get => _settings.lowLoadMode;
            set
            {
                _settings.lowLoadMode = value;
                ApplyFrameRate();
            }
        }

        public int Width => Screen.width;
        public int Height => Screen.height;

        public void Initialize(UnityEngine.Camera targetCamera, AppSettings settings)
        {
            _camera = targetCamera;
            _settings = settings;

            // 配信中にフォーカスが外れても描画を止めない
            Application.runInBackground = true;

            _savedShadowDistance = QualitySettings.shadowDistance;
            EnsureLights();

            _antiAliasing = _camera.GetComponent<CameraAntiAliasing>();
            if (_antiAliasing == null)
            {
                _antiAliasing = _camera.gameObject.AddComponent<CameraAntiAliasing>();
            }

            _antiAliasing.Initialize(_settings);

            _postProcess = _camera.GetComponent<CameraPostProcess>();
            if (_postProcess == null)
            {
                _postProcess = _camera.gameObject.AddComponent<CameraPostProcess>();
            }

            _postProcess.Initialize(_settings);
            ApplyAll();
        }

        /// <summary>
        /// 設定値を直接書き換えた後（全設定のリセット等）に、フレームレート・背景・ライト・環境光・アバターの明るさ・輪郭線を反映し直す。
        /// </summary>
        public void ApplyAll()
        {
            ApplyFrameRate();
            ApplyBackground();
            ApplyLights();
            ApplyAmbient();
            ApplyAvatarMaterials();
            _antiAliasing?.Apply();
            _postProcess?.SetActive(IsModern);
        }

        /// <summary>
        /// 読み込んだアバターのマテリアルを登録し、保存済みの明るさと輪郭線の太さを反映する。
        /// </summary>
        public void SetAvatar(GameObject instance)
        {
            _avatarMaterials.SetAvatar(instance);
            ApplyAvatarMaterials();
        }

        private void ApplyAvatarMaterials()
        {
            // 明るさと輪郭線の太さはどちらも登録時の元の値に倍率を掛ける
            _avatarMaterials.Apply(_settings.avatarBrightness);
            _avatarMaterials.ApplyOutline(_settings.outlineWidth);
        }

        /// <summary>
        /// ライティングのプリセットを適用する。
        /// </summary>
        public void ApplyPreset(LightingPreset preset)
        {
            var defaults = new AppSettings();
            if (!IsModern)
            {
                switch (preset)
                {
                    case LightingPreset.Sunny:
                        SetLegacyLighting(1.8f, 5600f, defaults.lightYaw, 35f, 1.4f);
                        break;
                    case LightingPreset.Soft:
                        SetLegacyLighting(0.9f, 6800f, 0f, 30f, 2f);
                        break;
                    default:
                        SetLegacyLighting(defaults.lightIntensity, defaults.lightTemperature, defaults.lightYaw,
                            defaults.lightPitch, defaults.ambientIntensity);
                        break;
                }

                return;
            }

            switch (preset)
            {
                case LightingPreset.Sunny:
                    SetModernLighting(
                        keyIntensity: 1.45f, keyTemp: 5200f, keyYaw: -48f, keyPitch: 26f,
                        ambient: 0.12f, fillIntensity: 0.2f, fillTemp: 7200f, fillYaw: 140f, fillPitch: 10f,
                        rimIntensity: 0.55f, rimTemp: 5800f, rimYaw: 155f, rimPitch: -8f,
                        ibl: 0.45f, IblPreset.Warm, softShadows: true, shadowStrength: 0.75f, shadowSoftness: 0.4f,
                        bloom: true, bloomIntensity: 0.05f, bloomThreshold: 1.65f, tonemap: true, tonemapStrength: 0.8f);
                    break;
                case LightingPreset.Soft:
                    // やわらかは塗りを優先するが、完全に正面にはせず鼻の陰は残す
                    SetModernLighting(
                        keyIntensity: 1.05f, keyTemp: 6200f, keyYaw: -35f, keyPitch: 24f,
                        ambient: 0.25f, fillIntensity: 0.35f, fillTemp: 7600f, fillYaw: 150f, fillPitch: 12f,
                        rimIntensity: 0.4f, rimTemp: 7000f, rimYaw: 170f, rimPitch: -2f,
                        ibl: 0.7f, IblPreset.Studio, softShadows: true, shadowStrength: 0.5f, shadowSoftness: 0.75f,
                        bloom: true, bloomIntensity: 0.03f, bloomThreshold: 1.75f, tonemap: true, tonemapStrength: 0.75f);
                    break;
                case LightingPreset.Studio:
                    SetModernLighting(
                        keyIntensity: 1.2f, keyTemp: 5400f, keyYaw: -58f, keyPitch: 24f,
                        ambient: 0.1f, fillIntensity: 0.25f, fillTemp: 8000f, fillYaw: 152f, fillPitch: 6f,
                        rimIntensity: 0.9f, rimTemp: 6200f, rimYaw: 170f, rimPitch: -14f,
                        ibl: 0.55f, IblPreset.Studio, softShadows: true, shadowStrength: 0.65f, shadowSoftness: 0.5f,
                        bloom: true, bloomIntensity: 0.04f, bloomThreshold: 1.7f, tonemap: true, tonemapStrength: 0.85f);
                    break;
                default:
                    SetModernLighting(
                        defaults.lightIntensity, defaults.lightTemperature, defaults.lightYaw, defaults.lightPitch,
                        defaults.ambientIntensity, defaults.fillIntensity, defaults.fillTemperature, defaults.fillYaw,
                        defaults.fillPitch, defaults.rimIntensity, defaults.rimTemperature, defaults.rimYaw,
                        defaults.rimPitch, defaults.iblIntensity, defaults.iblPreset, defaults.softShadows,
                        defaults.shadowStrength, defaults.shadowSoftness, defaults.bloomEnabled, defaults.bloomIntensity,
                        defaults.bloomThreshold, defaults.tonemapEnabled, defaults.tonemapStrength);
                    break;
            }
        }

        /// <summary>
        /// ウィンドウ解像度を変更し、設定にも記録する。Editor では記録のみ。
        /// </summary>
        public void SetResolution(int width, int height)
        {
            // 下限で補正して記録
            _settings.windowWidth = Mathf.Max(AppSettings.MinWindowSize, width);
            _settings.windowHeight = Mathf.Max(AppSettings.MinWindowSize, height);

            // Editor の Game View は変更しない
            if (!Application.isEditor)
            {
                Screen.SetResolution(_settings.windowWidth, _settings.windowHeight, FullScreenMode.Windowed);
            }
        }

        private void SetLegacyLighting(float intensity, float temperature, float yaw, float pitch, float ambient)
        {
            _settings.lightIntensity = Mathf.Clamp(intensity, 0f, AppSettings.MaxLightIntensity);
            _settings.lightTemperature = Mathf.Clamp(
                temperature, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
            _settings.lightYaw = yaw;
            _settings.lightPitch = Mathf.Clamp(pitch, -90f, 90f);
            _settings.ambientIntensity = Mathf.Clamp(ambient, 0f, AppSettings.MaxAmbientIntensity);
            ApplyLights();
            ApplyAmbient();
        }

        private void SetModernLighting(
            float keyIntensity, float keyTemp, float keyYaw, float keyPitch, float ambient,
            float fillIntensity, float fillTemp, float fillYaw, float fillPitch,
            float rimIntensity, float rimTemp, float rimYaw, float rimPitch,
            float ibl, IblPreset iblPreset, bool softShadows, float shadowStrength, float shadowSoftness,
            bool bloom, float bloomIntensity, float bloomThreshold, bool tonemap, float tonemapStrength)
        {
            _settings.lightIntensity = Mathf.Clamp(keyIntensity, 0f, AppSettings.MaxLightIntensity);
            _settings.lightTemperature = Mathf.Clamp(keyTemp, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
            _settings.lightYaw = keyYaw;
            _settings.lightPitch = Mathf.Clamp(keyPitch, -90f, 90f);
            _settings.ambientIntensity = Mathf.Clamp(ambient, 0f, AppSettings.MaxAmbientIntensity);
            _settings.fillIntensity = Mathf.Clamp(fillIntensity, 0f, AppSettings.MaxLightIntensity);
            _settings.fillTemperature = Mathf.Clamp(fillTemp, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
            _settings.fillYaw = fillYaw;
            _settings.fillPitch = Mathf.Clamp(fillPitch, -90f, 90f);
            _settings.rimIntensity = Mathf.Clamp(rimIntensity, 0f, AppSettings.MaxLightIntensity);
            _settings.rimTemperature = Mathf.Clamp(rimTemp, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
            _settings.rimYaw = rimYaw;
            _settings.rimPitch = Mathf.Clamp(rimPitch, -90f, 90f);
            _settings.iblIntensity = Mathf.Clamp(ibl, 0f, AppSettings.MaxIblIntensity);
            _settings.iblPreset = iblPreset;
            _settings.softShadows = softShadows;
            _settings.shadowStrength = Mathf.Clamp(shadowStrength, 0f, AppSettings.MaxShadowStrength);
            _settings.shadowSoftness = Mathf.Clamp(shadowSoftness, 0f, AppSettings.MaxShadowSoftness);
            _settings.bloomEnabled = bloom;
            _settings.bloomIntensity = Mathf.Clamp(bloomIntensity, 0f, AppSettings.MaxBloomIntensity);
            _settings.bloomThreshold = Mathf.Clamp(bloomThreshold, 0f, AppSettings.MaxBloomThreshold);
            _settings.tonemapEnabled = tonemap;
            _settings.tonemapStrength = Mathf.Clamp(tonemapStrength, 0f, AppSettings.MaxTonemapStrength);
            ApplyLights();
            ApplyAmbient();
            _postProcess?.SetActive(true);
        }

        private void ApplyFrameRate()
        {
            // 垂直同期に任せるとモニターのリフレッシュレート（144Hz 等）で描画するため、止めて上限を明示する
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = _settings.lowLoadMode ? LowLoadFrameRate : NormalFrameRate;
        }

        private void ApplyBackground()
        {
            // スカイボックスは使わず常に単色クリア（IBL 用 skybox があっても空は描かない）
            _camera.clearFlags = CameraClearFlags.SolidColor;

            // 透過時は色だけ塗って alpha 0（ウィンドウは alpha を無視して色を表示、ゲームキャプチャは alpha で抜く）
            Color color = _settings.backgroundColor;
            color.a = _settings.transparentBackground || _forceTransparent ? 0f : 1f;
            _camera.backgroundColor = color;
        }

        private void EnsureLights()
        {
            _keyLight = FindOrCreateDirectional("Directional Light", FindDirectionalLight());
            _fillLight = FindOrCreateDirectional("VRCast Fill Light", FindNamedLight("VRCast Fill Light"));
            _rimLight = FindOrCreateDirectional("VRCast Rim Light", FindNamedLight("VRCast Rim Light"));
        }

        private void ApplyLights()
        {
            if (_keyLight == null)
            {
                EnsureLights();
            }

            ApplyDirectional(_keyLight, _settings.lightIntensity, _settings.lightTemperature,
                _settings.lightYaw, _settings.lightPitch);

            if (IsModern)
            {
                _fillLight.enabled = true;
                _rimLight.enabled = true;
                ApplyDirectional(_fillLight, _settings.fillIntensity, _settings.fillTemperature,
                    _settings.fillYaw, _settings.fillPitch);
                ApplyDirectional(_rimLight, _settings.rimIntensity, _settings.rimTemperature,
                    _settings.rimYaw, _settings.rimPitch);

                _fillLight.shadows = LightShadows.None;
                _rimLight.shadows = LightShadows.None;

                bool shadows = _settings.softShadows;
                _keyLight.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                _keyLight.shadowStrength = Mathf.Clamp01(_settings.shadowStrength);
                // Normal Bias を上げすぎると鼻など細かいセルフシャドウが消えるので上限を抑える
                _keyLight.shadowBias = Mathf.Lerp(0.02f, 0.05f, _settings.shadowSoftness);
                _keyLight.shadowNormalBias = Mathf.Lerp(0.08f, 0.22f, _settings.shadowSoftness);
                _keyLight.shadowCustomResolution = ModernShadowResolution;

                QualitySettings.shadows = shadows ? ShadowQuality.All : ShadowQuality.Disable;
                QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
                QualitySettings.shadowDistance = ModernShadowDistance;
                QualitySettings.shadowCascades = 2;

                // 面から光源へ向かう向き（forward の逆）をポストのコンタクトシャドウへ
                _postProcess?.SetKeyLightDirection(-_keyLight.transform.forward);
            }
            else
            {
                _fillLight.enabled = false;
                _rimLight.enabled = false;
                // レガシーは影を品質設定に任せ、カスタム解像度を外す（2.x と同じ）
                _keyLight.shadowCustomResolution = -1;
                QualitySettings.shadowDistance = _savedShadowDistance;
            }
        }

        private void ApplyAmbient()
        {
            if (IsModern)
            {
                StudioIbl.Apply(_settings.iblPreset, _settings.iblIntensity, _settings.ambientIntensity);
            }
            else
            {
                StudioIbl.Clear();
                // ライティングデータを焼かないため、環境光は単色で直接与える（シェーダーが参照する SH も同じ色にする）
                Color ambient = new Color(AmbientBase, AmbientBase, AmbientBase) * _settings.ambientIntensity;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = ambient;
                var probe = new SphericalHarmonicsL2();
                probe.AddAmbientLight(ambient);
                RenderSettings.ambientProbe = probe;
            }
        }

        private static void ApplyDirectional(Light light, float intensity, float temperature, float yaw, float pitch)
        {
            light.intensity = intensity;
            light.color = Mathf.CorrelatedColorTemperatureToRGB(temperature);
            light.transform.rotation = Quaternion.Euler(pitch, FrontYaw + yaw, 0f);
        }

        private static Light FindOrCreateDirectional(string objectName, Light existing)
        {
            if (existing != null)
            {
                existing.type = LightType.Directional;
                return existing;
            }

            var light = new GameObject(objectName).AddComponent<Light>();
            light.type = LightType.Directional;
            return light;
        }

        private static Light FindDirectionalLight()
        {
            foreach (Light light in FindObjectsOfType<Light>())
            {
                if (light.type == LightType.Directional
                    && light.name != "VRCast Fill Light" && light.name != "VRCast Rim Light")
                {
                    return light;
                }
            }

            return null;
        }

        private static Light FindNamedLight(string objectName)
        {
            foreach (Light light in FindObjectsOfType<Light>())
            {
                if (light.name == objectName)
                {
                    return light;
                }
            }

            return null;
        }
    }
}
