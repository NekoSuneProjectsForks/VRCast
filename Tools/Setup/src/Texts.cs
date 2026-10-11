using System.Globalization;

namespace VRCast.Setup
{
    /// <summary>
    /// 画面の文言。VRCast 本体と同じ 5 言語（英語・日本語・韓国語・中国語の簡体字 / 繁体字）を OS の表示言語で選ぶ。
    /// それ以外の言語は英語（海外向けの配布先で使うため）。
    /// </summary>
    internal static class Texts
    {
        private enum Language
        {
            English,
            Japanese,
            Korean,
            ChineseSimplified,
            ChineseTraditional,
        }

        private static readonly Language Current = Detect(CultureInfo.CurrentUICulture);

        public static string Title(string version)
        {
            return Pick($"VRCast {version} Setup", $"VRCast {version} セットアップ", $"VRCast {version} 설치",
                $"VRCast {version} 安装程序", $"VRCast {version} 安裝程式");
        }

        public static string Intro => Pick(
            "VRCast (the app) and VRCast-Converter (the exporter for your avatar's Unity project) will be extracted into this folder.",
            "次のフォルダーの中に「VRCast」（アプリ本体）と「VRCast-Converter」（アバターの Unity プロジェクトに入れる書き出しツール）を展開します。",
            "이 폴더 안에 「VRCast」(앱 본체)와 「VRCast-Converter」(아바타의 Unity 프로젝트에 넣는 내보내기 도구)를 압축 해제합니다.",
            "将在此文件夹中解压「VRCast」（主程序）和「VRCast-Converter」（放入虚拟形象 Unity 项目的导出工具）。",
            "將在此資料夾中解壓「VRCast」（主程式）和「VRCast-Converter」（放入虛擬形象 Unity 專案的匯出工具）。");

        public static string Browse => Pick("Browse...", "参照...", "찾아보기...", "浏览...", "瀏覽...");

        public static string BrowseDescription => Pick(
            "Choose the folder to put VRCast in.",
            "VRCast を置くフォルダーを選んでください。",
            "VRCast를 둘 폴더를 선택하세요.",
            "请选择放置 VRCast 的文件夹。",
            "請選擇放置 VRCast 的資料夾。");

        public static string Note => Pick(
            "Folders that need administrator rights (such as Program Files) cannot be used. If you choose a folder that already has VRCast, it is replaced with this version and your settings are kept.",
            "Program Files など管理者権限の必要な場所には置けません。既に VRCast が入っているフォルダーを選ぶと、設定はそのままで新しいバージョンに入れ替えます。",
            "Program Files 등 관리자 권한이 필요한 위치에는 둘 수 없습니다. 이미 VRCast가 들어 있는 폴더를 선택하면 설정은 그대로 두고 새 버전으로 교체합니다.",
            "不能放在 Program Files 等需要管理员权限的位置。如果选择已有 VRCast 的文件夹，将保留设置并替换为新版本。",
            "不能放在 Program Files 等需要系統管理員權限的位置。如果選擇已有 VRCast 的資料夾，將保留設定並替換為新版本。");

        public static string OpenFolder => Pick(
            "Open the folder when finished", "完了後にフォルダーを開く", "완료 후 폴더 열기", "完成后打开文件夹", "完成後開啟資料夾");

        public static string Install => Pick("Extract", "展開する", "압축 해제", "解压", "解壓");

        public static string Cancel => Pick("Cancel", "キャンセル", "취소", "取消", "取消");

        public static string Close => Pick("Close", "閉じる", "닫기", "关闭", "關閉");

        public static string Verifying => Pick(
            "Checking the files...", "ファイルを確認しています...", "파일을 확인하는 중...", "正在检查文件...", "正在檢查檔案...");

        public static string Extracting => Pick(
            "Extracting...", "展開しています...", "압축 해제 중...", "正在解压...", "正在解壓...");

        public static string Updating => Pick(
            "Replacing with the new version...", "新しいバージョンに入れ替えています...", "새 버전으로 교체하는 중...",
            "正在替换为新版本...", "正在替換為新版本...");

        public static string Done => Pick(
            "Done. Start VRCast with VRCast.exe in the VRCast folder. The avatar exporter is in the VRCast-Converter folder.",
            "展開しました。VRCast フォルダーの VRCast.exe で起動できます。アバターの書き出しツールは「VRCast-Converter」フォルダーにあります。",
            "압축 해제했습니다. VRCast 폴더의 VRCast.exe로 시작할 수 있습니다. 아바타 내보내기 도구는 「VRCast-Converter」 폴더에 있습니다.",
            "解压完成。可通过 VRCast 文件夹中的 VRCast.exe 启动。虚拟形象导出工具位于「VRCast-Converter」文件夹。",
            "解壓完成。可透過 VRCast 資料夾中的 VRCast.exe 啟動。虛擬形象匯出工具位於「VRCast-Converter」資料夾。");

