using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Avatars
{
    /// <summary>
    /// アバターの階層（Unity のヒエラルキーと同じ並び）を一覧にし、オブジェクトごとに表示を ON/OFF する。
    /// 読込時の状態から切り替えたものだけを記録し、次に読み込んだときに当て直す。
    /// ルート自身は切り替えない（アバターの動きを受け持つコンポーネントが付いているため）。
    /// </summary>
    public class AvatarObjects : MonoBehaviour
    {
        /// <summary>
        /// 一覧の 1 行になるオブジェクト。
        /// </summary>
        public sealed class Node
        {
            // アバターのルートからの相対パスと、Unity 上のオブジェクト名・階層の深さ（ルートの子が 0）
            public string Path;
            public string Name;
            public int Depth;

            // この行・親の行の番号（ルートの子の親は -1）と、子孫の最後の行の番号（子が無ければ自身）
            public int Index;
            public int Parent;
            public int SubtreeEnd;

            // 自身か子孫に描画（メッシュ・パーティクル等）があるか
            public bool HasRenderer;

            // VRChat の FX で表示を ON/OFF しているオブジェクトか、その子孫に含むか
            public bool IsFxToggle;
            public bool ContainsFxToggle;

            // 読込時の表示状態
            public bool InitialActive;
            internal Transform Transform;

            /// <summary>
            /// 子の行があれば true。
            /// </summary>
            public bool HasChildren => SubtreeEnd > Index;

            /// <summary>
            /// 自身の表示の ON/OFF（Unity の Inspector のチェックと同じ）。
            /// </summary>
            public bool Active => Transform != null && Transform.gameObject.activeSelf;

            /// <summary>
            /// 親も含めて表示されているか（親が OFF なら自身が ON でも見えない）。
            /// </summary>
            public bool Visible => Transform != null && Transform.gameObject.activeInHierarchy;
        }

        private readonly List<Node> _nodes = new List<Node>();

        // 読込時の状態から切り替えたオブジェクト（パス → ON/OFF）
        private readonly Dictionary<string, bool> _overrides = new Dictionary<string, bool>();

        /// <summary>
        /// ルートの子孫の行（ヒエラルキーと同じ深さ優先の順）。
        /// </summary>
        public IReadOnlyList<Node> Nodes => _nodes;

        /// <summary>
        /// VRChat の FX で表示を ON/OFF しているオブジェクトの数。
        /// </summary>
        public int FxToggleCount { get; private set; }

        /// <summary>
        /// 読込時の状態から切り替えているオブジェクトの数。
        /// </summary>
        public int ChangedCount => _overrides.Count;

        /// <summary>
        /// 階層を一覧にし、記録済みの ON/OFF を当てる（見つからないパスは無視）。
        /// fxTogglePaths は FX で表示を ON/OFF しているオブジェクトのパス（.vrcaster の metadata。無ければ空）。
        /// </summary>
        public void Initialize(Transform root, IReadOnlyCollection<string> fxTogglePaths, List<ObjectToggle> toggles)
        {
            // 深さ優先で行を作る（ルート自身は含めない）
            var fxPaths = fxTogglePaths != null ? new HashSet<string>(fxTogglePaths) : new HashSet<string>();
            foreach (Transform child in root)
            {
                AddSubtree(child, root, 0, -1, fxPaths);
            }

            // 記録どおりに当てる（同じパスが複数あれば Transform.Find と同じく先頭のもの）
            foreach (ObjectToggle toggle in toggles)
            {
                Node node = toggle != null ? FindByPath(toggle.path) : null;
                if (node != null)
                {
                    SetActive(node, toggle.active);
                }
            }
        }

        /// <summary>
        /// オブジェクトの表示を ON/OFF する（読込時の状態に戻したものは記録から外す）。
        /// </summary>
        public void SetActive(Node node, bool active)
        {
            // 破棄済みなら何もしない
            if (node.Transform == null)
            {
                return;
            }

            node.Transform.gameObject.SetActive(active);

            // 読込時と違うものだけを覚える
            if (active == node.InitialActive)
            {
                _overrides.Remove(node.Path);
            }
            else
            {
                _overrides[node.Path] = active;
            }
        }

        /// <summary>
        /// 切り替えたオブジェクトを読込時の状態に戻す（パーティクルの ON で表示した親などは触らない）。
        /// </summary>
        public void ResetAll()
        {
            foreach (Node node in _nodes)
            {
                if (_overrides.ContainsKey(node.Path) && node.Transform != null)
                {
                    node.Transform.gameObject.SetActive(node.InitialActive);
                }
            }

            _overrides.Clear();
        }

        /// <summary>
        /// 保存用に、読込時の状態から切り替えたものだけを書き出す。
        /// </summary>
        public List<ObjectToggle> Export()
        {
            var toggles = new List<ObjectToggle>();
            foreach (KeyValuePair<string, bool> pair in _overrides)
            {
                toggles.Add(new ObjectToggle { path = pair.Key, active = pair.Value });
            }

            // 保存のたびに並びが変わらないようパスの順にする
            toggles.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            return toggles;
        }

        private Node FindByPath(string path)
        {
            // ルート自身（空）は対象外
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return _nodes.Find(node => node.Path == path);
        }

        private void AddSubtree(Transform target, Transform root, int depth, int parent, HashSet<string> fxPaths)
        {
            // 自身の行を作る（子孫の情報は子を並べた後に埋める）
            var node = new Node
            {
                Path = TransformPath.Of(target, root),
                Name = target.name,
                Depth = depth,
                Parent = parent,
                InitialActive = target.gameObject.activeSelf,
                Transform = target,
                Index = _nodes.Count,
            };
            node.IsFxToggle = fxPaths.Contains(node.Path);
            node.HasRenderer = target.GetComponent<Renderer>() != null;
            node.ContainsFxToggle = node.IsFxToggle;
            FxToggleCount += node.IsFxToggle ? 1 : 0;
            _nodes.Add(node);

            // 子を深さ優先で並べ、子孫の描画・FX の切り替えを親へ伝える
            foreach (Transform child in target)
            {
                int childIndex = _nodes.Count;
                AddSubtree(child, root, depth + 1, node.Index, fxPaths);
                Node childNode = _nodes[childIndex];
                node.HasRenderer |= childNode.HasRenderer;
                node.ContainsFxToggle |= childNode.ContainsFxToggle;
            }

            node.SubtreeEnd = _nodes.Count - 1;
        }
    }
}
