using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using VRCast.AvatarFormat;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// パーフェクトシンク版の .vrcaster を別名で保存する（元のファイルは読むだけで変えない）。
    /// 元の ZIP の全エントリを複製し、パーフェクトシンクの 2 つのエントリを足す（既にあれば置き換える）。
    /// manifest は名前だけを変える（bundle と、その SHA-256 は元のまま）。
    /// </summary>
    public static class PerfectSyncPackageWriter
    {
        // 新しいファイル名・アバター名に付ける語
        public const string NameSuffix = "_PerfectSync";

        /// <summary>
        /// 元のパスから、既定の保存先（同じフォルダの「元の名前_PerfectSync.vrcaster」）を作る。
        /// 既にパーフェクトシンク版なら、元と重ならないよう「_2」「_3」…を付ける。
        /// </summary>
        public static string DefaultOutputPath(string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            if (!name.EndsWith(NameSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(directory, name + NameSuffix + AvatarPackageLayout.Extension);
            }

            // まだ無い番号を探す
            for (int number = 2; ; number++)
            {
                string candidate = Path.Combine(directory, $"{name}_{number}{AvatarPackageLayout.Extension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        /// <summary>
        /// sourcePath の中身に set・shapes を足して outputPath へ保存する。
        /// 元と同じパス・不正な内容は例外（ArgumentException / InvalidOperationException）。
        /// </summary>
        public static void Save(string sourcePath, string outputPath, PerfectSyncSet set, PerfectSyncShapeData shapes)
        {
            // 元のファイルを壊さないよう、同じパスへの保存は受け付けない
            if (string.IsNullOrEmpty(outputPath)
                || string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The output must be a different file from the source.", nameof(outputPath));
            }

            // 書く前に内容を検証する（読み込み側と同じ規則）
            string error = set.Validate() ?? shapes.Validate();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            byte[] shapeBytes = shapes.ToBytes();
            if (shapeBytes.Length > AvatarPackageLayout.MaxPerfectSyncShapesBytes)
            {
                throw new InvalidOperationException("The generated shapes are too large.");
            }

            // 一時ファイルへ書いてから置き換える（途中で失敗しても壊れたファイルを残さない）
            string tempPath = outputPath + ".tmp";
            try
            {
                using (FileStream source = File.OpenRead(sourcePath))
                using (var input = new ZipArchive(source, ZipArchiveMode.Read))
                using (FileStream target = File.Create(tempPath))
                using (var output = new ZipArchive(target, ZipArchiveMode.Create))
                {
                    CopyEntries(input, output, Path.GetFileNameWithoutExtension(outputPath));
                    WriteEntry(output, AvatarPackageLayout.PerfectSyncEntry,
                        Encoding.UTF8.GetBytes(JsonUtility.ToJson(set, true)));
                    WriteEntry(output, AvatarPackageLayout.PerfectSyncShapesEntry, shapeBytes);
                }

                // 既存ファイル（上書きの確認済み）を置き換える
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                File.Move(tempPath, outputPath);
            }
            finally
            {
                // 失敗時の一時ファイルを残さない
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static void CopyEntries(ZipArchive input, ZipArchive output, string avatarName)
        {
            foreach (ZipArchiveEntry entry in input.Entries)
            {
                // 古いパーフェクトシンクのデータは書き直すので複製しない
                if (entry.FullName == AvatarPackageLayout.PerfectSyncEntry
                    || entry.FullName == AvatarPackageLayout.PerfectSyncShapesEntry)
                {
                    continue;
                }

                // manifest は名前だけ新しいファイル名にする（一覧で元のアバターと見分けられるように）
                if (entry.FullName == AvatarPackageLayout.ManifestEntry)
                {
                    WriteEntry(output, entry.FullName, Encoding.UTF8.GetBytes(RenameManifest(entry, avatarName)));
                    continue;
                }

                // それ以外（bundle・他の metadata）はそのまま無圧縮で複製する
                ZipArchiveEntry copy = output.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                using (Stream from = entry.Open())
                using (Stream to = copy.Open())
                {
                    from.CopyTo(to);
                }
            }
        }

        private static string RenameManifest(ZipArchiveEntry entry, string avatarName)
        {
            // 上限付きで読む（元のファイルは読み込み時に検証済み）
            if (entry.Length > AvatarPackageLayout.MaxMetadataBytes)
            {
                throw new InvalidOperationException("manifest.json is too large.");
            }

            string json;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                json = reader.ReadToEnd();
            }

            var manifest = JsonUtility.FromJson<AvatarManifest>(json);
            if (manifest == null || manifest.Validate() != null)
            {
                throw new InvalidOperationException("The source manifest is invalid.");
            }

            // 名前の上限に収める
            string name = string.IsNullOrWhiteSpace(avatarName) ? manifest.name + NameSuffix : avatarName;
            manifest.name = name.Length > AvatarManifest.MaxNameLength ? name.Substring(0, AvatarManifest.MaxNameLength) : name;
            return JsonUtility.ToJson(manifest, true);
        }

        private static void WriteEntry(ZipArchive output, string name, byte[] bytes)
        {
            // 他のエントリと同じく無圧縮で格納する
            ZipArchiveEntry entry = output.CreateEntry(name, CompressionLevel.NoCompression);
            using (Stream stream = entry.Open())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
