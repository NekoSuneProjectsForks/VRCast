using System.Collections.Generic;
using UnityEngine;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 顔の向き（ワールド座標の中心・左・上・前）。目印の自動配置で、頂点を顔の局所座標にするのに使う。
    /// 局所座標は x = アバターの左、y = 上、z = 前、原点は顔の中心（左右の真ん中）。
    /// </summary>
    public readonly struct FaceAxes
    {
        public readonly Vector3 Center;
        public readonly Vector3 Left;
        public readonly Vector3 Up;
        public readonly Vector3 Forward;

        public FaceAxes(Vector3 center, Vector3 left, Vector3 up, Vector3 forward)
        {
            Center = center;
            Left = left.normalized;
            Up = up.normalized;
            Forward = forward.normalized;
        }

        /// <summary>
        /// ワールド座標の点を局所座標にする。
        /// </summary>
        public Vector3 ToLocal(Vector3 point)
        {
            return ToLocalDirection(point - Center);
        }

        /// <summary>
        /// ワールド座標の向き（移動量）を局所座標にする。
        /// </summary>
        public Vector3 ToLocalDirection(Vector3 direction)
        {
            return new Vector3(Vector3.Dot(direction, Left), Vector3.Dot(direction, Up), Vector3.Dot(direction, Forward));
        }

        /// <summary>
        /// 局所座標の点をワールド座標にする。
        /// </summary>
        public Vector3 ToWorld(Vector3 local)
        {
            return Center + Left * local.x + Up * local.y + Forward * local.z;
        }
    }

    /// <summary>
    /// 自動配置に使う、BlendShape を 100 にしたときの頂点の位置（ワールド座標。無い形状は null）。
    /// 眉は使える形状をすべて入れる（眉を下げる・外側を上げる・内側を上げるなど。動く範囲を合わせて使う）。
    /// </summary>
    public sealed class LandmarkShapeSamples
    {
        public Vector3[] BlinkLeft;
        public Vector3[] BlinkRight;
        public Vector3[] MouthOpen;
        public Vector3[] Pucker;
        public readonly List<Vector3[]> Brows = new List<Vector3[]>();
    }

    /// <summary>
    /// 自動配置の結果（目印ごとのワールド座標の位置と、データからではなく比率で推定した目印）。
    /// Surface は目印を置く面（肌の三角形の番号。まつ毛・眉・歯など肌から離れた部品を除く）。肌が分からなければ空。
    /// </summary>
    public sealed class LandmarkEstimate
    {
        public readonly Dictionary<LandmarkId, Vector3> Points = new Dictionary<LandmarkId, Vector3>();
        public readonly HashSet<LandmarkId> Guessed = new HashSet<LandmarkId>();
        public readonly List<int> Surface = new List<int>();
    }

    /// <summary>
    /// アバターの BlendShape（まばたき・口の「あ」「う」・眉）で動く頂点から、目印の位置を推定する。
    /// 顔メッシュのうち肌（三角形でつながる最も大きなまとまり）だけを見て、まつ毛・眉・歯などの部品に引っ張られないようにする。
    /// 目・口は、肌の穴の縁（上下で動き方の違う縁。閉じた唇のように同じ位置で分かれた縁も含む）があればその線から求める:
    /// まばたきで動く縁 = 目（上側 = 上まぶた・下側 = 下まぶた・左右の端 = 目頭・目尻）、
    /// 「あ」で下側だけ下がる顔の中央の縁 = 口（上側 = 上唇・下側 = 下唇・左右の端 = 口角）。
    /// 縁が無いメッシュは、まばたきで最も動く頂点 = 上まぶた・その閉じた位置 = 下まぶた、
    /// 「あ」で下がる部分の上端 = 下唇・すぐ上の動かない頂点 = 上唇、「う」で内側へ寄る頂点 = 口角とする。
    /// あごは「あ」で下がる中央の列の下端（首の奥へは回り込まない）、眉は眉の形状で大きく動く部分、鼻先は目と口の間で最も前の頂点。
    /// データが無い眉・あご・口角は比率で推定する。
    /// </summary>
    public static class LandmarkEstimator
    {
        // 最大の移動量に対する割合: 上まぶたの縁 / 目の動く範囲（目頭・目尻） / 口で下がる部分
        private const float LashRatio = 0.8f;
        private const float EyeExtentRatio = 0.15f;
        private const float MouthMoveRatio = 0.3f;

        // 眉: 動く範囲とみなす割合（眉が別の部品なら部品、肌ごと動くなら肌）と、眉頭・眉尻・中央として平均する幅（眉の長さに対する比）
        private const float BrowPieceRatio = 0.4f;
        private const float BrowSkinRatio = 0.6f;
        private const float BrowEndBand = 0.12f;
        private const float BrowMidBand = 0.15f;

        // 両目の間隔に対する、目の上で眉を探す高さ
        private const float BrowReachRatio = 1.2f;

        // 目の幅に対する、まぶたの中央・途中の点を探す幅
        private const float LidCenterBand = 0.25f;
        private const float LidSideBand = 0.12f;

        // 穴の縁: 目とみなす最大の移動量の割合・顔の幅に対する最小の幅 /
        // 口とみなす下がり具合（最大・上下の差）・両目の間隔に対する最小の幅・前の面からの最大の奥行き
        private const float EyeLoopMoveRatio = 0.5f;
        private const float EyeLoopMinWidth = 0.02f;
        private const float MouthLoopMoveRatio = 0.5f;
        private const float MouthLoopSplitRatio = 0.3f;
        private const float MouthLoopMinWidth = 0.15f;
        private const float MouthLoopDepthRatio = 0.25f;

        // 口角から中央へ向かう、唇の途中の点の位置（MediaPipe の 269・405 などに合わせる）
        private const float LipSideRatio = 0.45f;

        // 角とみなす、端からの幅（目・口の幅に対する比）
        private const float CornerBand = 0.02f;

        // 両目の間隔に対する比率: 中央の列の幅 / 唇の上下の幅 / 口角を探す高さ / 鼻先を探す幅 / 眉の高さ / 口角の位置 /
        // 上唇が見つからないときの上下の唇の隙間 / あごとみなす下唇からの最大の奥行き
        private const float ColumnRatio = 0.1f;
        private const float LipBandRatio = 0.15f;
        private const float CornerBandRatio = 0.25f;
        private const float NoseColumnRatio = 0.12f;
        private const float BrowRaiseRatio = 0.3f;
        private const float CornerSpreadRatio = 0.3f;
        private const float LipGapRatio = 0.02f;
        private const float ChinDepthRatio = 0.5f;

        // 目から口までの高さに対する、あごの下がり具合（推定）と、あごとみなす最小の下がり具合
        private const float ChinDropRatio = 0.45f;
        private const float MinChinDropRatio = 0.2f;

        // 顔の幅に対する両目の間隔（目が見つからないとき）
        private const float EyeDistanceOfFaceWidth = 0.35f;

        // 前を向いた面とみなす、法線の前向きの成分
        private const float FrontFacing = 0.2f;

        // 動いていないとみなす移動量
        private const float Epsilon = 1e-7f;

        /// <summary>
        /// basePositions（今の姿勢のワールド座標）と triangles（顔メッシュの三角形）のうち、
        /// region の三角形（顔のまわり。null なら全部）を使って推定する。
        /// </summary>
        public static LandmarkEstimate Estimate(Vector3[] basePositions, int[] triangles, IReadOnlyList<int> region,
            LandmarkShapeSamples samples, FaceAxes axes)
        {
            var result = new LandmarkEstimate();
            var context = new Context(basePositions, triangles, region, axes);
            if (context.Count == 0)
            {
                return result;
            }

            // 形状ごとの移動量。左右別のまばたきが無ければ両目のまばたきを左右に分けて使う
            Vector3[] blinkLeft = context.Deltas(samples.BlinkLeft ?? samples.BlinkRight);
            Vector3[] blinkRight = context.Deltas(samples.BlinkRight ?? samples.BlinkLeft);
            Vector3[] open = context.Deltas(samples.MouthOpen);
            Vector3[] pucker = context.Deltas(samples.Pucker);
            Vector3[] brow = context.Strongest(samples.Brows);

            // 肌・面の向き・穴の縁（動き方の違う縁は分けて見る）
            context.BuildTopology(blinkLeft, blinkRight, open);
            result.Surface.AddRange(context.SkinTriangles);

            // 目（左右）
            float leftEyeX = 0f;
            float rightEyeX = 0f;
            float eyeY = float.NaN;
            bool left = EstimateEye(context, blinkLeft, 1f, result, ref leftEyeX, ref eyeY);
            bool right = EstimateEye(context, blinkRight, -1f, result, ref rightEyeX, ref eyeY);

            // 両目の間隔（片目だけなら中央からの距離の 2 倍、目が無ければ顔の幅から）
            float eyeDistance = left && right ? Mathf.Abs(leftEyeX - rightEyeX)
                : left ? Mathf.Abs(leftEyeX) * 2f
                : right ? Mathf.Abs(rightEyeX) * 2f
                : context.Width * EyeDistanceOfFaceWidth;
            eyeDistance = Mathf.Max(eyeDistance, Epsilon);

            // 口（唇・あご・口角）
            float mouthY = EstimateMouth(context, open, pucker, eyeDistance, eyeY, result);

            // 眉（形状があれば動く部分、無ければ目の上に推定）
            EstimateBrow(context, brow, 1f, eyeDistance, eyeY, result);
            EstimateBrow(context, brow, -1f, eyeDistance, eyeY, result);

            // 鼻先（目と口がそろったときだけ）
            if (!float.IsNaN(eyeY) && !float.IsNaN(mouthY))
            {
                EstimateNose(context, eyeDistance, eyeY, mouthY, result);
            }

            // 局所座標をワールド座標へ
            var ids = new List<LandmarkId>(result.Points.Keys);
            foreach (LandmarkId id in ids)
            {
                result.Points[id] = axes.ToWorld(result.Points[id]);
            }

            return result;
        }

        /// <summary>
        /// region の三角形（null なら全部）のうち肌の三角形（まつ毛・眉・歯など肌から離れた部品を除く）。
        /// 画像認識で光線を当てる面に使う。
        /// </summary>
        public static List<int> SkinTriangles(Vector3[] positions, int[] triangles, IReadOnlyList<int> region,
            FaceAxes axes)
        {
            var context = new Context(positions, triangles, region, axes);
            context.BuildTopology();
            return new List<int>(context.SkinTriangles);
        }

        private static bool EstimateEye(Context c, Vector3[] delta, float side, LandmarkEstimate result,
            ref float eyeX, ref float eyeY)
        {
            // まぶたの穴の縁があればそこから、無ければ動く頂点から（肌で見つからなければ全部の頂点で）
            if (delta == null)
            {
                return false;
            }

            return EyeFromLoop(c, delta, side, result, ref eyeX, ref eyeY)
                || EyeFromVertices(c, delta, side, c.Skin, result, ref eyeX, ref eyeY)
                || EyeFromVertices(c, delta, side, null, result, ref eyeX, ref eyeY);
        }

        private static bool EyeFromLoop(Context c, Vector3[] delta, float side, LandmarkEstimate result,
            ref float eyeX, ref float eyeY)
        {
            // この側の肌で最も動く量
            float sideMax = 0f;
            for (int k = 0; k < c.Count; k++)
            {
                if (c.Skin[k] && side * c.Local[k].x > 0f)
                {
                    sideMax = Mathf.Max(sideMax, delta[k].magnitude);
                }
            }

            if (sideMax <= Epsilon)
            {
                return false;
            }

            // この側だけにある縁のうち、大きく動く部分（上まぶた）と動きの小さい部分（下まぶた）の差が最も大きいもの
            Loop best = null;
            float bestScore = 0f;
            foreach (Loop loop in c.Loops)
            {
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                float maxMove = 0f;
                float minMove = float.MaxValue;
                bool inside = true;
                foreach (int k in loop.Vertices)
                {
                    float x = side * c.Local[k].x;
                    if (x <= 0f)
                    {
                        inside = false;
                        break;
                    }

                    float move = delta[k].magnitude;
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    maxMove = Mathf.Max(maxMove, move);
                    minMove = Mathf.Min(minMove, move);
                }

                if (!inside || maxMove < sideMax * EyeLoopMoveRatio || maxX - minX < c.Width * EyeLoopMinWidth)
                {
                    continue;
                }

                float score = maxMove + (maxMove - minMove);
                if (score > bestScore)
                {
                    best = loop;
                    bestScore = score;
                }
            }

            if (best == null)
            {
                return false;
            }

            // 縁の内側・外側の端（目頭・目尻）と、中央の x での上下の縁（上まぶた・下まぶた）
            Vector3 inner = Corner(c, best, -side);
            Vector3 outer = Corner(c, best, side);
            float innerX = side * inner.x;
            float outerX = side * outer.x;
            float centerX = (innerX + outerX) * 0.5f;
            if (!Crossings(c, best, side * centerX, out Vector3 upper, out Vector3 lower))
            {
                return false;
            }

            Put(result, LandmarkId.EyeUpperLeft, side, upper);
            Put(result, LandmarkId.EyeLowerLeft, side, lower);
            Put(result, LandmarkId.EyeInnerLeft, side, inner);
            Put(result, LandmarkId.EyeOuterLeft, side, outer);

            // まぶたの途中の点（中央と目頭・目尻の間の x での上下の縁）
            if (Crossings(c, best, side * (centerX + innerX) * 0.5f, out upper, out lower))
            {
                Put(result, LandmarkId.EyeUpperInnerLeft, side, upper);
                Put(result, LandmarkId.EyeLowerInnerLeft, side, lower);
            }

            if (Crossings(c, best, side * (centerX + outerX) * 0.5f, out upper, out lower))
            {
                Put(result, LandmarkId.EyeUpperOuterLeft, side, upper);
                Put(result, LandmarkId.EyeLowerOuterLeft, side, lower);
            }

            // 目の中心の x と、両目の上まぶたの高さ（平均）
            eyeX = side * centerX;
            float top = result.Points[SideOf(LandmarkId.EyeUpperLeft, side)].y;
            eyeY = float.IsNaN(eyeY) ? top : (eyeY + top) * 0.5f;
            return true;
        }

        private static bool EyeFromVertices(Context c, Vector3[] delta, float side, bool[] use, LandmarkEstimate result,
            ref float eyeX, ref float eyeY)
        {
            // この側で最も動く量
            float max = 0f;
            for (int k = 0; k < c.Count; k++)
            {
                if (Uses(use, k) && side * c.Local[k].x > 0f)
                {
                    max = Mathf.Max(max, delta[k].magnitude);
                }
            }

            if (max <= Epsilon)
            {
                return false;
            }

            // 動く範囲の内側・外側の端（目頭・目尻）
            int inner = -1;
            int outer = -1;
            for (int k = 0; k < c.Count; k++)
            {
                float x = side * c.Local[k].x;
                if (!Uses(use, k) || x <= 0f || delta[k].magnitude < max * EyeExtentRatio)
                {
                    continue;
                }

                if (inner < 0 || x < side * c.Local[inner].x)
                {
                    inner = k;
                }

                if (outer < 0 || x > side * c.Local[outer].x)
                {
                    outer = k;
                }
            }

            // 目の中央と幅（side を掛けた x）
            float innerX = side * c.Local[inner].x;
            float outerX = side * c.Local[outer].x;
            float centerX = (innerX + outerX) * 0.5f;
            float width = Mathf.Max(outerX - innerX, Epsilon);

            // 上まぶたの縁の中央: 中央付近で最も動く頂点。閉じた位置を下まぶたにする
            int lash = MostMoved(c, delta, use, side, centerX, width * LidCenterBand, max * LashRatio);
            if (lash < 0)
            {
                lash = MostMoved(c, delta, use, side, centerX, width, 0f);
            }

            Put(result, LandmarkId.EyeUpperLeft, side, c.Local[lash]);
            Put(result, LandmarkId.EyeLowerLeft, side, c.Local[lash] + delta[lash]);
            Put(result, LandmarkId.EyeInnerLeft, side, c.Local[inner]);
            Put(result, LandmarkId.EyeOuterLeft, side, c.Local[outer]);

            // まぶたの途中の点（中央と目頭・目尻の間で最も動く頂点と、その閉じた位置）
            PutLid(c, delta, use, side, (centerX + innerX) * 0.5f, width, LandmarkId.EyeUpperInnerLeft,
                LandmarkId.EyeLowerInnerLeft, result);
            PutLid(c, delta, use, side, (centerX + outerX) * 0.5f, width, LandmarkId.EyeUpperOuterLeft,
                LandmarkId.EyeLowerOuterLeft, result);

            // 目の中心の x と、両目の上まぶたの高さ（平均）
            eyeX = side * centerX;
            float top = c.Local[lash].y;
            eyeY = float.IsNaN(eyeY) ? top : (eyeY + top) * 0.5f;
            return true;
        }

        private static void PutLid(Context c, Vector3[] delta, bool[] use, float side, float targetX, float width,
            LandmarkId upper, LandmarkId lower, LandmarkEstimate result)
        {
            // 目標の x の近くで最も動く頂点
            int k = MostMoved(c, delta, use, side, targetX, width * LidSideBand, 0f);
            if (k < 0 || delta[k].magnitude <= Epsilon)
            {
                return;
            }

            Put(result, upper, side, c.Local[k]);
            Put(result, lower, side, c.Local[k] + delta[k]);
        }

        private static int MostMoved(Context c, Vector3[] delta, bool[] use, float side, float targetX, float band,
            float minimum)
        {
            // side 側で、x が targetX ± band に入り、minimum 以上動く頂点のうち最も動くもの
            int best = -1;
            float bestMove = minimum;
            for (int k = 0; k < c.Count; k++)
            {
                float x = side * c.Local[k].x;
                float move = delta[k].magnitude;
                if (Uses(use, k) && x > 0f && Mathf.Abs(x - targetX) <= band && move >= bestMove && move > Epsilon)
                {
                    best = k;
                    bestMove = move;
                }
            }

            return best;
        }

        private static float EstimateMouth(Context c, Vector3[] open, Vector3[] pucker, float eyeDistance, float eyeY,
            LandmarkEstimate result)
        {
            // 肌が「あ」で最も下がる量（目の少し下より下だけを見る）
            if (open == null)
            {
                return float.NaN;
            }

            float ceiling = float.IsNaN(eyeY) ? float.MaxValue : eyeY - eyeDistance * BrowRaiseRatio;
            float maxDown = 0f;
            for (int k = 0; k < c.Count; k++)
            {
                if (c.Skin[k] && c.Local[k].y < ceiling)
                {
                    maxDown = Mathf.Max(maxDown, -open[k].y);
                }
            }

            if (maxDown <= Epsilon)
            {
                return float.NaN;
            }

            // 唇の縁があればそこから（口角も決まる）、無ければ中央の列の動き方から
            bool corners = MouthFromLoop(c, open, maxDown, ceiling, eyeDistance, result,
                out Vector3 upperLip, out Vector3 lowerLip);
            if (!corners && !MouthFromVertices(c, open, maxDown, ceiling, eyeDistance, result, out upperLip, out lowerLip))
            {
                return float.NaN;
            }

            float mouthY = (upperLip.y + lowerLip.y) * 0.5f;
            EstimateChin(c, open, maxDown, eyeDistance, eyeY, mouthY, lowerLip, result);

            // 口角（左右）
            if (!corners)
            {
                EstimateCorner(c, open, pucker, 1f, eyeDistance, mouthY, maxDown, result);
                EstimateCorner(c, open, pucker, -1f, eyeDistance, mouthY, maxDown, result);
            }

            return mouthY;
        }

        private static bool MouthFromLoop(Context c, Vector3[] open, float maxDown, float ceiling, float eyeDistance,
            LandmarkEstimate result, out Vector3 upperLip, out Vector3 lowerLip)
        {
            upperLip = lowerLip = default;

            // 顔の中央をまたぎ、下側だけが大きく下がる縁（口の中の奥の縁ではなく、顔の前の面の近くにあるもの）
            Loop best = null;
            float bestScore = 0f;
            foreach (Loop loop in c.Loops)
            {
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                float minY = float.MaxValue;
                float maxY = float.MinValue;
                float maxZ = float.MinValue;
                float most = float.MinValue;
                float least = float.MaxValue;
                foreach (int k in loop.Vertices)
                {
                    Vector3 p = c.Local[k];
                    float down = -open[k].y;
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y);
                    maxY = Mathf.Max(maxY, p.y);
                    maxZ = Mathf.Max(maxZ, p.z);
                    most = Mathf.Max(most, down);
                    least = Mathf.Min(least, down);
                }

                if (minX >= 0f || maxX <= 0f || maxY >= ceiling || most < maxDown * MouthLoopMoveRatio
                    || most - least < maxDown * MouthLoopSplitRatio || maxX - minX < eyeDistance * MouthLoopMinWidth
                    || maxZ < FrontZ(c, (minY + maxY) * 0.5f, eyeDistance) - eyeDistance * MouthLoopDepthRatio)
                {
                    continue;
                }

                float score = (most - least) * (maxX - minX);
                if (score > bestScore)
                {
                    best = loop;
                    bestScore = score;
                }
            }

            if (best == null)
            {
                return false;
            }

            // 縁の左右の端（口角）と、その中央の x での上下の縁（上唇・下唇）
            Vector3 cornerLeft = Corner(c, best, 1f);
            Vector3 cornerRight = Corner(c, best, -1f);
            float centerX = (cornerLeft.x + cornerRight.x) * 0.5f;
            if (!Crossings(c, best, centerX, out upperLip, out lowerLip))
            {
                return false;
            }

            result.Points[LandmarkId.LipUpper] = upperLip;
            result.Points[LandmarkId.LipLower] = lowerLip;
            result.Points[LandmarkId.MouthCornerLeft] = cornerLeft;
            result.Points[LandmarkId.MouthCornerRight] = cornerRight;

            // 唇の途中の点（中央と口角の間の x での上下の縁）
            foreach ((Vector3 corner, LandmarkId up, LandmarkId down) in new[]
                     {
                         (cornerLeft, LandmarkId.LipUpperLeft, LandmarkId.LipLowerLeft),
                         (cornerRight, LandmarkId.LipUpperRight, LandmarkId.LipLowerRight),
                     })
            {
                if (Crossings(c, best, Mathf.Lerp(centerX, corner.x, LipSideRatio), out Vector3 top, out Vector3 bottom))
                {
                    result.Points[up] = top;
                    result.Points[down] = bottom;
                }
            }

            return true;
        }

        private static bool MouthFromVertices(Context c, Vector3[] open, float maxDown, float ceiling, float eyeDistance,
            LandmarkEstimate result, out Vector3 upperLip, out Vector3 lowerLip)
        {
            upperLip = lowerLip = default;

            // 中央の列の、前を向いた肌のうち大きく下がる頂点の最も上（下唇の上端）
            float column = eyeDistance * ColumnRatio;
            int lower = -1;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (!c.Skin[k] || c.Front[k] < FrontFacing || Mathf.Abs(p.x) > column || p.y >= ceiling
                    || -open[k].y < maxDown * MouthMoveRatio)
                {
                    continue;
                }

                // 同じ高さなら前の頂点（口の中の面より唇の表面）
                if (lower < 0 || p.y > c.Local[lower].y
                    || (Mathf.Approximately(p.y, c.Local[lower].y) && p.z > c.Local[lower].z))
                {
                    lower = k;
                }
            }

            if (lower < 0)
            {
                return false;
            }

            // 上唇: 下唇から上で、ほとんど下がらない前を向いた頂点のうち最も下（無ければ下唇の少し上）
            lowerLip = c.Local[lower];
            int upper = -1;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (c.Skin[k] && c.Front[k] >= FrontFacing && Mathf.Abs(p.x) <= column && p.y >= lowerLip.y
                    && p.y <= lowerLip.y + eyeDistance * LipBandRatio && -open[k].y < maxDown * MouthMoveRatio
                    && (upper < 0 || p.y < c.Local[upper].y
                        || (Mathf.Approximately(p.y, c.Local[upper].y) && p.z > c.Local[upper].z)))
                {
                    upper = k;
                }
            }

            upperLip = upper >= 0 ? c.Local[upper] : lowerLip + Vector3.up * (eyeDistance * LipGapRatio);
            result.Points[LandmarkId.LipUpper] = upperLip;
            result.Points[LandmarkId.LipLower] = lowerLip;
            return true;
        }

        private static void EstimateChin(Context c, Vector3[] open, float maxDown, float eyeDistance, float eyeY,
            float mouthY, Vector3 lowerLip, LandmarkEstimate result)
        {
            // 中央の列で大きく下がる肌のうち、下唇より下で最も下の頂点（あごの下から首の奥へは回り込まない）
            float column = eyeDistance * ColumnRatio;
            float depth = lowerLip.z - eyeDistance * ChinDepthRatio;
            int bottom = -1;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (c.Skin[k] && Mathf.Abs(p.x) <= column && p.y < lowerLip.y && p.z >= depth
                    && -open[k].y >= maxDown * MouthMoveRatio && (bottom < 0 || p.y < c.Local[bottom].y))
                {
                    bottom = k;
                }
            }

            // 下唇から十分離れていればそれ、無ければ目と口の高さから推定
            float faceHeight = float.IsNaN(eyeY) ? eyeDistance : Mathf.Max(eyeY - mouthY, Epsilon);
            if (bottom >= 0 && lowerLip.y - c.Local[bottom].y >= faceHeight * MinChinDropRatio)
            {
                result.Points[LandmarkId.Chin] = c.Local[bottom];
                return;
            }

            result.Points[LandmarkId.Chin] = lowerLip - Vector3.up * (faceHeight * ChinDropRatio);
            result.Guessed.Add(LandmarkId.Chin);
        }

        private static void EstimateCorner(Context c, Vector3[] open, Vector3[] pucker, float side, float eyeDistance,
            float mouthY, float maxDown, LandmarkEstimate result)
        {
            // 「う」で最も内側へ寄る頂点、無ければ「あ」で下がる部分の口の高さでの横の端
            int best = MostInward(c, pucker, side, eyeDistance, mouthY);
            if (best < 0)
            {
                best = OuterMostOpened(c, open, side, eyeDistance, mouthY, maxDown);
            }

            // どちらも無ければ口の中央から横へ推定
            if (best >= 0)
            {
                Put(result, LandmarkId.MouthCornerLeft, side, c.Local[best]);
                return;
            }

            Put(result, LandmarkId.MouthCornerLeft, side, new Vector3(side * eyeDistance * CornerSpreadRatio, mouthY, 0f));
            result.Guessed.Add(SideOf(LandmarkId.MouthCornerLeft, side));
        }

        private static int MostInward(Context c, Vector3[] pucker, float side, float eyeDistance, float mouthY)
        {
            // side 側の口の高さの近くで、最も内側（中央）へ寄る肌の頂点（形状が無ければ -1）
            int best = -1;
            float bestMove = Epsilon;
            for (int k = 0; pucker != null && k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                float inward = -side * pucker[k].x;
                if (c.Skin[k] && side * p.x > 0f && Mathf.Abs(p.y - mouthY) <= eyeDistance * CornerBandRatio
                    && inward > bestMove)
                {
                    best = k;
                    bestMove = inward;
                }
            }

            return best;
        }

        private static int OuterMostOpened(Context c, Vector3[] open, float side, float eyeDistance, float mouthY,
            float maxDown)
        {
            // side 側の口の高さで、「あ」で大きく下がる肌の頂点のうち最も外側（無ければ -1）
            int best = -1;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (c.Skin[k] && side * p.x > 0f && Mathf.Abs(p.y - mouthY) <= eyeDistance * LipBandRatio
                    && -open[k].y >= maxDown * MouthMoveRatio && (best < 0 || side * p.x > side * c.Local[best].x))
                {
                    best = k;
                }
            }

            return best;
        }

        private static void EstimateBrow(Context c, Vector3[] brow, float side, float eyeDistance, float eyeY,
            LandmarkEstimate result)
        {
            // 目の上の高さで、この側の眉の形状で動く量（肌全体と、肌から離れた部品（眉のメッシュ）だけ）
            float floor = float.IsNaN(eyeY) ? float.MinValue : eyeY;
            float roof = float.IsNaN(eyeY) ? float.MaxValue : eyeY + eyeDistance * BrowReachRatio;
            float max = 0f;
            float pieceMax = 0f;
            for (int k = 0; brow != null && k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (side * p.x > 0f && p.y > floor && p.y < roof)
                {
                    float move = brow[k].magnitude;
                    max = Mathf.Max(max, move);
                    pieceMax = c.Skin[k] ? pieceMax : Mathf.Max(pieceMax, move);
                }
            }

            if (max > Epsilon && TryPutBrow(c, brow, side, floor, roof, pieceMax >= max * 0.5f,
                    pieceMax >= max * 0.5f ? pieceMax * BrowPieceRatio : max * BrowSkinRatio, result))
            {
                return;
            }

            // 形状が無ければ、上まぶたの少し上を眉の中央と推定する（目が無ければ推定しない）
            LandmarkId eye = SideOf(LandmarkId.EyeUpperLeft, side);
            if (result.Points.TryGetValue(eye, out Vector3 lid))
            {
                Put(result, LandmarkId.BrowMidLeft, side, lid + Vector3.up * (eyeDistance * BrowRaiseRatio));
                result.Guessed.Add(SideOf(LandmarkId.BrowMidLeft, side));
            }
        }

        private static bool TryPutBrow(Context c, Vector3[] brow, float side, float floor, float roof, bool pieces,
            float threshold, LandmarkEstimate result)
        {
            // 大きく動く頂点（眉の部品があれば部品だけ）の内外の端
            var moving = new List<int>();
            float innerX = float.MaxValue;
            float outerX = float.MinValue;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                float x = side * p.x;
                if (x > 0f && p.y > floor && p.y < roof && (!pieces || !c.Skin[k]) && brow[k].magnitude >= threshold)
                {
                    moving.Add(k);
                    innerX = Mathf.Min(innerX, x);
                    outerX = Mathf.Max(outerX, x);
                }
            }

            if (moving.Count == 0)
            {
                return false;
            }

            // 眉頭・眉尻・中央は、その x の近くの頂点の平均（眉の上下の縁の間になる）
            float length = Mathf.Max(outerX - innerX, Epsilon);
            float midX = (innerX + outerX) * 0.5f;
            Put(result, LandmarkId.BrowInnerLeft, side, Average(c, moving, side, innerX, length * BrowEndBand));
            Put(result, LandmarkId.BrowOuterLeft, side, Average(c, moving, side, outerX, length * BrowEndBand));
            Put(result, LandmarkId.BrowMidLeft, side, Average(c, moving, side, midX, length * BrowMidBand));
            return true;
        }

        private static Vector3 Average(Context c, List<int> vertices, float side, float x, float band)
        {
            // side を掛けた x が x ± band の頂点の平均（無ければ最も近い頂点）
            Vector3 sum = Vector3.zero;
            int count = 0;
            int nearest = vertices[0];
            foreach (int k in vertices)
            {
                float distance = Mathf.Abs(side * c.Local[k].x - x);
                if (distance <= band)
                {
                    sum += c.Local[k];
                    count++;
                }

                nearest = distance < Mathf.Abs(side * c.Local[nearest].x - x) ? k : nearest;
            }

            return count > 0 ? sum / count : c.Local[nearest];
        }

        private static void EstimateNose(Context c, float eyeDistance, float eyeY, float mouthY, LandmarkEstimate result)
        {
            // 目と口の間の中央の列で、最も前の肌の頂点
            float column = eyeDistance * NoseColumnRatio;
            float margin = (eyeY - mouthY) * 0.1f;
            int best = -1;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (c.Skin[k] && Mathf.Abs(p.x) <= column && p.y > mouthY + margin && p.y < eyeY - margin
                    && (best < 0 || p.z > c.Local[best].z))
                {
                    best = k;
                }
            }

            if (best >= 0)
            {
                result.Points[LandmarkId.NoseTip] = c.Local[best];
            }
        }

        private static float FrontZ(Context c, float y, float eyeDistance)
        {
            // 顔の中央の列で、高さ y の近くの肌の最も前の z（無ければ制限しない）
            float column = eyeDistance * ColumnRatio;
            float band = eyeDistance * LipBandRatio * 2f;
            float front = float.MinValue;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (c.Skin[k] && Mathf.Abs(p.x) <= column && Mathf.Abs(p.y - y) <= band)
                {
                    front = Mathf.Max(front, p.z);
                }
            }

            return front == float.MinValue ? float.MinValue / 2f : front;
        }

        private static Vector3 Corner(Context c, Loop loop, float direction)
        {
            // 縁の x が最も direction 側（1 = 左、-1 = 右）の頂点。同じくらいの頂点が並べば平均（角の縦の辺の中央）
            float extreme = float.MinValue;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            foreach (int k in loop.Vertices)
            {
                extreme = Mathf.Max(extreme, direction * c.Local[k].x);
                minX = Mathf.Min(minX, c.Local[k].x);
                maxX = Mathf.Max(maxX, c.Local[k].x);
            }

            float band = (maxX - minX) * CornerBand;
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (int k in loop.Vertices)
            {
                if (direction * c.Local[k].x >= extreme - band)
                {
                    sum += c.Local[k];
                    count++;
                }
            }

            return sum / Mathf.Max(count, 1);
        }

        private static bool Crossings(Context c, Loop loop, float x, out Vector3 upper, out Vector3 lower)
        {
            // 縁の辺が x を横切る点のうち、最も上と最も下（2 か所以上なければ false）
            upper = lower = default;
            int count = 0;
            foreach ((int a, int b) in loop.Edges)
            {
                Vector3 pa = c.Local[a];
                Vector3 pb = c.Local[b];
                if ((x - pa.x) * (x - pb.x) > 0f || Mathf.Abs(pb.x - pa.x) <= Epsilon)
                {
                    continue;
                }

                Vector3 point = Vector3.Lerp(pa, pb, (x - pa.x) / (pb.x - pa.x));
                upper = count == 0 || point.y > upper.y ? point : upper;
                lower = count == 0 || point.y < lower.y ? point : lower;
                count++;
            }

            return count >= 2;
        }

        private static bool Uses(bool[] use, int k)
        {
            return use == null || use[k];
        }

        private static void Put(LandmarkEstimate result, LandmarkId left, float side, Vector3 local)
        {
            // 左側の目印の番号から、side の側の目印に入れる
            result.Points[SideOf(left, side)] = local;
        }

        private static LandmarkId SideOf(LandmarkId left, float side)
        {
            // 右側（side < 0）なら左右の相手
            return side < 0f && FaceLandmarks.TryGetMirror(left, out LandmarkId right) ? right : left;
        }

        /// <summary>
        /// 肌の穴の縁（三角形 1 つだけが使う辺）のつながった 1 まとまり。辺と頂点は Context の番号。
        /// </summary>
        private sealed class Loop
        {
            public readonly List<(int A, int B)> Edges = new List<(int A, int B)>();
            public readonly List<int> Vertices = new List<int>();
        }

        /// <summary>
        /// 顔のまわりの頂点の局所座標・形状ごとの移動量（局所座標）・肌・面の向き・穴の縁。
        /// 頂点は Context の番号（0〜Count-1）で扱う。
        /// </summary>
        private sealed class Context
        {
            // 同じ位置とみなす距離（顔の幅に対する比）と、同じ動きとみなす差（その形状の最大の移動量に対する比）
            private const float WeldRatio = 1e-4f;
            private const float SameMoveRatio = 0.05f;

            public readonly int Count;
            public readonly Vector3[] Local;
            public readonly float Width;
            public readonly bool[] Skin;
            public readonly float[] Front;
            public readonly List<int> SkinTriangles = new List<int>();
            public readonly List<Loop> Loops = new List<Loop>();

            private readonly Vector3[] _base;
            private readonly FaceAxes _axes;
            private readonly List<int> _vertices = new List<int>();

            // 三角形の 3 頂点（Context の番号）と、元の三角形の番号
            private readonly List<int> _corners = new List<int>();
            private readonly List<int> _triangleIds = new List<int>();

            public Context(Vector3[] basePositions, int[] triangles, IReadOnlyList<int> region, FaceAxes axes)
            {
                _base = basePositions ?? System.Array.Empty<Vector3>();
                _axes = axes;

                // 使う三角形とその頂点に番号を振る（範囲外の番号は飛ばす）
                var index = new Dictionary<int, int>();
                int triangleCount = triangles != null ? triangles.Length / 3 : 0;
                int count = region?.Count ?? triangleCount;
                for (int r = 0; r < count; r++)
                {
                    int t = region != null ? region[r] : r;
                    if (t < 0 || t >= triangleCount)
                    {
                        continue;
                    }

                    int i0 = triangles[t * 3];
                    int i1 = triangles[t * 3 + 1];
                    int i2 = triangles[t * 3 + 2];
                    if ((uint)i0 >= (uint)_base.Length || (uint)i1 >= (uint)_base.Length || (uint)i2 >= (uint)_base.Length)
                    {
                        continue;
                    }

                    _corners.Add(Number(index, i0));
                    _corners.Add(Number(index, i1));
                    _corners.Add(Number(index, i2));
                    _triangleIds.Add(t);
                }

                // 局所座標と、顔の幅（x の範囲）
                Count = _vertices.Count;
                Local = new Vector3[Count];
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                for (int k = 0; k < Count; k++)
                {
                    Local[k] = axes.ToLocal(_base[_vertices[k]]);
                    minX = Mathf.Min(minX, Local[k].x);
                    maxX = Mathf.Max(maxX, Local[k].x);
                }

                Width = Count > 0 ? maxX - minX : 0f;
                Skin = new bool[Count];
                Front = new float[Count];
            }

            public Vector3[] Deltas(Vector3[] shaped)
            {
                // 形状が無い・頂点数が合わなければ null
                if (shaped == null || shaped.Length != _base.Length)
                {
                    return null;
                }

                var deltas = new Vector3[Count];
                for (int k = 0; k < Count; k++)
                {
                    int v = _vertices[k];
                    deltas[k] = _axes.ToLocalDirection(shaped[v] - _base[v]);
                }

                return deltas;
            }

            public Vector3[] Strongest(List<Vector3[]> shapes)
            {
                // 頂点ごとに、いずれかの形状で最も大きく動く移動量（形状が 1 つも無ければ null）
                Vector3[] strongest = null;
                foreach (Vector3[] shaped in shapes)
                {
                    Vector3[] deltas = Deltas(shaped);
                    if (deltas == null)
                    {
                        continue;
                    }

                    strongest ??= new Vector3[Count];
                    for (int k = 0; k < Count; k++)
                    {
                        if (deltas[k].sqrMagnitude > strongest[k].sqrMagnitude)
                        {
                            strongest[k] = deltas[k];
                        }
                    }
                }

                return strongest;
            }

            public void BuildTopology(params Vector3[][] moves)
            {
                // 三角形が無ければ全部を肌とする
                int triangleCount = _triangleIds.Count;
                if (triangleCount == 0)
                {
                    for (int k = 0; k < Count; k++)
                    {
                        Skin[k] = true;
                        Front[k] = 1f;
                    }

                    return;
                }

                // 同じ位置の頂点（UV の継ぎ目で分かれたもの）をまとめ、三角形でつながる最も大きなまとまりを肌とする
                float weld = Mathf.Max(Width, Epsilon) * WeldRatio;
                int[] position = WeldByPosition(weld, out int positionCount);
                var parent = new int[positionCount];
                for (int i = 0; i < positionCount; i++)
                {
                    parent[i] = i;
                }

                for (int t = 0; t < triangleCount; t++)
                {
                    Union(parent, position[_corners[t * 3]], position[_corners[t * 3 + 1]]);
                    Union(parent, position[_corners[t * 3 + 1]], position[_corners[t * 3 + 2]]);
                }

                var sizes = new Dictionary<int, int>();
                int skin = -1;
                for (int t = 0; t < triangleCount; t++)
                {
                    int root = Find(parent, position[_corners[t * 3]]);
                    sizes.TryGetValue(root, out int size);
                    sizes[root] = ++size;
                    if (skin < 0 || size > sizes[skin])
                    {
                        skin = root;
                    }
                }

                var skinTriangle = new bool[triangleCount];
                for (int t = 0; t < triangleCount; t++)
                {
                    skinTriangle[t] = Find(parent, position[_corners[t * 3]]) == skin;
                    if (skinTriangle[t])
                    {
                        SkinTriangles.Add(_triangleIds[t]);
                    }
                }

                for (int k = 0; k < Count; k++)
                {
                    Skin[k] = Find(parent, position[k]) == skin;
                }

                // 面の向き（三角形の向きを面積で重みづけして足し、局所座標の前向きの成分を取る）
                var normals = new Vector3[Count];
                for (int t = 0; t < triangleCount; t++)
                {
                    int a = _corners[t * 3];
                    int b = _corners[t * 3 + 1];
                    int c = _corners[t * 3 + 2];
                    Vector3 pa = _base[_vertices[a]];
                    Vector3 normal = _axes.ToLocalDirection(Vector3.Cross(_base[_vertices[b]] - pa, _base[_vertices[c]] - pa));
                    normals[a] += normal;
                    normals[b] += normal;
                    normals[c] += normal;
                }

                for (int k = 0; k < Count; k++)
                {
                    Front[k] = normals[k].sqrMagnitude > 0f ? normals[k].normalized.z : 0f;
                }

                BuildLoops(position, positionCount, skinTriangle, moves);
            }

            private void BuildLoops(int[] position, int positionCount, bool[] skinTriangle, Vector3[][] moves)
            {
                // 同じ位置でも形状で動き方が違う頂点（閉じた唇・まぶたの上下）は分けたまま、肌の辺ごとに使う三角形を数える
                int[] split = SplitByMove(position, positionCount, moves, out List<int> representatives);
                long n = representatives.Count;
                var edges = new Dictionary<long, int>();
                for (int t = 0; t < skinTriangle.Length; t++)
                {
                    if (!skinTriangle[t])
                    {
                        continue;
                    }

                    int a = split[_corners[t * 3]];
                    int b = split[_corners[t * 3 + 1]];
                    int c = split[_corners[t * 3 + 2]];
                    if (a == b || b == c || c == a)
                    {
                        continue;
                    }

                    CountEdge(edges, a, b, n);
                    CountEdge(edges, b, c, n);
                    CountEdge(edges, c, a, n);
                }

                // 三角形 1 つだけが使う辺（縁）を、つながりごとにまとめる
                var parent = new int[representatives.Count];
                for (int i = 0; i < parent.Length; i++)
                {
                    parent[i] = i;
                }

                var boundary = new List<(int A, int B)>();
                foreach (KeyValuePair<long, int> edge in edges)
                {
                    if (edge.Value == 1)
                    {
                        int a = (int)(edge.Key / n);
                        int b = (int)(edge.Key % n);
                        boundary.Add((a, b));
                        Union(parent, a, b);
                    }
                }

                var byRoot = new Dictionary<int, Loop>();
                var added = new HashSet<int>();
                foreach ((int a, int b) in boundary)
                {
                    int root = Find(parent, a);
                    if (!byRoot.TryGetValue(root, out Loop loop))
                    {
                        loop = new Loop();
                        byRoot[root] = loop;
                        Loops.Add(loop);
                    }

                    loop.Edges.Add((representatives[a], representatives[b]));
                    if (added.Add(a))
                    {
                        loop.Vertices.Add(representatives[a]);
                    }

                    if (added.Add(b))
                    {
                        loop.Vertices.Add(representatives[b]);
                    }
                }
            }

            private int[] WeldByPosition(float weld, out int count)
            {
                // 位置を weld の格子に丸め、同じ格子の頂点に同じ番号を振る
                var ids = new int[Count];
                var byCell = new Dictionary<Vector3Int, int>();
                for (int k = 0; k < Count; k++)
                {
                    Vector3Int cell = Vector3Int.RoundToInt(Local[k] / weld);
                    if (!byCell.TryGetValue(cell, out int id))
                    {
                        id = byCell.Count;
                        byCell[cell] = id;
                    }

                    ids[k] = id;
                }

                count = byCell.Count;
                return ids;
            }

            private int[] SplitByMove(int[] position, int positionCount, Vector3[][] moves, out List<int> representatives)
            {
                // 形状ごとの、同じ動きとみなす差
                var tolerances = new float[moves.Length];
                for (int m = 0; m < moves.Length; m++)
                {
                    float max = 0f;
                    for (int k = 0; moves[m] != null && k < Count; k++)
                    {
                        max = Mathf.Max(max, moves[m][k].magnitude);
                    }

                    tolerances[m] = Mathf.Max(max * SameMoveRatio, Epsilon);
                }

                // 同じ位置の頂点のうち、どの形状でも同じように動くものに同じ番号を振る（代表の頂点と比べる）
                var ids = new int[Count];
                var groups = new List<int>[positionCount];
                representatives = new List<int>();
                for (int k = 0; k < Count; k++)
                {
                    List<int> group = groups[position[k]] ??= new List<int>();
                    int id = -1;
                    foreach (int candidate in group)
                    {
                        if (SameMove(moves, tolerances, k, representatives[candidate]))
                        {
                            id = candidate;
                            break;
                        }
                    }

                    if (id < 0)
                    {
                        id = representatives.Count;
                        representatives.Add(k);
                        group.Add(id);
                    }

                    ids[k] = id;
                }

                return ids;
            }

            private static bool SameMove(Vector3[][] moves, float[] tolerances, int a, int b)
            {
                for (int m = 0; m < moves.Length; m++)
                {
                    if (moves[m] != null && (moves[m][a] - moves[m][b]).magnitude > tolerances[m])
                    {
                        return false;
                    }
                }

                return true;
            }

            private int Number(Dictionary<int, int> index, int vertex)
            {
                // 元の頂点の番号 → Context の番号（初めてなら振る）
                if (!index.TryGetValue(vertex, out int k))
                {
                    k = _vertices.Count;
                    index[vertex] = k;
                    _vertices.Add(vertex);
                }

                return k;
            }

            private static void CountEdge(Dictionary<long, int> edges, int a, int b, long count)
            {
                // 向きを問わない辺の番号（小さい方 × 数 + 大きい方）
                long key = a < b ? a * count + b : b * count + a;
                edges.TryGetValue(key, out int used);
                edges[key] = used + 1;
            }

            private static int Find(int[] parent, int i)
            {
                // 根をたどり、途中の頂点を根へ付け替えて次から速くする
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }

                return i;
            }

            private static void Union(int[] parent, int a, int b)
            {
                int rootA = Find(parent, a);
                int rootB = Find(parent, b);
                if (rootA != rootB)
                {
                    parent[rootB] = rootA;
                }
            }
        }
    }
}
