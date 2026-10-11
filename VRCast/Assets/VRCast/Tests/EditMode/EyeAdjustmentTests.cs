using NUnit.Framework;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// トラッキングの目の閉じ具合の補正（目の開きの補正・左右をそろえる補正）を検証する。
    /// </summary>
    public class EyeAdjustmentTests
    {
        [Test]
        public void Apply_NoSync_KeepsWink()
        {
            // そろえない設定では片目だけ閉じたまま
            (float left, float right) = Adjust(new EyeAdjustment { Sync = BlinkSyncMode.None, Turned = 1f }, 1f, 0f);
            Assert.That(left, Is.EqualTo(1f));
            Assert.That(right, Is.EqualTo(0f));
        }

        [Test]
        public void Apply_Always_AveragesWhenFrontAndUsesOpenEyeWhenTurned()
        {
            // 正面では平均、横を向いたら開いている方へそろえること
            (float left, float right) = Adjust(new EyeAdjustment { Sync = BlinkSyncMode.Always }, 1f, 0f);
            Assert.That(left, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(right, Is.EqualTo(0.5f).Within(1e-5f));
            (left, right) = Adjust(new EyeAdjustment { Sync = BlinkSyncMode.Always, Turned = 1f }, 0.8f, 0.1f);
            Assert.That(left, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(right, Is.EqualTo(0.1f).Within(1e-5f));

            // 両目とも閉じたまばたきは閉じたまま
            (left, right) = Adjust(new EyeAdjustment { Sync = BlinkSyncMode.Always, Turned = 1f }, 1f, 1f);
            Assert.That(left, Is.EqualTo(1f));
            Assert.That(right, Is.EqualTo(1f));
        }

        [Test]
        public void Apply_WhenTurned_OnlySyncsWhileTurned()
        {
            // 正面ではそのまま、横を向いたらそろえること
            (float left, float right) = Adjust(new EyeAdjustment { Sync = BlinkSyncMode.WhenTurned }, 1f, 0f);
            Assert.That(left, Is.EqualTo(1f));
            Assert.That(right, Is.EqualTo(0f));
            float turned = EyeAdjustment.TurnedFromYaw(-40f);
            (left, right) = Adjust(new EyeAdjustment { Sync = BlinkSyncMode.WhenTurned, Turned = turned }, 1f, 0f);
            Assert.That(left, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(right, Is.EqualTo(0f).Within(1e-5f));
        }

        private static (float, float) Adjust(EyeAdjustment adjustment, float left, float right)
        {
            // 補正後の左右の閉じ具合を返す
            adjustment.Apply(ref left, ref right);
            return (left, right);
        }
    }
}
