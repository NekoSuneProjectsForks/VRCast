using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// まばたき。ランダム間隔の自動まばたき（AppSettings.autoBlink で ON/OFF）と、外部（トラッキング）からの左右別の閉じ具合。
    /// AppSettings.blinkPausedByExpression なら、ニュートラル以外の表情を出している間はどちらも止めて開いたままにする。
    /// 両目用の BlendShape には左右の小さい方（両目とも閉じている分）、片目用（ウインク）には左右の差分を上乗せし、二重に閉じないようにする。
    /// 使う BlendShape は、アバターごとの指定（BlinkShapeOverride）が有効ならそれを、無ければアバターのまぶた設定を使う。
    /// アバターごとに .anim（BlinkAnimation）を指定すると、自動まばたきはまぶたの BlendShape の代わりにその BlendShape のカーブで動かす。
    /// </summary>
    public class BlinkController : MonoBehaviour
    {
        // 表示する閉じ具合を目標へ寄せる速さ（1/秒）。閉じるときは速く、開くときはややゆっくり
        // （トラッキングの値は描画より粗い間隔で届くため、そのまま書くと段々に動く）
        private const float CloseSharpness = 60f;
        private const float OpenSharpness = 30f;

        // BlendShape が受け持つ目（アバターから見た左右）
        private enum EyeSide
        {
            Both,
            Left,
            Right,
        }

        private readonly List<BlendShapeOverlay> _overlays = new List<BlendShapeOverlay>();
        private readonly List<EyeSide> _sides = new List<EyeSide>();

        // 各上乗せの対象（メッシュと BlendShape の番号）と、直前に書いたまばたきの値（0〜100）
        private readonly List<(SkinnedMeshRenderer, int)> _targets = new List<(SkinnedMeshRenderer, int)>();
        private readonly List<float> _written = new List<float>();

        // 自動まばたきに使う .anim（null なら閉じる・閉じたまま・開くの設定で動かす）と、その書き込み先
        private BlinkAnimation _animation;
        private readonly List<AnimationOutput> _animationOutputs = new List<AnimationOutput>();

        // .anim の 1 つの BlendShape の書き込み先。まばたきの BlendShape と同じなら、その上乗せ（Shared の番号）を共用する
        private sealed class AnimationOutput
        {
            public SkinnedMeshRenderer Renderer;
            public int Index;
            public int Shared = -1;
            public BlendShapeOverlay Overlay;
            public readonly List<BlinkAnimation.Track> Tracks = new List<BlinkAnimation.Track>();
        }

        // 追加済みの BlendShape（パスと名前。同じ BlendShape に 2 つの上乗せを作らないため）
        private readonly HashSet<(string, string)> _added = new HashSet<(string, string)>();

        // 作り直しに使うアバターのルートとまぶた設定
        private Transform _root;
        private EyelidData _eyelids;
        private AppSettings _settings;
        private ExpressionController _expressions;
        private float _nextBlinkTime;
        private bool _hasBoth;

        // 外部入力（左右の閉じ具合 0〜1）と、その有無
        private bool _hasExternal;
        private float _externalLeft;
        private float _externalRight;

        // 今表示している左右の閉じ具合（0〜1）
        private float _shownLeft;
        private float _shownRight;

        // まばたき開始時刻（負なら非まばたき中）
        private float _blinkStart = -1f;

        // 自動まばたきを止めていたら true（再開時に予約し直し、止めている間に過ぎた予定ですぐ閉じないようにする）
        private bool _autoSuspended;

        public bool IsAvailable => _overlays.Count > 0;

        /// <summary>
        /// 自動まばたきに使っている .anim（無ければ null）。
        /// </summary>
        public BlinkAnimation Animation => _animation;

        /// <summary>
        /// .anim の BlendShape のうち、このアバターで見つかった数。
        /// </summary>
        public int AnimationTargetCount => _animationOutputs.Count;

        /// <summary>
        /// 片目だけ閉じられる（ウインク用 BlendShape がある）か。
        /// </summary>
        public bool HasWink { get; private set; }

        /// <summary>
        /// true の間はトラッキング・自動とも止めて目を開いたままにする（パーフェクトシンク化の編集中など）。
        /// </summary>
        public bool Suspended { get; set; }

        /// <summary>
        /// expressions は表情中にまばたきを止める設定のために見る（無ければ止めない）。
        /// custom はアバターごとのまばたきの BlendShape の指定（無効・null ならまぶた設定を使う）。
        /// </summary>
        public void Initialize(Transform root, EyelidData data, BlinkShapeOverride custom, AppSettings settings,
            ExpressionController expressions)
        {
            _root = root;
            _eyelids = data;
            _settings = settings;
            _expressions = expressions;
            Build(custom);
            ScheduleNext();
        }

        /// <summary>
        /// まばたきの BlendShape の指定が変わったときに作り直す（今の上乗せは元の値へ戻してから）。
        /// </summary>
        public void Rebuild(BlinkShapeOverride custom)
        {
            // 未初期化なら何もしない
            if (_root == null)
            {
                return;
            }

            Write(0f, 0f);
            Build(custom);
            ResolveAnimation();
        }

        /// <summary>
        /// 自動まばたきに使う .anim を替える（null で閉じる・閉じたまま・開くの設定に戻す）。
        /// </summary>
        public void SetAnimation(BlinkAnimation animation)
        {
            _animation = animation;
            _blinkStart = -1f;
            if (_root != null)
            {
                ResolveAnimation();
            }
        }

        private void ResolveAnimation()
        {
            // 前の書き込み先を元の値へ戻してから作り直す
            foreach (AnimationOutput output in _animationOutputs)
            {
                output.Overlay?.Write(0f);
            }

            _animationOutputs.Clear();
            if (_animation == null)
            {
                return;
            }

            foreach (BlinkAnimation.Track track in _animation.Tracks)
            {
                // パスで見つからなければ、同じ名前の BlendShape を持つメッシュから探す（書き出しで階層が変わった場合）
                if (!BlendShapeOverlay.TryFind(_root, track.Path, track.BlendShape, out SkinnedMeshRenderer renderer,
                        out int index) && !TryFindByName(track, out renderer, out index))
                {
                    continue;
                }

                // 同じ BlendShape を動かすカーブは 1 つの書き込み先にまとめる
                AnimationOutput output = _animationOutputs.Find(o => o.Renderer == renderer && o.Index == index);
                if (output == null)
                {
                    output = new AnimationOutput { Renderer = renderer, Index = index };
                    output.Shared = _targets.IndexOf((renderer, index));
                    if (output.Shared < 0)
                    {
                        output.Overlay = BlendShapeOverlay.Create(renderer, index);
                    }

                    _animationOutputs.Add(output);
                }

                output.Tracks.Add(track);
            }
        }

        private bool TryFindByName(BlinkAnimation.Track track, out SkinnedMeshRenderer renderer, out int index)
        {
            // パスの最後の名前と同じメッシュを優先し、無ければ最初に見つかったもの
            renderer = null;
            index = -1;
            string meshName = track.Path.Substring(track.Path.LastIndexOf('/') + 1);
            foreach (SkinnedMeshRenderer candidate in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int found = candidate.sharedMesh != null ? candidate.sharedMesh.GetBlendShapeIndex(track.BlendShape) : -1;
                if (found < 0)
                {
                    continue;
                }

                if (renderer == null || candidate.name == meshName && renderer.name != meshName)
                {
                    renderer = candidate;
                    index = found;
                }
            }

            return renderer != null;
        }

        /// <summary>
        /// アバターのまぶた設定（自動）を指定の形で返す（指定を始めるときの初期値に使う）。
        /// </summary>
        public BlinkShapeOverride DescribeAutomatic()
        {
            var automatic = new BlinkShapeOverride();
            if (_eyelids == null)
            {
                return automatic;
            }

            // 両目用は片目用と別名の最初のもの、片目用は左右そろっているときだけ
            foreach (string shape in _eyelids.blinkBlendShapes)
            {
                if (shape != _eyelids.winkLeftBlendShape && shape != _eyelids.winkRightBlendShape)
                {
                    automatic.both.Set(_eyelids.meshPath, shape);
                    break;
                }
            }

            if (!string.IsNullOrEmpty(_eyelids.winkLeftBlendShape) && !string.IsNullOrEmpty(_eyelids.winkRightBlendShape))
            {
                automatic.left.Set(_eyelids.meshPath, _eyelids.winkLeftBlendShape);
                automatic.right.Set(_eyelids.meshPath, _eyelids.winkRightBlendShape);
            }

            return automatic;
        }

        private void Build(BlinkShapeOverride custom)
        {
            // 前の対象を捨てる
            _overlays.Clear();
            _sides.Clear();
            _added.Clear();
            _targets.Clear();
            _written.Clear();

            // 指定が有効ならそれを、無ければまぶた設定を使う
            if (custom != null && custom.enabled)
            {
                BuildCustom(custom);
            }
            else if (_eyelids != null)
            {
                BuildFromEyelids(_eyelids);
            }

            // 左右とも片目用があればウインク可能
            _hasBoth = _sides.Contains(EyeSide.Both);
            HasWink = _sides.Contains(EyeSide.Left) && _sides.Contains(EyeSide.Right);
        }

        private void BuildFromEyelids(EyelidData data)
        {
            // まばたき用 BlendShape（ウインクと同名なら片目用として扱う）
            foreach (string shape in data.blinkBlendShapes)
            {
                EyeSide side = shape == data.winkLeftBlendShape ? EyeSide.Left
                    : shape == data.winkRightBlendShape ? EyeSide.Right
                    : EyeSide.Both;
                Add(data.meshPath, shape, side);
            }

            // ウインク用 BlendShape（まばたき用と別名のときだけ追加される。左右揃った場合のみ）
            bool winkPair = !string.IsNullOrEmpty(data.winkLeftBlendShape) && !string.IsNullOrEmpty(data.winkRightBlendShape);
            if (winkPair)
            {
                Add(data.meshPath, data.winkLeftBlendShape, EyeSide.Left);
                Add(data.meshPath, data.winkRightBlendShape, EyeSide.Right);
            }
        }

        private void BuildCustom(BlinkShapeOverride custom)
        {
            // 両目用（未指定なら片目用の左右だけで閉じる）
            if (custom.both.IsSet)
            {
                Add(custom.both.path, custom.both.blendShape, EyeSide.Both);
            }

            // 片目用は左右そろったときだけ（片方だけでは両目のまばたきで片目しか閉じない）
            if (custom.HasWinkPair)
            {
                Add(custom.left.path, custom.left.blendShape, EyeSide.Left);
                Add(custom.right.path, custom.right.blendShape, EyeSide.Right);
            }
        }

        /// <summary>
        /// 外部入力（アバターから見た左右の閉じ具合 0〜1）を設定する。設定中は自動まばたきより優先する。
        /// </summary>
        public void SetExternal(float left, float right)
        {
            _hasExternal = true;
            _externalLeft = Mathf.Clamp01(left);
            _externalRight = Mathf.Clamp01(right);
        }

        /// <summary>
        /// 外部入力を解除し、自動まばたきへ戻す。
        /// </summary>
        public void ClearExternal()
        {
            _hasExternal = false;
        }

        /// <summary>
        /// 自動まばたきを今すぐ 1 回行う（動きの設定を試す用。自動まばたきが止まっている間は何もしない）。
        /// </summary>
        public void BlinkNow()
        {
            if (_blinkStart < 0f)
            {
                _nextBlinkTime = Time.time;
            }
        }

        private void Add(string meshPath, string shape, EyeSide side)
        {
            // 同じ BlendShape に 2 つの上乗せを作らない（互いの書き込みを元の値と誤認して閉じたままになる）
            if (!_added.Add((meshPath ?? string.Empty, shape)))
            {
                return;
            }

            // 見つかった BlendShape だけを対象にする
            if (BlendShapeOverlay.TryFind(_root, meshPath, shape, out SkinnedMeshRenderer renderer, out int index))
            {
                _overlays.Add(BlendShapeOverlay.Create(renderer, index));
                _sides.Add(side);
                _targets.Add((renderer, index));
                _written.Add(0f);
            }
        }

        private void LateUpdate()
        {
            // 対象が無ければ何もしない
            if (!IsAvailable && _animationOutputs.Count == 0)
            {
                return;
            }

            // 一時停止中・表情中に止める設定なら、トラッキング・自動とも開いたまま（表情の目の形をそのまま見せる）
            if (Suspended || IsPausedByExpression)
            {
                SuspendAuto();
                Show(0f, 0f);
                WriteAnimation(-1f);
                return;
            }

            // 外部入力がある間はその値を使い、自動まばたきは止める
            if (_hasExternal)
            {
                SuspendAuto();
                Show(_externalLeft, _externalRight);
                WriteAnimation(-1f);
                return;
            }

            // OFF の間は開いたまま（元の値）
            if (!_settings.autoBlink)
            {
                SuspendAuto();
                Show(0f, 0f);
                WriteAnimation(-1f);
                return;
            }

            // 止めていた後は、再開した時点から次のまばたきを予約する
            if (_autoSuspended)
            {
                _autoSuspended = false;
                ScheduleNext();
            }

            // 予定時刻になったらまばたき開始
            float now = Time.time;
            if (_blinkStart < 0f && now >= _nextBlinkTime)
            {
                _blinkStart = now;
            }

            // まばたき中なら経過時間から閉じ具合を求める（.anim があればまぶたの BlendShape は開いたままにして .anim を再生する）
            bool animated = _animation != null && _animationOutputs.Count > 0;
            float closed = 0f;
            float animationElapsed = -1f;
            if (_blinkStart >= 0f)
            {
                float elapsed = now - _blinkStart;
                float close = _settings.blinkCloseSeconds;
                float hold = _settings.blinkHoldSeconds;
                float open = _settings.blinkOpenSeconds;
                if (animated)
                {
                    animationElapsed = elapsed;
                }
                else
                {
                    closed = Evaluate(elapsed, close, hold, open);
                }

                // 終わったら次を予約
                if (elapsed >= (animated ? _animation.Duration(close, hold, open) : close + hold + open))
                {
                    _blinkStart = -1f;
                    animationElapsed = -1f;
                    ScheduleNext();
                }
            }

            Show(closed, closed);
            WriteAnimation(animationElapsed);
        }

        private void WriteAnimation(float elapsed)
        {
            // elapsed が負なら .anim の BlendShape を元の値へ戻す（まぶたと共用のものはまぶたの値のまま）
            foreach (AnimationOutput output in _animationOutputs)
            {
                float value = 0f;
                if (elapsed >= 0f)
                {
                    foreach (BlinkAnimation.Track track in output.Tracks)
                    {
                        value = Mathf.Max(value, _animation.Sample(track, elapsed, _settings.blinkCloseSeconds,
                            _settings.blinkHoldSeconds, _settings.blinkOpenSeconds));
                    }
                }

                if (output.Shared < 0)
                {
                    output.Overlay.Write(value);
                }
                else if (elapsed >= 0f)
                {
                    _overlays[output.Shared].Write(Mathf.Max(value, _written[output.Shared]));
                }
            }
        }

        private void Show(float left, float right)
        {
            // 目標へなめらかに寄せてから書く（モードの切り替わりでも跳ねない）
            _shownLeft = Approach(_shownLeft, left);
            _shownRight = Approach(_shownRight, right);
            Write(_shownLeft, _shownRight);
        }

        private static float Approach(float current, float target)
        {
            float sharpness = target > current ? CloseSharpness : OpenSharpness;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-sharpness * Time.deltaTime));
        }

        // ニュートラル以外の表情を出していて、その間は止める設定なら true
        private bool IsPausedByExpression =>
            _settings.blinkPausedByExpression && _expressions != null && _expressions.Current >= 0;

        private void SuspendAuto()
        {
            // 途中のまばたきをやめ、再開時に予約し直す印を付ける
            _blinkStart = -1f;
            _autoSuspended = true;
        }

        private void Write(float left, float right)
        {
            // 両目とも閉じている分は両目用、残りは片目用（両目用が無ければ片目用だけで閉じる）
            float both = Mathf.Min(left, right);
            float leftOnly = _hasBoth ? left - both : left;
            float rightOnly = _hasBoth ? right - both : right;

            for (int i = 0; i < _overlays.Count; i++)
            {
                // 受け持つ目に応じた値を 0〜100 で上乗せ
                float value = _sides[i] == EyeSide.Both ? both : _sides[i] == EyeSide.Left ? leftOnly : rightOnly;
                _written[i] = value * 100f;
                _overlays[i].Write(_written[i]);
            }
        }

        /// <summary>
        /// まばたき開始からの経過秒での閉じ具合（0〜1）。close・hold・open は閉じる・閉じたまま・開くの時間（秒）。
        /// </summary>
        public static float Evaluate(float elapsed, float close, float hold, float open)
        {
            // 閉じる途中（加速して閉じきる）
            if (elapsed < close)
            {
                float t = elapsed / close;
                return t * t;
            }

            // 閉じたまま
            if (elapsed < close + hold)
            {
                return 1f;
            }

            // 開く途中（ゆっくり開き始めて減速しながら開ききる）
            float opened = open > 0f ? Mathf.Clamp01((elapsed - close - hold) / open) : 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, opened);
        }

        private void ScheduleNext()
        {
            // 次のまばたきをランダムな間隔で予約
            _nextBlinkTime = Time.time + Random.Range(_settings.blinkIntervalMin, _settings.blinkIntervalMax);
        }
    }
}
