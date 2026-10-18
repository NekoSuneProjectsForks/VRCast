using UnityEngine;
using VRCast.Core;

namespace VRCast.Audio
{
    /// <summary>
    /// Supplies basic four-band AudioLink-compatible texture coordinates to
    /// shaders rendered by VRCast. Other AudioLink passes are intentionally zero.
    /// </summary>
    public sealed class AudioReactiveBridge : MonoBehaviour
    {
        private const int Width = 128;
        private const int Height = 64;
        private const float MissingInputClearSeconds = 0.2f;
        private static readonly int AudioTextureId = Shader.PropertyToID("_AudioTexture");
        private static readonly int AudioEnabledId = Shader.PropertyToID("_AudioLinkEnabled");
        private static readonly int BandsId = Shader.PropertyToID("_VRCastAudioBands");
        private static readonly Color[] BlankTexturePixels = new Color[Width * Height];

        private readonly float[] _samples = new float[AudioBandAnalyzer.WindowSize];
        // Only the four active rows change. Avoid uploading the full 128x64
        // texture at the audio analysis rate.
        private readonly Color[] _pixels = new Color[Width * AudioBandAnalyzer.BandCount];
        private readonly AudioBandAnalyzer _analyzer = new AudioBandAnalyzer();
        private MicrophoneInput _microphone;
        private AppSettings _settings;
        private Texture2D _texture;
        private float _nextAnalysis;
        private float _lastSuccess;
        private bool _active;

        public AudioBandAnalyzer Analyzer => _analyzer;
        public bool HasInput { get; private set; }

        public void Initialize(MicrophoneInput microphone, AppSettings settings)
        {
            _microphone = microphone;
            _settings = settings;
        }

        private void Update()
        {
            if (_settings == null || !_settings.audioReactiveEnabled)
            {
                if (_active) Clear();
                return;
            }

            if (!_active) Activate();
            float now = Time.unscaledTime;
            if (now < _nextAnalysis) return;

            float interval = 1f / Mathf.Clamp(_settings.audioAnalysisHz, 5, 60);
            _nextAnalysis = now + interval;
            if (_microphone != null && _microphone.TryCopyLatest(_samples, out int sampleRate)
                && _analyzer.Analyze(_samples, sampleRate, _settings.audioReactiveGain, interval))
            {
                HasInput = true;
                _lastSuccess = now;
                WriteBands();
            }
            else if (HasInput && now - _lastSuccess >= MissingInputClearSeconds)
            {
                // A disconnected or newly-started microphone must not leave
                // frozen energy in shaders for the rest of the session.
                ClearPixels();
                _analyzer.Reset();
                HasInput = false;
            }
        }

        private void Activate()
        {
            _active = true;
            _nextAnalysis = 0f;
            _lastSuccess = Time.unscaledTime;
            _analyzer.Reset();
            // SetPixels(region) is reliably supported for RGBA32 on Unity 2022's
            // built-in renderer. Half-float support for this CPU write API varies
            // across texture formats/backends, and these bands are normalized.
            _texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
            {
                name = "VRCast Basic AudioLink Data",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _texture.SetPixels(BlankTexturePixels);
            ClearPixels();
            Shader.SetGlobalTexture(AudioTextureId, _texture);
            Shader.SetGlobalFloat(AudioEnabledId, 1f);
        }

        private void WriteBands()
        {
            // x = 0 is the newest value, x = 1..127 are time history. This is
            // useful to simple band/history shaders, but does not implement
            // AudioLink's exact processing, DFT, waveform or ColorChord passes.
            for (int row = 0; row < AudioBandAnalyzer.BandCount; row++)
            {
                int offset = row * Width;
                for (int x = Width - 1; x > 0; x--) _pixels[offset + x] = _pixels[offset + x - 1];
                float value = _analyzer.GetBand(row);
                _pixels[offset] = new Color(value, value, value, 1f);
            }
            _texture.SetPixels(0, 0, Width, AudioBandAnalyzer.BandCount, _pixels);
            _texture.Apply(false, false);
            Shader.SetGlobalVector(BandsId, new Vector4(_analyzer.GetBand(0), _analyzer.GetBand(1),
                _analyzer.GetBand(2), _analyzer.GetBand(3)));
        }

        private void ClearPixels()
        {
            System.Array.Clear(_pixels, 0, _pixels.Length);
            if (_texture != null)
            {
                _texture.SetPixels(0, 0, Width, AudioBandAnalyzer.BandCount, _pixels);
                _texture.Apply(false, false);
            }
            Shader.SetGlobalVector(BandsId, Vector4.zero);
        }

        private void Clear()
        {
            _active = false;
            HasInput = false;
            _analyzer.Reset();
            Shader.SetGlobalFloat(AudioEnabledId, 0f);
            Shader.SetGlobalTexture(AudioTextureId, Texture2D.blackTexture);
            Shader.SetGlobalVector(BandsId, Vector4.zero);
            if (_texture != null) Destroy(_texture);
            _texture = null;
        }

        private void OnDisable() { if (_active) Clear(); }
        private void OnDestroy() { if (_active) Clear(); }
    }
}
