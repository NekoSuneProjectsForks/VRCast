using UnityEngine;

namespace VRCast.Audio
{
    /// <summary>
    /// Allocation-free, four-band analysis of a mono PCM window. AudioLink's
    /// four row names are used, but the spectral response is an approximation.
    /// </summary>
    public sealed class AudioBandAnalyzer
    {
        public const int WindowSize = 2048;
        public const int BandCount = 4;
        private readonly float[] _real = new float[WindowSize];
        private readonly float[] _imag = new float[WindowSize];
        private readonly float[] _window = new float[WindowSize];
        private readonly float[] _bands = new float[BandCount];
        private readonly float[] _energy = new float[BandCount];
        private float _previousBass;
        private float _beatCooldown;

        public float Rms { get; private set; }
        public bool Beat { get; private set; }
        public float GetBand(int index) => _bands[index];

        public AudioBandAnalyzer()
        {
            for (int i = 0; i < WindowSize; i++)
                _window[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (WindowSize - 1));
        }

        public void Reset()
        {
            Rms = 0f;
            Beat = false;
            _previousBass = 0f;
            _beatCooldown = 0f;
            for (int i = 0; i < BandCount; i++) _bands[i] = 0f;
            for (int i = 0; i < BandCount; i++) _energy[i] = 0f;
        }

        /// <summary>Input is a 2048-sample mono window; time is the analysis interval.</summary>
        public bool Analyze(float[] samples, int sampleRate, float gain, float elapsedSeconds)
        {
            if (samples == null || samples.Length != WindowSize || sampleRate < 8000)
            {
                return false;
            }

            if (float.IsNaN(gain) || float.IsInfinity(gain)
                || float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds)) return false;

            float sum = 0f;
            for (int i = 0; i < WindowSize; i++)
            {
                float value = samples[i];
                if (float.IsNaN(value) || float.IsInfinity(value)) return false;
                sum += value * value;
                // Hann window suppresses leakage at the edges of the sample.
                _real[i] = value * _window[i];
                _imag[i] = 0f;
            }

            Rms = Mathf.Clamp01(Mathf.Sqrt(sum / WindowSize) * gain);
            Transform();
            for (int i = 0; i < BandCount; i++) _energy[i] = 0f;

            // Bands in Hz: bass 20-250, low mids 250-500, high mids 500-2000,
            // treble 2000-8000. Nyquist bins outside the range are ignored.
            float resolution = sampleRate / (float)WindowSize;
            for (int i = 1; i < WindowSize / 2; i++)
            {
                float hz = i * resolution;
                int band = hz < 20f || hz >= 8000f ? -1
                    : hz < 250f ? 0 : hz < 500f ? 1 : hz < 2000f ? 2 : 3;
                if (band >= 0)
                {
                    _energy[band] += _real[i] * _real[i] + _imag[i] * _imag[i];
                }
            }

            float dt = Mathf.Max(0.001f, elapsedSeconds);
            float sensitivity = Mathf.Clamp(gain, 0f, 8f) * (4f / WindowSize);
            for (int i = 0; i < BandCount; i++)
            {
                float target = Mathf.Clamp01(Mathf.Sqrt(_energy[i]) * sensitivity);
                float response = target > _bands[i] ? 20f : 7f;
                _bands[i] = Mathf.Lerp(_bands[i], target, 1f - Mathf.Exp(-response * dt));
            }

            _beatCooldown = Mathf.Max(0f, _beatCooldown - dt);
            Beat = _beatCooldown <= 0f && Rms > 0.025f
                && _bands[0] > 0.12f && _bands[0] > _previousBass * 1.3f;
            if (Beat) _beatCooldown = 0.18f;
            _previousBass = _bands[0];
            return true;
        }

        // Iterative radix-2 FFT; buffers are allocated once with the analyzer.
        private void Transform()
        {
            int j = 0;
            for (int i = 1; i < WindowSize; i++)
            {
                int bit = WindowSize >> 1;
                while ((j & bit) != 0) { j ^= bit; bit >>= 1; }
                j ^= bit;
                if (i >= j) continue;
                float real = _real[i]; _real[i] = _real[j]; _real[j] = real;
                float imag = _imag[i]; _imag[i] = _imag[j]; _imag[j] = imag;
            }

            for (int length = 2; length <= WindowSize; length <<= 1)
            {
                int half = length >> 1;
                float angle = -2f * Mathf.PI / length;
                float wrStep = Mathf.Cos(angle);
                float wiStep = Mathf.Sin(angle);
                for (int start = 0; start < WindowSize; start += length)
                {
                    float wr = 1f, wi = 0f;
                    for (int k = 0; k < half; k++)
                    {
                        int a = start + k, b = a + half;
                        float tr = wr * _real[b] - wi * _imag[b];
                        float ti = wr * _imag[b] + wi * _real[b];
                        _real[b] = _real[a] - tr; _imag[b] = _imag[a] - ti;
                        _real[a] += tr; _imag[a] += ti;
                        float nextWr = wr * wrStep - wi * wiStep;
                        wi = wr * wiStep + wi * wrStep;
                        wr = nextWr;
                    }
                }
            }
        }
    }
}
