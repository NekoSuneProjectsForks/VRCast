using System;
using UnityEngine;
using VRCast.Core;
using VRCast.Rendering;
using VRCast.Remote;

namespace VRCast.UI
{
    /// <summary>
    /// 設定タブ（パネルの見た目: 表示言語・UI の大きさ・背景の不透明度・テーマ（ライト / ダーク）、
    /// 設定プリセットの書き出し・読み込み、全設定のリセット（2 段階確認））。
    /// </summary>
    public class SettingsSection
    {
        // UI の大きさのプリセット（スライダーだとドラッグ中にパネルが伸縮して操作しにくいためボタンで選ぶ）
        private static readonly float[] ScalePresets = { 0.75f, 1f, 1.25f, 1.5f, 2f };
        private static readonly string[] ScaleLabels = BuildScaleLabels();

        private readonly AppSettings _settings;
        private readonly RenderingController _rendering;
        private readonly Action _resetAll;
        private readonly PresetSection _presets;
        private readonly OscParameterSender _oscParameterSender = new OscParameterSender();
        private string _oscOutputAddress;
        private int _oscOutputPort = -1;
        private float _nextOscUiRetryTime;
        private string _oscAddressText;
        private string _oscPortText;

        // リセットの確認中か、直前にリセットしたか
        private bool _confirmingReset;
        private bool _resetDone;

        /// <param name="resetAll">全設定を既定値に戻して各機能へ反映する処理</param>
        /// <param name="presets">設定プリセットのカード（ドロップでも開くため MainPanel と共有する）</param>
        public SettingsSection(AppSettings settings, RenderingController rendering, Action resetAll, PresetSection presets)
        {
            _settings = settings;
            _rendering = rendering;
            _resetAll = resetAll;
            _presets = presets;
            _oscAddressText = settings.oscParameterOutputAddress ?? "127.0.0.1";
            _oscPortText = settings.oscParameterOutputPort.ToString();
        }

        public void Draw()
        {
            DrawPanel();
            DrawOscParameterOutput();
            _presets.Draw();
            DrawReset();
        }

        public void Dispose()
        {
            _oscParameterSender.Dispose();
        }

        private void DrawOscParameterOutput()
        {
            GuiControls.BeginCard("VRChat OSC output");
            GuiControls.Hint(
                "Send OSC avatar-parameter messages to VRChat. This is outbound parameter control only; it does not send skeletal full-body, head, hand, or VRCFT tracking data.");

            bool enabled = GUILayout.Toggle(_settings.oscParameterOutputEnabled, "Enable avatar-parameter output");
            if (enabled != _settings.oscParameterOutputEnabled)
            {
                _settings.oscParameterOutputEnabled = enabled;
            }

            GUILayout.Label("Destination IP address");
            _oscAddressText = GUILayout.TextField(_oscAddressText ?? string.Empty);

            GUILayout.Label("Destination UDP port");
            _oscPortText = GUILayout.TextField(_oscPortText ?? string.Empty);
            if (GUILayout.Button("Apply destination"))
            {
                if (int.TryParse(_oscPortText, out int port) && port >= 1 && port <= 65535
                    && !string.IsNullOrWhiteSpace(_oscAddressText))
                {
                    _settings.oscParameterOutputAddress = _oscAddressText.Trim();
                    _settings.oscParameterOutputPort = port;
                }
            }

            if (!_settings.oscParameterOutputEnabled)
            {
                _oscParameterSender.Stop();
                _oscOutputAddress = null;
                _oscOutputPort = -1;
            }
            else if (_oscOutputAddress != _settings.oscParameterOutputAddress
                || _oscOutputPort != _settings.oscParameterOutputPort
                || (!_oscParameterSender.IsEnabled && Time.unscaledTime >= _nextOscUiRetryTime))
            {
                _oscParameterSender.Start(
                    _settings.oscParameterOutputAddress, _settings.oscParameterOutputPort);
                _oscOutputAddress = _settings.oscParameterOutputAddress;
                _oscOutputPort = _settings.oscParameterOutputPort;
                _nextOscUiRetryTime = Time.unscaledTime + 3f;
            }

            GUILayout.Label("Live face parameter mappings (blank disables a mapping)");
            GUILayout.Label("Left eye open");
            _settings.oscParameterEyeOpenLeft = GUILayout.TextField(_settings.oscParameterEyeOpenLeft ?? string.Empty);
            GUILayout.Label("Right eye open");
            _settings.oscParameterEyeOpenRight = GUILayout.TextField(_settings.oscParameterEyeOpenRight ?? string.Empty);
            GUILayout.Label("Mouth open");
            _settings.oscParameterMouthOpen = GUILayout.TextField(_settings.oscParameterMouthOpen ?? string.Empty);
            GUILayout.Label("Smile (providers with expression scores)");
            _settings.oscParameterSmile = GUILayout.TextField(_settings.oscParameterSmile ?? string.Empty);
            GuiControls.Hint(
                "Mapped values are sent at up to 30 updates per second while fresh face tracking is available. Use parameter names that exist in the avatar's Expressions/FX setup. This does not forward skeletal head, body, or hand motion.");

            GUILayout.Label("Status: " + _oscParameterSender.Status);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Send test float"))
            {
                _oscParameterSender.SendFloat("VRCastTest", 0.5f);
            }

            if (GUILayout.Button("Send test bool"))
            {
                _oscParameterSender.SendBool("VRCastTestToggle", true);
            }

            GUILayout.EndHorizontal();
            GuiControls.Hint(
                "Test messages use /avatar/parameters/VRCastTest (0.5) and /avatar/parameters/VRCastTestToggle (true). Create matching parameters in the avatar's FX/Expressions setup to observe them.");
            GuiControls.EndCard();
        }

