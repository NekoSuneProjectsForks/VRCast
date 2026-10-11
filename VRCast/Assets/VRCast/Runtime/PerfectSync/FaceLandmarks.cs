using System.Collections.Generic;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// パーフェクトシンク化で顔に打つ目印の種類。左右はアバターから見た左右（正面から見ると画面の右がアバターの左）。
    /// 番号はファイルに保存するため変えない（最小 0〜、標準 20〜、詳細 40〜）。
    /// </summary>
    public enum LandmarkId
    {
        // 最小
        EyeUpperLeft = 0,
        EyeUpperRight = 1,
        EyeLowerLeft = 2,
        EyeLowerRight = 3,
        BrowMidLeft = 4,
        BrowMidRight = 5,
        MouthCornerLeft = 6,
        MouthCornerRight = 7,
        LipUpper = 8,
        LipLower = 9,
        Chin = 10,

        // 標準
        EyeInnerLeft = 20,
        EyeInnerRight = 21,
        EyeOuterLeft = 22,
        EyeOuterRight = 23,
        BrowInnerLeft = 24,
        BrowInnerRight = 25,
        BrowOuterLeft = 26,
        BrowOuterRight = 27,
        NoseTip = 28,
        CheekLeft = 29,
        CheekRight = 30,

        // 詳細
        JawAngleLeft = 40,
        JawAngleRight = 41,
        NoseWingLeft = 42,
        NoseWingRight = 43,
        LipUpperLeft = 44,
        LipUpperRight = 45,
        LipLowerLeft = 46,
        LipLowerRight = 47,
        EyeUpperInnerLeft = 48,
        EyeUpperInnerRight = 49,
        EyeUpperOuterLeft = 50,
        EyeUpperOuterRight = 51,
        EyeLowerInnerLeft = 52,
        EyeLowerInnerRight = 53,
        EyeLowerOuterLeft = 54,
        EyeLowerOuterRight = 55,
    }

    /// <summary>
    /// 目印の細かさ（ユーザーが選ぶ 3 段階）。
    /// </summary>
    public enum LandmarkDetail
    {
        Minimal = 0,
        Standard = 1,
        Detailed = 2,
    }

    /// <summary>
    /// 目印の並び・細かさ・左右の対応。
    /// </summary>
    public static class FaceLandmarks
    {
        // 打つ順（案内の順。左右の組は左を先に）と、それぞれが必要になる細かさ
        private static readonly (LandmarkId Id, LandmarkDetail Detail)[] Order =
        {
            (LandmarkId.EyeUpperLeft, LandmarkDetail.Minimal),
            (LandmarkId.EyeUpperRight, LandmarkDetail.Minimal),
            (LandmarkId.EyeLowerLeft, LandmarkDetail.Minimal),
            (LandmarkId.EyeLowerRight, LandmarkDetail.Minimal),
            (LandmarkId.EyeInnerLeft, LandmarkDetail.Standard),
            (LandmarkId.EyeInnerRight, LandmarkDetail.Standard),
            (LandmarkId.EyeOuterLeft, LandmarkDetail.Standard),
            (LandmarkId.EyeOuterRight, LandmarkDetail.Standard),
            (LandmarkId.EyeUpperInnerLeft, LandmarkDetail.Detailed),
            (LandmarkId.EyeUpperInnerRight, LandmarkDetail.Detailed),
            (LandmarkId.EyeUpperOuterLeft, LandmarkDetail.Detailed),
            (LandmarkId.EyeUpperOuterRight, LandmarkDetail.Detailed),
            (LandmarkId.EyeLowerInnerLeft, LandmarkDetail.Detailed),
            (LandmarkId.EyeLowerInnerRight, LandmarkDetail.Detailed),
            (LandmarkId.EyeLowerOuterLeft, LandmarkDetail.Detailed),
            (LandmarkId.EyeLowerOuterRight, LandmarkDetail.Detailed),
            (LandmarkId.BrowMidLeft, LandmarkDetail.Minimal),
            (LandmarkId.BrowMidRight, LandmarkDetail.Minimal),
            (LandmarkId.BrowInnerLeft, LandmarkDetail.Standard),
            (LandmarkId.BrowInnerRight, LandmarkDetail.Standard),
            (LandmarkId.BrowOuterLeft, LandmarkDetail.Standard),
            (LandmarkId.BrowOuterRight, LandmarkDetail.Standard),
            (LandmarkId.NoseTip, LandmarkDetail.Standard),
            (LandmarkId.NoseWingLeft, LandmarkDetail.Detailed),
            (LandmarkId.NoseWingRight, LandmarkDetail.Detailed),
            (LandmarkId.CheekLeft, LandmarkDetail.Standard),
            (LandmarkId.CheekRight, LandmarkDetail.Standard),
            (LandmarkId.MouthCornerLeft, LandmarkDetail.Minimal),
            (LandmarkId.MouthCornerRight, LandmarkDetail.Minimal),
            (LandmarkId.LipUpper, LandmarkDetail.Minimal),
            (LandmarkId.LipUpperLeft, LandmarkDetail.Detailed),
            (LandmarkId.LipUpperRight, LandmarkDetail.Detailed),
            (LandmarkId.LipLower, LandmarkDetail.Minimal),
            (LandmarkId.LipLowerLeft, LandmarkDetail.Detailed),
            (LandmarkId.LipLowerRight, LandmarkDetail.Detailed),
            (LandmarkId.Chin, LandmarkDetail.Minimal),
            (LandmarkId.JawAngleLeft, LandmarkDetail.Detailed),
            (LandmarkId.JawAngleRight, LandmarkDetail.Detailed),
        };

        // 左右の組（左 → 右）
        private static readonly Dictionary<LandmarkId, LandmarkId> Mirrors = BuildMirrors();

        /// <summary>
        /// detail の細かさで打つ目印（案内の順）。
        /// </summary>
        public static List<LandmarkId> For(LandmarkDetail detail)
        {
            // 指定の細かさ以下で必要になるものを順に集める
            var list = new List<LandmarkId>();
            foreach ((LandmarkId id, LandmarkDetail need) in Order)
            {
                if (need <= detail)
                {
                    list.Add(id);
                }
            }

            return list;
        }

        /// <summary>
        /// 生成に最低限必要な目印（最小の段階のもの）。
        /// </summary>
        public static List<LandmarkId> Required => For(LandmarkDetail.Minimal);

        /// <summary>
        /// 番号が目印として定義されていれば true（ファイルから読んだ値の確認用）。
        /// </summary>
        public static bool IsDefined(int id)
        {
            foreach ((LandmarkId known, LandmarkDetail _) in Order)
            {
                if ((int)known == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 左右の相手（左右の無い目印は false）。
        /// </summary>
        public static bool TryGetMirror(LandmarkId id, out LandmarkId mirror)
        {
            return Mirrors.TryGetValue(id, out mirror);
        }

        /// <summary>
        /// アバターの右側の目印なら true（左右対称で打つときは左から自動で決める側）。
        /// </summary>
        public static bool IsRight(LandmarkId id)
        {
            return id.ToString().EndsWith("Right", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// アバターの左側の目印なら true。
        /// </summary>
        public static bool IsLeft(LandmarkId id)
        {
            return id.ToString().EndsWith("Left", System.StringComparison.Ordinal);
        }

        private static Dictionary<LandmarkId, LandmarkId> BuildMirrors()
        {
            // 名前の Left と Right を入れ替えた相手を、両方向に登録する
            var table = new Dictionary<LandmarkId, LandmarkId>();
            foreach ((LandmarkId id, LandmarkDetail _) in Order)
            {
                string name = id.ToString();
                if (!name.EndsWith("Left", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var right = (LandmarkId)System.Enum.Parse(typeof(LandmarkId), name.Substring(0, name.Length - 4) + "Right");
                table[id] = right;
                table[right] = id;
            }

            return table;
        }
    }
}
