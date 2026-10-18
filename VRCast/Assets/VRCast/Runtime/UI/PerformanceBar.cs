using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using VRCast.Core;
using VRCast.Platform;
using VRCast.Rendering;
using VRCast.Tracking;
using VRCast.Remote;

namespace VRCast.UI
{
    /// <summary>
    /// パネルの一番下に常に表示する動作状況（描画の fps・VRCast の CPU・GPU の使用率と処理時間・
    /// アバターの描画解像度・トラッカーの CPU・トラッキングの受信レート）。
    /// 表示は英語の 1 行のみ。1 秒ごとにまとめて計測し直し、設定の showPerformanceStats で ON/OFF する。
    /// </summary>
    public class PerformanceBar
    {
        // 表示を更新する間隔（秒）
        private const float RefreshInterval = 1f;

        // 項目の区切り
        private const string Separator = "  |  ";

        private readonly AppSettings _settings;
        private readonly IFaceTrackingProvider _tracker;
        private readonly TrackerProcess _trackerProcess;
        private readonly SessionClient _sessionClient;
        private readonly ProcessCpuMeter _appCpu = new ProcessCpuMeter();
        private readonly ProcessCpuMeter _trackerCpu = new ProcessCpuMeter();
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private readonly StringBuilder _builder = new StringBuilder();

        // Layout で決めた表示状態（同じフレームの他のイベント中に設定が変わっても配置を食い違わせない）
        private bool _visible;

        // 計測中の期間（開始時刻・開始フレーム。-1 = 未開始）と、その間の GPU の処理時間の合計・回数
        private float _windowStart;
        private int _windowFrame = -1;
        private double _gpuTotal;
        private int _gpuSamples;
        private float _maxFrameMs;
        private int _previousGen0Collections;
        private long _sentWindowStart;
        private long _receivedWindowStart;

        private string _text = "Measuring...";

        // 次の Layout で表示へ反映する計測結果（Repaint 中に文字を変えると、折り返しの高さと描画が食い違う）
        private string _pendingText;

        public PerformanceBar(AppSettings settings, IFaceTrackingProvider tracker, TrackerProcess trackerProcess,
            SessionClient sessionClient)
        {
            _settings = settings;
            _tracker = tracker;
            _trackerProcess = trackerProcess;
            _sessionClient = sessionClient;
        }

        public void Draw()
        {
            // 表示するかは Layout のときだけ決める（OFF の間は計測もしない）
            if (Event.current.type == EventType.Layout)
            {
                bool visible = _settings.showPerformanceStats;
                if (visible && !_visible)
                {
                    // 表示し直したら古い値を出さず計測からやり直す
                    _windowFrame = -1;
                    _pendingText = null;
                    _text = "Measuring...";
                }
                else if (_pendingText != null)
                {
                    // 計測結果は Layout でだけ表示へ出す（このフレームの Repaint と同じ文字列にする）
                    _text = _pendingText;
                    _pendingText = null;
                }

                _visible = visible;
            }

            if (!_visible)
            {
                return;
            }

            // 1 フレームに 1 回だけ来る Repaint で計測する
            if (Event.current.type == EventType.Repaint)
            {
                Sample();
            }

            GUILayout.Label(_text, UiTheme.Current.Stats);
        }

        private void Sample()
        {
            // GPU の処理時間（取れない環境・グラフィックス API では 0 になるので数えない）
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0 && _timings[0].gpuFrameTime > 0.0)
            {
                _gpuTotal += _timings[0].gpuFrameTime;
                _gpuSamples++;
            }

            float now = Time.unscaledTime;
            _maxFrameMs = Mathf.Max(_maxFrameMs, Time.unscaledDeltaTime * 1000f);
            if (_windowFrame < 0)
            {
                // CPU 使用率は差分で求めるため、期間の始めに基準を取っておく
                _appCpu.SampleCurrent();
                SampleTrackerCpu();
                StartWindow(now);
                return;
            }

            // 期間が終わるまでは前回の表示のまま
            float elapsed = now - _windowStart;
            int frames = Time.frameCount - _windowFrame;
            if (elapsed < RefreshInterval || frames <= 0)
            {
                return;
            }

            _pendingText = Format(frames / elapsed, elapsed * 1000f / frames, elapsed);
            StartWindow(now);
        }