        private void DrawPanel()
        {
            GuiControls.BeginCard(Loc.T("Panel", "パネル", "패널", "面板", "面板"));

            // 表示言語（Auto は OS の言語。日本語・韓国語・中国語以外は英語。並びは UiLanguage と同じ）
            string[] languages = Loc.LanguageLabels(
                Loc.T("Auto (OS)", "自動（OS に合わせる）", "자동 (OS에 맞춤)", "自动（跟随系统）", "自動（跟隨系統）"));
            _settings.uiLanguage = (UiLanguage)GuiControls.EnumSelector(
                Loc.T("Display language", "表示言語", "표시 언어", "显示语言", "顯示語言"),
                languages, (int)_settings.uiLanguage);

            // UI の大きさ（選択中のプリセットはアクセント色。どれにも当たらなければ未選択。2 行にして幅不足でも見切れにくくする）
            int scale = Array.FindIndex(ScalePresets, preset => Mathf.Approximately(_settings.uiScale, preset));
            GUILayout.Label(Loc.T("UI size", "UI の大きさ", "UI 크기", "界面大小", "介面大小"));
            int selectedScale = DrawScaleGrid(scale);
            if (selectedScale != scale && selectedScale >= 0)
            {
                _settings.uiScale = ScalePresets[selectedScale];
            }

            // 背景（ウィンドウ・カード・ボタン）だけを透かし、文字は読めるまま残す（パーセントで表示）
            float percent = GuiControls.Slider(
                Loc.T("Panel opacity", "パネルの不透明度", "패널 불투명도", "面板不透明度", "面板不透明度"),
                _settings.uiOpacity * 100f, AppSettings.MinUiOpacity * 100f, AppSettings.MaxUiOpacity * 100f, "F0");
            _settings.uiOpacity = Mathf.Round(percent) / 100f;
            GuiControls.Hint(Loc.T(
                "Lower it to see the avatar through the panel. Text stays readable. Also applies to the perfect sync editor.",
                "下げるとパネル越しにアバターが透けて見えます。文字は薄くなりません。パーフェクトシンク化の編集画面にも効きます。",
                "낮추면 패널 너머로 아바타가 비쳐 보입니다. 글자는 흐려지지 않습니다. 퍼펙트 싱크화 편집 화면에도 적용됩니다.",
                "调低后可透过面板看到虚拟形象。文字不会变淡。也适用于完美同步化的编辑画面。",
                "調低後可透過面板看到虛擬形象。文字不會變淡。也適用於完美同步化的編輯畫面。"));

            DrawTheme();
            GuiControls.EndCard();
        }

