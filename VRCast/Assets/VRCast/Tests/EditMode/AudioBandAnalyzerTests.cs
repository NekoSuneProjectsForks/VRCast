using NUnit.Framework;
using UnityEngine;
using VRCast.Audio;

namespace VRCast.Tests
{
    public class AudioBandAnalyzerTests
    {
        private const int SampleRate = 48000;

        [TestCase(100f, 0)]
        [TestCase(350f, 1)]
        [TestCase(1000f, 2)]
        [TestCase(4500f, 3)]
        public void PureTone_HasStrongestExpectedBand(float frequency, int expectedBand)
        {
            var analyzer = new AudioBandAnalyzer();
            float[] samples = MakeTone(frequency);
            for (int i = 0; i < 8; i++) Assert.IsTrue(analyzer.Analyze(samples, SampleRate, 1f, 1f / 30f));
            int strongest = 0;
            for (int i = 1; i < AudioBandAnalyzer.BandCount; i++)
                if (analyzer.GetBand(i) > analyzer.GetBand(strongest)) strongest = i;
            Assert.AreEqual(expectedBand, strongest);
            Assert.Greater(analyzer.GetBand(expectedBand), 0.1f);
        }

        [Test]
        public void Silence_DecaysBandsAndClearsBeat()
        {
            var analyzer = new AudioBandAnalyzer();
            float[] samples = MakeTone(100f);
            analyzer.Analyze(samples, SampleRate, 1f, 1f / 30f);
            float initial = analyzer.GetBand(0);
            Assert.Greater(initial, 0f);
            System.Array.Clear(samples, 0, samples.Length);
            for (int i = 0; i < 30; i++) analyzer.Analyze(samples, SampleRate, 1f, 1f / 30f);
            Assert.Less(analyzer.GetBand(0), initial * 0.01f);
            Assert.AreEqual(0f, analyzer.Rms);
            Assert.IsFalse(analyzer.Beat);
        }

        [Test]
        public void GainAndReset_AreBounded()
        {
            var analyzer = new AudioBandAnalyzer();
            Assert.IsFalse(analyzer.Analyze(null, SampleRate, 1f, 1f / 30f));
            float[] samples = MakeTone(100f);
            for (int i = 0; i < 8; i++) analyzer.Analyze(samples, SampleRate, 8f, 1f / 30f);
            Assert.That(analyzer.Rms, Is.InRange(0f, 1f));
            for (int i = 0; i < AudioBandAnalyzer.BandCount; i++)
                Assert.That(analyzer.GetBand(i), Is.InRange(0f, 1f));
            analyzer.Reset();
            Assert.AreEqual(0f, analyzer.Rms);
            for (int i = 0; i < AudioBandAnalyzer.BandCount; i++) Assert.AreEqual(0f, analyzer.GetBand(i));
        }

        [Test]
        public void NonFiniteInputs_AreRejectedWithoutChangingPublishedLevels()
        {
            var analyzer = new AudioBandAnalyzer();
            float[] samples = MakeTone(100f);
            Assert.IsTrue(analyzer.Analyze(samples, SampleRate, 1f, 1f / 30f));
            float previousRms = analyzer.Rms;
            float previousBass = analyzer.GetBand(0);
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.IsFalse(analyzer.Analyze(samples, SampleRate, invalid, 1f / 30f));
                Assert.IsFalse(analyzer.Analyze(samples, SampleRate, 1f, invalid));
                samples[42] = invalid;
                Assert.IsFalse(analyzer.Analyze(samples, SampleRate, 1f, 1f / 30f));
                samples[42] = 0f;
                Assert.AreEqual(previousRms, analyzer.Rms);
                Assert.AreEqual(previousBass, analyzer.GetBand(0));
            }
            Assert.IsTrue(analyzer.Analyze(samples, SampleRate, 1f, 1f / 30f));
        }

        [Test]
        public void InvalidShapeAndSampleRate_AreRejected()
        {
            var analyzer = new AudioBandAnalyzer();
            Assert.IsFalse(analyzer.Analyze(new float[8], SampleRate, 1f, 1f / 30f));
            Assert.IsFalse(analyzer.Analyze(new float[AudioBandAnalyzer.WindowSize], 7999, 1f, 1f / 30f));
        }

        private static float[] MakeTone(float frequency)
        {
            var samples = new float[AudioBandAnalyzer.WindowSize];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * frequency * i / SampleRate);
            return samples;
        }
    }
}
