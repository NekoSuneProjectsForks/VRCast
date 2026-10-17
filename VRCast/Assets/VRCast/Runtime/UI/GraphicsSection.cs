using System;
using UnityEngine;
using VRCast.Core;
using VRCast.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// グラフィックタブ（背景、レガシー／最新ライティング、輪郭線）。
    /// </summary>
    public class GraphicsSection
    {
        // 太陽光スライダーの上限
        private const float MaxSunlight = 4f;

        private readonly RenderingController _rendering;
        private readonly AppSettings _settings;
        private readonly Action _reapply;
        private readonly GraphicsPresetSection _presets;

        // 既定に戻すの二段階確認
        private bool _confirmingReset;

        public GraphicsSection(RenderingController rendering, AppSettings settings, Action reapply)
        {
            _rendering = rendering;
            _settings = settings;
            _reapply = reapply;
            _presets = new GraphicsPresetSection(settings, reapply);
        }

        public void Draw()
        {
            DrawBackground();
            DrawAntiAliasing();
            DrawLightingSystem();
            if (_rendering.IsModern)
            {
                DrawModernLight();
                DrawShadows();
                DrawLook();
            }
            else
            {
                DrawLegacyLight();
            }

            DrawOutline();
            DrawResetDefaults();
            _presets.Draw();
        }

        private void DrawBackground()
        {
            GuiControls.BeginCard(Loc.T("Background", "背景", "배경", "背景", "背景"));

            // 透過切替（OBS はゲームキャプチャ +「透過を許可」で取り込む）
            _rendering.TransparentBackground = GUILayout.Toggle(
                _rendering.TransparentBackground,
                Loc.T("Transparent (OBS Game Capture)", "透過（OBS ゲームキャプチャ）", "투명 (OBS 게임 캡처)",
                    "透明（OBS 游戏采集）", "透明（OBS 遊戲擷取）"));

            // 透過時は色がウィンドウ上だけに見えることを案内
            GuiControls.Hint(_rendering.TransparentBackground
                ? Loc.T("Background color (shown only in this window, not captured by OBS)",
                    "背景色（透過中はこの画面だけの表示で、OBS には映りません）",
                    "배경색 (투명 중에는 이 창에만 보이고 OBS에는 표시되지 않습니다)",
                    "背景色（透明时仅在此窗口显示，不会出现在 OBS 中）",
                    "背景色（透明時僅在此視窗顯示，不會出現在 OBS 中）")
                : Loc.T("Background color", "背景色", "배경색", "背景色", "背景色"));

            // RGB スライダー（値が変わったときだけ反映）
            Color color = _rendering.BackgroundColor;
            color.r = GuiControls.Slider(Loc.T("Red", "赤", "빨강", "红", "紅"), color.r, 0f, 1f);
            color.g = GuiControls.Slider(Loc.T("Green", "緑", "초록", "绿", "綠"), color.g, 0f, 1f);
            color.b = GuiControls.Slider(Loc.T("Blue", "青", "파랑", "蓝", "藍"), color.b, 0f, 1f);
            if (color != _rendering.BackgroundColor)
            {
                _rendering.BackgroundColor = color;
            }

            // 現在のテーマの既定色に戻す（ライト = ベージュ、ダーク = 暗い灰色）
            string defaultLabel = _rendering.DarkMode
                ? Loc.T("Dark gray (default)", "ダークグレー（既定）", "다크 그레이 (기본)", "深灰色（默认）", "深灰色（預設）")
                : Loc.T("Beige (default)", "ベージュ（既定）", "베이지 (기본)", "米色（默认）", "米色（預設）");
            if (GUILayout.Button(defaultLabel))
            {
                _rendering.BackgroundColor = _rendering.DefaultBackgroundColor;
            }

            GuiControls.EndCard();
        }

        private void DrawAntiAliasing()
        {
            GuiControls.BeginCard(Loc.T("Anti-aliasing", "アンチエイリアス", "안티앨리어싱", "抗锯齿", "抗鋸齒"));
            GuiControls.Hint(Loc.T(
                "MSAA keeps edges sharp. FXAA is lighter but can blur. Supersampling renders the avatar at a higher resolution (uses more GPU).",
                "MSAA は輪郭をシャープに保ちます。FXAA は軽いですがぼやけやすいです。高解像度描画は内部解像度を上げて描き（GPU 負荷増）、ジャギーを抑えます。",
                "MSAA는 윤곽을 선명하게 유지합니다. FXAA는 가볍지만 흐려질 수 있습니다. 고해상도 렌더링은 내부 해상도를 올려 그립니다(GPU 부하 증가).",
                "MSAA 保持边缘清晰。FXAA 更轻但可能发糊。高分辨率绘制以内部分辨率提高绘制（更耗 GPU）。",
                "MSAA 保持邊緣清晰。FXAA 較輕但可能發糊。高解析度繪製以內部分辨率提高繪製（更耗 GPU）。"));

            AntiAliasingMode[] modes =
            {
                AntiAliasingMode.Off, AntiAliasingMode.Msaa, AntiAliasingMode.Fxaa, AntiAliasingMode.Supersample,
            };
            string[] modeLabels =
            {
                Loc.T("Off", "オフ", "끄기", "关", "關"),
                "MSAA",
                "FXAA",
                Loc.T("Supersample", "高解像度描画", "고해상도 렌더", "高分辨率", "高解析度"),
            };
            int modeIndex = IndexOf(modes, _rendering.AntiAliasingMode == AntiAliasingMode.Unspecified
                ? AntiAliasingMode.Msaa
                : _rendering.AntiAliasingMode);
            int modeSelected = GuiControls.LabeledChoice(Loc.T("Method", "方式", "방식", "方式", "方式"), modeLabels,
                modeIndex);
            if (modeSelected != modeIndex)
            {
                _rendering.AntiAliasingMode = modes[modeSelected];
                if (_rendering.AntiAliasingMode != AntiAliasingMode.Off
                    && _rendering.AntiAliasing == AntiAliasingQuality.Off)
                {
                    _rendering.AntiAliasing = AntiAliasingQuality.x4;
                }
            }

            AntiAliasingMode mode = _rendering.AntiAliasingMode;
            if (mode == AntiAliasingMode.Msaa || mode == AntiAliasingMode.Supersample)
            {
                AntiAliasingQuality[] qualities =
                {
                    AntiAliasingQuality.x2, AntiAliasingQuality.x4, AntiAliasingQuality.x8,
                };
                string[] qualityLabels = mode == AntiAliasingMode.Supersample
                    ? new[] { "125%", "150%", "200%" }
                    : new[] { "2x", "4x", "8x" };
                AntiAliasingQuality current = _rendering.AntiAliasing == AntiAliasingQuality.Off
                    ? AntiAliasingQuality.x4
                    : _rendering.AntiAliasing;
                int qualityIndex = IndexOf(qualities, current);
                int qualitySelected = GuiControls.LabeledChoice(
                    Loc.T("Quality", "品質", "품질", "质量", "品質"), qualityLabels, qualityIndex);
                if (qualitySelected != qualityIndex)
                {
                    _rendering.AntiAliasing = qualities[qualitySelected];
                }
            }
            else if (mode == AntiAliasingMode.Fxaa)
            {
                GuiControls.Hint(Loc.T(
                    "FXAA smooths the whole image and may soften fine outlines.",
                    "FXAA は画面全体をならすため、細い輪郭がぼやけることがあります。",
                    "FXAA는 화면 전체를 부드럽게 해서 가는 윤곽이 흐려질 수 있습니다.",
                    "FXAA 会平滑整幅画面，细轮廓可能发糊。",
                    "FXAA 會平滑整幅畫面，細輪廓可能發糊。"));
            }

            GuiControls.EndCard();
        }

        private static int IndexOf<T>(T[] values, T current)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (Equals(values[i], current))
                {
                    return i;
                }
            }

            return 0;
        }

        private void DrawLightingSystem()
        {
            GuiControls.BeginCard(Loc.T("Lighting", "ライティング", "라이팅", "光照", "光照"));
            GuiControls.Hint(Loc.T(
                "Legacy matches VRCast 2.x (default). Modern adds three lights, soft shadows, studio light and bloom.",
                "レガシーは VRCast 2.x と同じ見た目です（既定）。最新は三点照明・柔らかい影・スタジオ環境光・ブルームを使います。",
                "레거시는 VRCast 2.x와 같은 모습입니다(기본). 최신은 삼점 조명·부드러운 그림자·스튜디오 환경광·블룸을 씁니다.",
                "旧版与 VRCast 2.x 相同（默认）。最新使用三点照明、柔和阴影、工作室环境光和辉光。",
                "舊版與 VRCast 2.x 相同（預設）。最新使用三點照明、柔和陰影、工作室環境光和輝光。"));

            int selected = GuiControls.LabeledChoice(
                Loc.T("System", "系統", "계통", "系统", "系統"),
                new[]
                {
                    Loc.T("Legacy (2.x)", "レガシー（2.x）", "레거시 (2.x)", "旧版 (2.x)", "舊版 (2.x)"),
                    Loc.T("Modern", "最新", "최신", "最新", "最新"),
                },
                (int)_rendering.LightingSystem);
            if (selected != (int)_rendering.LightingSystem)
            {
                _rendering.LightingSystem = (LightingSystem)selected;
            }

            GuiControls.EndCard();
        }

        private void DrawLegacyLight()
        {
            GuiControls.BeginCard(Loc.T("Light", "ライト", "조명", "灯光", "燈光"));
            GuiControls.Hint(Loc.T("Sunlight shines from one direction; ambient light brightens the whole avatar evenly",
                "太陽光は一方向から当たる光、環境光はアバター全体を均一に明るくする光です",
                "태양광은 한 방향에서 비추는 빛, 환경광은 아바타 전체를 고르게 밝히는 빛입니다",
                "阳光从一个方向照射，环境光会均匀照亮整个虚拟形象",
                "陽光從一個方向照射，環境光會均勻照亮整個虛擬形象"));
            DrawBrightnessHint();
            DrawLegacyPresets();
            DrawAvatarBrightness();
            DrawAmbientSlider();
            DrawKeyLightSliders(
                Loc.T("Sunlight", "太陽光", "태양광", "阳光", "陽光"),
                Loc.T("Sun color (K)", "太陽光の色 (K)", "태양광 색 (K)", "阳光颜色 (K)", "陽光顏色 (K)"));
            GuiControls.EndCard();
        }

        private void DrawModernLight()
        {
            GuiControls.BeginCard(Loc.T("Lights", "ライト", "조명", "灯光", "燈光"));
            GuiControls.Hint(Loc.T(
                "Key is the main light, Fill softens shadows, Rim outlines the silhouette. Remembered per avatar.",
                "キーが主光、フィルが影を明るく、リムが輪郭を縁取る光です。アバターごとに記憶されます。",
                "키가 주광, 필이 그림자를 밝히고, 림이 윤곽을 강조합니다. 아바타마다 기억됩니다.",
                "主光、辅光柔化阴影、轮廓光勾边。按虚拟形象分别记住。",
                "主光、輔光柔化陰影、輪廓光勾邊。依虛擬形象分別記住。"));
            DrawBrightnessHint();
            DrawModernPresets();
            DrawAvatarBrightness();
            DrawAmbientSlider();

            GuiControls.SubHeading(Loc.T("Key light", "キーライト", "키 라이트", "主光", "主光"));
            DrawKeyLightSliders(
                Loc.T("Intensity", "強さ", "세기", "强度", "強度"),
                Loc.T("Color (K)", "色 (K)", "색 (K)", "颜色 (K)", "顏色 (K)"));

            GuiControls.SubHeading(Loc.T("Fill light", "フィルライト", "필 라이트", "辅光", "輔光"));
            DrawFillSliders();

            GuiControls.SubHeading(Loc.T("Rim light", "リムライト", "림 라이트", "轮廓光", "輪廓光"));
            DrawRimSliders();

            GuiControls.SubHeading(Loc.T("Studio environment", "スタジオ環境光", "스튜디오 환경광", "工作室环境光", "工作室環境光"));
            DrawIblSliders();
            GuiControls.EndCard();
        }

        private void DrawShadows()
        {
            GuiControls.BeginCard(Loc.T("Soft shadows (PCSS)", "柔らかい影（PCSS）", "부드러운 그림자 (PCSS)",
                "柔和阴影（PCSS）", "柔和陰影（PCSS）"));
            GuiControls.Hint(Loc.T(
                "Softens self-shadows on the avatar. No floor is drawn (works with transparent backgrounds).",
                "アバター自身への影を柔らかくします。床は出さないため透過背景のまま使えます。",
                "아바타 자체 그림자를 부드럽게 합니다. 바닥은 그리지 않아 투명 배경에서도 쓸 수 있습니다.",
                "柔化虚拟形象自身的阴影。不绘制地面，可与透明背景并用。",
                "柔化虛擬形象自身的陰影。不繪製地面，可與透明背景並用。"));

            _rendering.SoftShadows = GUILayout.Toggle(_rendering.SoftShadows,
                Loc.T("Enable soft shadows", "柔らかい影を使う", "부드러운 그림자 사용", "启用柔和阴影", "啟用柔和陰影"));
            if (_rendering.SoftShadows)
            {
                float strength = GuiControls.Slider(Loc.T("Strength", "強さ", "세기", "强度", "強度"),
                    _rendering.ShadowStrength, 0f, AppSettings.MaxShadowStrength);
                if (!Mathf.Approximately(strength, _rendering.ShadowStrength))
                {
                    _rendering.ShadowStrength = strength;
                }

                float softness = GuiControls.Slider(Loc.T("Softness (PCSS)", "柔らかさ（PCSS）", "부드러움 (PCSS)",
                        "柔和度（PCSS）", "柔和度（PCSS）"),
                    _rendering.ShadowSoftness, 0f, AppSettings.MaxShadowSoftness);
                if (!Mathf.Approximately(softness, _rendering.ShadowSoftness))
                {
                    _rendering.ShadowSoftness = softness;
                }
            }

            GuiControls.EndCard();
        }

        private void DrawLook()
        {
            GuiControls.BeginCard(Loc.T("Look", "映え", "연출", "观感", "觀感"));
            GuiControls.Hint(Loc.T(
                "Bloom and tone mapping. Alpha is preserved for transparent capture.",
                "ブルームとトーンマップです。透過キャプチャのため透明度は保ちます。",
                "블룸과 톤맵입니다. 투명 캡처를 위해 알파를 유지합니다.",
                "辉光与色调映射。为透明采集保留透明度。",
                "輝光與色調映射。為透明擷取保留透明度。"));

            _rendering.BloomEnabled = GUILayout.Toggle(_rendering.BloomEnabled,
                Loc.T("Bloom", "ブルーム", "블룸", "辉光", "輝光"));
            if (_rendering.BloomEnabled)
            {
                float bloom = GuiControls.Slider(Loc.T("Bloom strength", "ブルームの強さ", "블룸 세기", "辉光强度", "輝光強度"),
                    _rendering.BloomIntensity, 0f, AppSettings.MaxBloomIntensity);
                if (!Mathf.Approximately(bloom, _rendering.BloomIntensity))
                {
                    _rendering.BloomIntensity = bloom;
                }

                float threshold = GuiControls.Slider(Loc.T("Bloom threshold", "ブルームのしきい値", "블룸 임계값",
                        "辉光阈值", "輝光閾值"),
                    _rendering.BloomThreshold, 0f, AppSettings.MaxBloomThreshold);
                if (!Mathf.Approximately(threshold, _rendering.BloomThreshold))
                {
                    _rendering.BloomThreshold = threshold;
                }
            }

            _rendering.TonemapEnabled = GUILayout.Toggle(_rendering.TonemapEnabled,
                Loc.T("Tone mapping", "トーンマップ", "톤맵", "色调映射", "色調映射"));
            if (_rendering.TonemapEnabled)
            {
                float tonemap = GuiControls.Slider(Loc.T("Tone map strength", "トーンマップの強さ", "톤맵 세기",
                        "色调映射强度", "色調映射強度"),
                    _rendering.TonemapStrength, 0f, AppSettings.MaxTonemapStrength);
                if (!Mathf.Approximately(tonemap, _rendering.TonemapStrength))
                {
                    _rendering.TonemapStrength = tonemap;
                }
            }

            GuiControls.EndCard();
        }

        private void DrawLegacyPresets()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Sunny", "晴れ", "맑음", "晴天", "晴天"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Sunny);
            }

            if (GUILayout.Button(Loc.T("Soft", "やわらか", "부드럽게", "柔和", "柔和"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Soft);
            }

            if (GUILayout.Button(Loc.T("Default", "既定", "기본", "默认", "預設"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Default);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawModernPresets()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Studio", "スタジオ", "스튜디오", "工作室", "工作室"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Studio);
            }

            if (GUILayout.Button(Loc.T("Sunny", "晴れ", "맑음", "晴天", "晴天"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Sunny);
            }

            if (GUILayout.Button(Loc.T("Soft", "やわらか", "부드럽게", "柔和", "柔和"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Soft);
            }

            if (GUILayout.Button(Loc.T("Default", "既定", "기본", "默认", "預設"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Default);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawBrightnessHint()
        {
            GuiControls.Hint(Loc.T("Light and avatar brightness are remembered for each avatar.",
                "ライトとアバターの明るさはアバターごとに記憶されます。",
                "조명과 아바타 밝기는 아바타마다 기억됩니다.",
                "灯光和虚拟形象亮度会按虚拟形象分别记住。",
                "燈光和虛擬形象亮度會依虛擬形象分別記住。"));
        }

        private void DrawAvatarBrightness()
        {
            float brightness = GuiControls.Slider(Loc.T("Avatar brightness", "アバターの明るさ", "아바타 밝기", "虚拟形象亮度", "虛擬形象亮度"),
                _rendering.AvatarBrightness, AppSettings.MinAvatarBrightness, AppSettings.MaxAvatarBrightness);
            if (!Mathf.Approximately(brightness, _rendering.AvatarBrightness))
            {
                _rendering.AvatarBrightness = brightness;
            }

            GuiControls.Hint(Loc.T("If more light does not help (lilToon etc. cap brightness), raise Avatar brightness",
                "ライトを強くしても明るくならないとき（lilToon 等は明るさに上限あり）はアバターの明るさを上げてください",
                "조명을 강하게 해도 밝아지지 않을 때 (lilToon 등은 밝기 상한이 있음) 아바타 밝기를 올리세요",
                "增强灯光仍不变亮时（lilToon 等有亮度上限），请调高虚拟形象亮度",
                "增強燈光仍不變亮時（lilToon 等有亮度上限），請調高虛擬形象亮度"));
        }

        private void DrawAmbientSlider()
        {
            float ambient = GuiControls.Slider(
                Loc.T("Ambient", "環境光", "환경광", "环境光", "環境光"), _rendering.AmbientIntensity, 0f,
                AppSettings.MaxAmbientIntensity);
            if (!Mathf.Approximately(ambient, _rendering.AmbientIntensity))
            {
                _rendering.AmbientIntensity = ambient;
            }
        }

        private void DrawKeyLightSliders(string intensityLabel, string temperatureLabel)
        {
            float intensity = GuiControls.Slider(intensityLabel, _rendering.LightIntensity, 0f, MaxSunlight);
            if (!Mathf.Approximately(intensity, _rendering.LightIntensity))
            {
                _rendering.LightIntensity = intensity;
            }

            float temperature = GuiControls.Slider(temperatureLabel,
                _rendering.LightTemperature, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature, "F0");
            if (!Mathf.Approximately(temperature, _rendering.LightTemperature))
            {
                _rendering.LightTemperature = Mathf.Round(temperature / 100f) * 100f;
            }

            float yaw = GuiControls.Slider(
                Loc.T("Direction", "向き", "방향", "方向", "方向"), _rendering.LightYaw, -180f, 180f, "F0");
            if (!Mathf.Approximately(yaw, _rendering.LightYaw))
            {
                _rendering.LightYaw = yaw;
            }

            float pitch = GuiControls.Slider(
                Loc.T("Height", "高さ", "높이", "高度", "高度"), _rendering.LightPitch, -90f, 90f, "F0");
            if (!Mathf.Approximately(pitch, _rendering.LightPitch))
            {
                _rendering.LightPitch = pitch;
            }
        }

        private void DrawFillSliders()
        {
            float intensity = GuiControls.Slider(Loc.T("Intensity", "強さ", "세기", "强度", "強度"),
                _rendering.FillIntensity, 0f, MaxSunlight);
            if (!Mathf.Approximately(intensity, _rendering.FillIntensity))
            {
                _rendering.FillIntensity = intensity;
            }

            float temperature = GuiControls.Slider(Loc.T("Color (K)", "色 (K)", "색 (K)", "颜色 (K)", "顏色 (K)"),
                _rendering.FillTemperature, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature, "F0");
            if (!Mathf.Approximately(temperature, _rendering.FillTemperature))
            {
                _rendering.FillTemperature = Mathf.Round(temperature / 100f) * 100f;
            }

            float yaw = GuiControls.Slider(Loc.T("Direction", "向き", "방향", "方向", "方向"),
                _rendering.FillYaw, -180f, 180f, "F0");
            if (!Mathf.Approximately(yaw, _rendering.FillYaw))
            {
                _rendering.FillYaw = yaw;
            }

            float pitch = GuiControls.Slider(Loc.T("Height", "高さ", "높이", "高度", "高度"),
                _rendering.FillPitch, -90f, 90f, "F0");
            if (!Mathf.Approximately(pitch, _rendering.FillPitch))
            {
                _rendering.FillPitch = pitch;
            }
        }

        private void DrawRimSliders()
        {
            float intensity = GuiControls.Slider(Loc.T("Intensity", "強さ", "세기", "强度", "強度"),
                _rendering.RimIntensity, 0f, MaxSunlight);
            if (!Mathf.Approximately(intensity, _rendering.RimIntensity))
            {
                _rendering.RimIntensity = intensity;
            }

            float temperature = GuiControls.Slider(Loc.T("Color (K)", "色 (K)", "색 (K)", "颜色 (K)", "顏色 (K)"),
                _rendering.RimTemperature, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature, "F0");
            if (!Mathf.Approximately(temperature, _rendering.RimTemperature))
            {
                _rendering.RimTemperature = Mathf.Round(temperature / 100f) * 100f;
            }

            float yaw = GuiControls.Slider(Loc.T("Direction", "向き", "방향", "方向", "方向"),
                _rendering.RimYaw, -180f, 180f, "F0");
            if (!Mathf.Approximately(yaw, _rendering.RimYaw))
            {
                _rendering.RimYaw = yaw;
            }

            float pitch = GuiControls.Slider(Loc.T("Height", "高さ", "높이", "高度", "高度"),
                _rendering.RimPitch, -90f, 90f, "F0");
            if (!Mathf.Approximately(pitch, _rendering.RimPitch))
            {
                _rendering.RimPitch = pitch;
            }
        }

        private void DrawIblSliders()
        {
            int preset = GuiControls.LabeledChoice(
                Loc.T("Preset", "プリセット", "프리셋", "预设", "預設"),
                new[]
                {
                    Loc.T("Studio", "スタジオ", "스튜디오", "工作室", "工作室"),
                    Loc.T("Warm", "暖色", "따뜻한", "暖色", "暖色"),
                    Loc.T("Cool", "寒色", "차가운", "冷色", "冷色"),
                },
                (int)_rendering.IblPreset);
            if (preset != (int)_rendering.IblPreset)
            {
                _rendering.IblPreset = (IblPreset)preset;
            }

            float ibl = GuiControls.Slider(Loc.T("IBL strength", "環境光の強さ", "환경광 세기", "环境光强度", "環境光強度"),
                _rendering.IblIntensity, 0f, AppSettings.MaxIblIntensity);
            if (!Mathf.Approximately(ibl, _rendering.IblIntensity))
            {
                _rendering.IblIntensity = ibl;
            }
        }

        private void DrawOutline()
        {
            GuiControls.BeginCard(Loc.T("Outline", "輪郭線", "윤곽선", "轮廓线", "輪廓線"));
            GuiControls.Hint(Loc.T(
                "Scales the avatar's outline (lilToon / MToon / Poiyomi / UTS). 1 = as set in the material, 0 = no outline. Remembered for each avatar.",
                "アバターの輪郭線（lilToon / MToon / Poiyomi / UTS）の太さを変えます。1 = マテリアルのまま、0 = 輪郭線なし。アバターごとに記憶されます。",
                "아바타 윤곽선 (lilToon / MToon / Poiyomi / UTS)의 굵기를 바꿉니다. 1 = 머티리얼 그대로, 0 = 윤곽선 없음. 아바타마다 기억됩니다.",
                "调整虚拟形象轮廓线（lilToon / MToon / Poiyomi / UTS）的粗细。1 = 保持材质设置，0 = 无轮廓线。按虚拟形象分别记住。",
                "調整虛擬形象輪廓線（lilToon / MToon / Poiyomi / UTS）的粗細。1 = 保持材質設定，0 = 無輪廓線。依虛擬形象分別記住。"));

            // 太さの倍率（値が変わったときだけ反映）
            float width = GuiControls.Slider(Loc.T("Thickness", "太さ", "굵기", "粗细", "粗細"),
                _rendering.OutlineWidth, 0f, AppSettings.MaxOutlineWidth);
            if (!Mathf.Approximately(width, _rendering.OutlineWidth))
            {
                _rendering.OutlineWidth = width;
            }

            // マテリアルのままの太さに戻す
            if (GUILayout.Button(Loc.T("As in material (1)", "マテリアルのまま（1）", "머티리얼 그대로 (1)", "保持材质（1）", "保持材質（1）")))
            {
                _rendering.OutlineWidth = 1f;
            }

            // 輪郭線の無いアバター（または未対応のシェーダー）ではスライダーが効かないことを案内
            if (!_rendering.HasAvatarOutline)
            {
                GuiControls.Hint(Loc.T("This avatar has no outline this slider can change.",
                    "このアバターには、ここで太さを変えられる輪郭線がありません。",
                    "이 아바타에는 여기서 굵기를 바꿀 수 있는 윤곽선이 없습니다.",
                    "此虚拟形象没有可在此调整粗细的轮廓线。",
                    "此虛擬形象沒有可在此調整粗細的輪廓線。"));
            }

            GuiControls.EndCard();
        }

        private void DrawResetDefaults()
        {
            GuiControls.BeginCard(Loc.T("Reset", "リセット", "초기화", "重置", "重設"));
            GuiControls.Hint(Loc.T(
                "Restores graphics settings only (background, anti-aliasing, lighting, outline). Pose and other tabs are kept.",
                "グラフィックの設定だけを既定に戻します（背景・アンチエイリアス・ライト・輪郭線）。ポーズなど他のタブはそのままです。",
                "그래픽 설정만 기본값으로 돌립니다(배경·안티앨리어싱·조명·윤곽선). 포즈 등 다른 탭은 그대로입니다.",
                "仅将图形设置恢复为默认（背景、抗锯齿、光照、轮廓线）。姿势等其他页保持不变。",
                "僅將圖形設定恢復為預設（背景、抗鋸齒、光照、輪廓線）。姿勢等其他分頁保持不變。"));

            if (_confirmingReset)
            {
                GuiControls.Warning(Loc.T(
                    "Reset all graphics settings to defaults?",
                    "グラフィックの設定をすべて既定に戻しますか？",
                    "그래픽 설정을 모두 기본값으로 되돌릴까요?",
                    "要将所有图形设置恢复为默认吗？",
                    "要將所有圖形設定恢復為預設嗎？"));
                GUILayout.BeginHorizontal();
                if (GuiControls.DangerButton(Loc.T("Reset to defaults", "既定に戻す", "기본값으로", "恢复默认", "恢復預設"),
                        GuiControls.Shrinkable))
                {
                    _settings.ResetGraphicsToDefaults();
                    _rendering.ApplyAll();
                    _reapply();
                    _confirmingReset = false;
                }

                if (GUILayout.Button(Loc.T("Cancel", "やめる", "취소", "取消", "取消"), GuiControls.Shrinkable))
                {
                    _confirmingReset = false;
                }

                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button(Loc.T("Reset graphics to defaults...", "グラフィックを既定に戻す...",
                         "그래픽 기본값으로...", "将图形恢复为默认...", "將圖形恢復為預設...")))
            {
                _confirmingReset = true;
            }

            GuiControls.EndCard();
        }
    }
}