        private void DrawTheme()
        {
            // 選択中はアクセント色（パネルの配色は次の Layout で MainPanel が作り直す。背景色は既定色のときだけ追従）
            string[] labels =
            {
                Loc.T("Light", "ライト", "라이트", "浅色", "淺色"),
                Loc.T("Dark", "ダーク", "다크", "深色", "深色"),
            };
            int current = _rendering.DarkMode ? 1 : 0;
            int selected = GuiControls.LabeledChoice(Loc.T("Theme", "テーマ", "테마", "主题", "主題"), labels, current);
            if (selected != current)
            {
                _rendering.DarkMode = selected == 1;
            }

            GuiControls.Hint(Loc.T(
                "The background color follows the theme unless you changed it. Same switch is on the Graphics tab (Background).",
                "背景色は変更していなければテーマに合わせて切り替わります。同じ切替はグラフィックタブの背景にもあります。",
                "배경색을 바꾸지 않았다면 테마에 맞춰 바뀝니다. 같은 전환은 그래픽 탭의 배경에도 있습니다.",
                "若未更改背景色，会随主题切换。图形标签页的背景处也有相同切换。",
                "若未變更背景色，會隨主題切換。圖形分頁的背景處也有相同切換。"));
        }

        private void DrawReset()
        {
            GuiControls.BeginCard(Loc.T("Reset", "リセット", "초기화", "重置", "重設"));
            GuiControls.Hint(Loc.T(
                "Restore every setting to its default (window size, recent avatars, each avatar's camera and " +
                "other avatars' light and pose are kept)",
                "全ての設定を初期状態に戻します（ウィンドウサイズ・最近使ったアバター・アバターごとのカメラと、" +
                "表示中以外のアバターに記憶したライト・待機ポーズは残ります）",
                "모든 설정을 초기 상태로 되돌립니다 (창 크기, 최근 사용한 아바타, 아바타별 카메라와 " +
                "표시 중이 아닌 아바타에 기억한 조명·대기 포즈는 유지됩니다)",
                "将所有设置恢复为默认值（窗口大小、最近使用的虚拟形象、各虚拟形象的相机，" +
                "以及当前未显示的虚拟形象记住的灯光和待机姿势会保留）",
                "將所有設定恢復為預設值（視窗大小、最近使用的虛擬形象、各虛擬形象的相機，" +
                "以及目前未顯示的虛擬形象記住的燈光和待機姿勢會保留）"));

            // 1 段階目: リセットを押すと確認を出す
            if (!_confirmingReset)
            {
                if (GuiControls.DangerButton(Loc.T("Reset all settings", "全ての設定をリセット", "모든 설정 초기화",
                        "重置所有设置", "重設所有設定")))
                {
                    _confirmingReset = true;
                    _resetDone = false;
                }

                // 直前のリセット結果
                if (_resetDone)
                {
                    GuiControls.Hint(Loc.T("All settings were reset.", "全ての設定をリセットしました。",
                        "모든 설정을 초기화했습니다.", "已重置所有设置。", "已重設所有設定。"));
                }

                GuiControls.EndCard();
                return;
            }

            // 2 段階目: 本当にリセットするか確認
            GUILayout.Label(Loc.T("Are you sure? This cannot be undone.", "本当にリセットしますか？元に戻せません。",
                "정말 초기화하시겠습니까? 되돌릴 수 없습니다.", "确定要重置吗？此操作无法撤销。", "確定要重設嗎？此操作無法復原。"));
            GUILayout.BeginHorizontal();
            if (GuiControls.DangerButton(Loc.T("Yes, reset", "リセットする", "초기화하기", "确定重置", "確定重設")))
            {
                _resetAll();
                _confirmingReset = false;
                _resetDone = true;
            }

            if (GUILayout.Button(Loc.T("Cancel", "キャンセル", "취소", "取消", "取消")))
            {
                _confirmingReset = false;
            }

            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }

        private static string[] BuildScaleLabels()
        {
            // 倍率をパーセントの文字列にする（毎フレーム作らない）
            var labels = new string[ScalePresets.Length];
            for (int i = 0; i < ScalePresets.Length; i++)
            {
                labels[i] = $"{ScalePresets[i] * 100f:F0}%";
            }

            return labels;
        }

        /// <summary>
        /// UI 大きさのボタンを 3+2 の格子で並べ、幅が足りても見切れないようにする。
        /// </summary>
        private static int DrawScaleGrid(int selected)
        {
            const int columns = 3;
            for (int start = 0; start < ScaleLabels.Length; start += columns)
            {
                GUILayout.BeginHorizontal();
                int end = Mathf.Min(start + columns, ScaleLabels.Length);
                for (int i = start; i < end; i++)
                {
                    if (GUILayout.Toggle(selected == i, ScaleLabels[i], GUI.skin.button,
                            GUILayout.ExpandWidth(true), GuiControls.Shrinkable)
                        && selected != i)
                    {
                        selected = i;
                    }
                }

                GUILayout.EndHorizontal();
            }

            return selected;
        }
    }
}
