using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using VRCast.AvatarFormat;
using VRCast.Platform;

namespace VRCast.Setup
{
    /// <summary>
    /// 展開の段階（画面の表示に使う）。
    /// </summary>
    internal enum InstallStage
    {
        Verifying,
        Extracting,
    }

    /// <summary>
    /// 利用者に見せる理由付きの失敗（メッセージは表示言語のもの）。
    /// </summary>
    internal sealed class SetupException : Exception
    {
        public SetupException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// 配布 zip と同じ構成（選んだフォルダの下に VRCast と VRCast-Converter）で展開する。
    /// 既に VRCast が入っていれば、新しい版を自動更新と同じ作業フォルダへ展開し、
    /// 同梱のアップデーターに差し替え（使用中の DLL の退避・失敗時の巻き戻し）と起動し直しを任せる。
    /// </summary>
    internal sealed class Installer
    {
        // 本体の実行ファイルと、選んだフォルダの下に作るフォルダの名前（配布 zip の最上位と同じ）
        public const string ExeName = "VRCast.exe";
        private const string AppFolderName = "VRCast";
        private const string ConverterFolderName = "VRCast-Converter";

        // 書き出しツールの unitypackage（版ごとに名前が変わるので、古い版を残さないよう消す）
        private const string ConverterPackagePattern = "VRCast-Converter-*.unitypackage";

        // 本体の StreamingAssets（Unity の出力では exe 名_Data の下）
        private const string StreamingAssetsFolderName = "StreamingAssets";

        private readonly Payload _payload;

        public Installer(Payload payload)
        {
            _payload = payload;
        }

        public static string AppFolder(string parent)
        {
            return Path.Combine(parent, AppFolderName);
        }

        public static string ConverterFolder(string parent)
        {
            return Path.Combine(parent, ConverterFolderName);
        }

        /// <summary>
        /// parent の下に VRCast が入っていれば true（展開ではなく更新になる）。
        /// </summary>
        public static bool IsInstalled(string parent)
        {
            return File.Exists(Path.Combine(AppFolder(parent), ExeName));
        }

        /// <summary>
        /// parent の下の VRCast フォルダの実行ファイルで動いているプロセス（VRCast・トラッカー）を 1 つ返す。無ければ null。
        /// </summary>
        public static string FindRunning(string parent)
        {
            string folder = Path.GetFullPath(AppFolder(parent)).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string found = null;
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    // 見つかった後は残りを閉じるだけ
                    if (found != null)
                    {
                        continue;
                    }

                    try
                    {
                        // 実行ファイルの場所が VRCast フォルダの中なら使用中
                        string path = process.MainModule?.FileName;
                        if (path != null && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                        {
                            found = path;
                        }
                    }
                    catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
                    {
                        // システム・他のユーザーのプロセスや、終了したプロセスは調べられない（VRCast ではない）
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// 検証してから展開する。更新のときはアップデーターを起動して true を返す（呼び出し側はすぐ終了すること）。
        /// report には段階と進み具合（0〜1）を渡す（別スレッドから呼ばれる）。
        /// </summary>
        public bool Run(string parent, Action<InstallStage, double> report)
        {
            report(InstallStage.Verifying, 0);
            using (Stream stream = _payload.Open())
            {
                // EXE が途中で切れた・書き換えられた場合は何も展開しない
                if (!string.Equals(HashUtility.ComputeSha256Hex(stream), _payload.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SetupException(Texts.Damaged);
                }

                stream.Position = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
                {
                    return Extract(zip, parent, report);
                }
            }
        }

        /// <summary>
        /// VRCast を起動する（新しく展開したとき）。
        /// </summary>
        public static void StartApp(string parent)
        {
            string folder = AppFolder(parent);
            var info = new ProcessStartInfo(Path.Combine(folder, ExeName))
            {
                UseShellExecute = false,
                WorkingDirectory = folder,
            };
            Process.Start(info)?.Dispose();
        }

        /// <summary>
        /// 展開先をエクスプローラーで開く（VRCast と VRCast-Converter が並んで見える）。
        /// </summary>
        public static void OpenFolder(string parent)
        {
            Process.Start("explorer.exe", UpdatePackage.Quote(parent))?.Dispose();
        }

        private static bool Extract(ZipArchive zip, string parent, Action<InstallStage, double> report)
        {
            // 進み具合の分母は、展開する 2 つのフォルダのファイルの大きさの合計
            long total = zip.Entries
                .Where(entry => entry.FullName.StartsWith(UpdatePackage.AppFolderPrefix, StringComparison.Ordinal)
                    || entry.FullName.StartsWith(UpdatePackage.ConverterFolderPrefix, StringComparison.Ordinal))
                .Sum(entry => entry.Length);
            long done = 0;
            Action<long> progress = bytes =>
            {
                done += bytes;
                report(InstallStage.Extracting, total > 0 ? (double)done / total : 1);
            };
            report(InstallStage.Extracting, 0);

            // 既に入っていれば作業フォルダへ、無ければそのまま VRCast フォルダへ展開する
            bool update = IsInstalled(parent);
            string app = update ? PrepareStaging() : AppFolder(parent);
            UpdatePackage.ExtractFolder(zip, UpdatePackage.AppFolderPrefix, app, progress);
            if (!UpdatePackage.HasAppFiles(app, ExeName))
            {
                throw new SetupException(Texts.Damaged);
            }

            // 書き出しツールは古い版を消してから置く（Unity で開いていなければ差し替えに支障は無い）
            string converter = ConverterFolder(parent);
            RemoveOldConverterPackages(converter);
            UpdatePackage.ExtractFolder(zip, UpdatePackage.ConverterFolderPrefix, converter, progress);

            // 本体の差し替えはアップデーターに任せる（このプロセスの終了を待ってから始まる）
            if (update)
            {
                StartUpdater(app, AppFolder(parent));
            }

            return update;
        }

        private static string PrepareStaging()
        {
            // 前回の残りと混ざらないよう消してから作る（自動更新と同じ場所。VRCast が次の起動時に消す）
            string staging = Path.Combine(UpdatePackage.WorkFolder, UpdatePackage.StagingFolderName);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            Directory.CreateDirectory(staging);
            return staging;
        }

        private static void RemoveOldConverterPackages(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(folder, ConverterPackagePattern))
            {
                File.Delete(file);
            }
        }

        private static void StartUpdater(string staging, string target)
        {
            // 新しい版に入っているアップデーターを作業フォルダへ写して使う（インストール先の分も差し替えられるように）
            string data = Path.GetFileNameWithoutExtension(ExeName) + "_Data";
            string source = Path.Combine(staging, data, StreamingAssetsFolderName, UpdatePackage.UpdaterFolder,
                UpdatePackage.UpdaterFileName);
            if (!File.Exists(source))
            {
                throw new SetupException(Texts.Damaged);
            }

            string work = UpdatePackage.WorkFolder;
            string updater = Path.Combine(work, UpdatePackage.UpdaterFileName);
            string result = Path.Combine(work, UpdatePackage.UpdaterResultFileName);
            File.Copy(source, updater, true);

            // 前回の結果を消す（VRCast は次の起動時に今回の結果を読んで表示する）
            if (File.Exists(result))
            {
                File.Delete(result);
            }

            string arguments = UpdatePackage.BuildUpdaterArguments(Process.GetCurrentProcess().Id, staging, target,
                ExeName, Path.Combine(work, UpdatePackage.UpdaterLogFileName), result);
            var info = new ProcessStartInfo(updater, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = work,
            };
            Process.Start(info)?.Dispose();
        }
    }
}
