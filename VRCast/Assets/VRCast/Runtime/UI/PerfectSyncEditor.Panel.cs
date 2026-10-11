using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;
using VRCast.PerfectSync;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// パーフェクトシンク化の編集画面の描画（右側のパネルと、顔に重ねる線・目印）。
    /// 重い処理（形状の生成・保存・閉じる）はボタンを押したフレームの Update で行う（描画の途中で状態を変えないため）。
    /// </summary>
    public sealed partial class PerfectSyncEditor
    {
        // IMGUI ウィンドウ ID・パネルの大きさ・画面端からの余白
        private const int WindowId = 0x5650;
        private const float PanelWidth = 440f;
        private const float PanelMargin = 10f;
        private const float MinPanelHeight = 320f;

        // 案内図の大きさと点の大きさ
        private const float DiagramSize = 150f;
        private const float DiagramDot = 6f;
        private const float DiagramSelectedDot = 10f;

        // 顔に重ねる目印の大きさ（画面の画素）
        private const float PointSize = 8f;
        private const float SelectedPointSize = 12f;

        // 目印の名前の札の大きさ（GUI の座標）
        private const float TagWidth = 240f;
        private const float TagHeight = 24f;

        // 形状の一覧の列幅と、割り当ての候補を一度に出す数
        private const float ShapeNameWidth = 150f;
        private const float StateWidth = 80f;
        private const float StrengthValueWidth = 44f;
        private const int MaxSearchResults = 30;

        // 線と目印の色（選択中は黄色、自動で決めた右側は水色）
        private static readonly Color WireColor = new Color(0.3f, 0.8f, 1f, 0.35f);
        private static readonly Color PlacedColor = new Color(0.3f, 1f, 0.4f, 1f);
        private static readonly Color MirroredColor = new Color(0.4f, 0.8f, 1f, 1f);
        private static readonly Color SelectedColor = new Color(1f, 0.9f, 0.2f, 1f);
        private static readonly Color PendingColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        // 次の Update で行う処理（ボタンで決めたもの）
        private Action _deferred;

        // パネルのスクロール位置・割り当ての候補の検索文字列・重ねて描く目印（使い回す）
        private Vector2 _scroll;
        private string _search = string.Empty;
        private readonly List<(Vector3 World, Color Color, float Size)> _points =
            new List<(Vector3, Color, float)>();

        /// <summary>
        /// 顔に重ねる線・目印と、右側のパネルを描く（MainPanel の OnGUI から、スキンと倍率を適用した状態で呼ぶ）。
        /// </summary>
        public void OnGUI(float uiScale)
        {
            if (!IsOpen)
            {
                return;
            }

            DrawOverlay(uiScale);

            // 画面の右端に、画面の高さいっぱいのパネルを置く
            float width = Screen.width / uiScale;
            float height = Screen.height / uiScale;
            PanelRect = new Rect(width - PanelWidth - PanelMargin, PanelMargin, PanelWidth,
                Mathf.Max(MinPanelHeight, height - PanelMargin * 2f));
            GUILayout.Window(WindowId, PanelRect, DrawPanel, GUIContent.none, GUI.skin.window,
                GUILayout.Width(PanelRect.width), GUILayout.Height(PanelRect.height));
        }

        /// <summary>
        /// ボタンで決めた処理を行う（Update の最初に呼ぶ）。閉じたら false。
        /// </summary>
        private bool RunDeferred()
        {
            Action deferred = _deferred;
            _deferred = null;
            deferred?.Invoke();
            return IsOpen;
        }

        private void DrawOverlay(float uiScale)
        {
            // 描くのは Repaint のときだけ
            if (Event.current.type != EventType.Repaint || _geometry == null || _camera == null
                || _world.Length != _geometry.Vertices.Length)
            {
                return;
            }

            // 顔メッシュの線
            _overlay.DrawWireframe(_camera, _world, _geometry.Triangles, _region, _regionVertices, _faceCenter, WireColor);

            // 打った目印（選択中は大きく黄色、自動で決めた右側は水色）
            _points.Clear();
            foreach (KeyValuePair<LandmarkId, PerfectSyncLandmark> pair in _landmarks)
            {
                if (!_geometry.TryGetPoint(pair.Value, out Vector3 world, _world))
                {
                    continue;
                }

                bool selected = _selected == pair.Key;
                Color color = selected ? SelectedColor : _autoMirrored.Contains(pair.Key) ? MirroredColor : PlacedColor;
                _points.Add((world, color, selected ? SelectedPointSize : PointSize));
            }

            _overlay.DrawPoints(_camera, _points);

            // 選択中の目印の横に名前の札を出す
            if (_selected.HasValue && _landmarks.TryGetValue(_selected.Value, out PerfectSyncLandmark current)
                && _geometry.TryGetPoint(current, out Vector3 selectedWorld, _world)
                && PerfectSyncOverlay.ToGui(_camera, selectedWorld, uiScale, out Vector2 gui))
            {
                GUI.Box(new Rect(gui.x + SelectedPointSize, gui.y - TagHeight * 0.5f, TagWidth, TagHeight),
                    PerfectSyncLabels.Landmark(_selected.Value));
            }
        }

        private void DrawPanel(int id)
        {
            UiTheme theme = UiTheme.Current;

            // 見出しと、編集中の注意
            GUILayout.Label(Loc.T("Perfect sync setup", "パーフェクトシンク化", "퍼펙트 싱크화", "完美同步化", "完美同步化"),
                theme != null ? theme.SectionTitle : GUI.skin.label);
            GUILayout.Label(_avatar.Name, theme != null ? theme.Hint : GUI.skin.label);
            GuiControls.Warning(Loc.T(
                "While editing, the output shows this close-up. The original file is not changed.",
                "編集中は出力にもこのアップが映ります。元のファイルは変更しません。",
                "편집 중에는 출력에도 이 클로즈업이 나옵니다. 원본 파일은 변경하지 않습니다.",
                "编辑期间输出画面也会显示此特写。不会修改原文件。",
                "編輯期間輸出畫面也會顯示此特寫。不會修改原檔案。"));

            // 段階の切替（目印 / 形状）
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_step == Step.Points,
                    Loc.T("1. Points", "1. 目印", "1. 표시", "1. 标记", "1. 標記"), GUI.skin.button) && _step != Step.Points)
            {
                _step = Step.Points;
            }

            if (GUILayout.Toggle(_step == Step.Shapes,
                    Loc.T("2. Shapes", "2. 形状", "2. 형상", "2. 形状", "2. 形狀"), GUI.skin.button) && _step != Step.Shapes)
            {
                _step = Step.Shapes;
            }

            GUILayout.EndHorizontal();

            // 段階ごとの内容（縦にスクロール）
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (_step == Step.Points)
            {
                DrawPointsStep(theme);
            }
            else
            {
                DrawShapesStep(theme);
            }

            GUILayout.EndScrollView();

            DrawFooter(theme);
        }

        private void DrawPointsStep(UiTheme theme)
        {
            GuiControls.BeginCard(Loc.T("Place points on the face", "顔に目印を打つ", "얼굴에 표시 찍기", "在脸上放置标记", "在臉上放置標記"));
            GuiControls.Hint(Loc.T(
                "Left-click the face to place the highlighted point, and drag a point to move it. " +
                "Right-drag rotates, the wheel zooms and middle-drag pans. Delete removes the selected point. " +
                "Left and right are the avatar's own (its left is on the right of the screen from the front).",
                "顔を左クリックすると、選択中の目印を打てます。目印はドラッグで動かせます。" +
                "右ドラッグで回転、ホイールで拡大、中ドラッグで移動、Delete キーで選択中の目印を消せます。" +
                "左右はアバターから見た左右です（正面から見ると画面の右がアバターの左）。",
                "얼굴을 왼쪽 클릭하면 선택한 표시를 찍습니다. 표시는 드래그로 옮길 수 있습니다. " +
                "오른쪽 드래그로 회전, 휠로 확대, 가운데 드래그로 이동, Delete 키로 선택한 표시를 지웁니다. " +
                "좌우는 아바타 기준입니다(정면에서 보면 화면 오른쪽이 아바타의 왼쪽).",
                "左键点击脸部即可放置当前选中的标记，拖动可移动标记。" +
                "右键拖动旋转，滚轮缩放，中键拖动平移，Delete 键删除选中的标记。" +
                "左右以虚拟形象自身为准（从正面看，屏幕右侧是形象的左侧）。",
                "左鍵點擊臉部即可放置目前選取的標記，拖曳可移動標記。" +
                "右鍵拖曳旋轉，滾輪縮放，中鍵拖曳平移，Delete 鍵刪除選取的標記。" +
                "左右以虛擬形象自身為準（從正面看，螢幕右側是形象的左側）。"));

            // 顔メッシュ（替えたら目印は打ち直し）
            int face = GuiControls.Selector(Loc.T("Face mesh", "顔メッシュ", "얼굴 메시", "面部网格", "臉部網格"),
                _rendererLabels, _faceIndex, null);
            if (face != _faceIndex && face >= 0)
            {
                SelectFace(face);
            }

            // 細かさ（少ない段階で打たない目印は推定する）
            var detail = (LandmarkDetail)GuiControls.EnumSelector(
                Loc.T("Detail", "細かさ", "세밀도", "精细度", "精細度"), PerfectSyncLabels.Details(), (int)_detail);
            if (detail != _detail)
            {
                _detail = detail;
                _dirty = true;
                _selected = NextUnplaced() ?? _selected;
            }

            // 左右対称（右側は左側から自動で決める）
            bool mirror = GUILayout.Toggle(_mirror, Loc.T(
                "Mirror the left points to the right", "左側の目印を右側にも反映（左右対称）",
                "왼쪽 표시를 오른쪽에도 반영 (좌우 대칭)", "将左侧标记镜像到右侧（左右对称）", "將左側標記鏡像到右側（左右對稱）"));
            if (mirror != _mirror)
            {
                // ON にしたら右側を決め直す。OFF にしたら自動で決めた右側は手で打ったものとして残す
                _mirror = mirror;
                _dirty = true;
                if (mirror)
                {
                    Remirror(null);
                }
                else
                {
                    _autoMirrored.Clear();
                }
            }

            // 歯・舌のメッシュも顎と一緒に動かす
            bool mouth = GUILayout.Toggle(_includeMouthParts, Loc.T(
                "Also move teeth and tongue meshes with the jaw", "歯・舌のメッシュも顎と一緒に動かす",
                "치아·혀 메시도 턱과 함께 움직이기", "牙齿·舌头网格也随下巴一起移动", "牙齒·舌頭網格也隨下巴一起移動"));
            if (mouth != _includeMouthParts)
            {
                _includeMouthParts = mouth;
                _dirty = true;
            }

            // 進み具合と案内図
            int placed = PlacedCount(out int total);
            GuiControls.Status(Loc.T($"Placed {placed}/{total}", $"打った目印 {placed}/{total}", $"찍은 표시 {placed}/{total}",
                $"已放置 {placed}/{total}", $"已放置 {placed}/{total}"), placed == total);
            DrawDiagram();

            // 目印の一覧（押すと選択）
            GUIStyle hint = theme != null ? theme.Hint : GUI.skin.label;
            foreach (LandmarkId landmark in FaceLandmarks.For(_detail))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(_selected == landmark, PerfectSyncLabels.Landmark(landmark), GUI.skin.button,
                        GuiControls.Shrinkable) && _selected != landmark)
                {
                    _selected = landmark;
                }

                GUILayout.Label(LandmarkState(landmark), hint, GUILayout.Width(StateWidth));
                GUILayout.EndHorizontal();
            }

            // 選択中の目印を消す・全部消す・顔へ寄り直す
            GUILayout.BeginHorizontal();
            GUI.enabled = _selected.HasValue && _landmarks.ContainsKey(_selected.Value);
            if (GUILayout.Button(Loc.T("Remove point", "目印を消す", "표시 지우기", "删除标记", "刪除標記"), GuiControls.Shrinkable))
            {
                RemoveLandmark(_selected.Value);
            }

            GUI.enabled = _landmarks.Count > 0;
            if (GuiControls.DangerButton(Loc.T("Clear all", "全部消す", "모두 지우기", "全部清除", "全部清除"), GuiControls.Shrinkable))
            {
                _landmarks.Clear();
                _autoMirrored.Clear();
                _dirty = true;
                _selected = NextUnplaced();
            }

            GUI.enabled = true;
            if (GUILayout.Button(Loc.T("Face the camera", "顔を正面に", "얼굴 정면으로", "正对脸部", "正對臉部"), GuiControls.Shrinkable))
            {
                FocusFace();
            }

            GUILayout.EndHorizontal();
            GuiControls.EndCard();

            // 形状を作って試す（最小の目印が揃っていなければ押せない）
            GUI.enabled = HasRequiredLandmarks();
            if (GUILayout.Button(Loc.T("Generate and preview", "生成して試す", "생성해서 확인", "生成并预览", "生成並預覽"),
                    theme != null ? theme.AccentButton : GUI.skin.button))
            {
                _deferred = GenerateAndShow;
            }

            GUI.enabled = true;
            if (!HasRequiredLandmarks())
            {
                GuiControls.Hint(Loc.T(
                    "Place at least the minimal points (eyes, brows, mouth corners, lips and chin) to generate.",
                    "生成には最小の目印（目・眉・口角・唇・あご）が必要です。",
                    "생성하려면 최소 표시(눈·눈썹·입꼬리·입술·턱)가 필요합니다.",
                    "生成至少需要最少的标记（眼睛·眉毛·嘴角·嘴唇·下巴）。",
                    "生成至少需要最少的標記（眼睛·眉毛·嘴角·嘴唇·下巴）。"));
            }
        }

        private void DrawDiagram()
        {
            // 案内図（中央寄せ。選択中の目印を大きく黄色で示し、押すとその目印を選ぶ）
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect rect = GUILayoutUtility.GetRect(DiagramSize, DiagramSize, GUILayout.Width(DiagramSize),
                GUILayout.Height(DiagramSize));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            List<LandmarkId> ids = FaceLandmarks.For(_detail);
            float radius = DiagramSize * 0.45f;

            // 押した位置に最も近い目印を選ぶ
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                float best = float.MaxValue;
                foreach (LandmarkId landmark in ids)
                {
                    float distance = (DiagramPoint(rect, radius, landmark) - current.mousePosition).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        _selected = landmark;
                    }
                }

                current.Use();
            }

            if (current.type != EventType.Repaint)
            {
                return;
            }

            // 背景と、目印の点（選択中 → 打った → まだ、の順に目立たせる）
            GUI.Box(rect, GUIContent.none);
            Color previous = GUI.color;
            foreach (LandmarkId landmark in ids)
            {
                bool selected = _selected == landmark;
                float size = selected ? DiagramSelectedDot : DiagramDot;
                GUI.color = selected ? SelectedColor : _landmarks.ContainsKey(landmark) ? PlacedColor : PendingColor;
                Vector2 point = DiagramPoint(rect, radius, landmark);
                GUI.DrawTexture(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            }

            GUI.color = previous;
        }

        private static Vector2 DiagramPoint(Rect rect, float radius, LandmarkId landmark)
        {
            // 案内図の座標（上が +y）を GUI の座標（下が +y）へ
            Vector2 position = PerfectSyncLabels.DiagramPosition(landmark);
            return rect.center + new Vector2(position.x, -position.y) * radius;
        }

        private string LandmarkState(LandmarkId landmark)
        {
            // 打った / 自動（左右対称） / まだ
            if (!_landmarks.ContainsKey(landmark))
            {
                return "-";
            }

            return _autoMirrored.Contains(landmark)
                ? Loc.T("Auto", "自動", "자동", "自动", "自動")
                : Loc.T("Placed", "打った", "찍음", "已放置", "已放置");
        }

        private bool HasRequiredLandmarks()
        {
            // 最小の段階の目印が全部あるか
            foreach (LandmarkId landmark in FaceLandmarks.Required)
            {
                if (!_landmarks.ContainsKey(landmark))
                {
                    return false;
                }
            }

            return true;
        }

        private void DrawShapesStep(UiTheme theme)
        {
            GuiControls.BeginCard(Loc.T("Check the shapes", "形状を確かめる", "형상 확인", "检查形状", "檢查形狀"));

            // 使える ARKit の形状の数（パーフェクトシンクとして動くか）
            int matched = _faceDriver != null ? _faceDriver.PerfectSyncShapeCount : 0;
            int total = ArKitFace.BlendShapeNames.Length;
            GuiControls.Status(Loc.T($"ARKit shapes in use: {matched}/{total}", $"使える ARKit の形状: {matched}/{total}",
                $"사용 가능한 ARKit 형상: {matched}/{total}", $"可用的 ARKit 形状: {matched}/{total}",
                $"可用的 ARKit 形狀: {matched}/{total}"), matched >= PerfectSyncBlendShapes.MinMatchedShapes);
            GuiControls.Hint(Loc.T(
                "For each shape choose Generate (made from the points), Existing shape (use one of the avatar's blend shapes) " +
                "or None. Eye movement is off by default because the eye bones already follow your gaze. " +
                "Select a shape name to test it with the slider and to choose the existing blend shape.",
                "形状ごとに「自動生成」（目印から作る）・「既存の BlendShape」（アバターの BlendShape を使う）・「なし」を選べます。" +
                "目の動きは目のボーンで視線を動かすため、既定では作りません。" +
                "形状の名前を押すと、スライダーで試したり、使う既存の BlendShape を選んだりできます。",
                "형상마다 '자동 생성'(표시로 만듦)·'기존 BlendShape'(아바타의 BlendShape 사용)·'없음'을 고를 수 있습니다. " +
                "눈 움직임은 눈 본으로 시선을 움직이므로 기본으로 만들지 않습니다. " +
                "형상 이름을 누르면 슬라이더로 확인하거나 사용할 기존 BlendShape를 고를 수 있습니다.",
                "每个形状可选择“自动生成”（由标记生成）·“现有 BlendShape”（使用形象的 BlendShape）·“无”。" +
                "眼球运动由眼睛骨骼控制视线，因此默认不生成。" +
                "点击形状名称可用滑块测试，或选择要使用的现有 BlendShape。",
                "每個形狀可選擇「自動生成」（由標記生成）·「現有 BlendShape」（使用形象的 BlendShape）·「無」。" +
                "眼球運動由眼睛骨骼控制視線，因此預設不生成。" +
                "點擊形狀名稱可用滑桿測試，或選擇要使用的現有 BlendShape。"));

            // 設定を変えたら作り直しが必要
            if (_dirty)
            {
                GuiControls.Warning(Loc.T("Settings changed. Press Apply to update the preview.",
                    "設定を変えました。「反映」で試す形状を更新します。", "설정을 바꿨습니다. '반영'으로 미리보기를 갱신합니다.",
                    "设置已更改。点击“应用”更新预览。", "設定已變更。點擊「套用」更新預覽。"));
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Apply", "反映", "반영", "应用", "套用"),
                    theme != null ? theme.AccentButton : GUI.skin.button, GuiControls.Shrinkable))
            {
                _deferred = GenerateAndShow;
            }

            if (GUILayout.Button(Loc.T("Auto-assign existing shapes", "既存の BlendShape を自動で割り当て",
                    "기존 BlendShape 자동 할당", "自动分配现有 BlendShape", "自動分配現有 BlendShape"), GuiControls.Shrinkable))
            {
                StopPreview();
                AutoAssign();
            }

            GUILayout.EndHorizontal();

            // 試す値（形状を選んでいるときだけ）
            if (_previewIndex >= 0)
            {
                _previewWeight = GuiControls.Slider(ArKitFace.BlendShapeNames[_previewIndex], _previewWeight, 0f, 100f, "F0");
            }

            // 形状の一覧
            string[] sources = PerfectSyncLabels.Sources();
            for (int i = 0; i < _shapes.Length; i++)
            {
                DrawShapeRow(theme, i, sources);
            }

            GuiControls.EndCard();
        }

        private void DrawShapeRow(UiTheme theme, int index, string[] sources)
        {
            ShapeState shape = _shapes[index];
            GUIStyle hint = theme != null ? theme.Hint : GUI.skin.label;

            // 名前（押すと試す形状にする）と出どころ（元からある形状は選べない）
            GUILayout.BeginHorizontal();
            bool selected = GUILayout.Toggle(_previewIndex == index, ArKitFace.BlendShapeNames[index], GUI.skin.button,
                GUILayout.Width(ShapeNameWidth));
            if (selected != (_previewIndex == index))
            {
                SelectPreview(selected ? index : -1);
                _search = string.Empty;
            }

            if (shape.Native)
            {
                GUILayout.Label(Loc.T("Built in", "元からある", "원래 있음", "原本就有", "原本就有"),
                    theme != null ? theme.Success : GUI.skin.label, GuiControls.Shrinkable);
            }
            else
            {
                int source = GuiControls.Selector(null, sources, shape.Source, null);
                if (source != shape.Source && source >= 0)
                {
                    shape.Source = source;
                    _dirty = true;
                    StopPreview();
                }
            }

            GUILayout.EndHorizontal();
            if (shape.Native || shape.Source == PerfectSyncSet.SourceNone)
            {
                return;
            }

            // 強さ
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("Strength", "強さ", "강도", "强度", "強度"), hint, GUILayout.Width(ShapeNameWidth));
            float strength = GUILayout.HorizontalSlider(shape.Strength, 0f, PerfectSyncSet.MaxStrength);
            GUILayout.Label(strength.ToString("F2"), hint, GUILayout.Width(StrengthValueWidth));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(strength, shape.Strength))
            {
                shape.Strength = strength;
                _dirty = true;
                StopPreview();
            }

            if (shape.Source != PerfectSyncSet.SourceExisting)
            {
                return;
            }

            // 割り当てた既存の BlendShape（選択中の形状なら候補を検索して選べる）
            string bound = shape.Binding >= 0 && shape.Binding < _candidates.Count
                ? _candidates[shape.Binding].Label
                : Loc.T("(not chosen)", "（未選択）", "(선택 안 함)", "（未选择）", "（未選擇）");
            GUILayout.Label(Loc.T("Uses: ", "使う BlendShape: ", "사용할 BlendShape: ", "使用的 BlendShape: ", "使用的 BlendShape: ")
                + bound, hint);
            if (_previewIndex == index)
            {
                DrawCandidateSearch(theme, shape);
            }
        }

        private void DrawCandidateSearch(UiTheme theme, ShapeState shape)
        {
            // 名前で絞り込んだ候補を、一度に出せる数まで並べる
            _search = GuiControls.TextField(Loc.T("Search", "検索", "검색", "搜索", "搜尋"), _search, 64);
            int shown = 0;
            for (int c = 0; c < _candidates.Count && shown < MaxSearchResults; c++)
            {
                ShapeCandidate candidate = _candidates[c];
                if (_search.Length > 0 && candidate.Label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                shown++;
                if (GUILayout.Toggle(shape.Binding == c, candidate.Label, GUI.skin.button) && shape.Binding != c)
                {
                    shape.Binding = c;
                    _dirty = true;
                    StopPreview();
                }
            }

            if (shown == 0)
            {
                GUILayout.Label(Loc.T("No matching blend shapes.", "一致する BlendShape はありません。", "일치하는 BlendShape가 없습니다.",
                    "没有匹配的 BlendShape。", "沒有符合的 BlendShape。"), theme != null ? theme.Hint : GUI.skin.label);
            }
        }

        private void DrawFooter(UiTheme theme)
        {
            // 直前の結果（失敗は赤、成功は緑）
            if (!string.IsNullOrEmpty(_message))
            {
                GUIStyle style = theme == null ? GUI.skin.label : _messageIsError ? theme.ErrorText : theme.Success;
                GUILayout.Label(_message, style);
            }

            // 別名で保存・閉じる（保存していない変更は捨てる）
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Save as new file...", "別名で保存...", "다른 이름으로 저장...", "另存为...", "另存新檔..."),
                    theme != null ? theme.AccentButton : GUI.skin.button, GuiControls.Shrinkable))
            {
                _deferred = Save;
            }

            if (GUILayout.Button(Loc.T("Close", "閉じる", "닫기", "关闭", "關閉"), GuiControls.Shrinkable))
            {
                _deferred = Close;
            }

            GUILayout.EndHorizontal();

            // 保存したファイルをすぐ読み込む
            if (!string.IsNullOrEmpty(_savedPath)
                && GUILayout.Button(Loc.T("Load the saved avatar", "保存したアバターを読み込む", "저장한 아바타 불러오기",
                    "加载已保存的虚拟形象", "載入已儲存的虛擬形象")))
            {
                _deferred = LoadSaved;
            }

            GuiControls.Hint(Loc.T("Closing discards unsaved changes.", "閉じると保存していない変更は破棄されます。",
                "닫으면 저장하지 않은 변경은 버려집니다.", "关闭后未保存的更改将被丢弃。", "關閉後未儲存的變更將被捨棄。"));
        }

        private void GenerateAndShow()
        {
            // 形状を作って試し、形状の段階へ進む
            if (!ApplyChanges())
            {
                return;
            }

            _step = Step.Shapes;
            int count = _generated != null ? _generated.Shapes.Count : 0;
            SetMessage(Loc.T($"Generated {count} shapes.", $"{count} 個の形状を作りました。", $"형상 {count}개를 만들었습니다.",
                $"已生成 {count} 个形状。", $"已生成 {count} 個形狀。"), false);
        }
    }
}
