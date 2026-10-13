using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// 小物のアニメーションを VRChat と同じく自動で再生する（設定 propAnimationsEnabled で ON/OFF）。
    /// 対象は、小物に付いた Animator（コントローラー付き、アバタールート以外）と Animation（自動再生のもの）、
    /// アバタールートの Animation に入った FX の初期状態の動き（<see cref="AvatarPackageLayout.FxMotionClipPrefix"/> の
    /// クリップ。FX のレイヤーと同じく全部を同時に、後のレイヤーを優先して再生）。
    /// OFF にすると最初の姿勢へ戻して止める。
    /// </summary>
    public class PropAnimationPlayer : MonoBehaviour
    {
        private readonly List<Animator> _animators = new List<Animator>();
        private readonly List<Animation> _animations = new List<Animation>();
        private readonly List<AnimationState> _fxStates = new List<AnimationState>();
        private Animation _fxAnimation;
        private AppSettings _settings;
        private bool _playing;

        // 小物の Animator・Animation の数と、FX から取り込んだ動きの数
        public int AnimatorCount => _animators.Count + _animations.Count;

        public int FxMotionCount => _fxStates.Count;

        public bool IsAvailable => AnimatorCount > 0 || FxMotionCount > 0;

        public void Initialize(Transform root, AppSettings settings)
        {
            _settings = settings;

            // 小物の Animator（ルートの Animator は人体のボーン用でコントローラーを持たない。
            // VRChat と同じく、コンポーネントを無効にしてあるものは動かさない）
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator.transform != root && animator.enabled && animator.runtimeAnimatorController != null)
                {
                    _animators.Add(animator);
                }
            }

            // 小物の Animation（VRChat と同じく自動再生のものだけ）
            foreach (Animation animation in root.GetComponentsInChildren<Animation>(true))
            {
                if (animation.transform != root && animation.enabled && animation.playAutomatically
                    && animation.clip != null)
                {
                    _animations.Add(animation);
                }
            }

            // FX の初期状態の動き。名前の番号順 = FX のレイヤー順に、後のものほど上のレイヤーにして同時に再生する
            _fxAnimation = root.GetComponent<Animation>();
            if (_fxAnimation != null)
            {
                foreach (AnimationState state in _fxAnimation)
                {
                    if (state.clip != null && state.name.StartsWith(AvatarPackageLayout.FxMotionClipPrefix,
                            System.StringComparison.Ordinal))
                    {
                        _fxStates.Add(state);
                    }
                }

                _fxStates.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                for (int i = 0; i < _fxStates.Count; i++)
                {
                    AnimationState state = _fxStates[i];
                    state.layer = i;
                    state.weight = 1f;
                    state.wrapMode = state.clip.wrapMode == WrapMode.Default ? WrapMode.Loop : state.clip.wrapMode;
                }

                _fxAnimation.cullingType = AnimationCullingType.AlwaysAnimate;
            }

            if (IsAvailable)
            {
                VRCastLog.Info("PropAnimation",
                    $"Prop animators: {_animators.Count}, animations: {_animations.Count}, FX motions: {_fxStates.Count}.");
            }

            // 読み込んだ時点で小物の Animator は動き始めているので、FX の動きもそろえて始める（OFF なら止める）
            SetPlaying(settings.propAnimationsEnabled);
        }

        private void Update()
        {
            if (_settings != null && _settings.propAnimationsEnabled != _playing)
            {
                SetPlaying(_settings.propAnimationsEnabled);
            }
        }

        private void SetPlaying(bool play)
        {
            _playing = play;
            foreach (Animator animator in _animators)
            {
                if (animator == null)
                {
                    continue;
                }

                if (play)
                {
                    animator.enabled = true;
                    continue;
                }

                // 初期のステートの最初の姿勢へ戻してから止める（非表示のものは評価できないので止めるだけ）
                if (animator.isActiveAndEnabled)
                {
                    animator.Rebind();
                    animator.Update(0f);
                }

                animator.enabled = false;
            }

            foreach (Animation animation in _animations)
            {
                if (animation == null)
                {
                    continue;
                }

                // 止めている間に表示されても自動再生しないよう、コンポーネントごと止める
                if (play)
                {
                    animation.enabled = true;
                    if (animation.isActiveAndEnabled)
                    {
                        animation.Play();
                    }
                }
                else
                {
                    if (animation.isActiveAndEnabled)
                    {
                        StopAtStart(animation);
                    }

                    animation.enabled = false;
                }
            }

            if (_fxStates.Count == 0)
            {
                return;
            }

            foreach (AnimationState state in _fxStates)
            {
                state.time = 0f;
                state.enabled = true;
            }

            if (!play)
            {
                _fxAnimation.Sample();
                foreach (AnimationState state in _fxStates)
                {
                    state.enabled = false;
                }
            }
        }

        private static void StopAtStart(Animation animation)
        {
            // 再生中のものを最初の姿勢にしてから止める
            foreach (AnimationState state in animation)
            {
                if (state.enabled)
                {
                    state.time = 0f;
                }
            }

            animation.Sample();
            animation.Stop();
        }
    }
}
