using System;
using NUnit.Framework;
using UnityEngine;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// パーフェクトシンクの BlendShape 名の照合と、左右の対応・書き込みを検証する。
    /// </summary>
    public class PerfectSyncBlendShapesTests
    {
        [Test]
        public void Match_AcceptsCommonNamingVariants()
        {
            int blinkLeft = Array.IndexOf(ArKitFace.BlendShapeNames, "eyeBlinkLeft");

            // 正式名・大文字始まり・区切り記号・L 表記・FBX の接頭辞付きは同じ名前とみなすこと
            Assert.That(PerfectSyncBlendShapes.Match("eyeBlinkLeft"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("EyeBlinkLeft"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("eye_blink_left"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("eyeBlink_L"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("blendShape1.eyeBlinkLeft"), Is.EqualTo(blinkLeft));
        }

        [Test]
        public void Boost_EstimatesCheekPuffFromWidenAndGates()
        {
            var scores = new float[ArKitFace.BlendShapeNames.Length];

            // 1.5〜6% の範囲: 真顔（1%）は 0、中間（3.75%）は 0.5、6% 以上は 1 になること
            Assert.That(PerfectSyncBoost.EstimateCheekPuff(0.01f, scores, 1.5f, 6f), Is.EqualTo(0f));
            Assert.That(PerfectSyncBoost.EstimateCheekPuff(0.0375f, scores, 1.5f, 6f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(PerfectSyncBoost.EstimateCheekPuff(0.1f, scores, 1.5f, 6f), Is.EqualTo(1f));

            // 口を大きく開けているときは推定しないこと
            scores[ArKitFace.JawOpen] = 0.5f;
            Assert.That(PerfectSyncBoost.EstimateCheekPuff(0.1f, scores, 1.5f, 6f), Is.EqualTo(0f));
        }

        [Test]
        public void Boost_UsesLargerOfReceivedAndEstimatedCheekPuff()
        {
            int cheekPuff = ArKitFace.IndexOf("cheekPuff");

            // 倍率 2: 推定 0.3 が受信 0.05 より大きいので 0.6、既定値は強調しないこと
            var boost = new PerfectSyncBoost(1f, 2f, 1f, 0.3f);
            Assert.That(boost.Apply(cheekPuff, 0.05f), Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(default(PerfectSyncBoost).Apply(cheekPuff, 0.05f), Is.EqualTo(0.05f).Within(1e-5f));
        }

        [Test]
        public void Match_RejectsOtherNames()
        {
            // ARKit 名でない名前・空文字は対象外
            Assert.That(PerfectSyncBlendShapes.Match("vrc.v_aa"), Is.EqualTo(-1));
            Assert.That(PerfectSyncBlendShapes.Match("blink"), Is.EqualTo(-1));
            Assert.That(PerfectSyncBlendShapes.Match(string.Empty), Is.EqualTo(-1));
            Assert.That(PerfectSyncBlendShapes.Match(null), Is.EqualTo(-1));
        }

        [Test]
        public void SourceIndex_SwapsSidesOnlyWithoutMirror()
        {
            string[] names = ArKitFace.BlendShapeNames;
            int smileLeft = Array.IndexOf(names, "mouthSmileLeft");
            int smileRight = Array.IndexOf(names, "mouthSmileRight");
            int jawOpen = Array.IndexOf(names, "jawOpen");

            // 受信値の左右は映像基準のため、鏡像 OFF では入れ替え、鏡像 ON ではそのまま。左右の無い名前は常にそのまま
            Assert.That(PerfectSyncBlendShapes.SourceIndex(smileLeft, false), Is.EqualTo(smileRight));
            Assert.That(PerfectSyncBlendShapes.SourceIndex(smileRight, false), Is.EqualTo(smileLeft));
            Assert.That(PerfectSyncBlendShapes.SourceIndex(smileLeft, true), Is.EqualTo(smileLeft));
            Assert.That(PerfectSyncBlendShapes.SourceIndex(jawOpen, false), Is.EqualTo(jawOpen));
        }

        [Test]
        public void Create_FindsShapesAndApplyWritesWeights()
        {
            // ARKit 名を全部持つ顔メッシュを用意
            string[] names = ArKitFace.BlendShapeNames;
            var root = new GameObject("Avatar");
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            var deltas = new Vector3[3];
            foreach (string name in names)
            {
                mesh.AddBlendShapeFrame(name, 100f, deltas, null, null);
            }

            renderer.sharedMesh = mesh;
            try
            {
                // 全種類を見つけ、まばたき・顎を動かせると判定すること
                PerfectSyncBlendShapes sync = PerfectSyncBlendShapes.Create(root.transform);
                Assert.That(sync.MatchedCount, Is.EqualTo(names.Length));
                Assert.That(sync.IsAvailable, Is.True);
                Assert.That(sync.DrivesBlink, Is.True);
                Assert.That(sync.DrivesJaw, Is.True);

                // 平滑化を飛ばす長い経過時間で、映像上の左の笑顔（鏡像 OFF ではアバターの右）と顎を最大にして書く
                var scores = new float[names.Length];
                scores[Array.IndexOf(names, "mouthSmileLeft")] = 1f;
                scores[Array.IndexOf(names, "jawOpen")] = 0.5f;
                scores[Array.IndexOf(names, "eyeBlinkLeft")] = 1f;
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true);
                Assert.That(Weight(renderer, "mouthSmileRight"), Is.EqualTo(100f).Within(0.01f));
                Assert.That(Weight(renderer, "mouthSmileLeft"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(Weight(renderer, "jawOpen"), Is.EqualTo(50f).Within(0.01f));
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(100f).Within(0.01f));

                // 目の開きの補正: 閉じ切ったまばたきは閉じたまま、補正分までの半目は開いた目になること
                var open = new EyeAdjustment { OpenOffset = 0.5f };
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true, open);
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(100f).Within(0.01f));
                scores[Array.IndexOf(names, "eyeBlinkLeft")] = 0.35f;
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true, open);
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(0f).Within(0.01f));
                scores[Array.IndexOf(names, "eyeBlinkLeft")] = 1f;

                // 左右をそろえる補正: 片目だけ閉じても両目が同じ閉じ具合（正面では平均）になること
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true,
                    new EyeAdjustment { Sync = BlinkSyncMode.Always });
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(50f).Within(0.01f));
                Assert.That(Weight(renderer, "eyeBlinkLeft"), Is.EqualTo(50f).Within(0.01f));

                // まばたきを動かさない設定では、まばたきだけ元の値（0）へ戻り、ほかは書き続けること
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, false);
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(Weight(renderer, "mouthSmileRight"), Is.EqualTo(100f).Within(0.01f));

                // 解除すると元の値（0）へ戻ること
                sync.Release();
                Assert.That(Weight(renderer, "mouthSmileRight"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(Weight(renderer, "jawOpen"), Is.EqualTo(0f).Within(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void IsAvailableFor_SwitchesRequiredShapeCount()
        {
            // まばたきの 2 種類だけを持つ顔メッシュを用意
            var root = new GameObject("Avatar");
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            var deltas = new Vector3[3];
            mesh.AddBlendShapeFrame("eyeBlinkLeft", 100f, deltas, null, null);
            mesh.AddBlendShapeFrame("eyeBlinkRight", 100f, deltas, null, null);
            renderer.sharedMesh = mesh;
            try
            {
                // 規定数の条件では非対応、1 種類でもの条件では対応と判定すること
                PerfectSyncBlendShapes sync = PerfectSyncBlendShapes.Create(root.transform);
                Assert.That(sync.MatchedCount, Is.EqualTo(2));
                Assert.That(sync.IsAvailable, Is.False);
                Assert.That(sync.IsAvailableFor(false), Is.False);
                Assert.That(sync.IsAvailableFor(true), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static float Weight(SkinnedMeshRenderer renderer, string name)
        {
            // 名前で BlendShape の現在値を読む
            return renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(name));
        }
    }
}
