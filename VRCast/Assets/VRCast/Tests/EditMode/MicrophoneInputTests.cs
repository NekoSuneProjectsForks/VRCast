using NUnit.Framework;
using VRCast.Audio;

namespace VRCast.Tests
{
    public sealed class MicrophoneInputTests
    {
        [Test]
        public void MonoInput_CopiesSamples()
        {
            float[] source = { -1f, 0.25f, 1f };
            float[] mono = new float[3];
            Assert.IsTrue(MicrophoneInput.DownmixInterleaved(source, 1, mono));
            CollectionAssert.AreEqual(source, mono);
        }

        [Test]
        public void StereoInput_AveragesChannelPairs()
        {
            float[] stereo = { -1f, 1f, 0.25f, 0.75f, -0.5f, 0f };
            float[] mono = new float[3];
            Assert.IsTrue(MicrophoneInput.DownmixInterleaved(stereo, 2, mono));
            CollectionAssert.AreEqual(new[] { 0f, 0.5f, -0.25f }, mono);
        }

        [Test]
        public void MultiChannelInput_AveragesAllChannels()
        {
            float[] channels = { 0.2f, 0.4f, 0.6f, -1f, 0f, 1f };
            float[] mono = new float[2];
            Assert.IsTrue(MicrophoneInput.DownmixInterleaved(channels, 3, mono));
            Assert.That(mono[0], Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(mono[1], Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void InvalidInput_LeavesDestinationUntouched()
        {
            float[] destination = { 0.75f, -0.25f };
            float[] source = { 1f, 2f, 3f, 4f };
            Assert.IsFalse(MicrophoneInput.DownmixInterleaved(null, 2, destination));
            Assert.IsFalse(MicrophoneInput.DownmixInterleaved(source, 2, null));
            Assert.IsFalse(MicrophoneInput.DownmixInterleaved(source, 0, destination));
            Assert.IsFalse(MicrophoneInput.DownmixInterleaved(source, -2, destination));
            Assert.IsFalse(MicrophoneInput.DownmixInterleaved(source, 3, destination));
            CollectionAssert.AreEqual(new[] { 0.75f, -0.25f }, destination);
        }
    }
}
