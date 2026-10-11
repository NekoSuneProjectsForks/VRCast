using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// Objects タブ。表示中アバターの階層を Unity のヒエラルキーと同じ並びで出し、オブジェクトごとに表示を ON/OFF する。
    /// 開閉できる木の表示のほか、名前の検索と、VRChat のメニュー（FX）で切り替えるものだけの一覧に絞り込める。
    /// 切り替えはアバターごとに記録する。
    /// </summary>
    public class ObjectSection
    {
        // 1 段の字下げと、開閉ボタンの幅
        private const float IndentWidth = 14f;
        private const float FoldButtonWidth = 24f;

        // 1 回に描く行の上限（多すぎると操作が重くなるため、超えた分は検索で絞り込んでもらう）
        private const int MaxRows = 400;

        // 検索の入力の最大文字数
        private const int MaxSearchLength = 64;

        // 親が OFF で見えていない行の文字の濃さ
        private const float HiddenRowAlpha = 0.5f;

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;
        private AvatarObjects _objects;

        // 開いている行の番号（アバター切替時に閉じる）
        private readonly HashSet<int> _expanded = new HashSet<int>();

        // 絞り込み（名前の検索・FX で切り替えるものだけ・描画を含むものだけ）
        private string _search = string.Empty;
        private bool _fxOnly;
        private bool _renderersOnly = true;

        // 行の数が変わる操作（開閉・絞り込み）は、配置の計算（Layout）と食い違わないよう次の Layout で当てる
        private readonly List<Action> _deferred = new List<Action>();

        // 検索に使う文字（入力中の前後の空白は残し、探すときだけ除く）
        private string Query => _search.Trim();

        public ObjectSection(AvatarSession session, AppSettings settings)
        {
            _session = session;
            _avatar = new AvatarComponentCache(session);
            _settings = settings;
        }

        public void Draw()
        {
            // 前のイベントで受けた開閉・絞り込みを、配置の計算の前に当てる
            ApplyDeferred();

            GuiControls.BeginCard(Loc.T("Objects", "オブジェクト", "오브젝트", "对象", "物件"));

            // アバター未表示・一覧の無いアバターなら案内だけ
            if (!Refresh() || _objects == null || _objects.Nodes.Count == 0)
            {
                GuiControls.Hint(Loc.T("Load an avatar first.", "先にアバターを読み込んでください。", "먼저 아바타를 불러오세요.",
                    "请先加载虚拟形象。", "請先載入虛擬形象。"));
                GuiControls.EndCard();
                return;
            }

            GuiControls.Hint(Loc.T(
                $"Same order as Unity's Hierarchy. Check to show, uncheck to hide. Objects turned off in the scene are included turned off when exported with \"Also export inactive objects\". {_objects.ChangedCount} changed. Saved per avatar.",
                $"Unity のヒエラルキーと同じ並びです。チェックで表示、外すと非表示にします。書き出しツールの「非アクティブのオブジェクトも書き出す」で書き出すと、シーンで OFF にしていたものも OFF のまま入ります。{_objects.ChangedCount} 個を変更中。アバターごとに保存されます。",
                $"Unity의 Hierarchy와 같은 순서입니다. 체크하면 표시, 해제하면 숨깁니다. 내보내기 도구의 '비활성 오브젝트도 내보내기'로 내보내면 씬에서 꺼 둔 것도 꺼진 상태로 들어갑니다. {_objects.ChangedCount}개 변경 중. 아바타별로 저장됩니다.",
                $"与 Unity 的 Hierarchy 顺序相同。勾选显示，取消则隐藏。用导出工具的“也导出未激活的对象”导出时，在场景中关闭的对象也会以关闭状态包含在内。已更改 {_objects.ChangedCount} 个。按虚拟形象分别保存。",
                $"與 Unity 的 Hierarchy 順序相同。勾選顯示，取消則隱藏。用匯出工具的「也匯出未啟用的物件」匯出時，在場景中關閉的物件也會以關閉狀態包含在內。已變更 {_objects.ChangedCount} 個。依虛擬形象分別儲存。"));

            DrawFilters();
            DrawButtons();

            // 検索中・FX だけのときは平らな一覧、それ以外は開閉できる木
            bool changed = Query.Length > 0 || _fxOnly ? DrawFlatList() : DrawTree();

            // 変えたらこのアバターの分として記録する（保存は終了時）
            if (changed)
            {
                Save();
            }

            GuiControls.EndCard();
        }

        private bool Refresh()
        {
            // アバターが替わったときだけ取り直し、開閉と絞り込みを初期に戻す
            if (_avatar.Refresh())
            {
                _objects = _avatar.Get<AvatarObjects>();
                _expanded.Clear();
                _deferred.Clear();
                _search = string.Empty;

                // VRChat のメニューで切り替えるものが分かるアバターは、その一覧から始める
                _fxOnly = _objects != null && _objects.FxToggleCount > 0;
            }

            return _avatar.HasAvatar;
        }

        private void DrawFilters()
        {
            // 名前の検索（空なら絞り込まない）
            string search = GuiControls.TextField(
                Loc.T("Search", "検索", "검색", "搜索", "搜尋"), _search, MaxSearchLength);
            if (search != _search)
            {
                _deferred.Add(() => _search = search);
            }

            // FX で切り替えるものだけ（書き出し時に分かったアバターだけ）
            if (_objects.FxToggleCount > 0)
            {
                bool fxOnly = GUILayout.Toggle(_fxOnly, string.Format(Loc.T(
                        "Only objects toggled by VRChat menus ({0})", "VRChat のメニューで切り替えるものだけ（{0} 個）",
                        "VRChat 메뉴로 전환하는 것만 ({0}개)", "仅 VRChat 菜单切换的对象（{0} 个）", "僅 VRChat 選單切換的物件（{0} 個）"),
                    _objects.FxToggleCount), GuiControls.Shrinkable);
                if (fxOnly != _fxOnly)
                {
                    _deferred.Add(() => _fxOnly = fxOnly);
                }
            }

            // 描画（メッシュ・パーティクル等）を含むものだけ（ボーンだけの行を隠す）
            bool renderersOnly = GUILayout.Toggle(_renderersOnly, Loc.T(
                "Hide bones and empty objects", "ボーン・空のオブジェクトを隠す", "본·빈 오브젝트 숨기기",
                "隐藏骨骼和空对象", "隱藏骨骼和空物件"), GuiControls.Shrinkable);
            if (renderersOnly != _renderersOnly)
            {
                _deferred.Add(() => _renderersOnly = renderersOnly);
            }
        }

        private void ApplyDeferred()
        {
            // 配置の計算のときだけ当てる（それ以外のイベントでは行の数を変えない）
            if (Event.current.type != EventType.Layout || _deferred.Count == 0)
            {
                return;
            }

            foreach (Action action in _deferred)
            {
                action();
            }

            _deferred.Clear();
        }

        private void DrawButtons()
        {
            GUILayout.BeginHorizontal();

            // 木をすべて開く・閉じる
            if (GUILayout.Button(Loc.T("Expand all", "すべて開く", "모두 펼치기", "全部展开", "全部展開"), GuiControls.Shrinkable))
            {
                AvatarObjects objects = _objects;
                _deferred.Add(() =>
                {
                    foreach (AvatarObjects.Node node in objects.Nodes)
                    {
                        _expanded.Add(node.Index);
                    }
                });
            }

            if (GUILayout.Button(Loc.T("Collapse all", "すべて閉じる", "모두 접기", "全部折叠", "全部摺疊"), GuiControls.Shrinkable))
            {
                _deferred.Add(_expanded.Clear);
            }

            // 切り替えたものを読込時の状態に戻す（変更が無ければ押せない）
            GUI.enabled = _objects.ChangedCount > 0;
            if (GUILayout.Button(Loc.T("Undo changes", "変更を元に戻す", "변경 되돌리기", "撤销更改", "復原變更"), GuiControls.Shrinkable))
            {
                _objects.ResetAll();
                Save();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private bool DrawTree()
        {
            bool changed = false;
            int rows = 0;
            IReadOnlyList<AvatarObjects.Node> nodes = _objects.Nodes;

            // 深さ優先の並びをたどり、隠す行・閉じた行の子孫は飛ばす
            int i = 0;
            while (i < nodes.Count)
            {
                AvatarObjects.Node node = nodes[i];
                if (!IsShown(node))
                {
                    i = node.SubtreeEnd + 1;
                    continue;
                }

                // 描く行の上限に達したら案内を出して終える
                if (rows++ >= MaxRows)
                {
                    DrawTooManyRows();
                    break;
                }

                changed |= DrawRow(node, true);
                i = _expanded.Contains(node.Index) ? i + 1 : node.SubtreeEnd + 1;
            }

            return changed;
        }

        private bool DrawFlatList()
        {
            bool changed = false;
            int rows = 0;
            foreach (AvatarObjects.Node node in _objects.Nodes)
            {
                // FX だけの指定ならその全部（描画の無いものも）、それ以外は描画の有無で絞り込み、さらに検索で絞り込む
                if ((_fxOnly ? !node.IsFxToggle : !IsShown(node)) || !MatchesSearch(node))
                {
                    continue;
                }

                // 描く行の上限に達したら案内を出して終える
                if (rows++ >= MaxRows)
                {
                    DrawTooManyRows();
                    break;
                }

                // 平らな一覧では場所が分からないため、Unity 上のパスも出す
                changed |= DrawRow(node, false);
                GuiControls.Hint(Loc.T("Path: ", "場所: ", "경로: ", "路径: ", "路徑: ") + node.Path);
            }

            // 何も無いときはその旨を出す
            if (rows == 0)
            {
                GuiControls.Hint(Loc.T("No matching objects.", "当てはまるオブジェクトはありません。", "해당하는 오브젝트가 없습니다.",
                    "没有符合的对象。", "沒有符合的物件。"));
            }

            return changed;
        }

        private bool DrawRow(AvatarObjects.Node node, bool tree)
        {
            GUILayout.BeginHorizontal();

            // 木の表示では深さで字下げし、見える子があれば開閉ボタン、無ければ同じ幅を空ける
            if (tree)
            {
                GUILayout.Space(node.Depth * IndentWidth);
                if (HasShownChild(node))
                {
                    bool expanded = _expanded.Contains(node.Index);
                    if (GUILayout.Button(expanded ? "-" : "+", GUILayout.Width(FoldButtonWidth)))
                    {
                        int index = node.Index;
                        _deferred.Add(() => SetExpanded(index, !expanded));
                    }
                }
                else
                {
                    GUILayout.Space(FoldButtonWidth + GUI.skin.button.margin.horizontal);
                }
            }

            // 親が OFF で見えていない行は薄く出す（自身の ON/OFF は Unity と同じくチェックで分かる）
            Color previous = GUI.color;
            if (node.Active && !node.Visible)
            {
                GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * HiddenRowAlpha);
            }

            // ON/OFF（VRChat のメニューで切り替えるものは印を付ける）
            string label = node.IsFxToggle ? node.Name + "  [VRChat]" : node.Name;
            bool active = GUILayout.Toggle(node.Active, label, GuiControls.Shrinkable);
            GUI.color = previous;
            GUILayout.EndHorizontal();

            // 変わったときだけ切り替える
            if (active == node.Active)
            {
                return false;
            }

            _objects.SetActive(node, active);
            return true;
        }

        private void DrawTooManyRows()
        {
            GuiControls.Hint(string.Format(Loc.T(
                    "Only the first {0} rows are shown. Use search to narrow down.",
                    "先頭の {0} 行だけを表示しています。検索で絞り込んでください。",
                    "처음 {0}행만 표시합니다. 검색으로 좁혀 주세요.",
                    "仅显示前 {0} 行。请用搜索缩小范围。",
                    "僅顯示前 {0} 行。請用搜尋縮小範圍。"),
                MaxRows));
        }

        private bool IsShown(AvatarObjects.Node node)
        {
            // 描画を含むものだけの指定なら、ボーン・空のオブジェクト（子孫にも描画が無いもの）を隠す
            return !_renderersOnly || node.HasRenderer;
        }

        private bool MatchesSearch(AvatarObjects.Node node)
        {
            // 大文字・小文字を区別せずに名前の一部で探す
            string query = Query;
            return query.Length == 0 || node.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool HasShownChild(AvatarObjects.Node node)
        {
            // 直下の子を順にたどり（孫は飛ばす）、出す行が 1 つでもあれば true
            IReadOnlyList<AvatarObjects.Node> nodes = _objects.Nodes;
            for (int i = node.Index + 1; i <= node.SubtreeEnd; i = nodes[i].SubtreeEnd + 1)
            {
                if (IsShown(nodes[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void SetExpanded(int index, bool expanded)
        {
            // 開く・閉じるを覚える
            if (expanded)
            {
                _expanded.Add(index);
            }
            else
            {
                _expanded.Remove(index);
            }
        }

        private void Save()
        {
            // このアバターの分として記録する（保存は終了時）
            if (_session.Current != null)
            {
                _settings.SetObjectToggles(_session.Current.SourcePath, _objects.Export());
            }
        }
    }
}
