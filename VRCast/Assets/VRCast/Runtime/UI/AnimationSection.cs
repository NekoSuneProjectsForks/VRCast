using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Dynamics;

namespace VRCast.UI
{
    /// <summary>
    /// 体・ポーズタブ（待機ポーズ・待機モーション・揺れもの・小物のアニメーション）。表示中アバターのコントローラーを操作する。
    /// </summary>
    public class AnimationSection
    {
        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;
        private PoseController _pose;
        private IdleMotionController _idleMotion;

        public AnimationSection(AvatarSession session, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _settings = settings;
        }

        public void Draw()
        {
            // アバター未表示なら案内だけ（揺れものの ON/OFF は全アバター共通なので出す）
            if (!RefreshControllers())
            {
                GuiControls.BeginCard(Loc.T("Pose", "ポーズ", "포즈", "姿势", "姿勢"));
                GuiControls.Hint(Loc.T("Load an avatar first.", "先にアバターを読み込んでください。", "먼저 아바타를 불러오세요.",
                    "请先加载虚拟形象。", "請先載入虛擬形象。"));
                GuiControls.EndCard();
                DrawPhysics();
                DrawPropAnimations();
                return;
            }

            DrawPose();
            DrawIdleMotion();
            DrawPhysics();
            DrawPropAnimations();
        }

        private bool RefreshControllers()
        {
            // アバターが替わったときだけ取り直す
            if (_avatar.Refresh())
            {
                _pose = _avatar.Get<PoseController>();
                _idleMotion = _avatar.Get<IdleMotionController>();
            }

            return _avatar.HasAvatar;
        }

        private void DrawPose()
        {
            GuiControls.BeginCard(Loc.T("Pose", "ポーズ", "포즈", "姿势", "姿勢"));
            if (_pose == null)
            {
                GuiControls.EndCard();
                return;
            }

            GuiControls.Hint(Loc.T("Body yaw and arm pose are remembered for each avatar.",
                "体の向きと腕のポーズはアバターごとに記憶されます。",
                "몸 방향과 팔 포즈는 아바타마다 기억됩니다.",
                "身体朝向和手臂姿势会按虚拟形象分别记住。",
                "身體朝向和手臂姿勢會依虛擬形象分別記住。"));

            // アバターの向き（非 Humanoid でも有効）
            _pose.BodyYaw = GuiControls.Slider(Loc.T("Body yaw", "体の向き", "몸 방향", "身体朝向", "身體朝向"), _pose.BodyYaw, -180f, 180f);

            // 非 Humanoid は腕の操作不可
            if (!_pose.IsAvailable)
            {
                GuiControls.Hint(Loc.T("Arm pose requires a Humanoid avatar.", "腕のポーズは Humanoid アバターのみ対応です。",
                    "팔 포즈는 Humanoid 아바타만 지원합니다.",
                    "手臂姿势仅支持 Humanoid 虚拟形象。", "手臂姿勢僅支援 Humanoid 虛擬形象。"));
                GuiControls.EndCard();
                return;
            }

            // よく使う 3 状態へのショートカット
            GUILayout.BeginHorizontal();

            // 気を付け（既定）: 腕を下ろし切り、肘はまっすぐ
            if (GUILayout.Button(Loc.T("Attention", "気を付け", "차렷", "立正", "立正"), GuiControls.Shrinkable))
            {
                _pose.ArmDown = 1f;
                _pose.ElbowBend = 0f;
            }

            // 腕を少し開き、肘を軽く曲げる
            if (GUILayout.Button(Loc.T("Relaxed", "リラックス", "편안하게", "放松", "放鬆"), GuiControls.Shrinkable))
            {
                _pose.ArmDown = 0.85f;
                _pose.ElbowBend = 0.3f;
            }

            // 読込時の姿勢（通常 T ポーズ）
            if (GUILayout.Button(Loc.T("T-Pose", "T ポーズ", "T 포즈", "T 姿势", "T 姿勢"), GuiControls.Shrinkable))
            {
                _pose.ArmDown = 0f;
                _pose.ElbowBend = 0f;
            }

            GUILayout.EndHorizontal();

            // 腕と肘の度合い
            _pose.ArmDown = GuiControls.Slider(Loc.T("Arms down", "腕を下ろす", "팔 내리기", "放下手臂", "放下手臂"), _pose.ArmDown, 0f, 1f);
            _pose.ElbowBend = GuiControls.Slider(Loc.T("Elbow bend", "肘の曲げ", "팔꿈치 굽힘", "肘部弯曲", "肘部彎曲"), _pose.ElbowBend, 0f, 1f);
            GuiControls.EndCard();
        }

