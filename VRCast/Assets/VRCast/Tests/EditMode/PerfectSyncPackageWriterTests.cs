using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using VRCast.AvatarFormat;
using VRCast.Avatars;
using VRCast.PerfectSync;

namespace VRCast.Tests
{
    /// <summary>
    /// パーフェクトシンク版の保存（元のファイルを変えず、別名の .vrcaster に設定と形状を足す）を確認する。
    /// </summary>
    public class PerfectSyncPackageWriterTests
    {
        // ダミー bundle の中身
        private static readonly byte[] DummyBundle = { 1, 2, 3, 4, 5, 6, 7, 8 };

        private string _directory;
        private string _cacheRoot;

        [SetUp]
        public void SetUp()
        {
            // テストごとに独立した一時ディレクトリを使う
            _directory = Path.Combine(Path.GetTempPath(), "VRCastPerfectSyncTests_" + Guid.NewGuid().ToString("N"));
            _cacheRoot = Path.Combine(_directory, "cache");
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            // 一時ディレクトリを後始末
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void DefaultOutputPath_AddsSuffixOrNumber()
        {
            // 元の名前に _PerfectSync を付け、既にパーフェクトシンク版なら番号を付けること
            string source = Path.Combine(_directory, "Avatar" + AvatarPackageLayout.Extension);
            Assert.That(PerfectSyncPackageWriter.DefaultOutputPath(source),
                Is.EqualTo(Path.Combine(_directory, "Avatar_PerfectSync" + AvatarPackageLayout.Extension)));

            string again = Path.Combine(_directory, "Avatar_PerfectSync" + AvatarPackageLayout.Extension);
            Assert.That(PerfectSyncPackageWriter.DefaultOutputPath(again),
                Is.EqualTo(Path.Combine(_directory, "Avatar_PerfectSync_2" + AvatarPackageLayout.Extension)));
        }

        [Test]
        public void Save_KeepsSourceAndWritesReadablePackage()
        {
            string source = WriteSource();
            byte[] before = File.ReadAllBytes(source);
            string output = PerfectSyncPackageWriter.DefaultOutputPath(source);

            PerfectSyncPackageWriter.Save(source, output, CreateSet("Body"), CreateShapes(-0.1f));

            // 元のファイルは 1 バイトも変わらないこと
            Assert.That(File.ReadAllBytes(source), Is.EqualTo(before));

            // 保存したファイルは読み込めて、設定・形状・新しい名前が入っていること
            AvatarPackage package = AvatarPackageReader.Extract(output, _cacheRoot);
            Assert.That(File.ReadAllBytes(package.BundlePath), Is.EqualTo(DummyBundle));
            Assert.That(package.Manifest.name, Is.EqualTo("Avatar_PerfectSync"));
            Assert.That(package.PerfectSync.facePath, Is.EqualTo("Body"));
            Assert.That(package.PerfectSync.landmarks, Has.Length.EqualTo(1));
            Assert.That(package.PerfectSync.bindings[0].blendShape, Is.EqualTo("blink_L"));
            Assert.That(package.PerfectSyncShapes.Shapes, Has.Count.EqualTo(1));
            Assert.That(package.PerfectSyncShapes.Shapes[0].Deltas[1], Is.EqualTo(-0.1f));
        }

        [Test]
        public void Save_FromPerfectSyncVersionReplacesData()
        {
            // パーフェクトシンク版からさらに別名で保存する
            string source = WriteSource();
            string first = PerfectSyncPackageWriter.DefaultOutputPath(source);
            PerfectSyncPackageWriter.Save(source, first, CreateSet("Body"), CreateShapes(-0.1f));
            string second = PerfectSyncPackageWriter.DefaultOutputPath(first);
            PerfectSyncPackageWriter.Save(first, second, CreateSet("Face"), CreateShapes(-0.3f));

            // 新しい内容に置き換わり、エントリは重複しないこと
            AvatarPackage package = AvatarPackageReader.Extract(second, _cacheRoot);
            Assert.That(package.PerfectSync.facePath, Is.EqualTo("Face"));
            Assert.That(package.PerfectSyncShapes.Shapes[0].Deltas[1], Is.EqualTo(-0.3f));
            using (FileStream stream = File.OpenRead(second))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                Assert.That(zip.Entries.Count(e => e.FullName == AvatarPackageLayout.PerfectSyncEntry), Is.EqualTo(1));
                Assert.That(zip.Entries.Count(e => e.FullName == AvatarPackageLayout.PerfectSyncShapesEntry), Is.EqualTo(1));
            }
        }

        [Test]
        public void Save_RejectsSamePathAndInvalidContent()
        {
            string source = WriteSource();
            string output = PerfectSyncPackageWriter.DefaultOutputPath(source);

            // 元と同じファイルには保存しないこと
            Assert.Throws<ArgumentException>(() =>
                PerfectSyncPackageWriter.Save(source, source, CreateSet("Body"), CreateShapes(-0.1f)));

            // 不正な内容は保存せず、ファイルも残さないこと
            PerfectSyncSet invalid = CreateSet("Body");
            invalid.detail = 9;
            Assert.Throws<InvalidOperationException>(() =>
                PerfectSyncPackageWriter.Save(source, output, invalid, CreateShapes(-0.1f)));
            Assert.That(File.Exists(output), Is.False);
        }

        private string WriteSource()
        {
            // 元の .vrcaster（manifest と bundle だけ）
            string path = Path.Combine(_directory, "Avatar" + AvatarPackageLayout.Extension);
            TestPackageFiles.Write(path, TestPackageFiles.CreateManifest(DummyBundle), DummyBundle);
            return path;
        }

        private static PerfectSyncSet CreateSet(string facePath)
        {
            // 目印 1 つ・形状の設定 1 つ・割り当て 1 つ
            return new PerfectSyncSet
            {
                sourceName = "Avatar.vrcaster",
                facePath = facePath,
                landmarks = new[] { new PerfectSyncLandmark { id = 0, triangle = 1, u = 0.25f, v = 0.25f } },
                shapes = new[] { new PerfectSyncShapeSetting { name = "jawOpen", source = PerfectSyncSet.SourceGenerated } },
                bindings = new[] { new PerfectSyncBinding { name = "eyeBlinkLeft", path = "Body", blendShape = "blink_L" } },
            };
        }

        private static PerfectSyncShapeData CreateShapes(float drop)
        {
            // 頂点 1 つを下げる jawOpen
            var data = new PerfectSyncShapeData();
            data.Shapes.Add(new PerfectSyncShape("Body", "jawOpen", new[] { 2 }, new[] { 0f, drop, 0f }));
            return data;
        }
    }
}
