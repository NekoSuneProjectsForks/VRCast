using System;
using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// トラッカーから不定期に届く点の列を、描画の毎フレームなめらかに追従させる。
    /// 点ごとに速度を持つバネ（SmoothDamp）で最新の値へ寄せるため、届く間隔が揺れても動きが止まったり跳ねたりしない。
    /// 間隔に合わせる指定があれば、値が実際に変わる間隔を測り、寄る秒数をそれに合わせて短くする
    /// （毎フレーム届くなら遅れを小さく、間引いて届くなら段差が見えないよう長めにする）。
    /// </summary>
    public sealed class PointSmoother
    {
        // 値が変わる間隔の平均へ、新しい間隔を混ぜる割合（揺れに振り回されないように）
        private const float IntervalBlend = 0.2f;

        // これより長く空いた間隔は測らない（見失い・停止の間を間隔とみなさない）
        private const float MaxMeasuredInterval = 0.5f;

        private readonly Vector3[] _target;
        private readonly Vector3[] _current;
        private readonly Vector3[] _velocity;
        private readonly float _minSmoothTime;
        private readonly float _maxSmoothTime;
        private readonly float _intervalShare;
        private float _updatedAt;

        // 値が最後に変わった時刻と、変わる間隔の平均（0 = まだ測れていない）
        private float _changedAt;
        private float _interval;

        /// <param name="count">点の数</param>
        /// <param name="smoothTime">最新の値へ寄る目安の秒数（大きいほどなめらかで遅れる）</param>
        public PointSmoother(int count, float smoothTime)
            : this(count, smoothTime, smoothTime, 0f)
        {
        }

        /// <param name="count">点の数</param>
        /// <param name="minSmoothTime">寄る秒数の下限</param>
        /// <param name="maxSmoothTime">寄る秒数の上限（間隔が測れるまではこの値）</param>
        /// <param name="intervalShare">値が変わる間隔に対する寄る秒数の割合（0 なら間隔に合わせず maxSmoothTime）</param>
        public PointSmoother(int count, float minSmoothTime, float maxSmoothTime, float intervalShare)
        {
            _target = new Vector3[count];
            _current = new Vector3[count];
            _velocity = new Vector3[count];
            _minSmoothTime = Mathf.Min(minSmoothTime, maxSmoothTime);
            _maxSmoothTime = maxSmoothTime;
            _intervalShare = intervalShare;
        }

        /// <summary>
        /// 現在の表示位置（Update で更新される）。
        /// </summary>
        public Vector3[] Current => _current;

        /// <summary>
        /// 値を受け取っていれば true（Reset で false）。
        /// </summary>
        public bool HasValue { get; private set; }

        /// <summary>
        /// 今の寄る秒数。
        /// </summary>
        public float SmoothTime => _intervalShare > 0f && _interval > 0f
            ? Mathf.Clamp(_interval * _intervalShare, _minSmoothTime, _maxSmoothTime)
            : _maxSmoothTime;

        /// <summary>
        /// 新しく届いた値を目標にする（最初の値はそのまま表示位置にする）。now は秒。
        /// 前と同じ値（トラッカーが推定を間引いたフレーム）は間隔の測定に数えない。
        /// </summary>
        public void Push(Vector3[] points, float now)
        {
            int count = Mathf.Min(points.Length, _target.Length);

            // 最初の値（見失った後を含む）は追従させずに合わせ、止まった状態から始める
            if (!HasValue)
            {
                Array.Copy(points, _target, count);
                Array.Copy(points, _current, count);
                Array.Clear(_velocity, 0, _velocity.Length);
                _updatedAt = now;
                _changedAt = now;
                HasValue = true;
                return;
            }

            if (_intervalShare > 0f && Changed(points, count))
            {
                MeasureInterval(now);
            }

            Array.Copy(points, _target, count);
        }

        private bool Changed(Vector3[] points, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (points[i] != _target[i])
                {
                    return true;
                }
            }

            return false;
        }

        private void MeasureInterval(float now)
        {
            float interval = now - _changedAt;
            _changedAt = now;

            // 同じ描画フレーム内・長く止まっていた後の間隔は使わない
            if (interval <= 0f || interval > MaxMeasuredInterval)
            {
                return;
            }

            _interval = _interval > 0f ? Mathf.Lerp(_interval, interval, IntervalBlend) : interval;
        }

        /// <summary>
        /// 表示位置を now（秒）の時点まで目標へ寄せる。
        /// </summary>
        public void Update(float now)
        {
            if (!HasValue)
            {
                return;
            }

            // 前回からの経過時間（同じ時刻・逆行なら動かさない）
            float deltaTime = now - _updatedAt;
            _updatedAt = now;
            if (deltaTime <= 0f)
            {
                return;
            }

            float smoothTime = SmoothTime;
            for (int i = 0; i < _current.Length; i++)
            {
                _current[i] = Vector3.SmoothDamp(
                    _current[i], _target[i], ref _velocity[i], smoothTime, Mathf.Infinity, deltaTime);
            }
        }

        /// <summary>
        /// 見失ったときに呼ぶ（次の値は追従させずに合わせる）。
        /// </summary>
        public void Reset()
        {
            HasValue = false;
        }
    }
}
