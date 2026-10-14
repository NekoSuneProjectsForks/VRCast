using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRCast.AvatarFormat;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// 小物のアニメーションを、VRChat と同じく自動で再生されるように書き出す。
    /// 1. 小物に付いた Animator（アバタールート以外）はコントローラーを残す。VRChat 固有の StateMachineBehaviour は
    ///    スクリプトなので、コントローラーを書き出し用の一時フォルダに複製して外す。
    /// 2. FX の初期状態で再生され続けるクリップのうち、人体のボーン以外の Transform を時間で動かすカーブだけを
    ///    legacy クリップにして、アバタールートの Animation に入れる（アプリが同時に再生する）。
    /// </summary>
    public static class PropAnimationExporter
    {
        // 複製したコントローラー・作ったクリップのファイル名の接頭辞
        private const string ControllerPrefix = "prop_controller_";
        private const string OverridePrefix = "prop_override_";
        private const string ClipPrefix = "fx_motion_";

        // BlendTree の重みがこれ以上のクリップだけを再生中とみなす（表示 ON/OFF の焼き込みと同じ境目）
        private const float MinWeight = 0.5f;

        // 速さ 0 とみなす値（止まったままのクリップは動かないので取り込まない）
        private const float MinSpeed = 1e-4f;

        // 値が変わったとみなす差（これ以下なら動かないカーブ）
        private const float MotionEpsilon = 1e-5f;

        // オイラー角のカーブを回転（四元数）へ変換するときの 1 秒あたりの標本数
        private const float EulerSampleRate = 60f;

        // Transform のプロパティ名の接頭辞（カーブをまとまりごとに扱う）
        private const string PositionGroup = "m_LocalPosition";
        private const string RotationGroup = "m_LocalRotation";
        private const string ScaleGroup = "m_LocalScale";
        private const string EulerRawGroup = "localEulerAnglesRaw";
        private const string EulerGroup = "localEulerAngles";
        private const string EulerBakedGroup = "localEulerAnglesBaked";

        private static readonly string[] Axes = { "x", "y", "z" };
        private static readonly string[] QuaternionAxes = { "x", "y", "z", "w" };

        public struct Result
        {
            // コントローラーを残した小物の Animator の数と、残せずに外した数
            public int Animators;
            public int DroppedAnimators;

            // FX から取り込んだ小物の動き（クリップ）の数
            public int FxMotions;
        }

        /// <summary>
        /// 小物の Animator のコントローラーを書き出せる形にする（アバタールートの Animator は対象外）。
        /// folder は書き出しの一時フォルダ（複製したコントローラーを置く）。
        /// </summary>
        public static void PrepareAnimators(GameObject root, string folder, ref Result result)
        {
            var sanitized = new Dictionary<RuntimeAnimatorController, RuntimeAnimatorController>();
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                if (animator.gameObject == root || controller == null)
                {
                    continue;
                }

                // 同じコントローラーは 1 回だけ複製する
                if (!sanitized.TryGetValue(controller, out RuntimeAnimatorController copy))
                {
                    copy = Sanitize(controller, folder, sanitized.Count);
                    sanitized[controller] = copy;
                }

                animator.runtimeAnimatorController = copy;
                if (copy != null)
                {
                    result.Animators++;
                }
                else
                {
                    result.DroppedAnimators++;
                    ExportLog.Warn(
                        "An animator controller could not be exported (not saved as an asset); its animation will not play.",
                        "Animator Controller を書き出せませんでした（アセットとして保存されていません）。そのアニメーションは再生されません。",
                        ReflectionUtility.PathOf(animator.transform, root.transform));
                }
            }
        }

        /// <summary>
        /// FX の初期状態で再生され続ける小物の動きを legacy クリップにしてアバタールートの Animation に入れる。
        /// movedObjects は書き出し中に付け替えたオブジェクト（元のパス → 移動後）で、古いパスのカーブを読み替える。
        /// </summary>
        public static void AddFxMotions(
            GameObject root,
            AnimatorController fx,
            IReadOnlyDictionary<string, float> parameterDefaults,
            IReadOnlyDictionary<string, Transform> movedObjects,
            string folder,
            ref Result result)
        {
            if (fx == null)
            {
                return;
            }

            // 人体のボーン（トラッキング・待機ポーズが動かす）とルート（アバター全体が動く）は取り込まない
            var excluded = new HashSet<Transform> { root.transform };
            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                    if (bone != null)
                    {
                        excluded.Add(bone);
                    }
                }
            }

            var clips = new List<AnimationClip>();
            foreach (FxDefaultStateBaker.DefaultMotion motion in FxDefaultStateBaker.CollectDefaultMotions(
                         fx, parameterDefaults))
            {
                float speed = Mathf.Abs(motion.Speed);
                if (motion.Clip == null || motion.Weight < MinWeight || speed < MinSpeed)
                {
                    continue;
                }

                AnimationClip clip = BuildLegacyClip(root.transform, motion.Clip, speed, excluded, movedObjects);
                if (clip == null)
                {
                    continue;
                }

                // 名前はレイヤー順に並ぶよう番号を付ける（Animation の中で一意にする）
                clip.name = $"{AvatarPackageLayout.FxMotionClipPrefix}{clips.Count:D3}_{motion.Clip.name}";
                AssetDatabase.CreateAsset(clip, $"{folder}/{ClipPrefix}{clips.Count}.anim");
                clips.Add(clip);
            }

            if (clips.Count == 0)
            {
                return;
            }

            // アバタールートの Animation に足す（自動再生はしない。アプリが全部を同時に再生する）
            Animation animation = root.GetComponent<Animation>();
            if (animation == null)
            {
                animation = root.AddComponent<Animation>();
                animation.playAutomatically = false;
            }

            animation.cullingType = AnimationCullingType.AlwaysAnimate;
            var all = new List<AnimationClip>(AnimationUtility.GetAnimationClips(root));
            all.AddRange(clips);
            AnimationUtility.SetAnimationClips(animation, all.ToArray());
            result.FxMotions = clips.Count;
        }

        private static RuntimeAnimatorController Sanitize(RuntimeAnimatorController controller, string folder, int number)
        {
            // 書き出しには保存済みのアセットが要る（Prefab から参照できない）
            if (!AssetDatabase.Contains(controller))
            {
                return null;
            }

            if (controller is AnimatorController animatorController)
            {
                return SanitizeController(animatorController, folder, number);
            }

            if (controller is AnimatorOverrideController overrideController)
            {
                return SanitizeOverride(overrideController, folder, number);
            }

            return null;
        }

        private static RuntimeAnimatorController SanitizeController(AnimatorController controller, string folder, int number)
        {
            // スクリプトが無ければそのまま使う
            if (!HasBehaviours(controller))
            {
                return controller;
            }

            // 1 つのファイルに複数のアセットが入ったもの（生成ツールの出力等）は複製できない。
            // スクリプトはアプリに無いので動かず（中身の無い参照になる）、そのまま使っても害は無い
            if (!AssetDatabase.IsMainAsset(controller))
            {
                return controller;
            }

            string path = $"{folder}/{ControllerPrefix}{number}.controller";
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(controller), path))
            {
                return controller;
            }

            var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (copy == null)
            {
                return controller;
            }

            foreach (AnimatorControllerLayer layer in copy.layers)
            {
                RemoveBehaviours(layer.stateMachine);
            }

            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssetIfDirty(copy);
            return copy;
        }

        private static RuntimeAnimatorController SanitizeOverride(
            AnimatorOverrideController controller, string folder, int number)
        {
            // 元のコントローラーを整え、変わらなければそのまま使う
            RuntimeAnimatorController baseController = controller.runtimeAnimatorController;
            RuntimeAnimatorController sanitizedBase = baseController != null
                ? Sanitize(baseController, folder, number)
                : null;
            if (sanitizedBase == null)
            {
                return null;
            }

            if (sanitizedBase == baseController)
            {
                return controller;
            }

            // 整えた元のコントローラーに同じ差し替えを当てた複製を作る
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            controller.GetOverrides(overrides);
            var copy = new AnimatorOverrideController(sanitizedBase) { name = controller.name };
            copy.ApplyOverrides(overrides);
            AssetDatabase.CreateAsset(copy, $"{folder}/{OverridePrefix}{number}.overrideController");
            return copy;
        }

        private static bool HasBehaviours(AnimatorController controller)
        {
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                if (HasBehaviours(layer.stateMachine))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasBehaviours(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null)
            {
                return false;
            }

            if (stateMachine.behaviours.Length > 0)
            {
                return true;
            }

            foreach (ChildAnimatorState child in stateMachine.states)
            {
                if (child.state != null && child.state.behaviours.Length > 0)
                {
                    return true;
                }
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                if (HasBehaviours(child.stateMachine))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveBehaviours(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null)
            {
                return;
            }

            // 参照を外し、複製のファイルからも消す（書き出しに残さない）
            DestroyAll(stateMachine.behaviours);
            stateMachine.behaviours = new StateMachineBehaviour[0];
            foreach (ChildAnimatorState child in stateMachine.states)
            {
                if (child.state != null)
                {
                    DestroyAll(child.state.behaviours);
                    child.state.behaviours = new StateMachineBehaviour[0];
                }
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                RemoveBehaviours(child.stateMachine);
            }
        }

        private static void DestroyAll(StateMachineBehaviour[] behaviours)
        {
            foreach (StateMachineBehaviour behaviour in behaviours)
            {
                if (behaviour != null)
                {
                    Object.DestroyImmediate(behaviour, true);
                }
            }
        }

        /// <summary>
        /// source のうち、取り込む Transform を時間で動かすカーブだけの legacy クリップを作る（無ければ null）。
        /// 位置・回転・大きさはまとまりのどれか 1 つでも動いていれば、そのまとまりのカーブをすべて入れる。
        /// </summary>
        internal static AnimationClip BuildLegacyClip(
            Transform root,
            AnimationClip source,
            float speed,
            HashSet<Transform> excluded,
            IReadOnlyDictionary<string, Transform> movedObjects)
        {
            // (対象, まとまり) ごとのカーブ（プロパティ名の末尾の軸 → カーブ）と、動くかどうか
            var groups = new Dictionary<(Transform target, string group), Dictionary<string, AnimationCurve>>();
            var moving = new HashSet<(Transform, string)>();
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                if (binding.type != typeof(Transform) || !TrySplitProperty(binding.propertyName, out string group,
                        out string axis))
                {
                    continue;
                }

                var target = FxDefaultStateBaker.ResolveAnimatedObject(root.gameObject, binding, movedObjects) as Transform;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
                if (target == null || curve == null || excluded.Contains(target))
                {
                    continue;
                }

                var key = (target, group);
                if (!groups.TryGetValue(key, out Dictionary<string, AnimationCurve> curves))
                {
                    curves = new Dictionary<string, AnimationCurve>();
                    groups[key] = curves;
                }

                curves[axis] = curve;
                if (IsMoving(curve))
                {
                    moving.Add(key);
                }
            }

            if (moving.Count == 0)
            {
                return null;
            }

            bool loop = AnimationUtility.GetAnimationClipSettings(source).loopTime;
            var clip = new AnimationClip
            {
                legacy = true,
                frameRate = source.frameRate,
                wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever,
            };

            foreach ((Transform target, string group) in moving)
            {
                string path = ReflectionUtility.PathOf(target, root);
                Dictionary<string, AnimationCurve> curves = groups[(target, group)];
                switch (group)
                {
                    case PositionGroup:
                    case ScaleGroup:
                        foreach (KeyValuePair<string, AnimationCurve> pair in curves)
                        {
                            SetCurve(clip, path, $"{group}.{pair.Key}", Scale(pair.Value, speed));
                        }

                        break;
                    case RotationGroup:
                        // 四元数は 4 成分そろって 1 つの回転になる（欠けた成分は今の値で埋める）
                        Quaternion current = target.localRotation;
                        for (int i = 0; i < QuaternionAxes.Length; i++)
                        {
                            string axis = QuaternionAxes[i];
                            AnimationCurve curve = curves.TryGetValue(axis, out AnimationCurve found)
                                ? Scale(found, speed)
                                : AnimationCurve.Constant(0f, 0f, current[i]);
                            SetCurve(clip, path, $"{RotationGroup}.{axis}", curve);
                        }

                        break;
                    default:
                        // オイラー角は四元数へ変換して入れる（legacy クリップで確実に再生されるように）
                        AddEulerAsQuaternion(clip, path, curves, target.localEulerAngles, source.length, speed);
                        break;
                }
            }

            return clip;
        }

        private static bool TrySplitProperty(string propertyName, out string group, out string axis)
        {
            group = null;
            axis = null;
            int dot = propertyName.LastIndexOf('.');
            if (dot <= 0)
            {
                return false;
            }

            group = propertyName.Substring(0, dot);
            axis = propertyName.Substring(dot + 1);
            switch (group)
            {
                case PositionGroup:
                case ScaleGroup:
                case EulerRawGroup:
                case EulerGroup:
                case EulerBakedGroup:
                    return System.Array.IndexOf(Axes, axis) >= 0;
                case RotationGroup:
                    return System.Array.IndexOf(QuaternionAxes, axis) >= 0;
                default:
                    return false;
            }
        }

        /// <summary>
        /// カーブの値が時間で変わるなら true（キーの値が違うか、平らでない接線がある）。
        /// </summary>
        internal static bool IsMoving(AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            if (keys.Length < 2)
            {
                return false;
            }

            float first = keys[0].value;
            foreach (Keyframe key in keys)
            {
                if (Mathf.Abs(key.value - first) > MotionEpsilon || IsSloped(key.inTangent) || IsSloped(key.outTangent))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSloped(float tangent)
        {
            // 無限大の接線は段差（値を保つ）なので動きではない
            return !float.IsInfinity(tangent) && !float.IsNaN(tangent) && Mathf.Abs(tangent) > MotionEpsilon;
        }

        private static AnimationCurve Scale(AnimationCurve curve, float speed)
        {
            // 速さ 1 ならそのまま、それ以外は時間を縮め、傾きをその分だけ急にする
            if (Mathf.Approximately(speed, 1f))
            {
                return curve;
            }

            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].time /= speed;
                keys[i].inTangent *= speed;
                keys[i].outTangent *= speed;
            }

            return new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        }

        private static void AddEulerAsQuaternion(
            AnimationClip clip, string path, Dictionary<string, AnimationCurve> curves, Vector3 current, float length,
            float speed)
        {
            // 元の時間で標本を取り、速さで縮めた時間に置く（隣と逆向きの四元数にならないよう符号をそろえる）
            int samples = Mathf.Max(2, Mathf.CeilToInt(length * EulerSampleRate) + 1);
            var keys = new Keyframe[4][];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = new Keyframe[samples];
            }

            Quaternion previous = Quaternion.identity;
            for (int s = 0; s < samples; s++)
            {
                float time = length * s / (samples - 1);
                var euler = new Vector3(
                    Evaluate(curves, "x", time, current.x),
                    Evaluate(curves, "y", time, current.y),
                    Evaluate(curves, "z", time, current.z));
                Quaternion rotation = Quaternion.Euler(euler);
                if (s > 0 && Quaternion.Dot(previous, rotation) < 0f)
                {
                    rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                }

                previous = rotation;
                for (int i = 0; i < 4; i++)
                {
                    keys[i][s] = new Keyframe(time / speed, rotation[i]);
                }
            }

            for (int i = 0; i < 4; i++)
            {
                var curve = new AnimationCurve(keys[i]);
                for (int k = 0; k < curve.length; k++)
                {
                    curve.SmoothTangents(k, 0f);
                }

                SetCurve(clip, path, $"{RotationGroup}.{QuaternionAxes[i]}", curve);
            }
        }

        private static float Evaluate(Dictionary<string, AnimationCurve> curves, string axis, float time, float fallback)
        {
            return curves.TryGetValue(axis, out AnimationCurve curve) ? curve.Evaluate(time) : fallback;
        }

        private static void SetCurve(AnimationClip clip, string path, string propertyName, AnimationCurve curve)
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName), curve);
        }
    }
}
