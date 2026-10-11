using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// トラッキングの目の閉じ具合（0 = 開き、1 = 閉じ）の補正。通常のまばたきとパーフェクトシンクで共通に使う。
    /// 目の開きの補正（OpenOffset までの閉じ具合は開いた目とみなす）と、左右をそろえる補正（Sync）を順にかける。
    /// </summary>
    public struct EyeAdjustment
    {
        // 横を向いたとみなし始める・完全にそろえる頭の左右の角度（度）
        public const float TurnStartDegrees = 15f;
        public const float TurnFullDegrees = 25f;

        // 開いているとみなす目の閉じ具合（0 = 補正なし）
        public float OpenOffset;

        // 左右をそろえる方法
        public BlinkSyncMode Sync;

        // 横を向いている度合い（0 = 正面、1 = 横向き）
        public float Turned;

        /// <summary>
        /// 正面からの頭の左右の角度（度、符号は問わない）を、横を向いている度合い 0〜1 へ写す。
        /// </summary>
        public static float TurnedFromYaw(float yawDegrees)
        {
            return Mathf.InverseLerp(TurnStartDegrees, TurnFullDegrees, Mathf.Abs(yawDegrees));
        }

        /// <summary>
        /// 左右の閉じ具合を補正する。
        /// </summary>
        public void Apply(ref float left, ref float right)
        {
            // 補正分の半目は開いた目とみなす
            left = TrackingMath.OpenEyes(left, OpenOffset);
            right = TrackingMath.OpenEyes(right, OpenOffset);

            // そろえない設定・正面を向いている間（横向きのときだけの設定）は左右別のまま
            float weight = Sync == BlinkSyncMode.Always ? 1f : Sync == BlinkSyncMode.WhenTurned ? Turned : 0f;
            if (weight <= 0f)
            {
                return;
            }

            // 正面では平均、横を向くほど開いている方（カメラに近い側の目。奥側は閉じて見えやすい）に寄せる
            float average = (left + right) * 0.5f;
            float target = Mathf.Lerp(average, Mathf.Min(left, right), Turned);
            left = Mathf.Lerp(left, target, weight);
            right = Mathf.Lerp(right, target, weight);
        }
    }
}
