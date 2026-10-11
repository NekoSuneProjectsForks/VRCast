using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using VRCast.PerfectSync;

namespace VRCast.Tests
{
    /// <summary>
    /// 目印の自動配置（BlendShape からの推定・MediaPipe の結果の読み取り）のテスト。
    /// </summary>
    public sealed class PerfectSyncAutoPlaceTests
    {
        // 平らな顔の格子（-1〜1 を 0.05 刻み。番号 i, j の頂点は x = (i - Half) * Step, y = (j - Half) * Step）
        private const int Size = 41;
        private const int Half = 20;
        private const float Step = 0.05f;
        private const float Tolerance = 1e-4f;

        [Test]
        public void Estimator_FindsEyesMouthAndNoseFromShapes()
        {
            // 左目は (0.35, 0.35) の行、右目はその鏡、口は中央の y = -0.4〜-0.9、鼻先は (0, 0) で少し前
            Vector3[] positions = CreateGrid();
            positions[Index(0, 0)].z = 0.1f;
            var samples = new LandmarkShapeSamples
            {
                BlinkLeft = Shaped(positions, (di, dj) => Blink(di - 7, dj - 7)),
                BlinkRight = Shaped(positions, (di, dj) => Blink(di + 7, dj - 7)),
                MouthOpen = Shaped(positions, (di, dj) =>
                    Mathf.Abs(di) <= 4 && dj >= -18 && dj <= -8 ? new Vector3(0f, -0.1f, 0f) : Vector3.zero),
            };

            var vertices = new List<int>();
            for (int k = 0; k < positions.Length; k++)
            {
                vertices.Add(k);
            }

            var axes = new FaceAxes(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward);
            LandmarkEstimate estimate = LandmarkEstimator.Estimate(positions, vertices, samples, axes);

            // 目: 最も動く頂点が上まぶた、閉じた位置が下まぶた、動く範囲の端が目頭・目尻
            AssertPoint(estimate, LandmarkId.EyeUpperLeft, new Vector3(0.35f, 0.35f, 0f));
            AssertPoint(estimate, LandmarkId.EyeLowerLeft, new Vector3(0.35f, 0.25f, 0f));
            Assert.That(estimate.Points[LandmarkId.EyeInnerLeft].x, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(estimate.Points[LandmarkId.EyeOuterLeft].x, Is.EqualTo(0.5f).Within(Tolerance));
            AssertPoint(estimate, LandmarkId.EyeUpperRight, new Vector3(-0.35f, 0.35f, 0f));

            // 口: 下がる部分の上端が下唇、下端があご（推定ではない）、上唇は下唇より上
            Assert.That(estimate.Points[LandmarkId.LipLower].y, Is.EqualTo(-0.4f).Within(Tolerance));
            Assert.That(estimate.Points[LandmarkId.LipUpper].y, Is.GreaterThan(-0.4f));
            Assert.That(estimate.Points[LandmarkId.Chin].y, Is.EqualTo(-0.9f).Within(Tolerance));
            Assert.That(estimate.Guessed.Contains(LandmarkId.Chin), Is.False);

            // 口角: 「う」が無ければ「あ」で下がる部分の横の端
            Assert.That(estimate.Points[LandmarkId.MouthCornerLeft].x, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(estimate.Points[LandmarkId.MouthCornerRight].x, Is.EqualTo(-0.2f).Within(Tolerance));

            // 鼻先は目と口の間で最も前の頂点、眉は形状が無いので上まぶたの上に推定
            AssertPoint(estimate, LandmarkId.NoseTip, new Vector3(0f, 0f, 0.1f));
            Assert.That(estimate.Guessed.Contains(LandmarkId.BrowMidLeft), Is.True);
            Assert.That(estimate.Guessed.Contains(LandmarkId.BrowMidRight), Is.True);
            Assert.That(estimate.Points[LandmarkId.BrowMidLeft].y, Is.GreaterThan(0.35f));
        }

        [Test]
        public void Estimator_WithoutShapes_PlacesNothing()
        {
            // BlendShape が 1 つも無ければ何も置かないこと
            Vector3[] positions = CreateGrid();
            var axes = new FaceAxes(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward);
            LandmarkEstimate estimate = LandmarkEstimator.Estimate(positions, new[] { 0, 1, 2 },
                new LandmarkShapeSamples(), axes);
            Assert.That(estimate.Points, Is.Empty);
        }

        [Test]
        public void MediaPipe_ParsesOutputAndMapsPoints()
        {
            // 点 i を (i / 1000, i / 2000) にした結果の行（前に状態ログがあってもよい）
            var json = new StringBuilder(MediaPipeFaceMap.OutputPrefix).Append("{\"face\":true,\"points\":[");
            for (int i = 0; i < MediaPipeFaceMap.PointCount; i++)
            {
                json.Append(i > 0 ? "," : string.Empty)
                    .Append((i / 1000f).ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append((i / 2000f).ToString(CultureInfo.InvariantCulture));
            }

            json.Append("]}");
            Assert.That(MediaPipeFaceMap.TryParse(new[] { "INFO: loading", json.ToString() }, out bool face,
                out float[] points), Is.True);
            Assert.That(face, Is.True);
            Assert.That(points.Length, Is.EqualTo(MediaPipeFaceMap.PointCount * 2));

            // 1 点の目印はその点、2 点の目印は中点
            Assert.That(MediaPipeFaceMap.TryGetPoint(points, LandmarkId.NoseTip, out Vector2 nose), Is.True);
            Assert.That(Vector2.Distance(nose, new Vector2(0.001f, 0.0005f)), Is.LessThan(1e-5f));
            Assert.That(MediaPipeFaceMap.TryGetPoint(points, LandmarkId.LipUpper, out Vector2 lip), Is.True);
            Assert.That(Vector2.Distance(lip, new Vector2(0.0065f, 0.00325f)), Is.LessThan(1e-5f));

            // 顔なし・結果の行なし・壊れた行・点が足りないとき
            Assert.That(MediaPipeFaceMap.TryParse(new[] { MediaPipeFaceMap.OutputPrefix + "{\"face\":false,\"points\":[]}" },
                out face, out _), Is.True);
            Assert.That(face, Is.False);
            Assert.That(MediaPipeFaceMap.TryParse(new[] { "INFO: loading" }, out _, out _), Is.False);
            Assert.That(MediaPipeFaceMap.TryParse(new[] { MediaPipeFaceMap.OutputPrefix + "{broken" }, out _, out _),
                Is.False);
            Assert.That(MediaPipeFaceMap.TryGetPoint(new float[4], LandmarkId.NoseTip, out _), Is.False);
        }

        private static Vector3[] CreateGrid()
        {
            // 前（+Z）を向いた平らな格子
            var positions = new Vector3[Size * Size];
            for (int i = 0; i < Size; i++)
            {
                for (int j = 0; j < Size; j++)
                {
                    positions[i * Size + j] = new Vector3((i - Half) * Step, (j - Half) * Step, 0f);
                }
            }

            return positions;
        }

        private static int Index(int di, int dj)
        {
            // 中央からの格子の番号のずれ → 頂点の番号
            return (di + Half) * Size + dj + Half;
        }

        private static Vector3[] Shaped(Vector3[] positions, System.Func<int, int, Vector3> delta)
        {
            // 格子の各頂点に移動量を足した形
            var shaped = (Vector3[])positions.Clone();
            for (int i = 0; i < Size; i++)
            {
                for (int j = 0; j < Size; j++)
                {
                    shaped[i * Size + j] += delta(i - Half, j - Half);
                }
            }

            return shaped;
        }

        private static Vector3 Blink(int di, int dj)
        {
            // 目の行だけ、中央ほど大きく下がる（端は中央の 20%）
            return dj == 0 && Mathf.Abs(di) <= 3 ? new Vector3(0f, -0.1f * (1f - Mathf.Abs(di) / 3f * 0.8f), 0f) : Vector3.zero;
        }

        private static void AssertPoint(LandmarkEstimate estimate, LandmarkId id, Vector3 expected)
        {
            Assert.That(estimate.Points.ContainsKey(id), Is.True, id.ToString());
            Assert.That(Vector3.Distance(estimate.Points[id], expected), Is.LessThan(Tolerance), id.ToString());
        }
    }
}
