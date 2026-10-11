using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;
using Object = UnityEngine.Object;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 生成した形状をアバターのメッシュへ足す。読み込んだメッシュ自体は変えず、複製に BlendShape を末尾へ足して差し替える
    /// （既存の BlendShape の番号は変わらないので、表情・上限・ホットキーの設定はそのまま使える）。
    /// Restore で元のメッシュへ戻し、複製を破棄する。
    /// </summary>
    public sealed class PerfectSyncMeshes
    {
        // ログのカテゴリ名
        private const string LogCategory = "PerfectSync";

        // 差し替えたレンダラーと、元のメッシュ・複製
        private readonly List<(SkinnedMeshRenderer Renderer, Mesh Original, Mesh Copy)> _swaps =
            new List<(SkinnedMeshRenderer, Mesh, Mesh)>();

        private PerfectSyncMeshes()
        {
        }

        /// <summary>
        /// 足した形状の数。
        /// </summary>
        public int AddedShapes { get; private set; }

        /// <summary>
        /// root 以下のメッシュへ data の形状を足す。読めないメッシュ・見つからないメッシュ・同じ名前が既にある形状は飛ばす。
        /// </summary>
        public static PerfectSyncMeshes Apply(Transform root, PerfectSyncShapeData data)
        {
            var result = new PerfectSyncMeshes();
            if (data == null || data.Shapes.Count == 0)
            {
                return result;
            }

            // メッシュのパスごとにまとめる（1 つのメッシュは 1 回だけ複製する）
            var byPath = new Dictionary<string, List<PerfectSyncShape>>();
            foreach (PerfectSyncShape shape in data.Shapes)
            {
                if (!byPath.TryGetValue(shape.Path, out List<PerfectSyncShape> list))
                {
                    list = new List<PerfectSyncShape>();
                    byPath[shape.Path] = list;
                }

                list.Add(shape);
            }

            foreach (KeyValuePair<string, List<PerfectSyncShape>> pair in byPath)
            {
                result.ApplyTo(root, pair.Key, pair.Value);
            }

            return result;
        }

        /// <summary>
        /// 差し替える前の元のメッシュ（差し替えていなければ今のメッシュ）。
        /// </summary>
        public Mesh OriginalOf(SkinnedMeshRenderer renderer)
        {
            foreach ((SkinnedMeshRenderer target, Mesh original, Mesh _) in _swaps)
            {
                if (target == renderer)
                {
                    return original;
                }
            }

            return renderer != null ? renderer.sharedMesh : null;
        }

        /// <summary>
        /// 元のメッシュへ戻して複製を破棄する（アバターごと破棄する途中なら restoreRenderers = false で複製だけ破棄）。
        /// </summary>
        public void Restore(bool restoreRenderers = true)
        {
            foreach ((SkinnedMeshRenderer renderer, Mesh original, Mesh copy) in _swaps)
            {
                // 元のメッシュへ戻し、元からある BlendShape の値を引き継ぐ
                if (restoreRenderers && renderer != null && original != null)
                {
                    SwapKeepingWeights(renderer, original);
                }

                DestroyMesh(copy);
            }

            _swaps.Clear();
            AddedShapes = 0;
        }

        private void ApplyTo(Transform root, string path, List<PerfectSyncShape> shapes)
        {
            // パスの SkinnedMeshRenderer（空ならルート）
            Transform node = string.IsNullOrEmpty(path) ? root : root.Find(path);
            var renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            if (mesh == null)
            {
                VRCastLog.Warning(LogCategory, $"Mesh not found for perfect sync shapes: {path}");
                return;
            }

            // 頂点を読めないメッシュには足せない（書き出し直し前のアバター）
            if (!mesh.isReadable)
            {
                VRCastLog.Warning(LogCategory, $"Mesh is not readable, skipped perfect sync shapes: {path}");
                return;
            }

            // 複製に形状を足す
            Mesh copy = Object.Instantiate(mesh);
            copy.name = mesh.name;
            int added = 0;
            int vertexCount = copy.vertexCount;
            foreach (PerfectSyncShape shape in shapes)
            {
                // 同じ名前が既にある・頂点番号がメッシュに合わない形状は飛ばす
                if (copy.GetBlendShapeIndex(shape.Name) >= 0 || !TryBuildDeltas(shape, vertexCount, out Vector3[] deltas))
                {
                    continue;
                }

                copy.AddBlendShapeFrame(shape.Name, 100f, deltas, null, null);
                added++;
            }

            // 1 つも足せなければ差し替えない
            if (added == 0)
            {
                DestroyMesh(copy);
                return;
            }

            SwapKeepingWeights(renderer, copy);
            _swaps.Add((renderer, mesh, copy));
            AddedShapes += added;
        }

        private static bool TryBuildDeltas(PerfectSyncShape shape, int vertexCount, out Vector3[] deltas)
        {
            // 動く頂点だけの移動量を、全頂点の配列へ広げる
            deltas = new Vector3[vertexCount];
            for (int i = 0; i < shape.Vertices.Length; i++)
            {
                int vertex = shape.Vertices[i];
                if (vertex < 0 || vertex >= vertexCount)
                {
                    VRCastLog.Warning(LogCategory, $"Perfect sync shape '{shape.Name}' does not match the mesh.");
                    return false;
                }

                deltas[vertex] = new Vector3(shape.Deltas[i * 3], shape.Deltas[i * 3 + 1], shape.Deltas[i * 3 + 2]);
            }

            return true;
        }

        private static void DestroyMesh(Mesh mesh)
        {
            // 実行中は通常の破棄、エディタのテストでは即時に破棄する
            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(mesh);
            }
            else
            {
                Object.DestroyImmediate(mesh);
            }
        }

        private static void SwapKeepingWeights(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            // 差し替えの前後で共通する BlendShape（元からあるもの）の値を引き継ぐ
            Mesh current = renderer.sharedMesh;
            int count = Mathf.Min(current != null ? current.blendShapeCount : 0, mesh.blendShapeCount);
            var weights = new float[count];
            for (int i = 0; i < count; i++)
            {
                weights[i] = renderer.GetBlendShapeWeight(i);
            }

            renderer.sharedMesh = mesh;
            for (int i = 0; i < count; i++)
            {
                renderer.SetBlendShapeWeight(i, weights[i]);
            }
        }
    }
}
