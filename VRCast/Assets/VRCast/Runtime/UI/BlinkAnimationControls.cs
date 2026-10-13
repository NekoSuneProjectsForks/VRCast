using System.IO;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// 顔・表情タブの自動まばたきの欄で、自動まばたきに使う .anim をアバターごとに選ぶ行（選ぶ・解除・読み込みの結果）。
    /// </summary>
    public sealed class BlinkAnimationControls
    {
        private const float ButtonWidth = 80f;

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;

        // 表示中のアバターのパスと、そのアバターの .anim のパス・読み込めなかった理由と対象のファイル名
        private string _avatarPath;
        private string _path = string.Empty;
        private BlinkAnimationError _error;
        private string _errorName = string.Empty;

        // 最後に選んだファイル（ダイアログの最初のフォルダ。起動中だけ覚える）
        private string _lastPath = string.Empty;

        public BlinkAnimationControls(AvatarSession session, AvatarComponentCache avatar, AppSettings settings)
        {
            _session = session;
            _avatar = avatar;
            _settings = settings;
        }

        public void Draw()
        {
            // アバター未表示なら出さない（指定はアバターごと）
            var blink = _avatar.Get<BlinkController>();
            LoadedAvatar current = _session.Current;
            if (blink == null || current == null)
            {
                return;
            }

            // アバターが替わったら、そのアバターの指定を読み直す（読み込みに失敗していれば理由を出すため読み直す）
            if (_avatarPath != current.SourcePath)
            {
                _avatarPath = current.SourcePath;
                _path = _settings.GetBlinkAnimation(_avatarPath);
                _error = BlinkAnimationError.None;
                if (_path.Length > 0 && blink.Animation == null)
                {
                    BlinkAnimation.TryLoad(_path, out _, out _error);
                    _errorName = Path.GetFileName(_path);
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("  " + Loc.T("Animation (.anim)", "アニメーション（.anim）", "애니메이션 (.anim)", "动画（.anim）",
                "動畫（.anim）"), GUILayout.ExpandWidth(false));
            UiTheme theme = UiTheme.Current;
            GUILayout.Label(_path.Length > 0 ? Path.GetFileName(_path) : Loc.T("None", "なし", "없음", "无", "無"),
                theme != null ? theme.Value : GUI.skin.label, GuiControls.Shrinkable);

            GUI.enabled = FileDialog.IsSupported;
            if (GUILayout.Button(Loc.T("Choose...", "選ぶ...", "선택...", "选择...", "選擇..."), GUILayout.Width(ButtonWidth)))
            {
                Browse(blink);
            }

            // 外す（未指定なら押せない）
            GUI.enabled = _path.Length > 0;
            if (GUILayout.Button(Loc.T("Clear", "解除", "해제", "清除", "清除"), GUILayout.Width(ButtonWidth)))
            {
                _path = string.Empty;
                _error = BlinkAnimationError.None;
                _settings.SetBlinkAnimation(_avatarPath, string.Empty);
                blink.SetAnimation(null);
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            DrawStatus(blink);
        }

        private void DrawStatus(BlinkController blink)
        {
            if (_error != BlinkAnimationError.None)
            {
                GuiControls.Warning(ErrorText(_error) + $": {_errorName}");
                return;
            }

            BlinkAnimation animation = blink.Animation;
            if (animation == null)
            {
                GuiControls.Hint(Loc.T(
                    "This avatar only. Choose a blink animation made in Unity (text format, using blend shapes) to use it instead of the eyelid blend shapes.",
                    "このアバターだけの指定です。Unity で作ったまばたきのアニメーション（テキスト形式、BlendShape を動かすもの）を選ぶと、まぶたの BlendShape の代わりに使います。",
                    "이 아바타만의 지정입니다. Unity로 만든 눈 깜빡임 애니메이션 (텍스트 형식, BlendShape를 움직이는 것)을 선택하면 눈꺼풀 BlendShape 대신 사용합니다.",
                    "仅此虚拟形象。选择在 Unity 中制作的眨眼动画（文本格式、驱动 BlendShape）后，将代替眼睑的 BlendShape 使用。",
                    "僅此虛擬形象。選擇在 Unity 中製作的眨眼動畫（文字格式、驅動 BlendShape）後，將代替眼瞼的 BlendShape 使用。"));
                return;
            }

            if (blink.AnimationTargetCount == 0)
            {
                GuiControls.Warning(Loc.T("⚠ None of the animation's blend shapes were found on this avatar, so the eyes do not blink.",
                    "⚠ アニメーションの BlendShape がこのアバターに見つからないため、まばたきしません。",
                    "⚠ 애니메이션의 BlendShape를 이 아바타에서 찾지 못해 눈을 깜빡이지 않습니다.",
                    "⚠ 在此虚拟形象上找不到动画的 BlendShape，因此不会眨眼。",
                    "⚠ 在此虛擬形象上找不到動畫的 BlendShape，因此不會眨眼。"));
                return;
            }

            int count = blink.AnimationTargetCount;
            if (animation.IsPose)
            {
                GuiControls.Hint(Loc.T(
                    $"A single-frame animation ({count} blend shapes): used as the closed-eye shape, moved with the times below.",
                    $"1 コマだけのアニメーション（BlendShape {count} 個）なので、目を閉じた形として下の時間で動かします。",
                    $"한 프레임뿐인 애니메이션 (BlendShape {count}개)이므로 눈을 감은 모양으로 아래 시간에 맞춰 움직입니다.",
                    $"单帧动画（{count} 个 BlendShape），作为闭眼形状按下方时间运动。",
                    $"單幀動畫（{count} 個 BlendShape），作為閉眼形狀按下方時間運動。"));
            }
            else
            {
                float length = animation.Length;
                GuiControls.Hint(Loc.T(
                    $"Plays the {length:F2} s animation ({count} blend shapes) for each blink.",
                    $"まばたきごとに {length:F2} 秒のアニメーション（BlendShape {count} 個）を再生します。",
                    $"깜빡일 때마다 {length:F2}초 애니메이션 (BlendShape {count}개)을 재생합니다.",
                    $"每次眨眼播放 {length:F2} 秒的动画（{count} 个 BlendShape）。",
                    $"每次眨眼播放 {length:F2} 秒的動畫（{count} 個 BlendShape）。"));
            }
        }

        private void Browse(BlinkController blink)
        {
            // 最初は前回のフォルダ、無ければアバターのフォルダ
            string initial = _lastPath.Length > 0 ? _lastPath : _path.Length > 0 ? _path : _avatarPath ?? string.Empty;
            string path = FileDialog.OpenFile(
                Loc.T("Choose a blink animation", "まばたきのアニメーションを選ぶ", "눈 깜빡임 애니메이션 선택",
                    "选择眨眼动画", "選擇眨眼動畫"),
                Loc.T("Unity animation", "Unity のアニメーション", "Unity 애니메이션", "Unity 动画", "Unity 動畫"),
                new[] { ".anim" }, initial);
            if (path == null)
            {
                return;
            }

            _lastPath = path;
            _errorName = Path.GetFileName(path);

            // 読めたときだけ指定を替える（読めなければ今の指定のまま理由を出す）
            if (!BlinkAnimation.TryLoad(path, out BlinkAnimation animation, out _error))
            {
                return;
            }

            _path = path;
            _settings.SetBlinkAnimation(_avatarPath, path);
            blink.SetAnimation(animation);
        }

        private static string ErrorText(BlinkAnimationError error)
        {
            switch (error)
            {
                case BlinkAnimationError.TooLarge:
                    return Loc.T("The file is too large", "ファイルが大きすぎます", "파일이 너무 큽니다", "文件过大", "檔案過大");
                case BlinkAnimationError.NotText:
                    return Loc.T("Binary .anim files cannot be read (save it as text in Unity)",
                        "バイナリ形式の .anim は読めません（Unity でテキスト形式で保存してください）",
                        "바이너리 형식의 .anim은 읽을 수 없습니다 (Unity에서 텍스트 형식으로 저장하세요)",
                        "无法读取二进制格式的 .anim（请在 Unity 中以文本格式保存）",
                        "無法讀取二進位格式的 .anim（請在 Unity 中以文字格式儲存）");
                case BlinkAnimationError.NotAnimationClip:
                    return Loc.T("This is not an animation clip", "アニメーションクリップではありません", "애니메이션 클립이 아닙니다",
                        "这不是动画剪辑", "這不是動畫片段");
                case BlinkAnimationError.Malformed:
                    return Loc.T("The animation could not be read", "アニメーションを読み取れませんでした", "애니메이션을 읽지 못했습니다",
                        "无法读取动画", "無法讀取動畫");
                case BlinkAnimationError.NoBlendShapes:
                    return Loc.T("The animation does not move any blend shapes", "BlendShape を動かすアニメーションではありません",
                        "BlendShape를 움직이는 애니메이션이 아닙니다", "该动画不驱动任何 BlendShape", "該動畫不驅動任何 BlendShape");
                default:
                    return Loc.T("Could not read the file", "ファイルを読み込めませんでした", "파일을 읽지 못했습니다", "无法读取文件",
                        "無法讀取檔案");
            }
        }
    }
}
