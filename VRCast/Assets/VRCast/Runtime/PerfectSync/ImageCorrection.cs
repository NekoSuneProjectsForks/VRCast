using System.Collections.Generic;
using UnityEngine;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 画像認識（MediaPipe）の点を、BlendShape から求めた目印（同じ目印の画像上の正しい位置）に合わせて補正する。
    /// 全体のずれ（位置・大きさ・傾き・縦横の伸び）はアフィン変換で直し、残ったずれは近くの目印ほど強く足す。
    /// 大きく食い違う組（アニメ調の目を MediaPipe が読み違えたなど）は合わせる対象から外す。
    /// </summary>
    public sealed class ImageCorrection
    {
        // 外れとみなす残りのずれ（中央値に対する倍率と、目印の広がりに対する最小の割合）
        private const float OutlierRatio = 3f;
        private const float MinOutlierRatio = 0.05f;

        // 外れを除く判断に必要な組の数
        private const int OutlierMinPairs = 5;

        // 残りのずれを足す範囲（目印の広がりに対する比）と、近くに目印が無いときにずれを足さない強さ
        private const float RadiusRatio = 0.35f;
        private const float PriorWeight = 0.2f;

        // x' = a x + b y + c、y' = d x + e y + f
        private double _a = 1.0;
        private double _b;
        private double _c;
        private double _d;
        private double _e = 1.0;
        private double _f;

        private readonly List<Vector2> _anchors = new List<Vector2>();
        private readonly List<Vector2> _residuals = new List<Vector2>();
        private float _radius = 1f;

        /// <summary>
        /// 合わせるのに使った組の数（外れを除いた後）。
        /// </summary>
        public int AnchorCount => _anchors.Count;

        /// <summary>
        /// detected（画像認識の点）を known（同じ目印の正しい位置）へ合わせる補正を作る。組が無ければ何もしない補正。
        /// </summary>
        public static ImageCorrection Fit(IReadOnlyList<Vector2> detected, IReadOnlyList<Vector2> known)
        {
            var correction = new ImageCorrection();
            int count = Mathf.Min(detected.Count, known.Count);
            var used = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                used.Add(i);
            }

            if (count == 0)
            {
                return correction;
            }

            correction.Solve(detected, known, used);
            float spread = Spread(detected, used);

            // 大きく外れた組を除いて合わせ直す
            if (used.Count >= OutlierMinPairs)
            {
                var errors = new List<float>(used.Count);
                foreach (int i in used)
                {
                    errors.Add((correction.Map(detected[i]) - known[i]).magnitude);
                }

                var sorted = new List<float>(errors);
                sorted.Sort();
                float limit = Mathf.Max(sorted[sorted.Count / 2] * OutlierRatio, spread * MinOutlierRatio);
                var kept = new List<int>(used.Count);
                for (int n = 0; n < used.Count; n++)
                {
                    if (errors[n] <= limit)
                    {
                        kept.Add(used[n]);
                    }
                }

                if (kept.Count < used.Count && kept.Count >= 3)
                {
                    used = kept;
                    correction.Solve(detected, known, used);
                    spread = Spread(detected, used);
                }
            }

            // 残りのずれ（目印の位置で足す量）と、それを足す範囲
            foreach (int i in used)
            {
                correction._anchors.Add(detected[i]);
                correction._residuals.Add(known[i] - correction.Map(detected[i]));
            }

            correction._radius = Mathf.Max(spread * RadiusRatio, 1e-6f);
            return correction;
        }

        /// <summary>
        /// 画像認識の点を補正した位置。
        /// </summary>
        public Vector2 Apply(Vector2 point)
        {
            // 全体の変換に、近くの目印の残りのずれを重みづけして足す（遠ければ足さない）
            Vector2 mapped = Map(point);
            Vector2 sum = Vector2.zero;
            float weight = PriorWeight;
            float scale = 2f * _radius * _radius;
            for (int i = 0; i < _anchors.Count; i++)
            {
                float w = Mathf.Exp(-(point - _anchors[i]).sqrMagnitude / scale);
                sum += _residuals[i] * w;
                weight += w;
            }

            return mapped + sum / weight;
        }

        private Vector2 Map(Vector2 p)
        {
            return new Vector2((float)(_a * p.x + _b * p.y + _c), (float)(_d * p.x + _e * p.y + _f));
        }

        private void Solve(IReadOnlyList<Vector2> detected, IReadOnlyList<Vector2> known, List<int> used)
        {
            // 3 組以上ならアフィン変換、並びが一直線などで解けなければ相似変換、1 組なら平行移動
            if (used.Count >= 3 && SolveAffine(detected, known, used))
            {
                return;
            }

            SolveSimilarity(detected, known, used);
        }

        private bool SolveAffine(IReadOnlyList<Vector2> detected, IReadOnlyList<Vector2> known, List<int> used)
        {
            // 最小二乗の正規方程式（[x y 1] の 3×3）を x' と y' のそれぞれで解く
            double sxx = 0, sxy = 0, sx = 0, syy = 0, sy = 0, n = used.Count;
            double qx = 0, qxx = 0, qxy = 0, qy = 0, qyx = 0, qyy = 0;
            foreach (int i in used)
            {
                double x = detected[i].x;
                double y = detected[i].y;
                double u = known[i].x;
                double v = known[i].y;
                sxx += x * x;
                sxy += x * y;
                sx += x;
                syy += y * y;
                sy += y;
                qxx += x * u;
                qxy += y * u;
                qx += u;
                qyx += x * v;
                qyy += y * v;
                qy += v;
            }

            double det = Det3(sxx, sxy, sx, sxy, syy, sy, sx, sy, n);
            double scale = sxx + syy + n;
            if (System.Math.Abs(det) <= 1e-12 * scale * scale * scale)
            {
                return false;
            }

            _a = Det3(qxx, sxy, sx, qxy, syy, sy, qx, sy, n) / det;
            _b = Det3(sxx, qxx, sx, sxy, qxy, sy, sx, qx, n) / det;
            _c = Det3(sxx, sxy, qxx, sxy, syy, qxy, sx, sy, qx) / det;
            _d = Det3(qyx, sxy, sx, qyy, syy, sy, qy, sy, n) / det;
            _e = Det3(sxx, qyx, sx, sxy, qyy, sy, sx, qy, n) / det;
            _f = Det3(sxx, sxy, qyx, sxy, syy, qyy, sx, sy, qy) / det;
            return true;
        }

        private void SolveSimilarity(IReadOnlyList<Vector2> detected, IReadOnlyList<Vector2> known, List<int> used)
        {
            // 重心を合わせ、回転と拡大を複素数の比で求める（広がりが無ければ平行移動だけ）
            Vector2 p = Vector2.zero;
            Vector2 q = Vector2.zero;
            foreach (int i in used)
            {
                p += detected[i];
                q += known[i];
            }

            p /= Mathf.Max(used.Count, 1);
            q /= Mathf.Max(used.Count, 1);
            double re = 0, im = 0, norm = 0;
            foreach (int i in used)
            {
                Vector2 dp = detected[i] - p;
                Vector2 dq = known[i] - q;
                re += dq.x * dp.x + dq.y * dp.y;
                im += dq.y * dp.x - dq.x * dp.y;
                norm += dp.x * dp.x + dp.y * dp.y;
            }

            double sa = norm > 1e-12 ? re / norm : 1.0;
            double sb = norm > 1e-12 ? im / norm : 0.0;
            _a = sa;
            _b = -sb;
            _d = sb;
            _e = sa;
            _c = q.x - (sa * p.x - sb * p.y);
            _f = q.y - (sb * p.x + sa * p.y);
        }

        private static float Spread(IReadOnlyList<Vector2> points, List<int> used)
        {
            // 重心からの距離の二乗平均の平方根
            Vector2 center = Vector2.zero;
            foreach (int i in used)
            {
                center += points[i];
            }

            center /= Mathf.Max(used.Count, 1);
            float sum = 0f;
            foreach (int i in used)
            {
                sum += (points[i] - center).sqrMagnitude;
            }

            return Mathf.Sqrt(sum / Mathf.Max(used.Count, 1));
        }

        private static double Det3(double a, double b, double c, double d, double e, double f, double g, double h,
            double i)
        {
            // 行ごとに (a b c)(d e f)(g h i)
            return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        }
    }
}
