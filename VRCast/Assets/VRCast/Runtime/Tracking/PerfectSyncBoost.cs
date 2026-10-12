using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// パーフェクトシンクの口・頬の強調: 検出が弱く 1 まで届きにくい ARKit の値を倍率で強める。
    /// ふくれっ面（mouthPucker・mouthFunnel）・口の左右（mouthLeft / mouthRight）・頬ふくらませ（cheekPuff）・
    /// 唇（巻き込み・押し付け・すぼめ上げ）。
    /// cheekPuff と mouthLeft / Right は MediaPipe では弱いため、顔の点から推定した値と大きい方を使う。
    /// 既定値（default）は全部の倍率が 0 で、1 未満の倍率は強調しない扱いなので、何も変えない。
    /// </summary>
    public readonly struct PerfectSyncBoost
    {
        // 組ごとの ARKit 名の位置（BlendShapeNames 内）
        private static readonly int MouthPucker = ArKitFace.IndexOf("mouthPucker");
        private static readonly int MouthFunnel = ArKitFace.IndexOf("mouthFunnel");
        private static readonly int CheekPuff = ArKitFace.IndexOf("cheekPuff");
        private static readonly int MouthLeft = ArKitFace.IndexOf("mouthLeft");
        private static readonly int MouthRight = ArKitFace.IndexOf("mouthRight");
        private static readonly int[] LipShapes =
        {
            ArKitFace.IndexOf("mouthRollLower"), ArKitFace.IndexOf("mouthRollUpper"),
            ArKitFace.IndexOf("mouthShrugLower"), ArKitFace.IndexOf("mouthShrugUpper"),
            ArKitFace.IndexOf("mouthPressLeft"), ArKitFace.IndexOf("mouthPressRight"),
        };

        // 頬のふくらみの推定を弱める判定（口を開けている・笑っていると頬の幅も変わるため）に使う位置と、
        // その値で弱め始める・推定を消し切る範囲
        private static readonly int JawOpen = ArKitFace.JawOpen;
        private static readonly int MouthSmileLeft = ArKitFace.IndexOf("mouthSmileLeft");
        private static readonly int MouthSmileRight = ArKitFace.IndexOf("mouthSmileRight");
        // MediaPipe は真顔でも jawOpen が 0.2 前後出ることがあるので、明らかに開いているときだけ弱める
        private const float JawGateStart = 0.35f;
        private const float JawGateEnd = 0.65f;
        private const float SmileGateStart = 0.35f;
        private const float SmileGateEnd = 0.7f;

        // 口の左右: 両目幅に対するずれがこの割合で出し始め、この割合で最大
        public const float MouthShiftStart = 0.02f;
        public const float MouthShiftFull = 0.08f;

        // 組ごとの倍率（1 = そのまま）
        private readonly float _pucker;
        private readonly float _mouth;
        private readonly float _cheekPuff;
        private readonly float _lips;

        // 顔の形から推定した頬のふくらみと、アバター基準の口の左右（0〜1。推定しない入力元は 0）
        private readonly float _cheekPuffEstimate;
        private readonly float _mouthLeft;
        private readonly float _mouthRight;

        public PerfectSyncBoost(float pucker, float mouth, float cheekPuff, float lips, float cheekPuffEstimate,
            float mouthLeft, float mouthRight)
        {
            _pucker = pucker;
            _mouth = mouth;
            _cheekPuff = cheekPuff;
            _lips = lips;
            _cheekPuffEstimate = cheekPuffEstimate;
            _mouthLeft = mouthLeft;
            _mouthRight = mouthRight;
        }

        /// <summary>
        /// 頬の幅の広がり widen（割合）から頬のふくらみ（0〜1）を推定する。
        /// startPercent で出し始め fullPercent で最大にし、scores（ARKit の値）で口を開けている・笑っている分だけ弱める。
        /// </summary>
        public static float EstimateCheekPuff(float widen, float[] scores, float startPercent, float fullPercent)
        {
            // 値が無ければ推定しない
            if (scores == null || scores.Length != ArKitFace.BlendShapeNames.Length)
            {
                return 0f;
            }

            // 広がり（%）を 0〜1 へ写す
            float puff = Mathf.InverseLerp(startPercent, fullPercent, widen * 100f);

            // 口の開き・笑顔（左右の平均）の分だけ弱める
            float smile = (scores[MouthSmileLeft] + scores[MouthSmileRight]) * 0.5f;
            puff *= 1f - Mathf.InverseLerp(JawGateStart, JawGateEnd, scores[JawOpen]);
            puff *= 1f - Mathf.InverseLerp(SmileGateStart, SmileGateEnd, smile);
            return puff;
        }

        /// <summary>
        /// 口の横ずれ shift（正 = 映像の右、両目幅に対する割合）から、映像基準の口の左右（0〜1）を返す。
        /// </summary>
        public static void EstimateMouthSides(float shift, out float imageLeft, out float imageRight)
        {
            imageRight = Mathf.InverseLerp(MouthShiftStart, MouthShiftFull, shift);
            imageLeft = Mathf.InverseLerp(MouthShiftStart, MouthShiftFull, -shift);
        }

        /// <summary>
        /// ARKit 名の位置 index の値 value（0〜1）を倍率で強め、0〜1 に収めて返す（対象外の名前はそのまま）。
        /// </summary>
        public float Apply(int index, float value)
        {
            // 頬のふくらみ・口の左右は、受信値と推定値の大きい方を強める
            if (index == CheekPuff)
            {
                value = Mathf.Max(value, _cheekPuffEstimate);
            }
            else if (index == MouthLeft)
            {
                value = Mathf.Max(value, _mouthLeft);
            }
            else if (index == MouthRight)
            {
                value = Mathf.Max(value, _mouthRight);
            }

            // 名前の組に応じた倍率（1 未満は 1 とみなし、弱めない）
            float gain = Mathf.Max(1f, GainFor(index));
            return Mathf.Clamp01(value * gain);
        }

        private float GainFor(int index)
        {
            // ふくれっ面・口の左右・頬ふくらませ・唇の順に判定し、どれでもなければ強調しない
            if (index == MouthPucker || index == MouthFunnel)
            {
                return _pucker;
            }

            if (index == MouthLeft || index == MouthRight)
            {
                return _mouth;
            }

            if (index == CheekPuff)
            {
                return _cheekPuff;
            }

            return System.Array.IndexOf(LipShapes, index) >= 0 ? _lips : 1f;
        }
    }
}
