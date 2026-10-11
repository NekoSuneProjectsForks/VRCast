using System.Collections.Generic;
using UnityEngine;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 打った目印から求めた顔の座標系（メッシュの座標系の中の、左・上・前の向きと大きさ）と、全種類の目印の位置。
    /// 打っていない目印（少ない段階で打たないもの）は打った目印から推定して補う。
    /// 局所座標は x = アバターの左、y = 上、z = 前（顔の外側）、原点は両目の中心、長さはメッシュの座標系のまま。
    /// </summary>
    public sealed class FaceRig
    {
        // 推定に使う比率（両目の中心の間隔 = 1 としたときの大きさ）
        private const float EyeHalfWidthRatio = 0.2f;
        private const float BrowSpreadRatio = 0.25f;
        private const float NoseForwardRatio = 0.15f;
        private const float JawPivotBackRatio = 0.7f;
        private const float JawPivotRaise = 0.45f;
        private const float JawAngleRaiseRatio = 0.35f;

        private readonly Dictionary<LandmarkId, Vector3> _local = new Dictionary<LandmarkId, Vector3>();

        public Vector3 Origin { get; private set; }
        public Vector3 Left { get; private set; }
        public Vector3 Up { get; private set; }
        public Vector3 Forward { get; private set; }

        /// <summary>
        /// 顔の大きさ（両目の中心の間隔。メッシュの座標系の長さ）。
        /// </summary>
        public float Scale { get; private set; }

        /// <summary>
        /// 顎の回転の中心（局所座標）。
        /// </summary>
        public Vector3 JawPivot { get; private set; }

        private FaceRig()
        {
        }

        /// <summary>
        /// 目印の位置（メッシュの座標系）から作る。最小の段階の目印が揃っていなければ null と、足りない目印を返す。
        /// normals は目印の位置の法線（前の向きの確認用。無ければ空でよい）。
        /// </summary>
        public static FaceRig Build(IReadOnlyDictionary<LandmarkId, Vector3> points,
            IReadOnlyDictionary<LandmarkId, Vector3> normals, out List<LandmarkId> missing)
        {
            // 最小の段階の目印が全部あること
            missing = new List<LandmarkId>();
            foreach (LandmarkId id in FaceLandmarks.Required)
            {
                if (!points.ContainsKey(id))
                {
                    missing.Add(id);
                }
            }

            if (missing.Count > 0)
            {
                return null;
            }

            var rig = new FaceRig();
            rig.BuildAxes(points, normals);

            // 打った目印を局所座標にして持ち、足りないものを推定する
            foreach (KeyValuePair<LandmarkId, Vector3> pair in points)
            {
                rig._local[pair.Key] = rig.ToLocal(pair.Value);
            }

            rig.Complete();
            return rig;
        }

        /// <summary>
        /// 局所座標の目印の位置（Build 後は全種類ある）。
        /// </summary>
        public Vector3 this[LandmarkId id] => _local[id];

        /// <summary>
        /// メッシュの座標系の点を局所座標にする。
        /// </summary>
        public Vector3 ToLocal(Vector3 point)
        {
            Vector3 offset = point - Origin;
            return new Vector3(Vector3.Dot(offset, Left), Vector3.Dot(offset, Up), Vector3.Dot(offset, Forward));
        }

        /// <summary>
        /// 局所座標の向き（移動量）をメッシュの座標系にする。
        /// </summary>
        public Vector3 DirectionToMesh(Vector3 local)
        {
            return Left * local.x + Up * local.y + Forward * local.z;
        }

        /// <summary>
        /// メッシュの座標系の向き（法線など）を局所座標にする。
        /// </summary>
        public Vector3 DirectionToLocal(Vector3 direction)
        {
            return new Vector3(Vector3.Dot(direction, Left), Vector3.Dot(direction, Up), Vector3.Dot(direction, Forward));
        }

        /// <summary>
        /// 左右対称の面（メッシュの座標系の点と法線）。左右の組が無ければメッシュの X = 0 の面とする。
        /// 左右対称で打つとき、反対側の位置を決めるのに使う。
        /// </summary>
        public static void EstimateSymmetryPlane(IReadOnlyDictionary<LandmarkId, Vector3> points,
            out Vector3 planePoint, out Vector3 planeNormal)
        {
            // 両側を打った組の「左 − 右」の平均を法線、中点の平均を面上の点にする
            Vector3 normal = Vector3.zero;
            Vector3 middle = Vector3.zero;
            int pairs = 0;
            foreach (KeyValuePair<LandmarkId, Vector3> pair in points)
            {
                if (!FaceLandmarks.IsLeft(pair.Key) || !FaceLandmarks.TryGetMirror(pair.Key, out LandmarkId right)
                    || !points.TryGetValue(right, out Vector3 rightPoint))
                {
                    continue;
                }

                normal += pair.Value - rightPoint;
                middle += (pair.Value + rightPoint) * 0.5f;
                pairs++;
            }

            // 中央の目印（唇・あご・鼻）があれば面上の点に加える
            int centers = 0;
            Vector3 center = Vector3.zero;
            foreach (LandmarkId id in new[] { LandmarkId.LipUpper, LandmarkId.LipLower, LandmarkId.Chin, LandmarkId.NoseTip })
            {
                if (points.TryGetValue(id, out Vector3 point))
                {
                    center += point;
                    centers++;
                }
            }

            // 組が無ければメッシュの X = 0 の面（多くのアバターは左右対称に作られている）
            planeNormal = pairs > 0 && normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.right;
            if (pairs > 0 || centers > 0)
            {
                planePoint = (middle + center) / (pairs + centers);
            }
            else
            {
                planePoint = Vector3.zero;
            }
        }

        private void BuildAxes(IReadOnlyDictionary<LandmarkId, Vector3> points,
            IReadOnlyDictionary<LandmarkId, Vector3> normals)
        {
            // 左: 左右の組の「左 − 右」の平均
            Vector3 left = Vector3.zero;
            foreach (KeyValuePair<LandmarkId, Vector3> pair in points)
            {
                if (FaceLandmarks.IsLeft(pair.Key) && FaceLandmarks.TryGetMirror(pair.Key, out LandmarkId right)
                    && points.TryGetValue(right, out Vector3 rightPoint))
                {
                    left += pair.Value - rightPoint;
                }
            }

            Left = left.sqrMagnitude > 1e-12f ? left.normalized : Vector3.left;

            // 原点は両目の中心、大きさは両目の中心の間隔
            Vector3 eyeLeft = (points[LandmarkId.EyeUpperLeft] + points[LandmarkId.EyeLowerLeft]) * 0.5f;
            Vector3 eyeRight = (points[LandmarkId.EyeUpperRight] + points[LandmarkId.EyeLowerRight]) * 0.5f;
            Origin = (eyeLeft + eyeRight) * 0.5f;
            Scale = Mathf.Max(Vector3.Distance(eyeLeft, eyeRight), 1e-6f);

            // 上: あごから眉の中心へ向かう向きを、左と直交させる
            Vector3 brows = (points[LandmarkId.BrowMidLeft] + points[LandmarkId.BrowMidRight]) * 0.5f;
            Vector3 up = brows - points[LandmarkId.Chin];
            up -= Left * Vector3.Dot(up, Left);
            Up = up.sqrMagnitude > 1e-12f ? up.normalized : Vector3.up;

            // 前: 上と左から決め、顔の表面の法線（あれば）・鼻の位置と逆なら反転する（鏡像のメッシュに備える）
            Forward = Vector3.Cross(Up, Left).normalized;
            float facing = 0f;
            foreach (KeyValuePair<LandmarkId, Vector3> normal in normals)
            {
                facing += Vector3.Dot(normal.Value, Forward);
            }

            if (points.TryGetValue(LandmarkId.NoseTip, out Vector3 nose))
            {
                facing += Mathf.Sign(Vector3.Dot(nose - Origin, Forward)) * Mathf.Max(1f, normals.Count);
            }

            if (facing < 0f)
            {
                Forward = -Forward;
            }
        }

        private void Complete()
        {
            float s = Scale;

            // 目: 目頭・目尻と、まぶたの輪郭の途中の点
            CompleteEye(LandmarkId.EyeUpperLeft, LandmarkId.EyeLowerLeft, LandmarkId.EyeInnerLeft, LandmarkId.EyeOuterLeft,
                LandmarkId.EyeUpperInnerLeft, LandmarkId.EyeUpperOuterLeft, LandmarkId.EyeLowerInnerLeft,
                LandmarkId.EyeLowerOuterLeft, 1f);
            CompleteEye(LandmarkId.EyeUpperRight, LandmarkId.EyeLowerRight, LandmarkId.EyeInnerRight,
                LandmarkId.EyeOuterRight, LandmarkId.EyeUpperInnerRight, LandmarkId.EyeUpperOuterRight,
                LandmarkId.EyeLowerInnerRight, LandmarkId.EyeLowerOuterRight, -1f);

            // 眉頭・眉尻（眉の中央から内側・外側へ）
            CompleteBrow(LandmarkId.BrowMidLeft, LandmarkId.BrowInnerLeft, LandmarkId.BrowOuterLeft, 1f);
            CompleteBrow(LandmarkId.BrowMidRight, LandmarkId.BrowInnerRight, LandmarkId.BrowOuterRight, -1f);

            // 鼻先: 目と口の間の高さで、顔の表面より少し前
            Vector3 mouth = (this[LandmarkId.LipUpper] + this[LandmarkId.LipLower]) * 0.5f;
            Vector3 eyes = (Eye(1f) + Eye(-1f)) * 0.5f;
            AddIfMissing(LandmarkId.NoseTip, new Vector3(0f, Mathf.Lerp(mouth.y, eyes.y, 0.45f),
                Mathf.Max(mouth.z, eyes.z) + s * NoseForwardRatio));
            Vector3 noseTip = this[LandmarkId.NoseTip];

            // 小鼻: 鼻先の少し横・下・奥
            AddIfMissing(LandmarkId.NoseWingLeft, noseTip + new Vector3(s * 0.15f, -s * 0.03f, -s * 0.06f));
            AddIfMissing(LandmarkId.NoseWingRight, noseTip + new Vector3(-s * 0.15f, -s * 0.03f, -s * 0.06f));

            // 頬: 目の下と口角の間
            CompleteCheek(LandmarkId.CheekLeft, LandmarkId.EyeLowerLeft, LandmarkId.MouthCornerLeft, 1f);
            CompleteCheek(LandmarkId.CheekRight, LandmarkId.EyeLowerRight, LandmarkId.MouthCornerRight, -1f);

            // 唇の途中の点: 中央と口角の間（上唇は少し上、下唇は少し下へ膨らませる）
            CompleteLip(LandmarkId.LipUpperLeft, LandmarkId.LipUpper, LandmarkId.MouthCornerLeft, 0.25f);
            CompleteLip(LandmarkId.LipUpperRight, LandmarkId.LipUpper, LandmarkId.MouthCornerRight, 0.25f);
            CompleteLip(LandmarkId.LipLowerLeft, LandmarkId.LipLower, LandmarkId.MouthCornerLeft, 0.25f);
            CompleteLip(LandmarkId.LipLowerRight, LandmarkId.LipLower, LandmarkId.MouthCornerRight, 0.25f);

            // エラ: 口の高さで、目より外側・奥
            AddIfMissing(LandmarkId.JawAngleLeft, new Vector3(s * 0.9f, mouth.y, eyes.z - s * 0.6f));
            AddIfMissing(LandmarkId.JawAngleRight, new Vector3(-s * 0.9f, mouth.y, eyes.z - s * 0.6f));

            // 顎の回転の中心: エラの中点の少し上（耳の前あたり）。エラを打っていなければ口と目の間の高さの奥
            Vector3 jawAngles = (this[LandmarkId.JawAngleLeft] + this[LandmarkId.JawAngleRight]) * 0.5f;
            JawPivot = new Vector3(0f, Mathf.Max(jawAngles.y + s * JawAngleRaiseRatio,
                    Mathf.Lerp(mouth.y, eyes.y, JawPivotRaise)),
                Mathf.Min(jawAngles.z, eyes.z - s * JawPivotBackRatio));
        }

        private void CompleteEye(LandmarkId upper, LandmarkId lower, LandmarkId inner, LandmarkId outer,
            LandmarkId upperInner, LandmarkId upperOuter, LandmarkId lowerInner, LandmarkId lowerOuter, float side)
        {
            // 目の中心と高さ。幅はアニメ調の大きな目に合わせ、高さか両目の間隔から決める
            Vector3 top = this[upper];
            Vector3 bottom = this[lower];
            Vector3 center = (top + bottom) * 0.5f;
            float halfHeight = Mathf.Abs(top.y - bottom.y) * 0.5f;
            float halfWidth = Mathf.Max(Scale * EyeHalfWidthRatio, halfHeight * 1.1f);

            // 目頭は中央側（side の逆）、目尻は外側で少し上
            AddIfMissing(inner, center + new Vector3(-side * halfWidth, -halfHeight * 0.1f, 0f));
            AddIfMissing(outer, center + new Vector3(side * halfWidth, halfHeight * 0.15f, -halfWidth * 0.2f));

            // まぶたの輪郭の途中の点は、目頭・目尻と上下の点を通る楕円の上に置く
            Vector3 innerPoint = this[inner];
            Vector3 outerPoint = this[outer];
            AddIfMissing(upperInner, OnLid(innerPoint, top, 0.5f));
            AddIfMissing(upperOuter, OnLid(outerPoint, top, 0.5f));
            AddIfMissing(lowerInner, OnLid(innerPoint, bottom, 0.5f));
            AddIfMissing(lowerOuter, OnLid(outerPoint, bottom, 0.5f));
        }

        private static Vector3 OnLid(Vector3 corner, Vector3 lid, float t)
        {
            // 角から上下の点へ向かう途中の点（楕円に近づけるため、高さは横より早く上げる）
            float x = Mathf.Lerp(corner.x, lid.x, t);
            float y = Mathf.Lerp(corner.y, lid.y, Mathf.Sqrt(1f - (1f - t) * (1f - t)));
            float z = Mathf.Lerp(corner.z, lid.z, t);
            return new Vector3(x, y, z);
        }

        private void CompleteBrow(LandmarkId mid, LandmarkId inner, LandmarkId outer, float side)
        {
            // 眉の中央から、内側は少し下、外側はさらに下・奥へ
            Vector3 center = this[mid];
            float spread = Scale * BrowSpreadRatio;
            AddIfMissing(inner, center + new Vector3(-side * spread, -spread * 0.08f, spread * 0.05f));
            AddIfMissing(outer, center + new Vector3(side * spread, -spread * 0.15f, -spread * 0.25f));
        }

        private void CompleteCheek(LandmarkId cheek, LandmarkId eyeLower, LandmarkId corner, float side)
        {
            // 目の下と口角の間で、少し外側
            Vector3 eye = this[eyeLower];
            Vector3 mouth = this[corner];
            AddIfMissing(cheek, new Vector3(eye.x + side * Scale * 0.05f, Mathf.Lerp(mouth.y, eye.y, 0.5f),
                Mathf.Lerp(mouth.z, eye.z, 0.5f)));
        }

        private void CompleteLip(LandmarkId target, LandmarkId middle, LandmarkId corner, float bulge)
        {
            // 中央と口角の中点から、中央の高さの方へ寄せる（唇の弧）
            Vector3 center = this[middle];
            Vector3 end = this[corner];
            Vector3 point = (center + end) * 0.5f;
            point.y = Mathf.Lerp(point.y, center.y, 0.5f + bulge);
            AddIfMissing(target, point);
        }

        private Vector3 Eye(float side)
        {
            // 左目（side = 1）・右目の中心
            return side > 0f
                ? (this[LandmarkId.EyeUpperLeft] + this[LandmarkId.EyeLowerLeft]) * 0.5f
                : (this[LandmarkId.EyeUpperRight] + this[LandmarkId.EyeLowerRight]) * 0.5f;
        }

        private void AddIfMissing(LandmarkId id, Vector3 local)
        {
            // 打った目印は推定で上書きしない
            if (!_local.ContainsKey(id))
            {
                _local[id] = local;
            }
        }
    }
}