        private void DrawIdleMotion()
        {
            GuiControls.BeginCard(Loc.T("Idle motion", "待機モーション", "대기 모션", "待机动作", "待機動作"));

            // 非 Humanoid は動かせるボーンが無い
            if (_idleMotion == null || !_idleMotion.IsAvailable)
            {
                GuiControls.Hint(Loc.T("Idle motion requires a Humanoid avatar.", "待機モーションは Humanoid アバターのみ対応です。",
                    "대기 모션은 Humanoid 아바타만 지원합니다.",
                    "待机动作仅支持 Humanoid 虚拟形象。", "待機動作僅支援 Humanoid 虛擬形象。"));
                GuiControls.EndCard();
                return;
            }

            _settings.idleMotionEnabled = GUILayout.Toggle(
                _settings.idleMotionEnabled, Loc.T("Breathing and sway", "呼吸・体の揺れ", "호흡 · 몸 흔들림",
                    "呼吸 · 身体摇摆", "呼吸 · 身體搖擺"));

            // OFF の間は強さの調整を出さない
            if (!_settings.idleMotionEnabled)
            {
                GuiControls.EndCard();
                return;
            }

            GuiControls.Hint(Loc.T("Head motion pauses while face tracking is active.",
                "顔のトラッキング中は、頭のゆらぎを止めて本人の動きに任せます。",
                "얼굴 트래킹 중에는 머리 흔들림을 멈추고 본인의 움직임을 따릅니다.",
                "面部追踪时会停止头部晃动，跟随本人的动作。",
                "臉部追蹤時會停止頭部晃動，跟隨本人的動作。"));

            // 強さ（0 = 動かさない、1 = 標準）と速さの倍率
            float max = AppSettings.MaxIdleMotionStrength;
            _settings.idleBreathing = GuiControls.Slider(
                Loc.T("Breathing", "呼吸", "호흡", "呼吸", "呼吸"), _settings.idleBreathing, 0f, max);
            _settings.idleSway = GuiControls.Slider(
                Loc.T("Body sway", "体の揺れ", "몸 흔들림", "身体摇摆", "身體搖擺"), _settings.idleSway, 0f, max);
            _settings.idleHeadMotion = GuiControls.Slider(
                Loc.T("Head motion", "頭のゆらぎ", "머리 흔들림", "头部晃动", "頭部晃動"), _settings.idleHeadMotion, 0f, max);
            _settings.idleMotionSpeed = GuiControls.Slider(
                Loc.T("Speed", "速さ", "속도", "速度", "速度"), _settings.idleMotionSpeed,
                AppSettings.MinIdleMotionSpeed, AppSettings.MaxIdleMotionSpeed);
            GuiControls.EndCard();
        }

        private void DrawPhysics()
        {
            GuiControls.BeginCard(Loc.T("Physics", "揺れもの", "흔들림", "物理摆动", "物理擺動"));
            _settings.physicsEnabled = GUILayout.Toggle(
                _settings.physicsEnabled, Loc.T("PhysBone (approx.)", "揺れもの（PhysBone 近似）", "흔들림 (PhysBone 근사)",
                    "物理摆动（PhysBone 近似）", "物理擺動（PhysBone 近似）"));

            // 揺れものが無いアバターでは注記、あればチェーン数を表示
            if (_avatar.HasAvatar)
            {
                var physics = _avatar.Get<PhysBoneSimulator>();
                int chains = physics != null ? physics.ChainCount : 0;
                GuiControls.Hint(physics != null && physics.IsAvailable
                    ? Loc.T($"{chains} chains", $"{chains} チェーン", $"{chains} 체인", $"{chains} 条链", $"{chains} 條鏈")
                    : Loc.T("No PhysBones in this avatar", "このアバターには PhysBone がありません",
                        "이 아바타에는 PhysBone이 없습니다", "此虚拟形象没有 PhysBone", "此虛擬形象沒有 PhysBone"));
            }

            GuiControls.EndCard();
        }

        private void DrawPropAnimations()
        {
            GuiControls.BeginCard(Loc.T("Prop animations", "小物のアニメーション", "소품 애니메이션", "小物件动画", "小物件動畫"));
            _settings.propAnimationsEnabled = GUILayout.Toggle(
                _settings.propAnimationsEnabled, Loc.T("Play automatically (like VRChat)", "自動で再生する（VRChat と同じ）",
                    "자동 재생 (VRChat과 동일)", "自动播放（与 VRChat 相同）", "自動播放（與 VRChat 相同）"));

            // 再生するものの数、無ければ書き出し直しの案内
            if (_avatar.HasAvatar)
            {
                var props = _avatar.Get<PropAnimationPlayer>();
                int animators = props != null ? props.AnimatorCount : 0;
                int motions = props != null ? props.FxMotionCount : 0;
                GuiControls.Hint(props != null && props.IsAvailable
                    ? Loc.T($"Prop animators: {animators}, motions from FX: {motions}",
                        $"小物の Animator: {animators}、FX の動き: {motions}",
                        $"소품 Animator: {animators}, FX 움직임: {motions}",
                        $"小物件的 Animator：{animators}，FX 动作：{motions}",
                        $"小物件的 Animator：{animators}，FX 動作：{motions}")
                    : Loc.T("No prop animations in this avatar. Avatars exported with an older exporter need to be exported again.",
                        "このアバターには小物のアニメーションがありません。以前の書き出しツールで書き出したアバターは、書き出し直すと取り込まれます。",
                        "이 아바타에는 소품 애니메이션이 없습니다. 이전 내보내기 도구로 내보낸 아바타는 다시 내보내면 포함됩니다.",
                        "此虚拟形象没有小物件动画。用旧版导出工具导出的虚拟形象，重新导出后即可包含。",
                        "此虛擬形象沒有小物件動畫。用舊版匯出工具匯出的虛擬形象，重新匯出後即可包含。"));
            }

            GuiControls.EndCard();
        }
    }
}
