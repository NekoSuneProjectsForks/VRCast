using System;
using NUnit.Framework;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.PerfectSync;
using VRCast.Tracking;
using Object = UnityEngine.Object;

namespace VRCast.Tests
{
    /// <summary>
    /// 保存した形状をメッシュの複製へ足す処理と、既存の BlendShape を ARKit 名へ割り当てて動かす処理を確認する。
    /// </summary>
    public class PerfectSyncMeshesTests
    {
        private GameObject _root;
        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;

        [SetUp]
        public void SetUp()
        {
            // 「Body」に BlendShape を 1 つ持つ三角形のメッシュ
            _root = new GameObject("Avatar");
            var body = new GameObject("Body");
            body.transform.SetParent(_root.transform, false);
            _renderer = body.AddComponent<SkinnedMeshRenderer>();
            _mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            _mesh.AddBlendShapeFrame("Blink_L", 100f, new Vector3[3], null, null);
            _renderer.sharedMesh = _mesh;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_mesh);
        }

        [Test]
        public void Apply_AddsShapesToCopyAndRestoreReturnsOriginal()
        {
            // 元からある BlendShape の値を入れておく
            _renderer.SetBlendShapeWeight(0, 40f);
            var data = new PerfectSyncShapeData();
            data.Shapes.Add(new PerfectSyncShape("Body", "jawOpen", new[] { 2 }, new[] { 0f, -0.5f, 0f }));
            data.Shapes.Add(new PerfectSyncShape("Body", "Blink_L", new[] { 0 }, new[] { 0f, 1f, 0f }));
            data.Shapes.Add(new PerfectSyncShape("Missing", "jawOpen", new[] { 0 }, new[] { 0f, 1f, 0f }));

            PerfectSyncMeshes meshes = PerfectSyncMeshes.Apply(_root.transform, data);

            // 複製に差し替わり、新しい形状だけが末尾に足され（同じ名前・無いメッシュは飛ばす）、元の値は引き継がれること
            Mesh copy = _renderer.sharedMesh;
            Assert.That(copy, Is.Not.SameAs(_mesh));
            Assert.That(meshes.AddedShapes, Is.EqualTo(1));
            Assert.That(copy.blendShapeCount, Is.EqualTo(2));
            Assert.That(copy.GetBlendShapeIndex("jawOpen"), Is.EqualTo(1));
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(40f));
            Assert.That(_mesh.blendShapeCount, Is.EqualTo(1));
            Assert.That(meshes.OriginalOf(_renderer), Is.SameAs(_mesh));

            // 足した形状の移動量がそのまま入っていること
            var deltas = new Vector3[3];
            copy.GetBlendShapeFrameVertices(1, 0, deltas, null, null);
            Assert.That(deltas[2], Is.EqualTo(new Vector3(0f, -0.5f, 0f)));

            // 戻すと元のメッシュになり、値も引き継がれること
            meshes.Restore();
            Assert.That(_renderer.sharedMesh, Is.SameAs(_mesh));
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(40f));
            Assert.That(meshes.AddedShapes, Is.EqualTo(0));
        }

        [Test]
        public void Apply_SkipsShapesThatDoNotMatchTheMesh()
        {
            // メッシュの頂点数を超える番号の形状は足さず、差し替えもしないこと
            var data = new PerfectSyncShapeData();
            data.Shapes.Add(new PerfectSyncShape("Body", "jawOpen", new[] { 10 }, new[] { 0f, -0.5f, 0f }));

            PerfectSyncMeshes meshes = PerfectSyncMeshes.Apply(_root.transform, data);

            Assert.That(meshes.AddedShapes, Is.EqualTo(0));
            Assert.That(_renderer.sharedMesh, Is.SameAs(_mesh));
        }

        [Test]
        public void BlendShapes_CreateUsesBindings()
        {
            // 既存の「Blink_L」を半分の強さで eyeBlinkLeft に割り当てる
            var bindings = new[]
            {
                new PerfectSyncBinding { name = "eyeBlinkLeft", path = "Body", blendShape = "Blink_L", weight = 0.5f },
                new PerfectSyncBinding { name = "unknownShape", path = "Body", blendShape = "Blink_L" },
                new PerfectSyncBinding { name = "jawOpen", path = "Body", blendShape = "Missing" },
            };
            PerfectSyncBlendShapes sync = PerfectSyncBlendShapes.Create(_root.transform, bindings);

            // 割り当てた 1 種類だけが見つかること（知らない名前・無い BlendShape は飛ばす）
            Assert.That(sync.MatchedCount, Is.EqualTo(1));
            Assert.That(sync.DrivesBlink, Is.False);

            // 閉じ切ったまばたきは、割り当ての強さを掛けた値で書かれること（鏡像 ON は左右を入れ替えない）
            var scores = new float[ArKitFace.BlendShapeNames.Length];
            scores[Array.IndexOf(ArKitFace.BlendShapeNames, "eyeBlinkLeft")] = 1f;
            sync.Apply(scores, ArKitRange.MediaPipe, true, 10f, true);
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(50f).Within(0.01f));

            // 解除すると元の値（0）へ戻ること
            sync.Release();
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void BlendShapes_SharedShapeFollowsBothSidesWithoutSticking()
        {
            // 両目まとめのまばたき 1 つに、左右のまばたきを割り当てる
            var bindings = new[]
            {
                new PerfectSyncBinding { name = "eyeBlinkLeft", path = "Body", blendShape = "Blink_L", weight = 1f },
                new PerfectSyncBinding { name = "eyeBlinkRight", path = "Body", blendShape = "Blink_L", weight = 1f },
            };
            PerfectSyncBlendShapes sync = PerfectSyncBlendShapes.Create(_root.transform, bindings);
            Assert.That(sync.DrivesBlink, Is.True);

            // 片目だけ閉じると、大きい方（閉じた目）の値になること
            var scores = new float[ArKitFace.BlendShapeNames.Length];
            scores[Array.IndexOf(ArKitFace.BlendShapeNames, "eyeBlinkLeft")] = 1f;
            sync.Apply(scores, ArKitRange.MediaPipe, true, 10f, true);
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.01f));

            // 両目を開くと、前の値に引きずられず開いた値（0）へ戻ること
            Array.Clear(scores, 0, scores.Length);
            sync.Apply(scores, ArKitRange.MediaPipe, true, 10f, true);
            sync.Apply(scores, ArKitRange.MediaPipe, true, 10f, true);
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.01f));
        }
    }
}
