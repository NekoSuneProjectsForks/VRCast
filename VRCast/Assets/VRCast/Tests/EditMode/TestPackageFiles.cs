using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;
using VRCast.AvatarFormat;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace VRCast.Tests
{
    /// <summary>
    /// テスト用の .vrcaster（実際の AssetBundle の代わりにダミーのバイト列を入れたもの）を作る。
    /// </summary>
    public static class TestPackageFiles
    {
        /// <summary>
        /// bundle の内容から正しいハッシュとサイズを入れた manifest。
        /// </summary>
        public static AvatarManifest CreateManifest(byte[] bundle)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return new AvatarManifest
                {
                    name = "TestAvatar",
                    unityVersion = Application.unityVersion,
                    bundleSha256 = BitConverter.ToString(sha.ComputeHash(bundle)).Replace("-", string.Empty).ToLowerInvariant(),
                    bundleSize = bundle.Length,
                };
            }
        }

        /// <summary>
        /// path へ .vrcaster を書く。manifest が null なら省略し、extraEntry があれば追加する（不正名や metadata の検証用）。
        /// </summary>
        public static void Write(string path, AvatarManifest manifest, byte[] bundle, string extraEntry = null,
            byte[] extraContent = null)
        {
            using (FileStream stream = File.Create(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                // manifest（null の場合は省略）
                if (manifest != null)
                {
                    WriteEntry(zip, AvatarPackageLayout.ManifestEntry,
                        System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest)));
                }

                // bundle 本体
                WriteEntry(zip, AvatarPackageLayout.BundleEntry, bundle);

                // 追加エントリ
                if (extraEntry != null)
                {
                    WriteEntry(zip, extraEntry, extraContent ?? Array.Empty<byte>());
                }
            }
        }

        private static void WriteEntry(ZipArchive zip, string name, byte[] data)
        {
            // 無圧縮でエントリを書き込む（Exporter と同じ形式）
            using (Stream entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open())
            {
                entry.Write(data, 0, data.Length);
            }
        }
    }
}
