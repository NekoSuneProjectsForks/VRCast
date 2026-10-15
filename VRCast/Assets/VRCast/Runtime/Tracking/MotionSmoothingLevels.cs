using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 動きのなめらかさ（MotionSmoothing）ごとの追従の強さ。
    /// 各処理は「差が小さいときの速さ（遅い）」「差が大きいときの速さ（速い）」を持ち、なめらかなほど遅い速さを下げ、
    /// 速い速さを遅い速さへ寄せる（中で 2.2.1 までと同じ一定の速さ）。腕・手は寄る秒数に下限を設ける。
    /// MediaPipe トラッカー側の強さは vrcast_tracker.py の SMOOTHING_FILTERS（段階の番号は同じ）。
    /// </summary>
    internal static class MotionSmoothingLevels
    {
        // 遅い速さの倍率と、速い速さを使う割合（0 なら遅い速さのまま）
        private static readonly float[] SlowScales = { 1f, 1f, 1f, 0.6f, 0.35f };
        private static readonly float[] FastShares = { 1f, 0.5f, 0f, 0f, 0f };

        // 腕・手の寄る秒数の下限（0 = 届く間隔に合わせるだけ）
        private static readonly float[] ArmFloors = { 0f, 0.05f, 0.07f, 0.11f, 0.18f };
        private static readonly float[] HandFloors = { 0f, 0.035f, 0.05f, 0.08f, 0.13f };

        /// <summary>
        /// 段階に合わせた遅い速さ・速い速さ（1 秒あたり）。
        /// </summary>
        public static void Rates(MotionSmoothing level, float slow, float fast, out float slowRate, out float fastRate)
        {
            int index = Index(level);
            slowRate = slow * SlowScales[index];
            fastRate = Mathf.Lerp(slowRate, fast, FastShares[index]);
        }

        /// <summary>
        /// 目標との差 error から、段階に合わせた 1 フレームの寄せる割合を求める（TrackingMath.FollowBlend と同じ形）。
        /// </summary>
        public static float FollowBlend(MotionSmoothing level, float error, float fullError, float slow, float fast,
            float deltaTime)
        {
            Rates(level, slow, fast, out float slowRate, out float fastRate);
            return TrackingMath.FollowBlend(error, fullError, slowRate, fastRate, deltaTime);
        }

        public static float ArmSmoothTimeFloor(MotionSmoothing level)
        {
            return ArmFloors[Index(level)];
        }

        public static float HandSmoothTimeFloor(MotionSmoothing level)
        {
            return HandFloors[Index(level)];
        }

        private static int Index(MotionSmoothing level)
        {
            return Mathf.Clamp((int)level, 0, SlowScales.Length - 1);
        }
    }
}
