using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRCast.Core;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// グラフィックタブのプリセット（設定プリセットと同じ .yaml / .json）。
    /// 書き出しでは他カテゴリも選べるが、読み込みではグラフィック項目だけを反映する。
    /// </summary>
    public class GraphicsPresetSection
    {
        private enum Result
        {
            None,
            Exported,
            ExportFailed,
            Applied,
            ApplyFailed,
            ReadFailed,
            TooLarge,
            NotPreset,
            Malformed,
            Empty,
            NoGraphics,
        }

        private readonly AppSettings _settings;
        private readonly Action _reapply;

        private readonly HashSet<string> _exportSelection = new HashSet<string>();
        private PresetFormat _exportFormat = PresetFormat.Yaml;

        private PresetFile _pending;
        private string _pendingName;
        private Result _result;
        private string _resultDetail = string.Empty;
        private string _lastPath = string.Empty;

        public GraphicsPresetSection(AppSettings settings, Action reapply)
        {
            _settings = settings;
            _reapply = reapply;
            // 既定はグラフィックだけ（他のカテゴリは利用者が足す）
            _exportSelection.Add(SettingsPreset.GraphicsCategoryId);
        }

        public void Draw()
        {
            GuiControls.BeginCard(Loc.T("Graphics preset", "グラフィックプリセット", "그래픽 프리셋", "图形预设", "圖形預設"));
            GuiControls.Hint(Loc.T(
                "Save graphics settings as YAML (.yaml) or JSON (.json), same as settings presets. "
                + "Export can include other categories, but import applies graphics only "
                + "(background, anti-aliasing, lighting, your saved lighting looks, outline).",
                "グラフィック設定を、設定プリセットと同じ YAML（.yaml）／JSON（.json）で書き出せます。"
                + "書き出しでは他のカテゴリも選べますが、読み込みではグラフィック"
                + "（背景・アンチエイリアス・ライト・保存したライティング・輪郭線）だけを反映します。",
                "그래픽 설정을 설정 프리셋과 같은 YAML(.yaml)/JSON(.json)으로 내보냅니다. "
                + "내보내기에서는 다른 카테고리도 고를 수 있지만, 불러오기는 그래픽"
                + "(배경·안티앨리어싱·조명·저장한 라이팅·윤곽선)만 적용합니다.",
                "可将图形设置导出为与设置预设相同的 YAML（.yaml）/ JSON（.json）。"
                + "导出时可包含其他类别，但导入只应用图形项"
                + "（背景、抗锯齿、光照、已保存的光照外观、轮廓线）。",
                "可將圖形設定匯出為與設定預設相同的 YAML（.yaml）/ JSON（.json）。"
                + "匯出時可包含其他類別，但匯入只套用圖形項"
                + "（背景、抗鋸齒、光照、已儲存的光照外觀、輪廓線）。"));

            if (_pending != null)
            {
                DrawImportConfirm();
            }
            else
            {
                DrawExportAndImport();
            }

            DrawResult();
            GuiControls.EndCard();
        }

        /// <summary>
        /// ドロップされたプリセットを開く（グラフィック項目が無ければエラー）。
        /// </summary>
        public void Open(string path)
        {
            _lastPath = path;
            _pending = null;
            _pendingName = Path.GetFileName(path);
            _resultDetail = _pendingName;

            string text;
            try
            {
                if (new FileInfo(path).Length > SettingsPreset.MaxFileBytes)
                {
                    _result = Result.TooLarge;
                    return;
                }

                text = File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                || e is NotSupportedException)
            {
                _result = Result.ReadFailed;
                return;
            }

            if (!SettingsPreset.TryParse(text, out PresetFile preset, out PresetError error))
            {
                _result = ToResult(error);
                return;
            }

            bool hasGraphics = false;
            foreach (PresetCategory category in preset.Categories)
            {
                if (category.Id == SettingsPreset.GraphicsCategoryId)
                {
                    hasGraphics = true;
                    break;
                }
            }

            if (!hasGraphics)
            {
                _result = Result.NoGraphics;
                return;
            }

            _pending = preset;
            _result = Result.None;
        }

        private void DrawExportAndImport()
        {
            GuiControls.SubHeading(Loc.T("Export", "書き出し", "내보내기", "导出", "匯出"));
            DrawCategorySelection();
            DrawFormatSelection();

            GUI.enabled = FileDialog.IsSupported && _exportSelection.Count > 0;
            if (GUILayout.Button(Loc.T("Export...", "書き出す...", "내보내기...", "导出...", "匯出..."), GuiControls.Shrinkable))
            {
                Export();
            }

            GUI.enabled = true;

            GuiControls.SubHeading(Loc.T("Import", "読み込み", "불러오기", "导入", "匯入"));
            GuiControls.Hint(Loc.T(
                "Only graphics settings in the file are applied. Other categories are ignored.",
                "ファイル内のグラフィック設定だけを反映し、他のカテゴリは無視します。",
                "파일의 그래픽 설정만 적용하고 다른 카테고리는 무시합니다.",
                "仅应用文件中的图形设置，忽略其他类别。",
                "僅套用檔案中的圖形設定，忽略其他類別。"));
            GUI.enabled = FileDialog.IsSupported;
            if (GUILayout.Button(Loc.T("Import...", "読み込む...", "불러오기...", "导入...", "匯入..."), GuiControls.Shrinkable))
            {
                Browse();
            }

            GUI.enabled = true;
        }

        private void DrawImportConfirm()
        {
            GuiControls.SubHeading(Loc.T("Import", "読み込み", "불러오기", "导入", "匯入") + $": {_pendingName}");
            if (_pending.AppVersion.Length > 0 || _pending.CreatedAt.Length > 0)
            {
                GuiControls.Hint($"VRCast {_pending.AppVersion}  {_pending.CreatedAt}".Trim());
            }

            if (_pending.IsNewerFormat)
            {
                GuiControls.Warning(Loc.T(
                    "⚠ Made with a newer VRCast. Only the settings this version understands are applied.",
                    "⚠ 新しいバージョンの VRCast で作られたプリセットです。この版で読める設定だけを反映します。",
                    "⚠ 새 버전의 VRCast로 만든 프리셋입니다. 이 버전에서 읽을 수 있는 설정만 적용합니다.",
                    "⚠ 此预设由较新版本的 VRCast 创建。仅应用此版本可读取的设置。",
                    "⚠ 此預設由較新版本的 VRCast 建立。僅套用此版本可讀取的設定。"));
            }

            GuiControls.Hint(Loc.T(
                "Graphics settings only will be applied (background, anti-aliasing, lighting, outline).",
                "グラフィック設定（背景・アンチエイリアス・ライト・輪郭線）だけを反映します。",
                "그래픽 설정(배경·안티앨리어싱·조명·윤곽선)만 적용합니다.",
                "仅应用图形设置（背景、抗锯齿、光照、轮廓线）。",
                "僅套用圖形設定（背景、抗鋸齒、光照、輪廓線）。"));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Apply", "適用", "적용", "应用", "套用"), GuiControls.Shrinkable))
            {
                ApplyPending();
            }

            if (GUILayout.Button(Loc.T("Cancel", "キャンセル", "취소", "取消", "取消"), GuiControls.Shrinkable))
            {
                _pending = null;
                _result = Result.None;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawCategorySelection()
        {
            foreach (PresetCategory category in SettingsPreset.Categories)
            {
                bool selected = _exportSelection.Contains(category.Id);
                string label = CategoryLabel(category);
                if (category.Id == SettingsPreset.GraphicsCategoryId)
                {
                    label += Loc.T(" (default)", "（既定）", " (기본)", "（默认）", "（預設）");
                }

                if (GUILayout.Toggle(selected, label) != selected)
                {
                    if (selected)
                    {
                        _exportSelection.Remove(category.Id);
                    }
                    else
                    {
                        _exportSelection.Add(category.Id);
                    }
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Graphics only", "グラフィックだけ", "그래픽만", "仅图形", "僅圖形"),
                    GuiControls.Shrinkable))
            {
                _exportSelection.Clear();
                _exportSelection.Add(SettingsPreset.GraphicsCategoryId);
            }

            if (GUILayout.Button(Loc.T("Select all", "全て選ぶ", "모두 선택", "全选", "全選"), GuiControls.Shrinkable))
            {
                foreach (PresetCategory category in SettingsPreset.Categories)
                {
                    _exportSelection.Add(category.Id);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawFormatSelection()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("Format", "形式", "형식", "格式", "格式"), GUILayout.ExpandWidth(false));
            if (GUILayout.Toggle(_exportFormat == PresetFormat.Yaml,
                    Loc.T("YAML (recommended)", "YAML（おすすめ）", "YAML (권장)", "YAML（推荐）", "YAML（推薦）"),
                    GUILayout.ExpandWidth(false)))
            {
                _exportFormat = PresetFormat.Yaml;
            }

            if (GUILayout.Toggle(_exportFormat == PresetFormat.Json, "JSON", GUILayout.ExpandWidth(false)))
            {
                _exportFormat = PresetFormat.Json;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawResult()
        {
            switch (_result)
            {
                case Result.Exported:
                    GuiControls.Status(Loc.T("Exported", "書き出しました", "내보냈습니다", "已导出", "已匯出")
                        + $": {_resultDetail}", true);
                    break;
                case Result.Applied:
                    GuiControls.Status(Loc.T("Applied graphics settings", "グラフィック設定を反映しました",
                        "그래픽 설정을 적용했습니다", "已应用图形设置", "已套用圖形設定") + $": {_resultDetail}", true);
                    break;
                case Result.ExportFailed:
                    GuiControls.Warning(Loc.T("Could not write the file", "ファイルを書き込めませんでした", "파일을 쓰지 못했습니다",
                        "无法写入文件", "無法寫入檔案") + $": {_resultDetail}");
                    break;
                case Result.ApplyFailed:
                    GuiControls.Warning(Loc.T("Some values in the preset could not be read. Nothing was changed.",
                        "プリセットに読めない値がありました。設定は変更していません。",
                        "프리셋에 읽을 수 없는 값이 있었습니다. 설정은 변경하지 않았습니다.",
                        "预设中有无法读取的值。未更改设置。", "預設中有無法讀取的值。未變更設定。"));
                    break;
                case Result.ReadFailed:
                    GuiControls.Warning(Loc.T("Could not read the file", "ファイルを読み込めませんでした", "파일을 읽지 못했습니다",
                        "无法读取文件", "無法讀取檔案") + $": {_resultDetail}");
                    break;
                case Result.TooLarge:
                    GuiControls.Warning(Loc.T("The file is too large to be a preset", "プリセットとしては大きすぎるファイルです",
                        "프리셋으로는 너무 큰 파일입니다", "文件过大，不是预设", "檔案過大，不是預設") + $": {_resultDetail}");
                    break;
                case Result.NotPreset:
                    GuiControls.Warning(Loc.T("This is not a VRCast preset", "VRCast のプリセットではありません",
                        "VRCast 프리셋이 아닙니다", "这不是 VRCast 预设", "這不是 VRCast 預設") + $": {_resultDetail}");
                    break;
                case Result.Malformed:
                    GuiControls.Warning(Loc.T("The preset is broken (check the text if you edited it)",
                        "プリセットが壊れています（手で編集した場合は書式をご確認ください）",
                        "프리셋이 손상되었습니다 (직접 편집했다면 형식을 확인하세요)",
                        "预设已损坏（如果手动编辑过，请检查格式）", "預設已損壞（如果手動編輯過，請檢查格式）")
                        + $": {_resultDetail}");
                    break;
                case Result.Empty:
                    GuiControls.Warning(Loc.T("The preset contains no settings this version can use",
                        "この版で使える設定がプリセットに入っていません", "이 버전에서 사용할 수 있는 설정이 프리셋에 없습니다",
                        "预设中没有此版本可用的设置", "預設中沒有此版本可用的設定") + $": {_resultDetail}");
                    break;
                case Result.NoGraphics:
                    GuiControls.Warning(Loc.T("This preset has no graphics settings to apply",
                        "このプリセットには反映できるグラフィック設定がありません",
                        "이 프리셋에는 적용할 그래픽 설정이 없습니다",
                        "此预设没有可应用的图形设置", "此預設沒有可套用的圖形設定") + $": {_resultDetail}");
                    break;
            }
        }

        private void Export()
        {
            string initial = _lastPath.Length > 0
                ? _lastPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string path = FileDialog.SaveFile(
                Loc.T("Export graphics preset", "グラフィックプリセットを書き出す", "그래픽 프리셋 내보내기", "导出图形预设",
                    "匯出圖形預設"),
                Loc.T("VRCast preset", "VRCast プリセット", "VRCast 프리셋", "VRCast 预设", "VRCast 預設"),
                SettingsPreset.ExtensionOf(_exportFormat), initial,
                SettingsPreset.GraphicsFileStem + SettingsPreset.ExtensionOf(_exportFormat));
            if (path == null)
            {
                return;
            }

            _lastPath = path;
            _resultDetail = Path.GetFileName(path);
            try
            {
                string text = SettingsPreset.Export(_settings, _exportSelection, Application.version, DateTimeOffset.Now,
                    _exportFormat);
                AtomicFile.WriteAllText(path, text);
                _result = Result.Exported;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                || e is NotSupportedException)
            {
                _result = Result.ExportFailed;
                _resultDetail += $" ({e.Message})";
            }
        }

        private void Browse()
        {
            string initial = _lastPath.Length > 0
                ? _lastPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string path = FileDialog.OpenFile(
                Loc.T("Import graphics preset", "グラフィックプリセットを読み込む", "그래픽 프리셋 불러오기", "导入图形预设",
                    "匯入圖形預設"),
                Loc.T("VRCast preset", "VRCast プリセット", "VRCast 프리셋", "VRCast 预设", "VRCast 預設"),
                SettingsPreset.ImportExtensions, initial);
            if (path != null)
            {
                Open(path);
            }
        }

        private void ApplyPending()
        {
            var onlyGraphics = new HashSet<string> { SettingsPreset.GraphicsCategoryId };
            if (!SettingsPreset.Apply(_pending, onlyGraphics, _settings))
            {
                _result = Result.ApplyFailed;
                return;
            }

            _reapply();
            _pending = null;
            _result = Result.Applied;
            _resultDetail = _pendingName;
        }

        private static Result ToResult(PresetError error)
        {
            switch (error)
            {
                case PresetError.TooLarge: return Result.TooLarge;
                case PresetError.NotPreset: return Result.NotPreset;
                case PresetError.Empty: return Result.Empty;
                default: return Result.Malformed;
            }
        }

        private static string CategoryLabel(PresetCategory category)
        {
            switch (category.Id)
            {
                case "performance":
                    return Loc.T("Performance", "動作最適化", "동작 최적화", "运行优化", "執行最佳化");
                case "display":
                    return Loc.T("Camera lock", "カメラの固定", "카메라 고정", "相机锁定", "相機鎖定");
                case SettingsPreset.GraphicsCategoryId:
                    return Loc.T("Graphics", "グラフィック", "그래픽", "图形", "圖形");
                case "motion":
                    return Loc.T("Idle motion / physics", "待機・揺れもの", "대기·흔들림", "待机·物理", "待機·物理");
                case "face":
                    return Loc.T("Face (blink / lip sync)", "顔（まばたき・口パク）", "얼굴(깜빡임·립싱크)", "面部", "臉部");
                case "tracking":
                    return Loc.T("Tracking", "トラッキング", "트래킹", "追踪", "追蹤");
                case "output":
                    return Loc.T("Output", "出力", "출력", "输出", "輸出");
                case "shortcuts":
                    return Loc.T("Shortcuts / OSC", "ショートカット・OSC", "단축키·OSC", "快捷键·OSC", "快捷鍵·OSC");
                case "interface":
                    return Loc.T("Panel", "パネル", "패널", "面板", "面板");
                case "avatarLook":
                    return Loc.T("Pose / direction", "ポーズ・向き", "포즈·방향", "姿势·朝向", "姿勢·朝向");
                case "expressionMapping":
                    return Loc.T("Expression mapping", "表情の割り当て", "표정 할당", "表情映射", "表情對應");
                default:
                    return category.Id;
            }
        }
    }
}
