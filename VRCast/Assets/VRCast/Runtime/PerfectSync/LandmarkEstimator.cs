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
    /// </summary>
    public sealed class LandmarkShapeSamples
    {
        public Vector3[] BlinkLeft;
        public Vector3[] BlinkRight;
        public Vector3[] MouthOpen;
        public Vector3[] Pucker;
        public Vector3[] Brow;
    }

    /// <summary>
    /// 自動配置の結果（目印ごとのワールド座標の位置と、データからではなく比率で推定した目印）。
    /// </summary>
    public sealed class LandmarkEstimate
    {
        public readonly Dictionary<LandmarkId, Vector3> Points = new Dictionary<LandmarkId, Vector3>();
        public readonly HashSet<LandmarkId> Guessed = new HashSet<LandmarkId>();
    }

    /// <summary>
    /// アバターの BlendShape（まばたき・口の「あ」「う」・眉）で動く頂点から、目印の位置を推定する。
    /// まばたきで最も動く頂点 = 上まぶたの縁、その閉じた位置 = 下まぶた、動く範囲の内外の端 = 目頭・目尻、
    /// 「あ」で下がる部分の上端 = 下唇・下端 = あご、「う」で内側へ寄る頂点 = 口角、眉の形状で動く部分 = 眉。
    /// 鼻先は目と口の間で最も前の頂点。データが無い眉・あご・口角は比率で推定する。
    /// </summary>
    public static class LandmarkEstimator
    {
        // 最大の移動量に対する割合: 上まぶたの縁 / 目の動く範囲（目頭・目尻） / 口で下がる部分 / 眉
        private const float LashRatio = 0.8f;
        private const float EyeExtentRatio = 0.15f;
        private const float MouthMoveRatio = 0.3f;
        private const float BrowMoveRatio = 0.5f;
        private const float BrowExtentRatio = 0.3f;

        // 目の幅に対する、まぶたの中央・途中の点を探す幅
        private const float LidCenterBand = 0.25f;
        private const float LidSideBand = 0.12f;

        // 両目の間隔に対する比率: 中央の列の幅 / 唇の上下の幅 / 口角を探す高さ / 鼻先を探す幅 / 眉の高さ / 口角の位置 /
        // 上唇が見つからないときの上下の唇の隙間
        private const float ColumnRatio = 0.1f;
        private const float LipBandRatio = 0.15f;
        private const float CornerBandRatio = 0.25f;
        private const float NoseColumnRatio = 0.12f;
        private const float BrowRaiseRatio = 0.3f;
        private const float CornerSpreadRatio = 0.3f;
        private const float LipGapRatio = 0.02f;

        // 目から口までの高さに対する、あごの下がり具合（推定）と、あごとみなす最小の下がり具合
        private const float ChinDropRatio = 0.45f;
        private const float MinChinDropRatio = 0.2f;

        // 顔の幅に対する両目の間隔（目が見つからないとき）
        private const float EyeDistanceOfFaceWidth = 0.35f;

        // 動いていないとみなす移動量
        private const float Epsilon = 1e-7f;

        /// <summary>
        /// basePositions（今の姿勢のワールド座標）のうち vertices（顔のまわりの頂点）を使って推定する。
        /// </summary>
        public static LandmarkEstimate Estimate(Vector3[] basePositions, IReadOnlyList<int> vertices,
            LandmarkShapeSamples samples, FaceAxes axes)
        {
            var context = new Context(basePositions, vertices, axes);
            var result = new LandmarkEstimate();

            // 目（左右）。左右別のまばたきが無ければ両目のまばたきを左右に分けて使う
            float leftEyeX = 0f;
            float rightEyeX = 0f;
            float eyeY = float.NaN;
            bool left = EstimateEye(context, context.Deltas(samples.BlinkLeft ?? samples.BlinkRight), 1f, result,
                ref leftEyeX, ref eyeY);
            bool right = EstimateEye(context, context.Deltas(samples.BlinkRight ?? samples.BlinkLeft), -1f, result,
                ref rightEyeX, ref eyeY);

            // 両目の間隔（片目だけなら中央からの距離の 2 倍、目が無ければ顔の幅から）
            float eyeDistance = left && right ? Mathf.Abs(leftEyeX - rightEyeX)
                : left ? Mathf.Abs(leftEyeX) * 2f
                : right ? Mathf.Abs(rightEyeX) * 2f
                : context.Width * EyeDistanceOfFaceWidth;
            eyeDistance = Mathf.Max(eyeDistance, Epsilon);

            // 口（唇・あご・口角）
            float mouthY = EstimateMouth(context, context.Deltas(samples.MouthOpen), context.Deltas(samples.Pucker),
                eyeDistance, eyeY, result);

            // 眉（形状があれば動く部分、無ければ目の上に推定）
            Vector3[] brow = context.Deltas(samples.Brow);
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

        private static bool EstimateEye(Context c, Vector3[] delta, float side, LandmarkEstimate result,
            ref float eyeX, ref float eyeY)
        {
            // この側で最も動く量
            if (delta == null)
            {
                return false;
            }

            float max = 0f;
            for (int k = 0; k < c.Count; k++)
            {
                if (side * c.Local[k].x > 0f)
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
                if (x <= 0f || delta[k].magnitude < max * EyeExtentRatio)
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
            int lash = MostMoved(c, delta, side, centerX, width * LidCenterBand, max * LashRatio);
            if (lash < 0)
            {
                lash = MostMoved(c, delta, side, centerX, width, 0f);
            }

            Put(result, LandmarkId.EyeUpperLeft, side, c.Local[lash]);
            Put(result, LandmarkId.EyeLowerLeft, side, c.Local[lash] + delta[lash]);
            Put(result, LandmarkId.EyeInnerLeft, side, c.Local[inner]);
            Put(result, LandmarkId.EyeOuterLeft, side, c.Local[outer]);

            // まぶたの途中の点（中央と目頭・目尻の間で最も動く頂点と、その閉じた位置）
            PutLid(c, delta, side, (centerX + innerX) * 0.5f, width, LandmarkId.EyeUpperInnerLeft,
                LandmarkId.EyeLowerInnerLeft, result);
            PutLid(c, delta, side, (centerX + outerX) * 0.5f, width, LandmarkId.EyeUpperOuterLeft,
                LandmarkId.EyeLowerOuterLeft, result);

            // 目の中心の x と、両目の上まぶたの高さ（平均）
            eyeX = side * centerX;
            float top = c.Local[lash].y;
            eyeY = float.IsNaN(eyeY) ? top : (eyeY + top) * 0.5f;
            return true;
        }

        private static void PutLid(Context c, Vector3[] delta, float side, float targetX, float width,
            LandmarkId upper, LandmarkId lower, LandmarkEstimate result)
        {
            // 目標の x の近くで最も動く頂点
            int k = MostMoved(c, delta, side, targetX, width * LidSideBand, 0f);
            if (k < 0 || delta[k].magnitude <= Epsilon)
            {
                return;
            }

            Put(result, upper, side, c.Local[k]);
            Put(result, lower, side, c.Local[k] + delta[k]);
        }

        private static int MostMoved(Context c, Vector3[] delta, float side, float targetX, float band, float minimum)
        {
            // side 側で、x が targetX ± band に入り、minimum 以上動く頂点のうち最も動くもの
            int best = -1;
            float bestMove = minimum;
            for (int k = 0; k < c.Count; k++)
            {
                float x = side * c.Local[k].x;
                float move = delta[k].magnitude;
                if (x > 0f && Mathf.Abs(x - targetX) <= band && move >= bestMove && move > Epsilon)
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
            // 「あ」で最も下がる量（目の少し下より下だけを見る）
            if (open == null)
            {
                return float.NaN;
            }

            float ceiling = float.IsNaN(eyeY) ? float.MaxValue : eyeY - eyeDistance * BrowRaiseRatio;
            float maxDown = 0f;
            for (int k = 0; k < c.Count; k++)
            {
                if (c.Local[k].y < ceiling)
                {
                    maxDown = Mathf.Max(maxDown, -open[k].y);
                }
            }

            if (maxDown <= Epsilon)
            {
                return float.NaN;
            }

            // 中央の列で大きく下がる頂点のうち、最も上（下唇の上端）と最も下（あごの候補）
            float column = eyeDistance * ColumnRatio;
            int lower = -1;
            int bottom = -1;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (Mathf.Abs(p.x) > column || p.y >= ceiling || -open[k].y < maxDown * MouthMoveRatio)
                {
                    continue;
                }

                // 同じ高さなら前の頂点（口の中の面より唇の表面）
                if (lower < 0 || p.y > c.Local[lower].y
                    || (Mathf.Approximately(p.y, c.Local[lower].y) && p.z > c.Local[lower].z))
                {
                    lower = k;
                }

                if (bottom < 0 || p.y < c.Local[bottom].y)
                {
                    bottom = k;
                }
            }

            if (lower < 0)
            {
                return float.NaN;
            }

            // 上唇: 下唇のすぐ上で、ほとんど下がらない頂点のうち最も前（無ければ下唇の少し上）
            Vector3 lowerLip = c.Local[lower];
            int upper = -1;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (Mathf.Abs(p.x) <= column && p.y >= lowerLip.y && p.y <= lowerLip.y + eyeDistance * LipBandRatio
                    && -open[k].y < maxDown * MouthMoveRatio && (upper < 0 || p.z > c.Local[upper].z))
                {
                    upper = k;
                }
            }

            Vector3 upperLip = upper >= 0 ? c.Local[upper] : lowerLip + Vector3.up * (eyeDistance * LipGapRatio);
            result.Points[LandmarkId.LipUpper] = upperLip;
            result.Points[LandmarkId.LipLower] = lowerLip;
            float mouthY = (upperLip.y + lowerLip.y) * 0.5f;

            // あご: 下がる部分の下端が下唇から十分離れていればそれ、無ければ目と口の高さから推定
            float faceHeight = float.IsNaN(eyeY) ? eyeDistance : Mathf.Max(eyeY - mouthY, Epsilon);
            if (bottom >= 0 && lowerLip.y - c.Local[bottom].y >= faceHeight * MinChinDropRatio)
            {
                result.Points[LandmarkId.Chin] = c.Local[bottom];
            }
            else
            {
                result.Points[LandmarkId.Chin] = lowerLip - Vector3.up * (faceHeight * ChinDropRatio);
                result.Guessed.Add(LandmarkId.Chin);
            }

            // 口角（左右）
            EstimateCorner(c, open, pucker, 1f, eyeDistance, mouthY, maxDown, result);
            EstimateCorner(c, open, pucker, -1f, eyeDistance, mouthY, maxDown, result);
            return mouthY;
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
            // side 側の口の高さの近くで、最も内側（中央）へ寄る頂点（形状が無ければ -1）
            int best = -1;
            float bestMove = Epsilon;
            for (int k = 0; pucker != null && k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                float inward = -side * pucker[k].x;
                if (side * p.x > 0f && Mathf.Abs(p.y - mouthY) <= eyeDistance * CornerBandRatio && inward > bestMove)
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
            // side 側の口の高さで、「あ」で大きく下がる頂点のうち最も外側（無ければ -1）
            int best = -1;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (side * p.x > 0f && Mathf.Abs(p.y - mouthY) <= eyeDistance * LipBandRatio
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
            // 目より上で、この側の眉の形状で動く量
            float max = 0f;
            for (int k = 0; brow != null && k < c.Count; k++)
            {
                if (side * c.Local[k].x > 0f && (float.IsNaN(eyeY) || c.Local[k].y > eyeY))
                {
                    max = Mathf.Max(max, brow[k].magnitude);
                }
            }

            if (max > Epsilon)
            {
                // 大きく動く部分の中央に最も近い頂点を眉の中央、少し動く部分の内外の端を眉頭・眉尻にする
                Vector3 sum = Vector3.zero;
                int count = 0;
                int inner = -1;
                int outer = -1;
                for (int k = 0; k < c.Count; k++)
                {
                    Vector3 p = c.Local[k];
                    float x = side * p.x;
                    float move = brow[k].magnitude;
                    if (x <= 0f || (!float.IsNaN(eyeY) && p.y <= eyeY) || move < max * BrowExtentRatio)
                    {
                        continue;
                    }

                    if (move >= max * BrowMoveRatio)
                    {
                        sum += p;
                        count++;
                    }

                    inner = inner < 0 || x < side * c.Local[inner].x ? k : inner;
                    outer = outer < 0 || x > side * c.Local[outer].x ? k : outer;
                }

                Vector3 middle = sum / Mathf.Max(count, 1);
                int nearest = -1;
                for (int k = 0; k < c.Count; k++)
                {
                    if (side * c.Local[k].x > 0f && brow[k].magnitude >= max * BrowMoveRatio
                        && (nearest < 0 || (c.Local[k] - middle).sqrMagnitude < (c.Local[nearest] - middle).sqrMagnitude))
                    {
                        nearest = k;
                    }
                }

                Put(result, LandmarkId.BrowMidLeft, side, c.Local[nearest]);
                Put(result, LandmarkId.BrowInnerLeft, side, c.Local[inner]);
                Put(result, LandmarkId.BrowOuterLeft, side, c.Local[outer]);
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

        private static void EstimateNose(Context c, float eyeDistance, float eyeY, float mouthY, LandmarkEstimate result)
        {
            // 目と口の間の中央の列で、最も前の頂点
            float column = eyeDistance * NoseColumnRatio;
            float margin = (eyeY - mouthY) * 0.1f;
            int best = -1;
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Local[k];
                if (Mathf.Abs(p.x) <= column && p.y > mouthY + margin && p.y < eyeY - margin
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
        /// 顔のまわりの頂点の局所座標と、形状ごとの移動量（局所座標）。
        /// </summary>
        private sealed class Context
        {
            public readonly int Count;
            public readonly Vector3[] Local;
            public readonly float Width;

            private readonly Vector3[] _base;
            private readonly IReadOnlyList<int> _vertices;
            private readonly FaceAxes _axes;

            public Context(Vector3[] basePositions, IReadOnlyList<int> vertices, FaceAxes axes)
            {
                _base = basePositions;
                _vertices = vertices;
                _axes = axes;
                Count = vertices.Count;
                Local = new Vector3[Count];

                // 局所座標と、顔の幅（x の範囲）
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                for (int k = 0; k < Count; k++)
                {
                    Local[k] = axes.ToLocal(basePositions[vertices[k]]);
                    minX = Mathf.Min(minX, Local[k].x);
                    maxX = Mathf.Max(maxX, Local[k].x);
                }

                Width = Count > 0 ? maxX - minX : 0f;
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
        }
    }
}
