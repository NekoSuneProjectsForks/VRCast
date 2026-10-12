using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// パーフェクトシンクの口・頬の強調: 検出が弱く 1 まで届きにくい ARKit の値を倍率で強める。
    /// ふくれっ面（mouthPucker・mouthFunnel）・頬ふくらませ（cheekPuff）・唇（巻き込み・押し付け・すぼめ上げ）の 3 組。
    /// 既定値（default）は全部の倍率が 0 で、1 未満の倍率は強調しない扱いなので、何も変えない。
    /// </summary>
    public readonly struct PerfectSyncBoost
    {
        // 組ごとの ARKit 名の位置（BlendShapeNames 内）
        private static readonly int MouthPucker = ArKitFace.IndexOf("mouthPucker");
        private static readonly int MouthFunnel = ArKitFace.IndexOf("mouthFunnel");
        private static readonly int CheekPuff = ArKitFace.IndexOf("cheekPuff");
        private static readonly int[] LipShapes =
        {
            ArKitFace.IndexOf("mouthRollLower"), ArKitFace.IndexOf("mouthRollUpper"),
            ArKitFace.IndexOf("mouthShrugLower"), ArKitFace.IndexOf("mouthShrugUpper"),
            ArKitFace.IndexOf("mouthPressLeft"), ArKitFace.IndexOf("mouthPressRight"),
        };

        // 組ごとの倍率（1 = そのまま）
        private readonly float _pucker;
        private readonly float _cheekPuff;
        private readonly float _lips;

        public PerfectSyncBoost(float pucker, float cheekPuff, float lips)
        {
            _pucker = pucker;
            _cheekPuff = cheekPuff;
            _lips = lips;
        }

        /// <summary>
        /// ARKit 名の位置 index の値 value（0〜1）を倍率で強め、0〜1 に収めて返す（対象外の名前はそのまま）。
        /// </summary>
        public float Apply(int index, float value)
        {
            // 名前の組に応じた倍率（1 未満は 1 とみなし、弱めない）
            float gain = Mathf.Max(1f, GainFor(index));
            return Mathf.Clamp01(value * gain);
        }

        private float GainFor(int index)
        {
            // ふくれっ面・頬ふくらませ・唇の順に判定し、どれでもなければ強調しない
            if (index == MouthPucker || index == MouthFunnel)
            {
                return _pucker;
            }

            if (index == CheekPuff)
            {
                return _cheekPuff;
            }

            return System.Array.IndexOf(LipShapes, index) >= 0 ? _lips : 1f;
        }
    }
}
