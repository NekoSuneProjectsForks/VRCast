using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;
using VRCast.Remote;

namespace VRCast.Tracking
{
    /// <summary>
    /// トラッカーの UDP 出力を受信する Provider。アプリ全体で 1 つ。
    /// 設定の入力元に合わせて受信口（TrackingChannel）を開き、OpenSeeFace（顔のみ）・MediaPipe（顔・腕・手）・VMC（顔・全身）・
    /// iFacialMocap（顔のみ）を受け取る。
    /// 併用の入力元では、iPhone 等の外部アプリ（VMC / iFacialMocap）と同梱の MediaPipe の 2 つの受信口を同時に開き、
    /// 顔は外部アプリ（途切れている間は MediaPipe）、腕・手は MediaPipe から渡す。
    /// </summary>
    public class TrackingReceiver : MonoBehaviour, IFaceTrackingProvider, IBodyTrackingProvider, IHumanoidPoseProvider
    {
        // 設定の入力元の受信口（併用では外部アプリ側）
        private readonly TrackingChannel _main = new TrackingChannel();

        // 併用のときだけ使う MediaPipe の受信口
        private readonly TrackingChannel _camera = new TrackingChannel();

        private AppSettings _settings;
        private readonly OscParameterSender _oscOutput = new OscParameterSender();
        private readonly HashSet<string> _oscParametersSentThisFrame = new HashSet<string>();
        private string _oscOutputAddress;
        private int _oscOutputPort = -1;
        private float _nextOscOutputTime;
        private float _nextOscOutputRetryTime;

        // 併用中なら true（Update で設定から決める）
        private bool _hybrid;

        public string Status { get; private set; } = "Disabled";

        /// <summary>
        /// 顔のフレームを受け取っている入力元（併用では外部アプリか MediaPipe か。受信していない間は最後の値）。
        /// </summary>
        public TrackingSource FaceSource { get; private set; }

        // 併用で MediaPipe の顔を使っている間はそのレート、それ以外は設定の入力元のレート
        public int FramesPerSecond => UsesCameraFace ? _camera.FramesPerSecond : _main.FramesPerSecond;

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
        }

        public bool TryGetFrame(out FaceTrackingFrame frame)
        {
            // 外部アプリの顔を優先し、併用で途切れている間だけ MediaPipe の顔を使う
            if (_main.TryGetFrame(out frame))
            {
                return true;
            }

            return _hybrid && _camera.TryGetFrame(out frame);
        }

        public bool TryGetBody(out BodyTrackingFrame frame)
        {
            // 腕・手は併用なら MediaPipe の受信口、それ以外は設定の入力元（MediaPipe 以外では常に false）
            return _hybrid ? _camera.TryGetBody(out frame) : _main.TryGetBody(out frame);
        }

        public bool TryGetPose(out HumanoidPoseFrame pose)
        {
            // 全身の姿勢は VMC の受信口のみ（併用では FullBodyTrackingDriver が使わない）
            return _main.TryGetPose(out pose);
        }

        // 併用で外部アプリの顔が途切れ、MediaPipe の顔を使っている間なら true
        private bool UsesCameraFace => _hybrid && !_main.TryGetFrame(out _) && _camera.TryGetFrame(out _);

        private void Update()
        {
            // 未初期化なら何もしない
            if (_settings == null)
            {
                return;
            }

            UpdateOscOutputConnection();

            // 無効化されたら閉じ、再有効化時はすぐ開けるようにする
            if (!_settings.trackingEnabled)
            {
                _main.Stop("Disabled");
                _camera.Stop("Disabled");
                Status = "Disabled";
                return;
            }

            // 併用は外部アプリ側と MediaPipe 側を両方、それ以外は設定の入力元だけを待ち受ける。
            // 併用をやめたときは、MediaPipe のポートを設定の入力元が使えるよう先に閉じる
            TrackingSource source = _settings.trackingSource;
            _hybrid = TrackingSourceInfo.IsHybrid(source);
            if (!_hybrid)
            {
                _camera.Stop("Not used");
            }

            _main.Update(_settings, TrackingSourceInfo.NetworkSourceOf(source));
            if (_hybrid)
            {
                _camera.Update(_settings, TrackingSource.MediaPipe);
            }

            // 顔を受け取っている入力元（受信していない間は前の値のまま。正面の取り直しの判定に使う）
            if (_main.TryGetFrame(out _))
            {
                FaceSource = _main.BoundSource;
            }
            else if (UsesCameraFace)
            {
                FaceSource = TrackingSource.MediaPipe;
            }

            // 併用は両方の受信口の状態を並べる
            Status = _hybrid
                ? $"{_main.BoundSource}: {_main.Status} / MediaPipe: {_camera.Status}"
                  + (UsesCameraFace ? " (using the webcam face)" : string.Empty)
                : _main.Status;

            UpdateOscFaceParameters();
        }

