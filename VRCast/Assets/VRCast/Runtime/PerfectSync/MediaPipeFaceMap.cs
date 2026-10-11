using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// MediaPipe Face Landmarker の 478 点の番号と目印の対応（画像認識で目印を自動で置くときに使う）。
    /// MediaPipe の左右は本人（映っている顔）から見た左右で、目印の左右（アバターから見た左右）と同じ。
    /// 2 つの番号を持つ目印は、その中点（眉の上下の縁の間・唇の外側と内側の縁の間など）を使う。
    /// </summary>
    public static class MediaPipeFaceMap
    {
        /// <summary>
        /// MediaPipe の点の数（虹彩を含む）。
        /// </summary>
        public const int PointCount = 478;

        /// <summary>
        /// トラッカーの --face-image の結果の行の先頭（vrcast_tracker.py の FACE_POINTS_PREFIX と一致させる）。
        /// </summary>
        public const string OutputPrefix = "FACE_POINTS: ";

        /// <summary>
        /// 結果の行の JSON（JsonUtility で読む）。
        /// </summary>
        [Serializable]
        private sealed class Output
        {
            public bool face;
            public float[] points;
        }

        /// <summary>
        /// トラッカーの出力行から結果を読む。結果の行が無い・壊れていれば false。
        /// face = 顔が見つかったか、points = x, y の繰り返し（見つからなければ空）。
        /// </summary>
        public static bool TryParse(IEnumerable<string> lines, out bool face, out float[] points)
        {
            face = false;
            points = Array.Empty<float>();
            foreach (string line in lines)
            {
                // 結果の行だけを読む（INFO などの状態ログは飛ばす）
                if (line == null || !line.StartsWith(OutputPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    Output output = JsonUtility.FromJson<Output>(line.Substring(OutputPrefix.Length));
                    if (output == null)
                    {
                        return false;
                    }

                    // 顔ありなら点の数がそろっていること
                    points = output.points ?? Array.Empty<float>();
                    face = output.face && points.Length >= PointCount * 2;
                    return true;
                }
                catch (ArgumentException)
                {
                    // JSON として読めない
                    return false;
                }
            }

            return false;
        }

        // 目印 → MediaPipe の点の番号 2 つ（同じなら 1 点）
        private static readonly (LandmarkId Id, int A, int B)[] Table =
        {
            // 目（上下の縁の中央・目頭・目尻・縁の途中）
            (LandmarkId.EyeUpperLeft, 386, 386),
            (LandmarkId.EyeLowerLeft, 374, 374),
            (LandmarkId.EyeInnerLeft, 362, 362),
            (LandmarkId.EyeOuterLeft, 263, 263),
            (LandmarkId.EyeUpperInnerLeft, 384, 384),
            (LandmarkId.EyeUpperOuterLeft, 388, 388),
            (LandmarkId.EyeLowerInnerLeft, 381, 381),
            (LandmarkId.EyeLowerOuterLeft, 390, 390),
            (LandmarkId.EyeUpperRight, 159, 159),
            (LandmarkId.EyeLowerRight, 145, 145),
            (LandmarkId.EyeInnerRight, 133, 133),
            (LandmarkId.EyeOuterRight, 33, 33),
            (LandmarkId.EyeUpperInnerRight, 157, 157),
            (LandmarkId.EyeUpperOuterRight, 161, 161),
            (LandmarkId.EyeLowerInnerRight, 154, 154),
            (LandmarkId.EyeLowerOuterRight, 163, 163),

            // 眉（上の縁と下の縁の中点）
            (LandmarkId.BrowInnerLeft, 336, 285),
            (LandmarkId.BrowMidLeft, 334, 282),
            (LandmarkId.BrowOuterLeft, 300, 276),
            (LandmarkId.BrowInnerRight, 107, 55),
            (LandmarkId.BrowMidRight, 105, 52),
            (LandmarkId.BrowOuterRight, 70, 46),

            // 鼻・頬・エラ・あご
            (LandmarkId.NoseTip, 1, 1),
            (LandmarkId.NoseWingLeft, 327, 327),
            (LandmarkId.NoseWingRight, 98, 98),
            (LandmarkId.CheekLeft, 280, 280),
            (LandmarkId.CheekRight, 50, 50),
            (LandmarkId.JawAngleLeft, 397, 397),
            (LandmarkId.JawAngleRight, 172, 172),
            (LandmarkId.Chin, 152, 152),

            // 口（口角と、唇の外側の縁と内側の縁の中点）
            (LandmarkId.MouthCornerLeft, 291, 291),
            (LandmarkId.MouthCornerRight, 61, 61),
            (LandmarkId.LipUpper, 0, 13),
            (LandmarkId.LipLower, 17, 14),
            (LandmarkId.LipUpperLeft, 269, 269),
            (LandmarkId.LipUpperRight, 39, 39),
            (LandmarkId.LipLowerLeft, 405, 405),
            (LandmarkId.LipLowerRight, 181, 181),
        };

        /// <summary>
        /// points（x, y の繰り返し。画像に対する 0〜1、左上原点）から目印の画像上の位置を取り出す。
        /// 対応が無い・点が足りなければ false。
        /// </summary>
        public static bool TryGetPoint(float[] points, LandmarkId id, out Vector2 point)
        {
            point = default;
            if (points == null || points.Length < PointCount * 2)
            {
                return false;
            }

            // 対応表から 2 点の中点を求める
            foreach ((LandmarkId known, int a, int b) in Table)
            {
                if (known == id)
                {
                    point = (new Vector2(points[a * 2], points[a * 2 + 1]) + new Vector2(points[b * 2], points[b * 2 + 1]))
                        * 0.5f;
                    return true;
                }
            }

            return false;
        }
    }
}
