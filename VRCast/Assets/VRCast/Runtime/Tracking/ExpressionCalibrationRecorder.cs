using System.Collections.Generic;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 表情のキャリブレーションの記録係。「〇〇の顔をしてください」の準備時間のあと、受け取った ARKit 互換の値を
    /// 一定時間平均して、表情ごとに設定へ書き込む。複数の表情を順に記録できる（真顔を含める場合は先頭に置く）。
    /// 顔の画像は扱わず、トラッカーが送る数値だけを使う。
    /// </summary>
    public sealed class ExpressionCalibrationRecorder
    {
        // 表情を作るまでの準備時間と、記録する時間（秒。記録は顔を受け取っている間だけ進む）
        public const float PrepareSeconds = 3f;
        public const float RecordSeconds = 2f;

        // 記録に必要な最少のフレーム数（足りなければ記録を続ける）
        private const int MinFrames = 10;

        /// <summary>
        /// 進み具合。
        /// </summary>
        public enum Phase
        {
            Idle,
            Prepare,
            Record,
        }

        /// <summary>
        /// 最後に終わった記録の結果。
        /// </summary>
        public enum Outcome
        {
            None,
            Completed,
            Cancelled,
            SourceChanged,
            NotDistinct,
        }

        private readonly Queue<FaceExpression> _queue = new Queue<FaceExpression>();
        private readonly float[] _sum = new float[ArKitFace.BlendShapeNames.Length];
        private readonly List<FaceExpression> _notDistinct = new List<FaceExpression>();
        private int _frames;
        private float _timer;
        private int _lastTickFrame = -1;

        public Phase Current { get; private set; } = Phase.Idle;

        /// <summary>
        /// 今記録している表情（Neutral = 真顔）。
        /// </summary>
        public FaceExpression Target { get; private set; }

        /// <summary>
        /// 記録している入力元。
        /// </summary>
        public TrackingSource Source { get; private set; }

        public bool IsRunning => Current != Phase.Idle;

        /// <summary>
        /// 準備中は残り秒数、記録中は残りの記録秒数。
        /// </summary>
        public float Remaining => Current == Phase.Prepare ? _timer : Current == Phase.Record ? RecordSeconds - _timer : 0f;

        /// <summary>
        /// 今の表情の後に残っている表情の数。
        /// </summary>
        public int QueuedCount => _queue.Count;

        /// <summary>
        /// 最後に終わった記録の結果と、真顔と区別できず登録しなかった表情。
        /// </summary>
        public Outcome LastOutcome { get; private set; }

        public IReadOnlyList<FaceExpression> NotDistinct => _notDistinct;

        /// <summary>
        /// 顔を受け取っていないため記録が止まっているなら true（表示用）。
        /// </summary>
        public bool IsWaitingForFace { get; private set; }

        /// <summary>
        /// expressions を順に記録し始める（記録中なら最初からやり直す）。
        /// </summary>
        public void Start(IEnumerable<FaceExpression> expressions, TrackingSource source)
        {
            _queue.Clear();
            foreach (FaceExpression expression in expressions)
            {
                _queue.Enqueue(expression);
            }

            Source = source;
            LastOutcome = Outcome.None;
            _notDistinct.Clear();
            Next();
        }

        /// <summary>
        /// 記録をやめる（記録済みの表情は残す）。
        /// </summary>
        public void Cancel()
        {
            Finish(Outcome.Cancelled);
        }

        /// <summary>
        /// 毎フレーム呼ぶ（同じフレームで二度呼んでも一度だけ進む）。記録が終わった表情は samples へ書き込み、
        /// 書き込んだら true を返す（呼び出し側で設定を保存する）。
        /// </summary>
        public bool Tick(IFaceTrackingProvider provider, List<ExpressionCalibrationSample> samples, float deltaTime,
            int frame)
        {
            if (!IsRunning || frame == _lastTickFrame)
            {
                return false;
            }

            _lastTickFrame = frame;

            // 入力元が変わったら値の出方が違うので中止する
            if (provider == null || provider.FaceSource != Source)
            {
                Finish(Outcome.SourceChanged);
                return false;
            }

            bool received = provider.TryGetFrame(out FaceTrackingFrame face)
                && face.BlendShapes != null && face.BlendShapes.Length == _sum.Length;
            IsWaitingForFace = !received;

            // 準備中は時間だけ進める
            if (Current == Phase.Prepare)
            {
                _timer -= deltaTime;
                if (_timer <= 0f)
                {
                    Current = Phase.Record;
                    _timer = 0f;
                    System.Array.Clear(_sum, 0, _sum.Length);
                    _frames = 0;
                }

                return false;
            }

            // 記録中は顔を受け取ったフレームだけ足し、受け取っている間だけ時間を進める
            if (!received)
            {
                return false;
            }

            for (int i = 0; i < _sum.Length; i++)
            {
                _sum[i] += UnityEngine.Mathf.Clamp01(face.BlendShapes[i]);
            }

            _frames++;
            _timer += deltaTime;
            if (_timer < RecordSeconds || _frames < MinFrames)
            {
                return false;
            }

            bool stored = Store(samples);
            Next();
            return stored;
        }

        private bool Store(List<ExpressionCalibrationSample> samples)
        {
            var average = new float[_sum.Length];
            for (int i = 0; i < average.Length; i++)
            {
                average[i] = _sum[i] / _frames;
            }

            // 表情は真顔と区別できるときだけ登録する（区別できない記録は判定を乱すため）
            if (Target != FaceExpression.Neutral)
            {
                ExpressionCalibrationSample neutral = ExpressionCalibration.Find(samples, Source, FaceExpression.Neutral);
                if (neutral != null && !ExpressionCalibration.IsDistinct(neutral.values, average))
                {
                    _notDistinct.Add(Target);
                    return false;
                }
            }

            ExpressionCalibration.Set(samples, Source, Target, average);
            return true;
        }

        private void Next()
        {
            // 次の表情の準備へ。残りが無ければ終わり
            if (_queue.Count == 0)
            {
                Finish(_notDistinct.Count > 0 ? Outcome.NotDistinct : Outcome.Completed);
                return;
            }

            Target = _queue.Dequeue();
            Current = Phase.Prepare;
            _timer = PrepareSeconds;
            IsWaitingForFace = false;
        }

        private void Finish(Outcome outcome)
        {
            // 走っていなければ結果を変えない
            if (!IsRunning)
            {
                return;
            }

            _queue.Clear();
            Current = Phase.Idle;
            IsWaitingForFace = false;
            LastOutcome = outcome;
        }
    }
}
