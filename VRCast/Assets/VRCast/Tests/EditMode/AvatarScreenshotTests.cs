using NUnit.Framework;
using UnityEngine;
using VRCast.Rendering;

namespace VRCast.Tests
{
    /// <summary>
    /// 黒・白背景の描画結果から透明度と色を求める処理を検証する。
    /// </summary>
    public class AvatarScreenshotTests
    {
        [Test]
        public void Unmix_Background_IsTransparent()
        {
            // 何も描かれていない画素は黒背景で黒、白背景で白
            Color32[] pixels = AvatarScreenshot.Unmix(new[] { Color.black }, new[] { Color.white }, false);

            // 完全に透明になること
            Assert.That(pixels[0].a, Is.EqualTo(0));
        }

        [Test]
        public void Unmix_OpaquePixel_KeepsColor()
        {
            // 不透明な赤は背景によらず赤
            var red = new Color(1f, 0f, 0f, 1f);
            Color32[] pixels = AvatarScreenshot.Unmix(new[] { red }, new[] { red }, false);

            // 不透明の赤のまま
            Assert.That(pixels[0], Is.EqualTo(new Color32(255, 0, 0, 255)));
        }

        [Test]
        public void Unmix_HalfTransparentPixel_RecoversAlphaAndColor()
        {
            // 半透明（50%）の赤: 黒背景で (0.5, 0, 0)、白背景で (1, 0.5, 0.5)
            Color32[] pixels = AvatarScreenshot.Unmix(
                new[] { new Color(0.5f, 0f, 0f) }, new[] { new Color(1f, 0.5f, 0.5f) }, false);

            // 透明度 50%・元の色は赤
            Assert.That(pixels[0].a, Is.EqualTo(128));
            Assert.That(pixels[0].r, Is.EqualTo(255));
            Assert.That(pixels[0].g, Is.EqualTo(0));
        }
    }
}
