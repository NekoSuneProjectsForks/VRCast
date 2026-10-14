using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// Descriptor の Lip Sync で口を動かさず、FX のアニメーションで口を動かすアバター（Viseme Parameter Only 等）向けに、
    /// VRChat 組み込みの Viseme / Voice パラメーターを使うレイヤーから Viseme ごとの BlendShape の組を読む。
    /// 各 Viseme の値でパラメーターを置き換えて初期状態を辿り（<see cref="FxDefaultStateBaker.CollectDefaultMotions"/>）、
    /// 無音（Viseme 0・Voice 0）より大きくなる BlendShape だけを、その Viseme の口の形として書き出す。
    /// </summary>
    public static class FxLipSyncReader
    {
        // VRChat が口パクのために送る組み込みパラメーター（Viseme は 0〜14 の整数、Voice は 0〜1 の音量）
        internal const string VisemeParameter = "Viseme";
        internal const string VoiceParameter = "Voice";

        private const string BlendShapePrefix = "blendShape.";

        // 無音との差がこれ未満の BlendShape は口の形に含めない（0〜100）
        private const float MinDifference = 0.5f;

        /// <summary>
        /// Viseme / Voice を使うレイヤーが無い、または口の形が読めなければ mode = none を返す。
        /// overrides は Expression Parameters の既定値、movedObjects は付け替え前のパスで書かれたカーブの読み替えに使う。
        /// </summary>
        public static LipSyncData Read(
            GameObject root,
            AnimatorController fx,
            IReadOnlyDictionary<string, float> overrides,
            IReadOnlyDictionary<string, Transform> movedObjects = null)
        {
            var data = new LipSyncData();
            if (fx == null)
            {
                return data;
            }

            HashSet<int> layers = FindLipSyncLayers(fx);
            if (layers.Count == 0)
            {
                return data;
            }

            // 無音の口の形を基準に、各 Viseme で大きくなる BlendShape を集める（sil は無音そのものなので空）
            Dictionary<(string, string), float> silence = Sample(root, fx, overrides, movedObjects, layers, 0, 0f);
            var poses = new VisemePose[AvatarDescriptorData.VisemeCount];
            poses[0] = new VisemePose();
            bool found = false;
            for (int viseme = 1; viseme < poses.Length; viseme++)
            {
                Dictionary<(string, string), float> pose = Sample(root, fx, overrides, movedObjects, layers, viseme, 1f);
                var values = new List<BlendShapeValue>();
                foreach (KeyValuePair<(string path, string blendShape), float> pair in pose)
                {
                    silence.TryGetValue(pair.Key, out float baseWeight);
                    if (pair.Value - baseWeight < MinDifference || values.Count >= VisemePose.MaxValues)
                    {
                        continue;
                    }

                    values.Add(new BlendShapeValue
                    {
                        path = pair.Key.path,
                        blendShape = pair.Key.blendShape,
                        weight = Mathf.Clamp(pair.Value, 0f, 100f),
                    });
                }

                poses[viseme] = new VisemePose { values = values.ToArray() };
                found |= values.Count > 0;
            }

            if (found)
            {
                data.mode = LipSyncData.ModeVisemeAnimation;
                data.visemePoses = poses;
            }

            return data;
        }

        /// <summary>
        /// 遷移の条件・BlendTree・ステートの速さや再生位置のどこかで Viseme / Voice を使うレイヤーの番号を返す。
        /// </summary>
        internal static HashSet<int> FindLipSyncLayers(AnimatorController fx)
        {
            var result = new HashSet<int>();
            AnimatorControllerLayer[] layers = fx.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].stateMachine != null && UsesLipSyncParameter(layers[i].stateMachine))
                {
                    result.Add(i);
                }
            }

            return result;
        }

        private static bool UsesLipSyncParameter(AnimatorStateMachine stateMachine)
        {
            if (UsesLipSyncParameter(stateMachine.anyStateTransitions) || UsesLipSyncParameter(stateMachine.entryTransitions))
            {
                return true;
            }

            foreach (ChildAnimatorState child in stateMachine.states)
            {
                AnimatorState state = child.state;
                if (state == null)
                {
                    continue;
                }

                if (UsesLipSyncParameter(state.transitions)
                    || (state.timeParameterActive && IsLipSyncParameter(state.timeParameter))
                    || (state.speedParameterActive && IsLipSyncParameter(state.speedParameter))
                    || UsesLipSyncParameter(state.motion))
                {
                    return true;
                }
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                if (child.stateMachine != null && UsesLipSyncParameter(child.stateMachine))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool UsesLipSyncParameter(AnimatorTransitionBase[] transitions)
        {
            foreach (AnimatorTransitionBase transition in transitions)
            {
                if (transition == null || transition.mute)
                {
                    continue;
                }

                foreach (AnimatorCondition condition in transition.conditions)
                {
                    if (IsLipSyncParameter(condition.parameter))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool UsesLipSyncParameter(Motion motion)
        {
            if (!(motion is BlendTree tree))
            {
                return false;
            }

            if (IsLipSyncParameter(tree.blendParameter)
                || (tree.blendType != BlendTreeType.Simple1D && IsLipSyncParameter(tree.blendParameterY)))
            {
                return true;
            }

            foreach (ChildMotion child in tree.children)
            {
                if ((tree.blendType == BlendTreeType.Direct && IsLipSyncParameter(child.directBlendParameter))
                    || UsesLipSyncParameter(child.motion))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLipSyncParameter(string name)
        {
            return name == VisemeParameter || name == VoiceParameter;
        }

        private static Dictionary<(string, string), float> Sample(
            GameObject root,
            AnimatorController fx,
            IReadOnlyDictionary<string, float> overrides,
            IReadOnlyDictionary<string, Transform> movedObjects,
            HashSet<int> layers,
            int viseme,
            float voice)
        {
            // 口パクのパラメーターだけを置き換えて初期状態を辿る
            var values = new Dictionary<string, float>();
            foreach (KeyValuePair<string, float> pair in overrides)
            {
                values[pair.Key] = pair.Value;
            }

            values[VisemeParameter] = viseme;
            values[VoiceParameter] = voice;

            // レイヤーごとに BlendTree の重みで混ぜ、後のレイヤーで上書きする（Animator と同じ優先順）
            var result = new Dictionary<(string, string), float>();
            var layerSums = new Dictionary<(string, string), (float sum, float weight)>();
            int currentLayer = -1;
            foreach (FxDefaultStateBaker.DefaultMotion motion in FxDefaultStateBaker.CollectDefaultMotions(fx, values))
            {
                if (!layers.Contains(motion.Layer))
                {
                    continue;
                }

                if (motion.Layer != currentLayer)
                {
                    Flush(layerSums, result);
                    currentLayer = motion.Layer;
                }

                AddClip(root, motion, movedObjects, layerSums);
            }

            Flush(layerSums, result);
            return result;
        }

        private static void AddClip(
            GameObject root,
            FxDefaultStateBaker.DefaultMotion motion,
            IReadOnlyDictionary<string, Transform> movedObjects,
            Dictionary<(string, string), (float sum, float weight)> layerSums)
        {
            float time = motion.NormalizedTime * motion.Clip.length;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(motion.Clip))
            {
                // SkinnedMeshRenderer の BlendShape で、メッシュに実在するものだけ
                if (!binding.propertyName.StartsWith(BlendShapePrefix, System.StringComparison.Ordinal)
                    || !(FxDefaultStateBaker.ResolveAnimatedObject(root, binding, movedObjects) is SkinnedMeshRenderer renderer)
                    || renderer.sharedMesh == null)
                {
                    continue;
                }

                string blendShape = binding.propertyName.Substring(BlendShapePrefix.Length);
                AnimationCurve curve = AnimationUtility.GetEditorCurve(motion.Clip, binding);
                if (curve == null || renderer.sharedMesh.GetBlendShapeIndex(blendShape) < 0)
                {
                    continue;
                }

                var key = (AnimationUtility.CalculateTransformPath(renderer.transform, root.transform), blendShape);
                layerSums.TryGetValue(key, out (float sum, float weight) sample);
                layerSums[key] = (sample.sum + motion.Weight * curve.Evaluate(time), sample.weight + motion.Weight);
            }
        }

        private static void Flush(
            Dictionary<(string, string), (float sum, float weight)> layerSums,
            Dictionary<(string, string), float> result)
        {
            // 重みの合計が 1 未満なら足りない分は 0（口を閉じた形）とみなす
            foreach (KeyValuePair<(string, string), (float sum, float weight)> pair in layerSums)
            {
                result[pair.Key] = pair.Value.weight >= 1f ? pair.Value.sum / pair.Value.weight : pair.Value.sum;
            }

            layerSums.Clear();
        }
    }
}
