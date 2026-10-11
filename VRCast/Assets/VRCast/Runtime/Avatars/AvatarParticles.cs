using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Avatars
{
    /// <summary>
    /// アバターに含まれるパーティクル（ParticleSystem）を見つけ、まとまりごとに表示を ON/OFF する。
    /// まとまりは、VRChat の FX で表示を ON/OFF しているオブジェクトのうちパーティクルに最も近いもの。
    /// FX で切り替えないパーティクルは、ボーンを含まずパーティクルだけを描く親（エフェクトのフォルダー）までをまとめる。
    /// OFF は放出を止めて消し、描画もやめる（同じオブジェクトのメッシュ等は隠さない）。
    /// ON は FX の初期状態などで非表示のオブジェクトも表示してから再生する。
    /// </summary>
    public class AvatarParticles : MonoBehaviour
    {
        // 利用者が付ける名前の長さの上限
        public const int MaxNameLength = 64;

        /// <summary>
        /// ON/OFF の単位になるパーティクルのまとまり。
        /// </summary>
        public sealed class Group
        {
            // アバターのルートからの相対パスと、Unity 上のオブジェクト名
            public string Path;
            public string NodeName;

            // 利用者が付けた名前（空なら Unity 上の名前を表示する）
            public string CustomName = string.Empty;
            public bool Enabled;
            internal readonly List<ParticleSystem> Systems = new List<ParticleSystem>();
            internal readonly List<ParticleSystemRenderer> Renderers = new List<ParticleSystemRenderer>();

            /// <summary>
            /// 画面に出す名前（付けた名前があればそれ、無ければ Unity 上の名前）。
            /// </summary>
            public string Label => string.IsNullOrEmpty(CustomName) ? NodeName : CustomName;

            /// <summary>
            /// まとめたパーティクルの数。
            /// </summary>
            public int SystemCount => Systems.Count;
        }

        private readonly List<Group> _groups = new List<Group>();
        private Transform _root;

        /// <summary>
        /// 見つかったパーティクルのまとまり（階層順）。
        /// </summary>
        public IReadOnlyList<Group> Groups => _groups;

        /// <summary>
        /// ON にしているまとまりの数。
        /// </summary>
        public int EnabledCount
        {
            get
            {
                // 画面の描画ごとに呼ばれるので一覧を作らずに数える
                int count = 0;
                foreach (Group group in _groups)
                {
                    count += group.Enabled ? 1 : 0;
                }

                return count;
            }
        }

        /// <summary>
        /// アバター内のパーティクルをまとまりに分け、記録済みの ON/OFF と名前を当てる（記録の無いものは読込時の表示状態のまま）。
        /// fxGroupPaths は FX で表示を ON/OFF しているオブジェクトのパス（.vrcaster の metadata。無ければ空）。
        /// </summary>
        public void Initialize(Transform root, IReadOnlyCollection<string> fxGroupPaths, List<ParticleToggle> toggles)
        {
            _root = root;
            HashSet<Transform> fxNodes = ResolveNodes(root, fxGroupPaths);
            HashSet<Transform> bones = CollectBones(root);

            // まとまりの単位のオブジェクトごとにパーティクルを集める（同じ単位のものは 1 つにまとめる）
            var byNode = new Dictionary<Transform, Group>();
            var folderCache = new Dictionary<Transform, bool>();
            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                Transform node = FindGroupNode(system.transform, fxNodes, bones, folderCache);
                if (!byNode.TryGetValue(node, out Group group))
                {
                    group = new Group { Path = TransformPath.Of(node, root), NodeName = node.name };
                    byNode.Add(node, group);
                    _groups.Add(group);
                }

                group.Systems.Add(system);
                ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    group.Renderers.Add(renderer);
                }
            }

            // 読込時に表示されていて描画も有効なものが 1 つでもあれば ON とする
            foreach (Group group in _groups)
            {
                group.Enabled = IsShowing(group);
            }

            // 記録済みの ON/OFF と名前を当てる（見つからないまとまりは無視）
            foreach (ParticleToggle toggle in toggles)
            {
                Group group = _groups.Find(g => g.Path == toggle.path);
                if (group != null)
                {
                    SetName(group, toggle.name);
                    SetEnabled(group, toggle.enabled, true);
                }
            }
        }

        /// <summary>
        /// まとまりの表示を ON/OFF する。
        /// </summary>
        public void SetEnabled(Group group, bool enabled)
        {
            SetEnabled(group, enabled, false);
        }

        /// <summary>
        /// 全てのまとまりを ON/OFF する。
        /// </summary>
        public void SetAll(bool enabled)
        {
            foreach (Group group in _groups)
            {
                SetEnabled(group, enabled, false);
            }
        }

        /// <summary>
        /// まとまりに名前を付ける（前後の空白を除き、長すぎる分は切る。空なら Unity 上の名前に戻す）。
        /// </summary>
        public static void SetName(Group group, string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            group.CustomName = trimmed.Length > MaxNameLength ? trimmed.Substring(0, MaxNameLength) : trimmed;
        }

        /// <summary>
        /// 保存用に、全てのまとまりの ON/OFF と名前を書き出す。
        /// </summary>
        public List<ParticleToggle> Export()
        {
            return _groups.ConvertAll(group => new ParticleToggle
            {
                path = group.Path,
                enabled = group.Enabled,
                name = group.CustomName,
            });
        }

        private void SetEnabled(Group group, bool enabled, bool force)
        {
            // 変わらないときは何もしない（読込時の記録は状態をそろえるため必ず当てる）
            if (!force && group.Enabled == enabled)
            {
                return;
            }

            group.Enabled = enabled;
            foreach (ParticleSystem system in group.Systems)
            {
                // 破棄済みのものは飛ばす
                if (system == null)
                {
                    continue;
                }

                if (enabled)
                {
                    // 非表示のオブジェクト（自身と親）を表示し、最初から再生する（子は各自で再生する）
                    Activate(system.transform);
                    system.Play(false);
                }
                else
                {
                    // 放出を止めて出ている粒も消す（オブジェクトは隠さない）
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            // 描画をそろえる
            foreach (ParticleSystemRenderer renderer in group.Renderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = enabled;
                }
            }
        }

        private void Activate(Transform target)
        {
            // アバタールートまでの非表示のオブジェクトを表示する
            for (Transform node = target; node != null && node != _root; node = node.parent)
            {
                if (!node.gameObject.activeSelf)
                {
                    node.gameObject.SetActive(true);
                }
            }
        }

        private Transform FindGroupNode(
            Transform particle, HashSet<Transform> fxNodes, HashSet<Transform> bones, Dictionary<Transform, bool> folderCache)
        {
            // FX で切り替えるオブジェクトの中なら、パーティクルに最も近いものを単位にする
            for (Transform node = particle; node != null && node != _root; node = node.parent)
            {
                if (fxNodes.Contains(node))
                {
                    return node;
                }
            }

            // それ以外は、ボーンでもルートでもないエフェクトのフォルダーまで親をたどる
            Transform current = particle;
            while (current.parent != null && current.parent != _root && !bones.Contains(current.parent)
                && IsEffectFolder(current.parent, fxNodes, bones, folderCache))
            {
                current = current.parent;
            }

            return current;
        }

        private static bool IsEffectFolder(
            Transform node, HashSet<Transform> fxNodes, HashSet<Transform> bones, Dictionary<Transform, bool> folderCache)
        {
            // 同じ親を何度も調べないよう結果を覚える
            if (folderCache.TryGetValue(node, out bool cached))
            {
                return cached;
            }

            // ボーン・FX の切り替え単位・パーティクル以外の描画を含まないものだけをエフェクトのフォルダーとみなす
            bool result = true;
            foreach (Transform child in node.GetComponentsInChildren<Transform>(true))
            {
                Renderer renderer = child.GetComponent<Renderer>();
                if (bones.Contains(child) || fxNodes.Contains(child)
                    || (renderer != null && !(renderer is ParticleSystemRenderer)))
                {
                    result = false;
                    break;
                }
            }

            folderCache.Add(node, result);
            return result;
        }

        private static HashSet<Transform> ResolveNodes(Transform root, IReadOnlyCollection<string> paths)
        {
            // パスからオブジェクトを探す（ルート自身・見つからないものは除く）
            var nodes = new HashSet<Transform>();
            if (paths == null)
            {
                return nodes;
            }

            foreach (string path in paths)
            {
                Transform node = string.IsNullOrEmpty(path) ? null : root.Find(path);
                if (node != null)
                {
                    nodes.Add(node);
                }
            }

            return nodes;
        }

        private static HashSet<Transform> CollectBones(Transform root)
        {
            // メッシュが使うボーンと Humanoid のボーンを集める（エフェクトのフォルダーをたどる上限にする）
            var bones = new HashSet<Transform>();
            foreach (SkinnedMeshRenderer skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Transform bone in skinned.bones)
                {
                    if (bone != null)
                    {
                        bones.Add(bone);
                    }
                }

                if (skinned.rootBone != null)
                {
                    bones.Add(skinned.rootBone);
                }
            }

            Animator animator = root.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++)
                {
                    Transform boneTransform = animator.GetBoneTransform(bone);
                    if (boneTransform != null)
                    {
                        bones.Add(boneTransform);
                    }
                }
            }

            return bones;
        }

        private static bool IsShowing(Group group)
        {
            // 表示中で描画も有効な（描画の無いものは表示中扱い）パーティクルが 1 つでもあれば表示中
            foreach (ParticleSystem system in group.Systems)
            {
                if (system == null || !system.gameObject.activeInHierarchy)
                {
                    continue;
                }

                ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.enabled)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