        private void StartWindow(float now)
        {
            // 次の期間の計測を始める
            _windowStart = now;
            _windowFrame = Time.frameCount;
            _gpuTotal = 0.0;
            _gpuSamples = 0;
            _maxFrameMs = 0f;
            if (_sessionClient != null)
            {
                _sentWindowStart = _sessionClient.BytesSent;
                _receivedWindowStart = _sessionClient.BytesReceived;
            }
        }

        private float SampleTrackerCpu()
        {
            // 同梱トラッカーを起動していなければ（iPhone などから受信中も）計測しない
            int processId = _trackerProcess.ProcessId;
            return processId != 0 ? _trackerCpu.Sample(processId) : -1f;
        }

        private string Format(float fps, float frameMs, float elapsed)
        {
            _builder.Clear();
            _builder.Append("FPS ").Append(fps.ToString("F0", Invariant))
                .Append(" (").Append(frameMs.ToString("F1", Invariant)).Append(" ms avg, ")
                .Append(_maxFrameMs.ToString("F1", Invariant)).Append(" ms max)");

            // VRCast 本体の CPU 使用率
            _builder.Append(Separator).Append("CPU ").Append(Percent(_appCpu.SampleCurrent()));
            // Approximate managed heap and Gen0 collection count. CollectionCount
            // is cumulative, so report the number during this sample window.
            int gen0 = GC.CollectionCount(0);
            _builder.Append(Separator).Append("Heap ")
                .Append((GC.GetTotalMemory(false) / (1024f * 1024f)).ToString("F0", Invariant))
                .Append(" MB, GC0 ").Append((gen0 - _previousGen0Collections).ToString(Invariant));
            _previousGen0Collections = gen0;

            // GPU はフレーム時間に占める処理時間の割合（このアプリの描画負荷の目安。カード全体の使用率ではない）
            _builder.Append(Separator).Append("GPU ");
            if (_gpuSamples > 0)
            {
                float gpuMs = (float)(_gpuTotal / _gpuSamples);
                float gpuPct = frameMs > 0.01f ? Mathf.Min(100f, gpuMs / frameMs * 100f) : 0f;
                _builder.Append(gpuPct.ToString("F0", Invariant)).Append("% (")
                    .Append(gpuMs.ToString("F1", Invariant)).Append(" ms)");
            }
            else
            {
                _builder.Append("-");
            }

            // アバターの描画解像度（高解像度描画時はウィンドウより大きい）
            CameraAntiAliasing.RenderSizeOf(_settings, out int renderWidth, out int renderHeight);
            float scale = CameraAntiAliasing.RenderScaleOf(_settings);
            _builder.Append(Separator).Append("Avatar ").Append(renderWidth.ToString(Invariant))
                .Append("x").Append(renderHeight.ToString(Invariant));
            if (scale > 1.001f)
            {
                _builder.Append(" (").Append((scale * 100f).ToString("F0", Invariant)).Append("%)");
            }

            // 同梱トラッカーを起動中ならその CPU 使用率
            if (_trackerProcess.IsRunning)
            {
                _builder.Append(Separator).Append("Tracker CPU ").Append(Percent(SampleTrackerCpu()));
            }

            // トラッキングの受信レート（受信していなければ -）
            int trackingFps = _tracker.FramesPerSecond;
            _builder.Append(Separator).Append("Tracking ")
                .Append(trackingFps > 0 ? trackingFps.ToString(Invariant) + " fps" : "-");
            if (_sessionClient != null && _sessionClient.IsConnected)
            {
                float sentKbps = (_sessionClient.BytesSent - _sentWindowStart) / (1024f * elapsed);
                float recvKbps = (_sessionClient.BytesReceived - _receivedWindowStart) / (1024f * elapsed);
                _builder.Append(Separator).Append("Net ")
                    .Append(sentKbps.ToString("F1", Invariant)).Append("/ ")
                    .Append(recvKbps.ToString("F1", Invariant)).Append(" KB/s send/recv");
            }
            return _builder.ToString();
        }

        private static string Percent(float usage)
        {
            // 計測できなかったときは -
            return usage >= 0f ? usage.ToString("F1", Invariant) + "%" : "-";
        }

        // 小数点がカンマになる言語の OS でも同じ表記にする
        private static CultureInfo Invariant => CultureInfo.InvariantCulture;
    }
}
