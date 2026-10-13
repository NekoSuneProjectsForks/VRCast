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

            var axes = new FaceAxes(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward);
            LandmarkEstimate estimate = LandmarkEstimator.Estimate(positions, CreateTriangles((di, dj) => false), null,
                samples, axes);

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
            LandmarkEstimate estimate = LandmarkEstimator.Estimate(positions, CreateTriangles((di, dj) => false), null,
                new LandmarkShapeSamples(), axes);
            Assert.That(estimate.Points, Is.Empty);
        }

        [Test]
        public void Estimator_UsesEyeAndMouthOpeningsOfTheSkin()
        {
            // 目の穴: 左は x = 0.2〜0.5、y = 0.30〜0.35（右はその鏡）。口の穴: x = -0.2〜0.2、y = -0.45〜-0.40
            int grid = Size * Size;
            var positions = new Vector3[grid + 4];
            System.Array.Copy(CreateGrid(), positions, grid);
            positions[Index(0, 0)].z = 0.1f;

            // 左目の前に浮いた、まばたきで大きく動くまつ毛（肌とつながらない部品）
            positions[grid] = new Vector3(0.2f, 0.36f, 0.05f);
            positions[grid + 1] = new Vector3(0.5f, 0.36f, 0.05f);
            positions[grid + 2] = new Vector3(0.2f, 0.40f, 0.05f);
            positions[grid + 3] = new Vector3(0.5f, 0.40f, 0.05f);
            var triangles = new List<int>(CreateTriangles((di, dj) =>
                (dj == 6 && ((di >= 4 && di <= 9) || (di >= -10 && di <= -5))) || (dj == -9 && di >= -4 && di <= 3)));
            triangles.AddRange(new[] { grid, grid + 1, grid + 2, grid + 1, grid + 3, grid + 2 });

            // まばたきは上まぶたの縁（y = 0.35）とその上が下がる。「あ」は下唇から下が下がる
            var samples = new LandmarkShapeSamples
            {
                BlinkLeft = Shaped(positions, (di, dj) => Lid(di, dj, 1)),
                BlinkRight = Shaped(positions, (di, dj) => Lid(di, dj, -1)),
                MouthOpen = Shaped(positions, (di, dj) =>
                    Mathf.Abs(di) <= 5 && dj >= -18 && dj <= -9 ? new Vector3(0f, -0.1f, 0f) : Vector3.zero),
            };
            for (int k = grid; k < grid + 4; k++)
            {
                samples.BlinkLeft[k] += new Vector3(0f, -0.3f, 0f);
            }

            var axes = new FaceAxes(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward);
            LandmarkEstimate estimate = LandmarkEstimator.Estimate(positions, triangles.ToArray(), null, samples, axes);

            // 目: 穴の上の縁 = 上まぶた、下の縁 = 下まぶた、左右の端 = 目頭・目尻（まつ毛には引っ張られない）
            AssertPoint(estimate, LandmarkId.EyeUpperLeft, new Vector3(0.35f, 0.35f, 0f));
            AssertPoint(estimate, LandmarkId.EyeLowerLeft, new Vector3(0.35f, 0.30f, 0f));
            AssertPoint(estimate, LandmarkId.EyeUpperInnerLeft, new Vector3(0.275f, 0.35f, 0f));
            AssertPoint(estimate, LandmarkId.EyeLowerOuterLeft, new Vector3(0.425f, 0.30f, 0f));
            Assert.That(estimate.Points[LandmarkId.EyeInnerLeft].x, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(estimate.Points[LandmarkId.EyeOuterLeft].x, Is.EqualTo(0.5f).Within(Tolerance));
            AssertPoint(estimate, LandmarkId.EyeUpperRight, new Vector3(-0.35f, 0.35f, 0f));
            AssertPoint(estimate, LandmarkId.EyeLowerRight, new Vector3(-0.35f, 0.30f, 0f));

            // 口: 穴の上下の縁 = 上唇・下唇、左右の端 = 口角、途中の点も縁の上
            AssertPoint(estimate, LandmarkId.LipUpper, new Vector3(0f, -0.40f, 0f));
            AssertPoint(estimate, LandmarkId.LipLower, new Vector3(0f, -0.45f, 0f));
            Assert.That(estimate.Points[LandmarkId.MouthCornerLeft].x, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(estimate.Points[LandmarkId.MouthCornerRight].x, Is.EqualTo(-0.2f).Within(Tolerance));
            Assert.That(estimate.Points[LandmarkId.LipUpperLeft].y, Is.EqualTo(-0.40f).Within(Tolerance));
            Assert.That(estimate.Points[LandmarkId.LipLowerRight].y, Is.EqualTo(-0.45f).Within(Tolerance));
            Assert.That(estimate.Guessed.Contains(LandmarkId.MouthCornerLeft), Is.False);

            // あご・鼻先、置く面にまつ毛の三角形を含まない
            Assert.That(estimate.Points[LandmarkId.Chin].y, Is.EqualTo(-0.9f).Within(Tolerance));
            AssertPoint(estimate, LandmarkId.NoseTip, new Vector3(0f, 0f, 0.1f));
            int lashTriangle = triangles.Count / 3 - 1;
            Assert.That(estimate.Surface.Contains(lashTriangle), Is.False);
            Assert.That(estimate.Surface.Contains(0), Is.True);
        }

        [Test]
        public void Estimator_ClosedLipsAreSplitByTheirMotion()
        {
            // 口を閉じたメッシュ: 上唇と下唇の頂点が同じ位置にあり、つながっていない（「あ」で下唇だけ下がる）
            Vector3[] grid = CreateGrid();
            var positions = new List<Vector3>(grid);
            var triangles = new List<int>(CreateTriangles((di, dj) => false));

            // 口の線（y = -0.4、x = -0.2〜0.2）の下側の三角形が使う頂点を、同じ位置の別の頂点に付け替える（口角は共有）
            var lower = new Dictionary<int, int>();
            for (int di = -3; di <= 3; di++)
            {
                lower[Index(di, -8)] = positions.Count;
                positions.Add(grid[Index(di, -8)]);
            }

            for (int t = 0; t < triangles.Count / 3; t++)
            {
                float centroidY = (positions[triangles[t * 3]].y + positions[triangles[t * 3 + 1]].y
                    + positions[triangles[t * 3 + 2]].y) / 3f;
                float centroidX = (positions[triangles[t * 3]].x + positions[triangles[t * 3 + 1]].x
                    + positions[triangles[t * 3 + 2]].x) / 3f;
                if (centroidY >= -0.4f || Mathf.Abs(centroidX) >= 0.2f)
                {
                    continue;
                }

                for (int c = 0; c < 3; c++)
                {
                    if (lower.TryGetValue(triangles[t * 3 + c], out int split))
                    {
                        triangles[t * 3 + c] = split;
                    }
                }
            }

            Vector3[] array = positions.ToArray();
            Vector3[] open = (Vector3[])array.Clone();
            for (int k = 0; k < array.Length; k++)
            {
                // 付け替えた下唇の頂点と、口の線より下のあごまで
                if (k >= grid.Length || (array[k].y < -0.4f - 1e-4f && array[k].y > -0.9f - 1e-4f
                        && Mathf.Abs(array[k].x) <= 0.25f + 1e-4f))
                {
                    open[k] += new Vector3(0f, -0.1f, 0f);
                }
            }

            var samples = new LandmarkShapeSamples
            {
                BlinkLeft = Shaped(array, (di, dj) => Lid(di, dj, 1)),
                BlinkRight = Shaped(array, (di, dj) => Lid(di, dj, -1)),
                MouthOpen = open,
            };

            var axes = new FaceAxes(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward);
            LandmarkEstimate estimate = LandmarkEstimator.Estimate(array, triangles.ToArray(), null, samples, axes);

            // 唇の線の両端が口角、中央が上唇・下唇（同じ位置）
            Assert.That(estimate.Points[LandmarkId.MouthCornerLeft].x, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(estimate.Points[LandmarkId.MouthCornerRight].x, Is.EqualTo(-0.2f).Within(Tolerance));
            AssertPoint(estimate, LandmarkId.LipUpper, new Vector3(0f, -0.4f, 0f));
            AssertPoint(estimate, LandmarkId.LipLower, new Vector3(0f, -0.4f, 0f));
        }

        [Test]
        public void Correction_RecoversAffineAndDropsOutliers()
        {
            // 画像認識の点を、拡大・傾き・平行移動した位置が正しい位置（1 組だけ大きく外れる）
            var detected = new List<Vector2>();
            var known = new List<Vector2>();
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                Vector2 point = new Vector2(0.5f, 0.5f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.2f;
                detected.Add(point);
                known.Add(Affine(point));
            }

            known[3] += new Vector2(0.3f, -0.2f);
            ImageCorrection correction = ImageCorrection.Fit(detected, known);

            // 外れた組を除き、ほかの点は変換どおりに直る
            Assert.That(correction.AnchorCount, Is.EqualTo(7));
            foreach (Vector2 point in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.45f, 0.62f), new Vector2(0.1f, 0.9f) })
            {
                Assert.That(Vector2.Distance(correction.Apply(point), Affine(point)), Is.LessThan(1e-4f));
            }
        }

        [Test]
        public void Correction_AddsNearbyResiduals()
        {
            // 1 組だけ少しずれる（外れではない）と、その近くの点はずれた位置へ寄る
            var detected = new List<Vector2>();
            var known = new List<Vector2>();
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                Vector2 point = new Vector2(0.5f, 0.5f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.2f;
                detected.Add(point);
                known.Add(Affine(point));
            }

            known[0] += new Vector2(0.01f, 0f);
            ImageCorrection correction = ImageCorrection.Fit(detected, known);
            Assert.That(correction.AnchorCount, Is.EqualTo(8));
            Assert.That(Vector2.Distance(correction.Apply(detected[0]), known[0]), Is.LessThan(0.004f));

            // 組が無ければ何もしない
            Vector2 same = ImageCorrection.Fit(new List<Vector2>(), new List<Vector2>()).Apply(new Vector2(0.3f, 0.7f));
            Assert.That(Vector2.Distance(same, new Vector2(0.3f, 0.7f)), Is.LessThan(1e-6f));
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

        private static int[] CreateTriangles(System.Func<int, int, bool> skip)
        {
            // 格子の四角を 2 つの三角形にする（前（+Z）向き）。skip が true の四角（左下の頂点のずれ）は穴にする
            var triangles = new List<int>();
            for (int i = 0; i < Size - 1; i++)
            {
                for (int j = 0; j < Size - 1; j++)
                {
                    if (skip(i - Half, j - Half))
                    {
                        continue;
                    }

                    int a = i * Size + j;
                    int b = (i + 1) * Size + j;
                    int c = i * Size + j + 1;
                    int d = (i + 1) * Size + j + 1;
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            }

            return triangles.ToArray();
        }

        private static Vector3 Lid(int di, int dj, int side)
        {
            // side の目（1 = 左、-1 = 右）の上まぶたの縁（y = 0.35）が大きく、その上が半分だけ下がる
            int x = di * side;
            if (x < 4 || x > 10)
            {
                return Vector3.zero;
            }

            return dj == 7 ? new Vector3(0f, -0.05f, 0f) : dj == 8 ? new Vector3(0f, -0.025f, 0f) : Vector3.zero;
        }

        private static Vector2 Affine(Vector2 p)
        {
            // テスト用の拡大・傾き・平行移動
            return new Vector2(1.2f * p.x + 0.1f * p.y + 0.03f, -0.05f * p.x + 0.9f * p.y - 0.02f);
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
