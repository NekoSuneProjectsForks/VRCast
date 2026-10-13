using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// 一定時間ごとの自動リセット（設定 autoReset*、既定 OFF）。
    /// 顔の向き・視線は起動時と同じく正面付近で静止したところで取り直し（FaceTrackingDriver.RequestRecalibration）、
    /// 表情はニュートラルへ戻す（手動の固定も外す）。パネルを隠していても動く。
    /// </summary>
    public class AutoResetScheduler : MonoBehaviour
    {
        private AvatarComponentCache _avatar;
        private AppSettings _settings;

        // 次に行う時刻（Time.unscaledTime。OFF の間は負）
        private float _nextAt = -1f;

        /// <summary>
        /// 次の自動リセットまでの秒数（OFF なら負）。
        /// </summary>
        public float SecondsUntilNext => _nextAt < 0f ? -1f : Mathf.Max(0f, _nextAt - Time.unscaledTime);

        /// <summary>
        /// 顔の向き・視線の取り直しが、正面付近で静止するのを待っていれば true。
        /// </summary>
        public bool IsWaitingForFront
        {
            get
            {
                // アバターが替わっていればコンポーネントを取り直す
                _avatar?.Refresh();
                var face = _avatar?.Get<FaceTrackingDriver>();
                return face != null && face.RecalibrationPending;
            }
        }

        public void Initialize(AvatarSession session, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _settings = settings;
        }

        /// <summary>
        /// 次の自動リセットを今から 1 間隔後にする（ON にしたとき・間隔を変えたとき）。
        /// </summary>
        public void Restart()
        {
            _nextAt = _settings != null && _settings.autoResetEnabled ? Time.unscaledTime + IntervalSeconds : -1f;
        }

        private float IntervalSeconds => _settings.autoResetIntervalMinutes * 60f;

        private void Update()
        {
            // 未初期化なら何もしない
            if (_settings == null)
            {
                return;
            }

            // OFF の間は予定を消し、ON になったら今から 1 間隔後に予約する
            if (!_settings.autoResetEnabled)
            {
                _nextAt = -1f;
                return;
            }

            float now = Time.unscaledTime;
            if (_nextAt < 0f)
            {
                _nextAt = now + IntervalSeconds;
                return;
            }

            // 間隔を短くしたら、残りが新しい間隔を超えないよう詰める（設定プリセット・全設定のリセットを含む）
            _nextAt = Mathf.Min(_nextAt, now + IntervalSeconds);

            // 時刻が来たら行い、次を予約する
            if (now >= _nextAt)
            {
                Run();
                _nextAt = now + IntervalSeconds;
            }
        }

        private void Run()
        {
            // アバターが替わっていればコンポーネントを取り直す
            _avatar.Refresh();

            // 顔の向き・視線は、正面付近で静止したときに取り直すよう頼む（トラッキングしていなければ何もしない）
            var face = _avatar.Get<FaceTrackingDriver>();
            if (face != null && face.IsTracking && (_settings.autoResetHead || _settings.autoResetGaze))
            {
                face.RequestRecalibration(_settings.autoResetHead, _settings.autoResetGaze);
            }

            // 表情は出ているときだけニュートラルへ戻す（手動の固定も外す）
            var expressions = _avatar.Get<ExpressionController>();
            if (_settings.autoResetExpression && expressions != null
                && (expressions.Current >= 0 || expressions.IsManual))
            {
                expressions.ResetToNeutral();
            }

            VRCastLog.Detail("AutoReset", $"Auto reset (head {_settings.autoResetHead}, gaze {_settings.autoResetGaze}, " +
                $"expression {_settings.autoResetExpression})");
        }
    }
}
