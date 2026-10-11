using System.Globalization;

namespace VRCast.Setup
{
    /// <summary>
    /// 画面の文言。OS の表示言語が日本語なら日本語、それ以外は英語（海外向けの配布先で使うため英語を既定にする）。
    /// </summary>
    internal static class Texts
    {
        private static readonly bool Japanese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";

        public static string Title(string version)
        {
            return Japanese ? $"VRCast {version} セットアップ" : $"VRCast {version} Setup";
        }

        public static string Intro => Japanese
            ? "次のフォルダーの中に「VRCast」（アプリ本体）と「VRCast-Converter」（アバターの Unity プロジェクトに入れる書き出しツール）を展開します。"
            : "VRCast (the app) and VRCast-Converter (the exporter for your avatar's Unity project) will be extracted into this folder.";

        public static string Browse => Japanese ? "参照..." : "Browse...";

        public static string BrowseDescription => Japanese
            ? "VRCast を置くフォルダーを選んでください。"
            : "Choose the folder to put VRCast in.";

        public static string Note => Japanese
            ? "Program Files など管理者権限の必要な場所には置けません。既に VRCast が入っているフォルダーを選ぶと、設定はそのままで新しいバージョンに入れ替えます。"
            : "Folders that need administrator rights (such as Program Files) cannot be used. If you choose a folder that already has VRCast, it is updated to this version and your settings are kept.";

        public static string StartApp => Japanese ? "完了後に VRCast を起動する" : "Start VRCast when finished";

        public static string OpenFolder => Japanese ? "完了後にフォルダーを開く" : "Open the folder when finished";

        public static string Install => Japanese ? "展開する" : "Install";

        public static string Cancel => Japanese ? "キャンセル" : "Cancel";

        public static string Close => Japanese ? "閉じる" : "Close";

        public static string Verifying => Japanese ? "ファイルを確認しています..." : "Checking the files...";

        public static string Extracting => Japanese ? "展開しています..." : "Extracting...";

        public static string Done => Japanese
            ? "展開しました。アバターの書き出しツールは「VRCast-Converter」フォルダーにあります。"
            : "Done. The avatar exporter is in the VRCast-Converter folder.";

        public static string Updating => Japanese
            ? "VRCast を新しいバージョンに入れ替えています。終わると VRCast が起動します。"
            : "Updating VRCast. It starts when the update is finished.";

        public static string Damaged => Japanese
            ? "セットアップのファイルが壊れています。もう一度ダウンロードしてください。"
            : "The setup file is damaged. Please download it again.";

        public static string Incomplete => Japanese
            ? "このファイルには VRCast が入っていません。配布ページからダウンロードし直してください。"
            : "This file does not contain VRCast. Please download it again from the store page.";

        public static string NotWritable => Japanese
            ? "このフォルダーには書き込めません。デスクトップやドキュメントなど、別のフォルダーを選んでください。"
            : "This folder cannot be written to. Please choose another folder, such as Desktop or Documents.";

        public static string Running(string path)
        {
            return Japanese
                ? $"VRCast が起動しています。終了してから「再試行」を押してください。\n\n{path}"
                : $"VRCast is running. Close it, then press Retry.\n\n{path}";
        }

        public static string Failed(string detail)
        {
            return Japanese ? $"展開できませんでした。\n\n{detail}" : $"Could not install VRCast.\n\n{detail}";
        }
    }
}
