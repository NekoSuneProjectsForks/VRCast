using NUnit.Framework;
using VRCast.Animations;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// 自動まばたきの動き（閉じ具合の時間変化と、設定の既定値・補正）を検証する。
    /// </summary>
    public class BlinkMotionTests
    {
        [Test]
        public void Evaluate_FollowsCloseHoldOpen()
        {
            // 閉じ始めは開いていて、閉じたままの間は 1、開き終わりで 0 に戻ること
            Assert.That(BlinkController.Evaluate(0f, 0.1f, 0.2f, 0.3f), Is.EqualTo(0f));
            Assert.That(BlinkController.Evaluate(0.05f, 0.1f, 0.2f, 0.3f), Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(BlinkController.Evaluate(0.2f, 0.1f, 0.2f, 0.3f), Is.EqualTo(1f));
            Assert.That(BlinkController.Evaluate(0.45f, 0.1f, 0.2f, 0.3f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(BlinkController.Evaluate(0.6f, 0.1f, 0.2f, 0.3f), Is.EqualTo(0f));
        }

        [Test]
        public void Defaults_KeepPreviousMotion()
        {
            // 既定値はこれまでの固定値のままであること
            var settings = new AppSettings();
            Assert.That(settings.blinkCloseSeconds, Is.EqualTo(0.07f));
            Assert.That(settings.blinkHoldSeconds, Is.EqualTo(0.05f));
            Assert.That(settings.blinkOpenSeconds, Is.EqualTo(0.16f));
            Assert.That(settings.blinkIntervalMin, Is.EqualTo(2f));
            Assert.That(settings.blinkIntervalMax, Is.EqualTo(6f));
        }

        [Test]
        public void Sanitize_ClampsMotion()
        {
            // 範囲外は範囲内へ、壊れた値は既定値へ、間隔の最長は最短より短くならないこと
            var settings = new AppSettings
            {
                blinkCloseSeconds = 0f,
                blinkHoldSeconds = float.NaN,
                blinkOpenSeconds = 5f,
                blinkIntervalMin = 8f,
                blinkIntervalMax = 3f,
            };
            settings.Sanitize();
            Assert.That(settings.blinkCloseSeconds, Is.EqualTo(AppSettings.MinBlinkMoveSeconds));
            Assert.That(settings.blinkHoldSeconds, Is.EqualTo(AppSettings.DefaultBlinkHoldSeconds));
            Assert.That(settings.blinkOpenSeconds, Is.EqualTo(AppSettings.MaxBlinkMoveSeconds));
            Assert.That(settings.blinkIntervalMin, Is.EqualTo(8f));
            Assert.That(settings.blinkIntervalMax, Is.EqualTo(8f));
        }
    }
}
