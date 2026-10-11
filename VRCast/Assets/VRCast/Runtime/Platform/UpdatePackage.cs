using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VRCast.Platform
{
    /// <summary>
    /// 自動更新の配布 zip（GitHub Releases の VRCast-x.y.z-win64.zip）の検証・展開と、アップデーターへ渡す引数。
    /// zip には VRCast/（本体）と VRCast-Converter/（書き出しツール）が入っており、本体の分だけを展開する。
    /// Unity に依存しないため、セットアップ EXE（Tools/Setup）もこのファイルをそのままコンパイルして使う。
    /// </summary>
    public static class UpdatePackage
    {
        // ダウンロードを許可する URL の接頭辞（取得した version.json から任意の場所を取りに行かないため）
        public const string DownloadUrlPrefix = "https://github.com/coffin299/VRCast/releases/download/";

        // zip 内の本体のフォルダ（この中身をインストール先へ置く）
        public const string AppFolderPrefix = "VRCast/";

        // zip 内の書き出しツールのフォルダ（セットアップ EXE が本体の隣へ置く）
        public const string ConverterFolderPrefix = "VRCast-Converter/";

        // 同梱のアップデーター（StreamingAssets 内のフォルダとファイル名）
        public const string UpdaterFolder = "Updater";
        public const string UpdaterFileName = "VRCastUpdater.exe";

        // 作業フォルダ（%LOCALAPPDATA%\VRCast\update）内の名前（展開先・アップデーターのログと結果）
        public const string StagingFolderName = "VRCast";
        public const string UpdaterLogFileName = "updater.log";
        public const string UpdaterResultFileName = "result.txt";

        // SHA-256 の 16 進表記の長さ
        private const int Sha256HexLength = 64;

        // 作業フォルダの名前（%LOCALAPPDATA%\VRCast の下）
        private const string WorkFolderName = "update";

        // インストール先に書き込めるかを確かめる一時ファイル
        private const string WriteTestFileName = ".vrcast-write-test";

        /// <summary>
        /// 自動更新の作業フォルダ（%LOCALAPPDATA%\VRCast\update）。VRCast とセットアップ EXE が同じ場所を使う。
        /// </summary>
        public static string WorkFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCast", WorkFolderName);

        /// <summary>
        /// version.json の update の内容が自動更新に使えれば true（許可された URL・正の大きさ・SHA-256）。
        /// </summary>
        public static bool IsValid(string zipUrl, long size, string sha256)
        {
            return IsAllowedUrl(zipUrl) && size > 0 && IsSha256(sha256);
        }

        /// <summary>
        /// GitHub Releases の VRCast のダウンロード URL で、上位参照などを含まなければ true。
        /// </summary>
        public static bool IsAllowedUrl(string url)
        {
            // 接頭辞の後ろで別の場所へ抜けられないよう、上位参照・クエリ・空白を拒否する
            return !string.IsNullOrEmpty(url)
                && url.StartsWith(DownloadUrlPrefix, StringComparison.Ordinal)
                && url.Length > DownloadUrlPrefix.Length
                && url.IndexOf("..", StringComparison.Ordinal) < 0
                && url.IndexOfAny(new[] { '?', '#', '\\', ' ' }) < 0;
        }

        /// <summary>
        /// 64 桁の 16 進数なら true（大文字小文字は問わない）。
        /// </summary>
        public static bool IsSha256(string text)
        {
            if (text == null || text.Length != Sha256HexLength)
            {
                return false;
            }

            foreach (char c in text)
            {
                // 0-9・a-f・A-F 以外は不可
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// zip のエントリ名から本体フォルダ内の相対パス（区切りは OS のもの）を得る。
        /// 本体以外（書き出しツール）・フォルダのエントリは false。不正な名前（上位参照・絶対パス等）は InvalidDataException。
        /// </summary>
        public static bool TryGetAppRelativePath(string entryName, out string relative)
        {
            return TryGetRelativePath(entryName, AppFolderPrefix, out relative);
        }

        /// <summary>
        /// zip のエントリ名から folderPrefix（"VRCast/" 等）のフォルダ内の相対パス（区切りは OS のもの）を得る。
        /// そのフォルダの外・フォルダのエントリは false。不正な名前（上位参照・絶対パス等）は InvalidDataException。
        /// </summary>
        public static bool TryGetRelativePath(string entryName, string folderPrefix, out string relative)
        {
            relative = null;

            // 指定のフォルダの外は展開しない
            if (string.IsNullOrEmpty(entryName) || !entryName.StartsWith(folderPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            // フォルダのエントリはファイルの展開時に作るので飛ばす
            string path = entryName.Substring(folderPrefix.Length);
            if (path.Length == 0 || path.EndsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            // zip slip（展開先の外へ書く名前）を拒否する: バックスラッシュ・ドライブ・先頭の区切り・上位参照
            if (path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 || path.StartsWith("/", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Invalid entry: {entryName}");
            }

            foreach (string part in path.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == "..")
                {
                    throw new InvalidDataException($"Invalid entry: {entryName}");
                }
            }

            relative = path.Replace('/', Path.DirectorySeparatorChar);
            return true;
        }

        /// <summary>
        /// zip の本体フォルダの中身を destination へ展開する（destination は作り直す）。展開したファイル数を返す。
        /// </summary>
        public static int ExtractApp(string zipPath, string destination)
        {
            // 前回の残りと混ざらないよう消してから作る
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            using (FileStream stream = File.OpenRead(zipPath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                return ExtractFolder(zip, AppFolderPrefix, destination, null);
            }
        }

        /// <summary>
        /// zip の folderPrefix のフォルダの中身を destination へ展開する（同じ名前のファイルは上書きし、それ以外は残す）。
        /// 展開したファイル数を返す。progress には書き出したバイト数をファイルごとに渡す（null なら知らせない）。
        /// </summary>
        public static int ExtractFolder(ZipArchive zip, string folderPrefix, string destination, Action<long> progress)
        {
            Directory.CreateDirectory(destination);
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            int count = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (!TryGetRelativePath(entry.FullName, folderPrefix, out string relative))
                {
                    continue;
                }

                // 名前の検査に加え、解決後のパスが展開先の中にあることも確かめる
                string path = Path.GetFullPath(Path.Combine(root, relative));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Invalid entry: {entry.FullName}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
                using (Stream input = entry.Open())
                using (FileStream output = File.Create(path))
                {
                    input.CopyTo(output);
                }

                count++;
                progress?.Invoke(entry.Length);
            }

            return count;
        }

        /// <summary>
        /// 展開した本体フォルダに exe と (exe 名)_Data があれば true。
        /// </summary>
        public static bool HasAppFiles(string folder, string exeName)
        {
            string data = Path.GetFileNameWithoutExtension(exeName) + "_Data";
            return File.Exists(Path.Combine(folder, exeName)) && Directory.Exists(Path.Combine(folder, data));
        }

        /// <summary>
        /// folder にファイルを作って消せれば true（Program Files 等は管理者権限が無いと失敗する）。
        /// </summary>
        public static bool CanWrite(string folder)
        {
            string path = Path.Combine(folder, WriteTestFileName);
            try
            {
                File.WriteAllBytes(path, Array.Empty<byte>());
                File.Delete(path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// アップデーター（Tools/Updater）の起動引数。pid が 0 なら終了を待つプロセスは無く、
        /// restart が false なら差し替え後に VRCast を起動しない（セットアップ EXE が使う）。
        /// </summary>
        public static string BuildUpdaterArguments(int pid, string source, string target, string exeName,
            string logPath, string resultPath, bool restart = true)
        {
            var builder = new StringBuilder();
            builder.Append("--pid ").Append(pid);
            builder.Append(" --source ").Append(Quote(source));
            builder.Append(" --target ").Append(Quote(target));
            builder.Append(" --exe ").Append(Quote(exeName));
            builder.Append(" --log ").Append(Quote(logPath));
            builder.Append(" --result ").Append(Quote(resultPath));

            // 起動し直さないときだけ付ける（付けない引数は古いアップデーターでも読める）
            if (!restart)
            {
                builder.Append(" --restart no");
            }

            return builder.ToString();
        }

        /// <summary>
        /// コマンドラインの 1 引数として引用符で囲む（末尾の \ は閉じの " を打ち消さないよう 2 つにする）。
        /// </summary>
        public static string Quote(string value)
        {
            string text = value ?? string.Empty;
            int trailing = 0;
            while (trailing < text.Length && text[text.Length - 1 - trailing] == '\\')
            {
                trailing++;
            }

            return "\"" + text + new string('\\', trailing) + "\"";
        }
    }
}