        private void UpdateOscOutputConnection()
        {
            if (!_settings.oscParameterOutputEnabled)
            {
                _oscOutput.Stop();
                _oscOutputAddress = null;
                _oscOutputPort = -1;
                return;
            }

            bool sameEndpoint = _oscOutputAddress == _settings.oscParameterOutputAddress
                && _oscOutputPort == _settings.oscParameterOutputPort;
            if (sameEndpoint && (_oscOutput.IsEnabled || Time.unscaledTime < _nextOscOutputRetryTime))
            {
                return;
            }

            _oscOutput.Start(_settings.oscParameterOutputAddress, _settings.oscParameterOutputPort);
            _oscOutputAddress = _settings.oscParameterOutputAddress;
            _oscOutputPort = _settings.oscParameterOutputPort;
            _nextOscOutputRetryTime = Time.unscaledTime + 3f;
        }

        private void UpdateOscFaceParameters()
        {
            if (!_oscOutput.IsEnabled || Time.unscaledTime < _nextOscOutputTime
                || !TryGetFrame(out FaceTrackingFrame frame))
            {
                return;
            }

            // Bound the network update rate so tracking/render FPS changes do not flood the OSC receiver.
            int outputRate = Mathf.Clamp(_settings.oscParameterOutputRate <= 0 ? 30 : _settings.oscParameterOutputRate, 1, 60);
            _nextOscOutputTime = Time.unscaledTime + (1f / outputRate);
            _oscParametersSentThisFrame.Clear();
            SendMappedFloat(_settings.oscParameterEyeOpenLeft, frame.EyeOpenLeft);
            SendMappedFloat(_settings.oscParameterEyeOpenRight, frame.EyeOpenRight);
            SendMappedFloat(_settings.oscParameterMouthOpen, frame.MouthOpen);
            if (frame.HasExpression)
            {
                SendMappedFloat(_settings.oscParameterSmile, frame.Expression.Smile);
            }

            // ARKit-compatible providers expose the original blendshape vector; map selected channels directly.
            float[] blendShapes = frame.BlendShapes;
            if (blendShapes != null)
            {
                SendBlendShape(_settings.oscParameterEyeBlinkLeft, blendShapes, ArKitFace.EyeBlinkLeft);
                SendBlendShape(_settings.oscParameterEyeBlinkRight, blendShapes, ArKitFace.EyeBlinkRight);
                SendBlendShape(_settings.oscParameterJawOpen, blendShapes, ArKitFace.JawOpen);
                SendBlendShape(_settings.oscParameterMouthSmileLeft, blendShapes, ArKitFace.MouthSmileLeft);
                SendBlendShape(_settings.oscParameterMouthSmileRight, blendShapes, ArKitFace.MouthSmileRight);
                SendBlendShape(_settings.oscParameterBrowInnerUp, blendShapes, ArKitFace.BrowInnerUp);
                SendConfiguredBlendShapes(blendShapes);
            }
        }

        private void SendConfiguredBlendShapes(float[] blendShapes)
        {
            var mappings = _settings.oscBlendShapeMappings;
            if (mappings == null)
            {
                return;
            }

            foreach (OscBlendShapeParameterMapping mapping in mappings)
            {
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.parameterName)
                    || string.IsNullOrWhiteSpace(mapping.blendShapeName))
                {
                    continue;
                }

                // Cache the fixed ARKit channel index per mapping. The mapping UI can change
                // names at runtime, so refresh only when its source name changes.
                if (!string.Equals(mapping.cachedBlendShapeName, mapping.blendShapeName, System.StringComparison.Ordinal))
                {
                    mapping.cachedBlendShapeName = mapping.blendShapeName;
                    mapping.cachedBlendShapeIndex = System.Array.IndexOf(ArKitFace.BlendShapeNames, mapping.blendShapeName);
                }

                SendBlendShape(mapping.parameterName, blendShapes, mapping.cachedBlendShapeIndex);
            }
        }

        private void SendBlendShape(string parameterName, float[] blendShapes, int index)
        {
            if (index >= 0 && index < blendShapes.Length)
            {
                SendMappedFloat(parameterName, blendShapes[index]);
            }
        }

        private void SendMappedFloat(string parameterName, float value)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            string name = parameterName.Trim();
            // Multiple channels targeting the same VRChat parameter would overwrite each other in one update.
            // Keep the first configured source for this update instead of sending conflicting values.
            if (float.IsNaN(value) || float.IsInfinity(value) || name.IndexOf('/') >= 0)
            {
                return;
            }

            if (_oscParametersSentThisFrame.Add(name))
            {
                _oscOutput.SendFloat(name, Mathf.Clamp01(value));
            }
        }

        private void OnDestroy()
        {
            // 終了時にポートを解放
            _main.Close("Stopped");
            _camera.Close("Stopped");
            _oscOutput.Dispose();
        }
    }
}
