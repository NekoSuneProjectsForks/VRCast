using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// エクスポート用の複製アバターから、許可リスト外のコンポーネントと EditorOnly オブジェクトを除去する。
    /// </summary>
    public static class ComponentStripper
    {
        // EditorOnly タグ（ビルドに含めない慣例）
        private const string EditorOnlyTag = "EditorOnly";

        // RequireComponent の依存順で消せない場合に備えた最大試行回数
        private const int MaxPasses = 8;

        public struct Result
        {
            public int RemovedComponents;
            public int RemovedMissingScripts;
            public int RemovedEditorOnlyObjects;
        }

        public static Result Strip(GameObject root)
        {
            var result = new Result();

            // EditorOnly オブジェクトは子ごと削除
            result.RemovedEditorOnlyObjects = RemoveEditorOnlyObjects(root);

            // Missing Script を全階層から除去
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                result.RemovedMissingScripts += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }

            // 依存関係で一度に消せないものがあるため、変化が無くなるまで繰り返す
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                int removed = RemoveDisallowedOnce(root);
                result.RemovedComponents += removed;
                if (removed == 0)
                {
                    break;
                }
            }

            // ルートの Controller（VRChat の FX 等）は人体のポーズまで動かすため外す。
            // 小物の Animator の Controller は PropAnimationExporter が書き出せる形にして残す
            var rootAnimator = root.GetComponent<Animator>();
            if (rootAnimator != null)
            {
                rootAnimator.runtimeAnimatorController = null;
            }

            return result;
        }

        /// <summary>
        /// 非アクティブ（エディタでチェックを外した）オブジェクトを子ごと削除し、削除した数を返す（ルートは残す）。
        /// 非アクティブのものを書き出さない指定のときに使う。
        /// FX の初期状態で表示されるものも含め、書き出しには一切含めない。
        /// ただし残るメッシュがボーンとして使う Transform は消さず、コンポーネントだけを外して残す
        /// （消すとそのボーンに乗った頂点が原点へ伸びる。VRChat では非アクティブでもボーンとして動くため）。
        /// パーティクルは非アクティブのまま残す（アプリで ON にできるように。メッシュ等の他の表示物は外す）。
        /// </summary>
        public static int RemoveInactiveObjects(GameObject root)
        {
            return RemoveObjects(root, go => !go.activeSelf, true);
        }

        /// <summary>
        /// 非アクティブ（エディタでチェックを外した）オブジェクトを集める（ルートは除く）。
        /// 非アクティブのものも書き出す指定のときに使い、FX の焼き込みの後に <see cref="Deactivate"/> で OFF に戻す。
        /// </summary>
        public static List<GameObject> CollectInactiveObjects(GameObject root)
        {
            var inactive = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                // ルート自体は対象外
                if (t.gameObject != root && !t.gameObject.activeSelf)
                {
                    inactive.Add(t.gameObject);
                }
            }

            return inactive;
        }

        /// <summary>
        /// 集めておいたオブジェクトを非アクティブに戻し、書き出しに残った数を返す
        /// （FX の初期状態で表示されるものも OFF にそろえる。改変・最適化で消えたものは数えない）。
        /// </summary>
        public static int Deactivate(List<GameObject> objects)
        {
            int count = 0;
            foreach (GameObject go in objects)
            {
                // 改変・最適化で破棄されたものは飛ばす
                if (go == null)
                {
                    continue;
                }

                go.SetActive(false);
                count++;
            }

            return count;
        }

        /// <summary>
        /// 非アクティブなオブジェクトを消しても残すコンポーネントなら true（パーティクルの本体と描画）。
        /// </summary>
        private static bool IsKeptParticleComponent(Component component)
        {
            return component is ParticleSystem || component is ParticleSystemRenderer;
        }

        /// <summary>
        /// ボーンが見つからない（削除済み・アバター外を参照している）SkinnedMeshRenderer を警告し、その数を返す。
        /// </summary>
        public static int WarnMissingBones(GameObject root)
        {
            int count = 0;
            Transform rootTransform = root.transform;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // null とアバター外のボーンを数える（アバター外は Prefab 保存時に null になる）
                int missing = 0;
                foreach (Transform bone in renderer.bones)
                {
                    if (bone == null || !bone.IsChildOf(rootTransform))
                    {
                        missing++;
                    }
                }

                if (missing == 0)
                {
                    continue;
                }

                count++;
                ExportLog.Warn(
                    $"A mesh has {missing} missing bone(s); the parts weighted to them will stretch to the feet (origin) in the app.",
                    $"メッシュのボーンが {missing} 本見つかりません。そのボーンで動く部分は、アプリで足元（原点）へ伸びてしまいます。",
                    ReflectionUtility.PathOf(renderer.transform, rootTransform));
            }

            return count;
        }

        private static int RemoveEditorOnlyObjects(GameObject root)
        {
            return RemoveObjects(root, go => go.CompareTag(EditorOnlyTag), false);
        }

        private static int RemoveObjects(GameObject root, System.Predicate<GameObject> match, bool keepUsedBones)
        {
            // 列挙中に破棄しないよう先に対象を集める
            var targets = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                // ルート自体は消さない
                if (t.gameObject != root && match(t.gameObject))
                {
                    targets.Add(t.gameObject);
                }
            }

            // 残るメッシュが使うボーンとパーティクル、およびその親（指定時のみ）
            HashSet<Transform> usedBones = keepUsedBones ? CollectKeptNodes(root, targets) : new HashSet<Transform>();

            // 親が先に消えた子は null になるのでスキップ
            int count = 0;
            foreach (GameObject go in targets)
            {
                if (go == null)
                {
                    continue;
                }

                // 使用中のボーン・パーティクルを含むなら、それらだけを残す
                if (usedBones.Contains(go.transform))
                {
                    PruneKeepingBones(go.transform, usedBones);
                }
                else
                {
                    Object.DestroyImmediate(go);
                }

                count++;
            }

            return count;
        }

        private static HashSet<Transform> CollectKeptNodes(GameObject root, List<GameObject> removing)
        {
            var used = new HashSet<Transform>();
            Transform rootTransform = root.transform;

            // パーティクルは非アクティブでも残す（アクティブなものも、親が消えないよう登録）
            foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                AddWithAncestors(used, particle.transform, rootTransform);
            }

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // 一緒に消えるメッシュのボーンは不要
                if (IsUnderAny(renderer.transform, removing))
                {
                    continue;
                }

                // ボーンとルートボーンを、アバタールートまでの親ごと登録（親が消えると子も消えるため）
                foreach (Transform bone in renderer.bones)
                {
                    AddWithAncestors(used, bone, rootTransform);
                }

                AddWithAncestors(used, renderer.rootBone, rootTransform);
            }

            return used;
        }

        private static bool IsUnderAny(Transform transform, List<GameObject> objects)
        {
            // 消える対象（またはその子孫）なら true
            foreach (GameObject go in objects)
            {
                if (go != null && transform.IsChildOf(go.transform))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddWithAncestors(HashSet<Transform> set, Transform bone, Transform root)
        {
            // アバター外・未設定のボーンは対象外。登録済みの親に当たったら以降も登録済み
            if (bone == null || !bone.IsChildOf(root))
            {
                return;
            }

            Transform t = bone;
            while (t != null && t != root && set.Add(t))
            {
                t = t.parent;
            }
        }

        private static void PruneKeepingBones(Transform kept, HashSet<Transform> usedBones)
        {
            // ボーンに使われない子は子ごと削除（付け替えで並びが変わらないよう先に集める）
            var children = new List<Transform>();
            foreach (Transform child in kept)
            {
                children.Add(child);
            }

            foreach (Transform child in children)
            {
                if (usedBones.Contains(child))
                {
                    PruneKeepingBones(child, usedBones);
                }
                else
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }

            // 表示物・揺れもの等は書き出さないため Transform とパーティクル以外を外す（RequireComponent の依存順で繰り返す）
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                int removed = 0;
                foreach (Component component in kept.GetComponents<Component>())
                {
                    if (component != null && !(component is Transform) && !IsKeptParticleComponent(component)
                        && CanRemove(component))
                    {
                        Object.DestroyImmediate(component);
                        removed++;
                    }
                }

                if (removed == 0)
                {
                    break;
                }
            }

            // Missing Script も外す
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(kept.gameObject);
        }

        private static int RemoveDisallowedOnce(GameObject root)
        {
            int removed = 0;
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                // null（Missing Script）と許可済みはスキップ
                if (component == null || AllowedComponents.IsAllowed(component))
                {
                    continue;
                }

                // 他コンポーネントから RequireComponent されている場合は後のパスで消す
                if (!CanRemove(component))
                {
                    continue;
                }

                Object.DestroyImmediate(component);
                removed++;
            }

            return removed;
        }

        private static bool CanRemove(Component target)
        {
            // 同じ GameObject 上の他コンポーネントが target の型を要求していないか確認
            foreach (Component other in target.GetComponents<Component>())
            {
                if (other == null || other == target)
                {
                    continue;
                }

                // RequireComponent 属性を走査
                foreach (object attr in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    var require = (RequireComponent)attr;
                    if (IsRequired(require, target))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsRequired(RequireComponent require, Component target)
        {
            // 3 つの型指定のいずれかに target が該当すれば必須
            System.Type type = target.GetType();
            return (require.m_Type0 != null && require.m_Type0.IsAssignableFrom(type))
                || (require.m_Type1 != null && require.m_Type1.IsAssignableFrom(type))
                || (require.m_Type2 != null && require.m_Type2.IsAssignableFrom(type));
        }
    }
}
