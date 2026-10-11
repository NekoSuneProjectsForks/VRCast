using UnityEngine;
using static VRCast.PerfectSync.FaceShapeMath;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 片目の領域。目頭 → 目尻の向きを u、それと直交する上向きを v とし、
    /// 上まぶた・下まぶたの輪郭を u（目の幅の半分を 1 とする）に対する v の折れ線で持つ。
    /// </summary>
    public sealed class EyeRegion
    {
        // 閉じた目の線の位置（下まぶた 0 〜 上まぶた 1 のうち。アニメ調の目は下寄りで閉じる）
        private const float CloseRatio = 0.3f;

        private readonly Vector2 _middle;
        private readonly Polyline _upper;
        private readonly Polyline _lower;

        /// <summary>
        /// 目頭 → 目尻の向き（局所座標の x, y）。
        /// </summary>
        public Vector2 U { get; }

        /// <summary>
        /// U と直交する上向き。
        /// </summary>
        public Vector2 V { get; }

        public float HalfWidth { get; }
        public float Height { get; }
        public float Z { get; }

        public EyeRegion(FaceRig rig, LandmarkId inner, LandmarkId upperInner, LandmarkId upper, LandmarkId upperOuter,
            LandmarkId outer, LandmarkId lowerInner, LandmarkId lower, LandmarkId lowerOuter)
        {
            // 目頭と目尻から軸と幅を決める
            Vector3 innerPoint = rig[inner];
            Vector3 outerPoint = rig[outer];
            Vector2 axis = new Vector2(outerPoint.x - innerPoint.x, outerPoint.y - innerPoint.y);
            U = axis.sqrMagnitude > 1e-12f ? axis.normalized : Vector2.right;
            Vector2 perpendicular = new Vector2(-U.y, U.x);
            V = perpendicular.y >= 0f ? perpendicular : -perpendicular;
            _middle = new Vector2(innerPoint.x + outerPoint.x, innerPoint.y + outerPoint.y) * 0.5f;
            HalfWidth = Mathf.Max(axis.magnitude * 0.5f, rig.Scale * 0.05f);
            Z = (rig[upper].z + rig[lower].z) * 0.5f;

            // 輪郭の折れ線（目頭・途中・中央・途中・目尻）
            _upper = Contour(rig, inner, upperInner, upper, upperOuter, outer);
            _lower = Contour(rig, inner, lowerInner, lower, lowerOuter, outer);
            Height = Mathf.Max(Upper(0f) - Lower(0f), rig.Scale * 0.02f);
        }

        /// <summary>
        /// 点の目の座標（t = 目頭 -1 〜 目尻 1、v = 上向きの距離）。
        /// </summary>
        public void Coordinates(Vector3 point, out float t, out float v)
        {
            var offset = new Vector2(point.x - _middle.x, point.y - _middle.y);
            t = Vector2.Dot(offset, U) / HalfWidth;
            v = Vector2.Dot(offset, V);
        }

        /// <summary>
        /// 上まぶたの輪郭（下まぶたより必ず上）。
        /// </summary>
        public float Upper(float t)
        {
            return Mathf.Max(_upper.Evaluate(t), _lower.Evaluate(t) + 1e-6f);
        }

        /// <summary>
        /// 下まぶたの輪郭。
        /// </summary>
        public float Lower(float t)
        {
            return _lower.Evaluate(t);
        }

        /// <summary>
        /// 目を閉じたときの線。
        /// </summary>
        public float Close(float t)
        {
            return Mathf.Lerp(Lower(t), Upper(t), CloseRatio);
        }

        /// <summary>
        /// 目の横の範囲の重み（目の幅の中で 1、目頭・目尻の外で減衰）と奥行きの重み。
        /// </summary>
        public float Mask(Vector3 point, float t, float scale)
        {
            return Falloff(Mathf.Max(0f, Mathf.Abs(t) - 1f), 0.45f) * FrontMask(point.z, Z, scale * 0.5f);
        }

        /// <summary>
        /// 目の座標の移動量（du = U 向き、dv = V 向き、dz = 前）を局所座標の移動量にする。
        /// </summary>
        public Vector3 ToLocal(float du, float dv, float dz)
        {
            return new Vector3(U.x * du + V.x * dv, U.y * du + V.y * dv, dz);
        }

        private Polyline Contour(FaceRig rig, params LandmarkId[] ids)
        {
            // 各点の目の座標を鍵（t）と値（v）にする
            var keys = new float[ids.Length];
            var values = new float[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                Coordinates(rig[ids[i]], out keys[i], out values[i]);
            }

            return new Polyline(keys, values);
        }
    }

    /// <summary>
    /// 片方の眉の領域（眉頭・中央・眉尻の折れ線のまわり）。
    /// </summary>
    public sealed class BrowRegion
    {
        private readonly Vector2[] _points;
        private readonly float[] _lengths;
        private readonly float _total;
        private readonly float _radius;
        private readonly float _z;
        private readonly float _eyeTop;
        private readonly float _scale;

        public BrowRegion(FaceRig rig, LandmarkId inner, LandmarkId mid, LandmarkId outer, LandmarkId eyeUpper)
        {
            // 眉頭 → 眉尻の折れ線と、各区間の長さ
            _points = new[] { Flat(rig[inner]), Flat(rig[mid]), Flat(rig[outer]) };
            _lengths = new[] { Vector2.Distance(_points[0], _points[1]), Vector2.Distance(_points[1], _points[2]) };
            _total = Mathf.Max(_lengths[0] + _lengths[1], 1e-9f);
            _scale = rig.Scale;
            _radius = rig.Scale * 0.22f;
            _z = rig[mid].z;
            _eyeTop = rig[eyeUpper].y;
        }

        /// <summary>
        /// 点の重み（眉の線の近くで 1）と、眉頭 0 〜 眉尻 1 の位置。目は動かさないよう、上まぶたより下は 0。
        /// </summary>
        public float Weight(Vector3 point, out float along)
        {
            // 折れ線までの最短距離と、そこまでの道のり
            Vector2 p = Flat(point);
            float best = float.MaxValue;
            along = 0f;
            float walked = 0f;
            for (int i = 0; i < 2; i++)
            {
                Vector2 a = _points[i];
                Vector2 b = _points[i + 1];
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                float distance = Vector2.Distance(p, a + ab * t);
                if (distance < best)
                {
                    best = distance;
                    along = (walked + _lengths[i] * t) / _total;
                }

                walked += _lengths[i];
            }

            // 眉の近さ・奥行き・目より上かを掛け合わせる
            float aboveEye = Smooth01((point.y - _eyeTop + _scale * 0.02f) / (_scale * 0.12f));
            return Falloff(best, _radius) * FrontMask(point.z, _z, _scale * 0.45f) * aboveEye;
        }

        private static Vector2 Flat(Vector3 point)
        {
            return new Vector2(point.x, point.y);
        }
    }

    /// <summary>
    /// 口の領域（口角・唇の折れ線・口の線・顎）。
    /// </summary>
    public sealed class MouthRegion
    {
        // 顎を開いたときの下唇の下がり幅（両目の間隔に対する比）
        public const float JawDropRatio = 0.3f;

        private readonly Polyline _line;
        private readonly float _scale;
        private readonly float _jawHalfWidth;
        private readonly float _chinY;
        private readonly Vector3 _pivot;
        private readonly float _jawAngle;

        public Vector3 Center { get; }
        public Vector3 CornerLeft { get; }
        public Vector3 CornerRight { get; }
        public Vector3 Upper { get; }
        public Vector3 Lower { get; }
        public float HalfWidth { get; }
        public float Height { get; }
        public float NoseY { get; }

        public MouthRegion(FaceRig rig)
        {
            _scale = rig.Scale;
            CornerLeft = rig[LandmarkId.MouthCornerLeft];
            CornerRight = rig[LandmarkId.MouthCornerRight];
            Upper = rig[LandmarkId.LipUpper];
            Lower = rig[LandmarkId.LipLower];

            // 中心・幅・高さ（閉じた口でも 0 にしない）
            Center = new Vector3((CornerLeft.x + CornerRight.x) * 0.5f, (Upper.y + Lower.y) * 0.5f,
                Mathf.Max(Upper.z, Lower.z));
            HalfWidth = Mathf.Max(Mathf.Abs(CornerLeft.x - CornerRight.x) * 0.5f, _scale * 0.05f);
            Height = Mathf.Max(Upper.y - Lower.y, _scale * 0.02f);
            NoseY = rig[LandmarkId.NoseTip].y;
            _chinY = rig[LandmarkId.Chin].y;

            // 唇が合わさる線（右の口角・中央・左の口角）
            _line = new Polyline(new[] { CornerRight.x, Center.x, CornerLeft.x },
                new[] { CornerRight.y, Center.y, CornerLeft.y });

            // 顎の幅（エラの間隔の半分）と、回転の中心・下唇が JawDropRatio だけ下がる回転角
            _jawHalfWidth = Mathf.Max(Mathf.Abs(rig[LandmarkId.JawAngleLeft].x - rig[LandmarkId.JawAngleRight].x) * 0.5f,
                HalfWidth);
            _pivot = rig.JawPivot;
            float lever = Mathf.Max(new Vector2(Lower.y - _pivot.y, Lower.z - _pivot.z).magnitude, _scale * 0.1f);
            _jawAngle = _scale * JawDropRatio / lever;
        }

        /// <summary>
        /// 唇が合わさる線より上なら 1、下なら 0（口角より外は境目を広げて頬をなめらかにつなぐ）。
        /// </summary>
        public float UpperSide(Vector3 point)
        {
            float outside = Mathf.Max(0f, Mathf.Abs(point.x - Center.x) - HalfWidth);
            float band = Mathf.Max(Height * 0.3f, _scale * 0.03f) + outside * 0.8f;
            return Smooth01((point.y - _line.Evaluate(point.x)) / band + 0.5f);
        }

        /// <summary>
        /// 唇のまわりの重み（rx は口の幅の半分に対する倍率、上下は鼻・あごまでの割合）。
        /// </summary>
        public float Lips(Vector3 point, float rx, float reach)
        {
            float up = Mathf.Max((NoseY - Center.y) * reach, Height);
            float down = Mathf.Max((Center.y - _chinY) * reach, Height);
            return Ellipse(point, Center, HalfWidth * rx, point.y >= Center.y ? up : down)
                * FrontMask(point.z, Center.z, _scale * 0.4f);
        }

        /// <summary>
        /// 口角のまわりの重み（side = 1 で左、-1 で右）。
        /// </summary>
        public float Corner(Vector3 point, float side, float radius)
        {
            Vector3 corner = side > 0f ? CornerLeft : CornerRight;
            return Ellipse(point, corner, HalfWidth * radius, HalfWidth * radius)
                * FrontMask(point.z, corner.z, _scale * 0.4f);
        }

        /// <summary>
        /// 口の片側の重み（side = 1 で左側ほど 1）。
        /// </summary>
        public float Side(Vector3 point, float side)
        {
            return Smooth01(side * (point.x - Center.x) / HalfWidth * 1.5f + 0.5f);
        }

        /// <summary>
        /// 顎と一緒に動く範囲の重み（唇の線より下・エラの内側・あごより上・回転の中心より前）。
        /// </summary>
        public float Jaw(Vector3 point)
        {
            float below = 1f - UpperSide(point);
            float inside = Falloff(Mathf.Max(0f, Mathf.Abs(point.x - Center.x) - _jawHalfWidth), _scale * 0.35f);
            float aboveNeck = Smooth01((point.y - (_chinY - _scale * 0.4f)) / (_scale * 0.35f));
            float front = Smooth01((point.z - (_pivot.z - _scale * 0.05f)) / (_scale * 0.3f));
            return below * inside * aboveNeck * front;
        }

        /// <summary>
        /// 顎を開いたとき（回転の中心まわりに回す）の移動量。
        /// </summary>
        public Vector3 JawOpen(Vector3 point)
        {
            // 左右の軸まわりに回すと、前にある点ほど下がる
            float y = point.y - _pivot.y;
            float z = point.z - _pivot.z;
            float cos = Mathf.Cos(_jawAngle);
            float sin = Mathf.Sin(_jawAngle);
            return new Vector3(0f, y * cos - z * sin - y, y * sin + z * cos - z);
        }
    }
}
