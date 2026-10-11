using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Avatars
{
    /// <summary>
    /// アバターに含まれるパーティクル（ParticleSystem）を見つけ、まとまりごとに表示を ON/OFF する。
    /// まとまりは親にパーティクルを持たない ParticleSystem と、その子孫のパーティクル（子のエフェクト・サブエミッター）。
    /// OFF は放出を止めて消し、描画もやめる（同じオブジェクトのメッシュ等は隠さない）。
    /// ON は FX の初期状態などで非表示のオブジェクトも表示してから再生する。
    /// </summary>
    public class AvatarParticles : MonoBehaviour
    {
        /// <summary>
        /// ON/OFF の単位になるパーティクルのまとまり。
        /// </summary>
        public sealed class Group
        {
            public string Path;
            public string Label;
            public bool Enabled;
            internal ParticleSystem Root;
            internal ParticleSystemRenderer[] Renderers;
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
        /// アバター内のパーティクルを列挙し、記録済みの ON/OFF を当てる（記録の無いものは読込時の表示状態のまま）。
        /// </summary>
        public void Initialize(Transform root, List<ParticleToggle> toggles)
        {
            _root = root;
            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                // 親側にパーティクルがあるものは、そのまとまりの一部として一緒に切り替える
                if (HasParticleAncestor(system.transform))
                {
                    continue;
                }

                // 読込時に表示されていて描画も有効なものを ON とする
                var renderers = system.GetComponentsInChildren<ParticleSystemRenderer>(true);
                _groups.Add(new Group
                {
                    Path = TransformPath.Of(system.transform, root),
                    Label = system.name,
                    Enabled = system.gameObject.activeInHierarchy && AnyEnabled(renderers),
                    Root = system,
                    Renderers = renderers,
                });
            }

            // 記録済みの ON/OFF を当てる（見つからないパーティクルは無視）
            foreach (ParticleToggle toggle in toggles)
            {
                Group group = _groups.Find(g => g.Path == toggle.path);
                if (group != null)
                {
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
        /// 保存用に、全てのまとまりの ON/OFF を書き出す。
        /// </summary>
        public List<ParticleToggle> Export()
        {
            return _groups.ConvertAll(group => new ParticleToggle { path = group.Path, enabled = group.Enabled });
        }

        private void SetEnabled(Group group, bool enabled, bool force)
        {
            // 変わらないとき・破棄済みのときは何もしない（読込時の記録は状態をそろえるため必ず当てる）
            if ((!force && group.Enabled == enabled) || group.Root == null)
            {
                return;
            }

            group.Enabled = enabled;
            if (enabled)
            {
                // 非表示のオブジェクト（自身と親）を表示し、描画を戻して最初から再生する
                Activate(group.Root.transform);
                SetRenderers(group, true);
                group.Root.Play(true);
            }
            else
            {
                // 放出を止めて出ている粒も消し、描画もやめる（オブジェクトは隠さない）
                group.Root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                SetRenderers(group, false);
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

        private static void SetRenderers(Group group, bool enabled)
        {
            // 破棄済みの描画は飛ばす
            foreach (ParticleSystemRenderer renderer in group.Renderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = enabled;
                }
            }
        }

        private bool HasParticleAncestor(Transform target)
        {
            // アバタールートより下の親だけを見る
            for (Transform node = target.parent; node != null && node != _root; node = node.parent)
            {
                if (node.GetComponent<ParticleSystem>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyEnabled(ParticleSystemRenderer[] renderers)
        {
            // 描画が 1 つでも有効なら表示中（描画の無いパーティクルは表示中扱い）
            if (renderers.Length == 0)
            {
                return true;
            }

            foreach (ParticleSystemRenderer renderer in renderers)
            {
                if (renderer.enabled)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
