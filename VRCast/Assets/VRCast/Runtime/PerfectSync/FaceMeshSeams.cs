using System.Collections.Generic;
using UnityEngine;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 顔メッシュの肌の外周（首・頭など別のメッシュとの継ぎ目になりうる縁）を求める。
    /// 同じ位置の頂点（UV の継ぎ目で分かれたもの）は 1 つとみなし、三角形でつながるまとまりのうち
    /// 三角形の最も多いもの（肌）だけを見る。眉・まつ毛・歯など、肌から離れた部品の縁は含めない。
    /// 目・口の穴の縁も外周に入るので、呼び出し側で除く。
    /// </summary>
    public static class FaceMeshSeams
    {
        /// <summary>
        /// 肌の外周の頂点の位置（同じ位置は 1 つ）。weldDistance 以内の頂点は同じ位置とみなす。
        /// </summary>
        public static List<Vector3> OuterBoundary(Vector3[] positions, int[] triangles, float weldDistance)
        {
            var boundary = new List<Vector3>();
            if (positions == null || triangles == null || triangles.Length < 3 || weldDistance <= 0f)
            {
                return boundary;
            }

            // 同じ位置の頂点をまとめた番号と、その代表の頂点
            int[] weld = Weld(positions, weldDistance, out List<int> representatives);

            // 三角形でつながる頂点をまとめ、まとまりごとの三角形の数を数える
            var parent = new int[representatives.Count];
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }

            int triangleCount = triangles.Length / 3;
            for (int t = 0; t < triangleCount; t++)
            {
                if (TryGetTriangle(positions, triangles, weld, t, out int a, out int b, out int c))
                {
                    Union(parent, a, b);
                    Union(parent, b, c);
                }
            }

            var sizes = new Dictionary<int, int>();
            int skin = -1;
            for (int t = 0; t < triangleCount; t++)
            {
                if (!TryGetTriangle(positions, triangles, weld, t, out int a, out _, out _))
                {
                    continue;
                }

                // 三角形の最も多いまとまりを肌とする
                int root = Find(parent, a);
                sizes.TryGetValue(root, out int size);
                sizes[root] = ++size;
                if (skin < 0 || size > sizes[skin])
                {
                    skin = root;
                }
            }

            if (skin < 0)
            {
                return boundary;
            }

            // 肌の辺ごとに使う三角形の数（1 つだけなら外周）
            var edges = new Dictionary<long, int>();
            for (int t = 0; t < triangleCount; t++)
            {
                if (TryGetTriangle(positions, triangles, weld, t, out int a, out int b, out int c)
                    && Find(parent, a) == skin)
                {
                    CountEdge(edges, a, b, representatives.Count);
                    CountEdge(edges, b, c, representatives.Count);
                    CountEdge(edges, c, a, representatives.Count);
                }
            }

            // 外周の辺の両端（同じ頂点は 1 回だけ）
            var added = new HashSet<int>();
            foreach (KeyValuePair<long, int> edge in edges)
            {
                if (edge.Value != 1)
                {
                    continue;
                }

                int first = (int)(edge.Key / representatives.Count);
                int second = (int)(edge.Key % representatives.Count);
                if (added.Add(first))
                {
                    boundary.Add(positions[representatives[first]]);
                }

                if (added.Add(second))
                {
                    boundary.Add(positions[representatives[second]]);
                }
            }

            return boundary;
        }

        private static int[] Weld(Vector3[] positions, float weldDistance, out List<int> representatives)
        {
            // 位置を weldDistance の格子に丸め、同じ格子の頂点に同じ番号を振る
            var weld = new int[positions.Length];
            var byCell = new Dictionary<Vector3Int, int>();
            representatives = new List<int>();
            for (int v = 0; v < positions.Length; v++)
            {
                Vector3Int cell = Vector3Int.RoundToInt(positions[v] / weldDistance);
                if (!byCell.TryGetValue(cell, out int id))
                {
                    id = representatives.Count;
                    byCell[cell] = id;
                    representatives.Add(v);
                }

                weld[v] = id;
            }

            return weld;
        }

        private static bool TryGetTriangle(Vector3[] positions, int[] triangles, int[] weld, int t,
            out int a, out int b, out int c)
        {
            // 範囲外の番号・つぶれた三角形（まとめた結果 2 点以上が同じ）は使わない
            a = b = c = -1;
            int i0 = triangles[t * 3];
            int i1 = triangles[t * 3 + 1];
            int i2 = triangles[t * 3 + 2];
            if ((uint)i0 >= (uint)positions.Length || (uint)i1 >= (uint)positions.Length
                || (uint)i2 >= (uint)positions.Length)
            {
                return false;
            }

            a = weld[i0];
            b = weld[i1];
            c = weld[i2];
            return a != b && b != c && c != a;
        }

        private static void CountEdge(Dictionary<long, int> edges, int a, int b, int count)
        {
            // 向きを問わない辺の番号（小さい方 × 数 + 大きい方）
            long key = a < b ? (long)a * count + b : (long)b * count + a;
            edges.TryGetValue(key, out int used);
            edges[key] = used + 1;
        }

        private static int Find(int[] parent, int i)
        {
            // 根をたどり、途中の頂点を根へ付け替えて次から速くする
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            // 2 つのまとまりを 1 つにする
            int rootA = Find(parent, a);
            int rootB = Find(parent, b);
            if (rootA != rootB)
            {
                parent[rootB] = rootA;
            }
        }
    }
}
