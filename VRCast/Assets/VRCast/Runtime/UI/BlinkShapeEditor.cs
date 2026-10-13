using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// 顔・表情タブのまばたきのカード内で、まばたきに使う BlendShape をアバターごとに選び直す欄
    /// （両目用・左目用・右目用。自動で見つからない・違う形で閉じるアバター向け）。
    /// 候補はアバター内の全 BlendShape を名前で絞り込んで選ぶ（軽くするため見えている行だけを描く）。
    /// </summary>
    public sealed class BlinkShapeEditor
    {
        // 候補の一覧の 1 行の高さと、一覧の欄の最大の高さ
        private const float RowHeight = 26f;
        private const float MaxListHeight = 240f;

        // 行のラベル・ボタンの幅
        private const float SlotLabelWidth = 130f;
        private const float SlotButtonWidth = 80f;

        // 選び直す対象（両目・左目・右目）。-1 は選んでいない
        private const int NoSlot = -1;
        private const int SlotBoth = 0;
        private const int SlotLeft = 1;
        private const int SlotRight = 2;

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;

        // 表示中のアバターの指定と、そのアバターのパス（アバターが替わったら読み直す）
        private BlinkShapeOverride _value;
        private string _valuePath;

        // 候補の一覧を開いている対象・検索文字列・スクロール位置
        private int _picking = NoSlot;
        private string _search = string.Empty;
        private Vector2 _scroll;

        // 検索に合う候補（アバター・検索文字列が変わったときだけ作り直す）と、作ったときの条件
        private readonly List<BlendShapeLimiter.Shape> _matches = new List<BlendShapeLimiter.Shape>();
        private (BlendShapeLimiter limiter, string search) _matchesKey;

        public BlinkShapeEditor(AvatarSession session, AvatarComponentCache avatar, AppSettings settings)
        {
            _session = session;
            _avatar = avatar;
            _settings = settings;
        }

        public void Draw()
        {
            // アバター未表示なら出さない（指定はアバターごと）
            var blink = _avatar.Get<BlinkController>();
            var limiter = _avatar.Get<BlendShapeLimiter>();
            LoadedAvatar current = _session.Current;
            if (blink == null || limiter == null || current == null)
            {
                return;
            }

            // アバターが替わったら、そのアバターの指定を読み直し、開いていた一覧は閉じる
            if (_valuePath != current.SourcePath)
            {
                _valuePath = current.SourcePath;
                _value = _settings.GetBlinkShapes(_valuePath);
                _picking = NoSlot;
            }

            // 指定の ON/OFF（OFF ならアバターのまぶた設定を自動で使う）
            bool enabled = GUILayout.Toggle(_value.enabled, Loc.T(
                "Choose the blink blend shapes (this avatar only)", "まばたきの BlendShape を指定する（このアバターだけ）",
                "눈 깜빡임 BlendShape 지정 (이 아바타만)", "指定眨眼的 BlendShape（仅此虚拟形象）",
                "指定眨眼的 BlendShape（僅此虛擬形象）"));
            if (enabled != _value.enabled)
            {
                // 初めて ON にしたときは、今の自動の割り当てを初期値にする（そこから直せるように）
                if (enabled && _value.IsEmpty)
                {
                    BlinkShapeOverride automatic = blink.DescribeAutomatic();
                    _value.both = automatic.both;
                    _value.left = automatic.left;
                    _value.right = automatic.right;
                }

                _value.enabled = enabled;
                Apply(blink);
            }

            // OFF の間は選ぶ欄を出さない
            if (!_value.enabled)
            {
                return;
            }

            GuiControls.Hint(Loc.T(
                "For avatars whose eyes don't close or close with the wrong shape. The one-eye shapes (wink) are used only when both left and right are chosen.",
                "目が閉じない・違う形で閉じるアバター向けです。片目用（ウインク）は左右両方を選んだときだけ使います。",
                "눈이 감기지 않거나 다른 모양으로 감기는 아바타용입니다. 한쪽 눈용 (윙크)은 좌우 모두 선택했을 때만 사용합니다.",
                "适用于眼睛闭不上或以错误形状闭合的虚拟形象。单眼用（眨单眼）仅在左右都选择时使用。",
                "適用於眼睛閉不上或以錯誤形狀閉合的虛擬形象。單眼用（眨單眼）僅在左右都選擇時使用。"));
            DrawSlot(SlotBoth, Loc.T("Both eyes", "両目", "양쪽 눈", "双眼", "雙眼"), _value.both, blink);
            DrawSlot(SlotLeft, Loc.T("Left eye (wink)", "左目（ウインク）", "왼쪽 눈 (윙크)", "左眼（眨单眼）", "左眼（眨單眼）"),
                _value.left, blink);
            DrawSlot(SlotRight, Loc.T("Right eye (wink)", "右目（ウインク）", "오른쪽 눈 (윙크)", "右眼（眨单眼）", "右眼（眨單眼）"),
                _value.right, blink);

            // 片目用が片方だけのときは使われないことを伝える
            if (_value.left.IsSet != _value.right.IsSet)
            {
                GuiControls.Warning(Loc.T("⚠ Choose both left and right to use the one-eye shapes.",
                    "⚠ 片目用は左右両方を選ぶと使われます。", "⚠ 한쪽 눈용은 좌우 모두 선택해야 사용됩니다.",
                    "⚠ 单眼用需左右都选择才会使用。", "⚠ 單眼用需左右都選擇才會使用。"));
            }

            // 選んだ BlendShape がこのアバターで見つからなければ、まばたきしないことを伝える
            if (!blink.IsAvailable)
            {
                GuiControls.Warning(Loc.T("⚠ No usable blend shape is chosen, so the eyes do not blink.",
                    "⚠ 使える BlendShape が選ばれていないため、まばたきしません。",
                    "⚠ 사용할 수 있는 BlendShape가 선택되지 않아 눈을 깜빡이지 않습니다.",
                    "⚠ 未选择可用的 BlendShape，因此不会眨眼。", "⚠ 未選擇可用的 BlendShape，因此不會眨眼。"));
            }

            if (_picking != NoSlot)
            {
                DrawPicker(limiter, blink);
            }
        }

        private void DrawSlot(int slot, string label, BlendShapeRef value, BlinkController blink)
        {
            // 対象の名前・今の BlendShape・選ぶ（開いている行なら閉じる）・なし の 1 行
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(SlotLabelWidth));
            UiTheme theme = UiTheme.Current;
            GUILayout.Label(value.IsSet ? value.blendShape : Loc.T("None", "なし", "없음", "无", "無"),
                theme != null ? theme.Value : GUI.skin.label, GuiControls.Shrinkable);

            bool open = _picking == slot;
            if (GUILayout.Button(open
                    ? Loc.T("Close", "閉じる", "닫기", "关闭", "關閉")
                    : Loc.T("Choose", "選ぶ", "선택", "选择", "選擇"), GUILayout.Width(SlotButtonWidth)))
            {
                _picking = open ? NoSlot : slot;
                _scroll = Vector2.zero;
            }

            // 外す（未指定なら押せない）
            GUI.enabled = value.IsSet;
            if (GUILayout.Button(Loc.T("Clear", "解除", "해제", "清除", "清除"), GUILayout.Width(SlotButtonWidth)))
            {
                value.Set(null, null);
                Apply(blink);
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawPicker(BlendShapeLimiter limiter, BlinkController blink)
        {
            // 名前で絞り込み（大文字・小文字は区別しない）
            GuiControls.SubHeading(Loc.T("Pick a blend shape", "BlendShape を選んでください", "BlendShape를 선택하세요",
                "请选择 BlendShape", "請選擇 BlendShape"));
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("Search", "検索", "검색", "搜索", "搜尋"), GUILayout.ExpandWidth(false));
            _search = GUILayout.TextField(_search, GuiControls.Shrinkable);
            GUILayout.EndHorizontal();
            GuiControls.Hint(Loc.T("e.g. blink, eye, close", "例: blink、eye、まばたき、目", "예: blink, eye, close",
                "例：blink、eye、close", "例：blink、eye、close"));

            RefreshMatches(limiter);
            if (_matches.Count == 0)
            {
                GuiControls.Hint(Loc.T("No matching blend shapes", "該当する BlendShape がありません",
                    "해당하는 BlendShape가 없습니다", "没有符合的 BlendShape", "沒有符合的 BlendShape"));
                return;
            }

            // 全件分の高さを持つ欄に、見えている行だけを描く（上下の見えない行は空白で高さだけ確保する）
            float height = Mathf.Min(MaxListHeight, _matches.Count * RowHeight);
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Height(height));
            int first = Mathf.Clamp(Mathf.FloorToInt(_scroll.y / RowHeight), 0, _matches.Count);
            int last = Mathf.Min(_matches.Count, first + Mathf.CeilToInt(height / RowHeight) + 1);
            GUILayout.Space(first * RowHeight);

            BlendShapeLimiter.Shape chosen = null;
            for (int i = first; i < last; i++)
            {
                // 押した行を選ぶ（描き終えてから反映する）
                if (GUILayout.Button(_matches[i].Label, GUILayout.Height(RowHeight - 2f)))
                {
                    chosen = _matches[i];
                }
            }

            GUILayout.Space((_matches.Count - last) * RowHeight);
            GUILayout.EndScrollView();

            // 選んだ BlendShape を開いていた対象へ書き、一覧を閉じる
            if (chosen != null)
            {
                SlotRef(_picking).Set(chosen.Path, chosen.Name);
                _picking = NoSlot;
                Apply(blink);
            }
        }

        private BlendShapeRef SlotRef(int slot)
        {
            // 対象の番号から書き込み先を返す
            return slot == SlotLeft ? _value.left : slot == SlotRight ? _value.right : _value.both;
        }

        private void RefreshMatches(BlendShapeLimiter limiter)
        {
            // アバター・検索文字列が前回と同じなら作り直さない
            var key = (limiter, _search);
            if (_matchesKey.Equals(key))
            {
                return;
            }

            _matchesKey = key;
            _matches.Clear();
            _scroll = Vector2.zero;
            foreach (BlendShapeLimiter.Shape shape in limiter.Shapes)
            {
                // 検索文字列を名前（メッシュ名付きの表示名）に含むものだけ
                if (_search.Length == 0 || shape.Label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _matches.Add(shape);
                }
            }
        }

        private void Apply(BlinkController blink)
        {
            // このアバターの分として記録し（保存は終了時）、まばたきを作り直す
            _settings.SetBlinkShapes(_valuePath, _value);
            blink.Rebuild(_value);
        }
    }
}