        public static string Updated => Pick(
            "Updated to the new version (your settings are kept). Please also import the new exporter in the VRCast-Converter folder.",
            "新しいバージョンに入れ替えました（設定はそのままです）。書き出しツールも「VRCast-Converter」フォルダーの新しいものをインポートしてください。",
            "새 버전으로 교체했습니다 (설정은 그대로입니다). 내보내기 도구도 「VRCast-Converter」 폴더의 새 버전을 가져오세요.",
            "已替换为新版本（设置保持不变）。请同时导入「VRCast-Converter」文件夹中的新版导出工具。",
            "已替換為新版本（設定保持不變）。請同時匯入「VRCast-Converter」資料夾中的新版匯出工具。");

        public static string Damaged => Pick(
            "The setup file is damaged. Please download it again.",
            "セットアップのファイルが壊れています。もう一度ダウンロードしてください。",
            "설치 파일이 손상되었습니다. 다시 다운로드하세요.",
            "安装文件已损坏。请重新下载。",
            "安裝檔案已損壞。請重新下載。");

        public static string Incomplete => Pick(
            "This file does not contain VRCast. Please download it again from the store page.",
            "このファイルには VRCast が入っていません。配布ページからダウンロードし直してください。",
            "이 파일에는 VRCast가 들어 있지 않습니다. 판매 페이지에서 다시 다운로드하세요.",
            "此文件不包含 VRCast。请从商店页面重新下载。",
            "此檔案不包含 VRCast。請從商店頁面重新下載。");

        public static string NotWritable => Pick(
            "This folder cannot be written to. Please choose another folder, such as Desktop or Documents.",
            "このフォルダーには書き込めません。デスクトップやドキュメントなど、別のフォルダーを選んでください。",
            "이 폴더에는 쓸 수 없습니다. 바탕 화면이나 문서 등 다른 폴더를 선택하세요.",
            "无法写入此文件夹。请选择桌面或文档等其他文件夹。",
            "無法寫入此資料夾。請選擇桌面或文件等其他資料夾。");

        public static string Running(string path)
        {
            // ボタン名は OS の言語で表示されるメッセージボックスの「再試行」に合わせる
            return Pick(
                "VRCast is running. Close it, then press Retry.",
                "VRCast が起動しています。終了してから「再試行」を押してください。",
                "VRCast가 실행 중입니다. 종료한 후 「다시 시도」를 누르세요.",
                "VRCast 正在运行。请关闭后按「重试」。",
                "VRCast 正在執行。請關閉後按「重試」。") + "\n\n" + path;
        }

        public static string Failed(string detail)
        {
            return WithDetail(Pick(
                "Could not extract VRCast.", "展開できませんでした。", "압축을 해제하지 못했습니다.", "无法解压。", "無法解壓。"), detail);
        }

        public static string UpdateFailed(string detail)
        {
            return WithDetail(Pick(
                "Could not update VRCast (the previous version is kept).",
                "新しいバージョンに入れ替えられませんでした（元のバージョンのままです）。",
                "새 버전으로 교체하지 못했습니다 (이전 버전이 유지됩니다).",
                "无法替换为新版本（保留原版本）。",
                "無法替換為新版本（保留原版本）。"), detail);
        }

        private static string WithDetail(string message, string detail)
        {
            // 理由（英語）があれば 1 行空けて添える
            return string.IsNullOrEmpty(detail) ? message : message + "\n\n" + detail;
        }

        private static string Pick(string english, string japanese, string korean, string simplified, string traditional)
        {
            return Current switch
            {
                Language.Japanese => japanese,
                Language.Korean => korean,
                Language.ChineseSimplified => simplified,
                Language.ChineseTraditional => traditional,
                _ => english,
            };
        }

        private static Language Detect(CultureInfo culture)
        {
            // 中国語は文字（zh-Hant）か地域（台湾・香港・マカオ）が繁体字なら繁体字、それ以外は簡体字
            string name = culture.Name;
            switch (culture.TwoLetterISOLanguageName)
            {
                case "ja":
                    return Language.Japanese;
                case "ko":
                    return Language.Korean;
                case "zh":
                    bool traditional = name.Contains("Hant") || name.EndsWith("-TW") || name.EndsWith("-HK")
                        || name.EndsWith("-MO");
                    return traditional ? Language.ChineseTraditional : Language.ChineseSimplified;
                default:
                    return Language.English;
            }
        }
    }
}
