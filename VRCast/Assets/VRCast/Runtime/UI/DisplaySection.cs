using UnityEngine;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// 表示タブ（カメラの固定・画角・リセット、解像度）。背景・ライト・輪郭線はグラフィックタブ（GraphicsSection）。
    /// </summary>
    public class DisplaySection
    {
        // FOV スライダーの範囲
        private const float MinFov = 10f;
        private const float MaxFov = 90f;

        // 解像度プリセット（横長 / 縦長配信）
        private static readonly Vector2Int[] ResolutionPresets =
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1920, 1080),
            new Vector2Int(720, 1280),
            new Vector2Int(1080, 1920),
        };

        private readonly OrbitCameraController _orbit;
        private readonly RenderingController _rendering;
        private readonly AppSettings _settings;

        public DisplaySection(OrbitCameraController orbit, RenderingController rendering, AppSettings settings)
        {
            _orbit = orbit;
            _rendering = rendering;
            _settings = settings;
        }

        public void Draw()
        {
            DrawCamera();
            DrawResolution();
        }

        private void DrawCamera()
        {
            GuiControls.BeginCard(Loc.T("Camera", "カメラ", "카메라", "相机", "相機"));
            GuiControls.Hint(Loc.T("Right drag: rotate / Middle drag: pan / Wheel: zoom",
                "右ドラッグ: 回転 / 中ドラッグ: 移動 / ホイール: ズーム",
                "오른쪽 드래그: 회전 / 가운데 드래그: 이동 / 휠: 확대·축소",
                "右键拖动：旋转 / 中键拖动：平移 / 滚轮：缩放",
                "右鍵拖曳：旋轉 / 中鍵拖曳：平移 / 滾輪：縮放"));
            GuiControls.Hint(Loc.T(
                "The camera position and field of view are remembered for each avatar, also after restarting.",
                "カメラの位置と画角はアバターごとに記憶され、再起動後も戻ります。",
                "카메라 위치와 화각은 아바타마다 기억되며, 다시 시작한 후에도 유지됩니다.",
                "相机位置和视野会按虚拟形象分别记住，重启后也会恢复。",
                "相機位置和視野會依虛擬形象分別記住，重新啟動後也會恢復。"));

            // 位置を決めた後にマウス操作で動かしてしまわないよう固定する（画角とリセットは固定中も使える）
            _settings.cameraLocked = GUILayout.Toggle(_settings.cameraLocked,
                Loc.T("Lock camera (ignore mouse drag and wheel)", "カメラを固定（マウスのドラッグ・ホイールで動かさない）",
                    "카메라 고정 (마우스 드래그·휠로 움직이지 않음)", "锁定相机（不响应鼠标拖动和滚轮）",
                    "鎖定相機（不回應滑鼠拖曳和滾輪）"));
            if (_settings.cameraLocked)
            {
                GuiControls.Hint(Loc.T("Field of view and Reset camera still work while locked.",
                    "固定中も画角の変更とカメラのリセットは使えます。",
                    "고정 중에도 화각 변경과 카메라 초기화는 사용할 수 있습니다.",
                    "锁定时仍可更改视野和重置相机。",
                    "鎖定時仍可變更視野和重設相機。"));
            }

            // 近づけても体に入り込まない（配信中に体の中が映る事故を防ぐ）
            _settings.cameraZoomInsteadOfEntering = GUILayout.Toggle(_settings.cameraZoomInsteadOfEntering,
                Loc.T("Don't go inside the avatar (zoom in instead)", "体に入り込まない（近づくと画角で拡大）",
                    "아바타 안으로 들어가지 않기 (가까이 가면 화각으로 확대)", "不进入虚拟形象内部（靠近时用视野放大）",
                    "不進入虛擬形象內部（靠近時用視野放大）"));
            GuiControls.Hint(Loc.T(
                "The camera stops before entering the body; zooming in further narrows the view for a close-up. Turn off to move the camera freely.",
                "体に入り込む手前でカメラを止め、それ以上は画角を狭めて超アップにします。OFF にすると自由に近づけます。",
                "몸 안으로 들어가기 전에 카메라를 멈추고, 더 확대하면 화각을 좁혀 클로즈업합니다. 끄면 자유롭게 다가갈 수 있습니다.",
                "相机会在进入身体前停下，继续放大时会缩小视野来特写。关闭后可自由靠近。",
                "相機會在進入身體前停下，繼續放大時會縮小視野來特寫。關閉後可自由靠近。"));

            _orbit.FieldOfView = GuiControls.Slider(
                Loc.T("Field of view", "画角", "화각", "视野", "視野"), _orbit.FieldOfView, MinFov, MaxFov);

            // 視点リセット
            if (GUILayout.Button(Loc.T("Reset camera", "カメラをリセット", "카메라 초기화", "重置相机", "重設相機")))
            {
                _orbit.ResetView();
            }

            GuiControls.EndCard();
        }

        private void DrawResolution()
        {
            GuiControls.BeginCard(Loc.T("Resolution", "解像度", "해상도", "分辨率", "解析度"));
            GuiControls.Hint(Loc.T("Current", "現在", "현재", "当前", "目前") + $": {_rendering.Width} x {_rendering.Height}");

            // プリセットボタン（2 列）
            for (int i = 0; i < ResolutionPresets.Length; i++)
            {
                // 行の開始
                if (i % 2 == 0)
                {
                    GUILayout.BeginHorizontal();
                }

                Vector2Int preset = ResolutionPresets[i];
                if (GUILayout.Button($"{preset.x} x {preset.y}", GuiControls.Shrinkable))
                {
                    _rendering.SetResolution(preset.x, preset.y);
                }

                // 行の終了（最終要素でも閉じる）
                if (i % 2 == 1 || i == ResolutionPresets.Length - 1)
                {
                    GUILayout.EndHorizontal();
                }
            }

            GuiControls.EndCard();
        }
    }
}
