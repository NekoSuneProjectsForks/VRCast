using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 顔メッシュの形（取り込み時の頂点・法線・三角形）。目印の位置の計算・左右の反転・クリック位置の判定に使う。
    /// 頂点を読むため、読み書きできるメッシュ（Mesh.isReadable）が必要。
    /// </summary>
    public sealed class FaceMeshGeometry
    {
        // 頂点ごとに、その頂点を含む三角形（最も近い点を探すときに必要になったら作る）
        private List<int>[] _vertexTriangles;

        public Vector3[] Vertices { get; }
        public Vector3[] Normals { get; }
        public int[] Triangles { get; }

        /// <summary>
        /// 三角形の数。
        /// </summary>
        public int TriangleCount => Triangles.Length / 3;

        public FaceMeshGeometry(Vector3[] vertices, Vector3[] normals, int[] triangles)
        {
            Vertices = vertices;
            Normals = normals != null && normals.Length == vertices.Length ? normals : null;
            Triangles = triangles;
        }

        /// <summary>
        /// メッシュから作る。読めないメッシュなら null。
        /// </summary>
        public static FaceMeshGeometry From(Mesh mesh)
        {
            // 頂点を読めないメッシュ（書き出し直し前の .vrcaster）は扱えない
            if (mesh == null || !mesh.isReadable)
            {
                return null;
            }

            return new FaceMeshGeometry(mesh.vertices, mesh.normals, mesh.triangles);
        }

        /// <summary>
        /// 目印が指す点（三角形の番号が範囲外なら false）。positions を渡すとその頂点（姿勢を付けた形など）で計算する。
        /// </summary>
        public bool TryGetPoint(PerfectSyncLandmark landmark, out Vector3 point, Vector3[] positions = null)
        {
            point = default;
            if (landmark == null || landmark.triangle < 0 || landmark.triangle >= TriangleCount)
            {
                return false;
            }

            // 重心座標で 3 頂点を混ぜる（1 番目の重みは残り）
            Vector3[] source = positions ?? Vertices;
            int i = landmark.triangle * 3;
            point = source[Triangles[i]] * (1f - landmark.u - landmark.v)
                + source[Triangles[i + 1]] * landmark.u + source[Triangles[i + 2]] * landmark.v;
            return true;
        }

        /// <summary>
        /// 目印の位置の法線（法線が無いメッシュは false）。
        /// </summary>
        public bool TryGetNormal(PerfectSyncLandmark landmark, out Vector3 normal)
        {
            normal = default;
            if (Normals == null || landmark == null || landmark.triangle < 0 || landmark.triangle >= TriangleCount)
            {
                return false;
            }

            int i = landmark.triangle * 3;
            normal = (Normals[Triangles[i]] * (1f - landmark.u - landmark.v)
                + Normals[Triangles[i + 1]] * landmark.u + Normals[Triangles[i + 2]] * landmark.v).normalized;
            return true;
        }

        /// <summary>
        /// point に最も近い表面上の点を目印にする（左右対称で反対側に打つときに使う）。
        /// </summary>
        public PerfectSyncLandmark Nearest(Vector3 point, int id)
        {
            // まず最も近い頂点を探す（どの三角形にも使われていない頂点は除く）
            BuildVertexTriangles();
            int nearest = 0;
            float best = float.MaxValue;
            for (int v = 0; v < Vertices.Length; v++)
            {
                float distance = (Vertices[v] - point).sqrMagnitude;
                if (distance < best && _vertexTriangles[v].Count > 0)
                {
                    best = distance;
                    nearest = v;
                }
            }

            // その頂点を含む三角形のうち、最も近い点を持つものを選ぶ
            var result = new PerfectSyncLandmark { id = id };
            float bestTriangle = float.MaxValue;
            foreach (int triangle in _vertexTriangles[nearest])
            {
                int i = triangle * 3;
                Vector3 closest = ClosestOnTriangle(Vertices[Triangles[i]], Vertices[Triangles[i + 1]],
                    Vertices[Triangles[i + 2]], point, out float u, out float v);
                float distance = (closest - point).sqrMagnitude;
                if (distance < bestTriangle)
                {
                    bestTriangle = distance;
                    result.triangle = triangle;
                    result.u = u;
                    result.v = v;
                }
            }

            return result;
        }

        /// <summary>
        /// positions（姿勢を付けた形など）の candidates の三角形のうち、point に最も近い表面上の点を目印にする。
        /// facing が 0 でなければ、面の向きが facing と逆の三角形（まぶたの裏・口の中の面など）は除く。
        /// 当てはまる三角形が無ければ null。
        /// </summary>
        public PerfectSyncLandmark Nearest(Vector3[] positions, IReadOnlyList<int> candidates, Vector3 point,
            Vector3 facing, int id)
        {
            PerfectSyncLandmark result = null;
            float best = float.MaxValue;
            bool checkFacing = facing.sqrMagnitude > 1e-12f;
            foreach (int triangle in candidates)
            {
                // 向きが逆の面は飛ばす
                int i = triangle * 3;
                Vector3 a = positions[Triangles[i]];
                Vector3 b = positions[Triangles[i + 1]];
                Vector3 c = positions[Triangles[i + 2]];
                if (checkFacing && Vector3.Dot(Vector3.Cross(b - a, c - a), facing) <= 0f)
                {
                    continue;
                }

                // より近い点を残す
                Vector3 closest = ClosestOnTriangle(a, b, c, point, out float u, out float v);
                float distance = (closest - point).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    result = new PerfectSyncLandmark { id = id, triangle = triangle, u = u, v = v };
                }
            }

            return result;
        }

        /// <summary>
        /// 三角形の面の向き（positions の座標系。頂点の並びから求め、長さは 1 にしない）。
        /// </summary>
        public Vector3 TriangleNormal(Vector3[] positions, int triangle)
        {
            int i = triangle * 3;
            Vector3 a = positions[Triangles[i]];
            return Vector3.Cross(positions[Triangles[i + 1]] - a, positions[Triangles[i + 2]] - a);
        }

        /// <summary>
        /// 光線（positions と同じ座標系）が最初に当たる三角形を探す。candidates が null なら全三角形を調べる。
        /// </summary>
        public bool Raycast(Vector3[] positions, Ray ray, IReadOnlyList<int> candidates, out PerfectSyncLandmark hit,
            out float distance)
        {
            hit = null;
            distance = float.MaxValue;
            int count = candidates?.Count ?? TriangleCount;
            for (int c = 0; c < count; c++)
            {
                // 候補の三角形と交差判定（Möller–Trumbore）
                int triangle = candidates != null ? candidates[c] : c;
                int i = triangle * 3;
                if (!Intersect(ray, positions[Triangles[i]], positions[Triangles[i + 1]], positions[Triangles[i + 2]],
                        out float t, out float u, out float v) || t >= distance)
                {
                    continue;
                }

                // より近い交点を残す
                distance = t;
                hit = new PerfectSyncLandmark { triangle = triangle, u = u, v = v };
            }

            return hit != null;
        }

        /// <summary>
        /// 対称面（点 planePoint と法線 planeNormal）で point を反転した位置。
        /// </summary>
        public static Vector3 Reflect(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
        {
            // 面からの距離の 2 倍だけ法線の逆向きへ動かす
            Vector3 normal = planeNormal.normalized;
            return point - 2f * Vector3.Dot(point - planePoint, normal) * normal;
        }

        /// <summary>
        /// 対称面（法線 planeNormal）で向き direction を反転したもの。
        /// </summary>
        public static Vector3 ReflectDirection(Vector3 direction, Vector3 planeNormal)
        {
            // 法線方向の成分だけ逆にする
            Vector3 normal = planeNormal.normalized;
            return direction - 2f * Vector3.Dot(direction, normal) * normal;
        }

        private static bool Intersect(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
        {
            t = u = v = 0f;
            const float epsilon = 1e-9f;

            // 光線と三角形の面が平行なら当たらない（裏面にも当てる）
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (determinant > -epsilon && determinant < epsilon)
            {
                return false;
            }

            // 重心座標が三角形の内側か
            float inverse = 1f / determinant;
            Vector3 s = ray.origin - a;
            u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(s, edge1);
            v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            // 光線の前方で当たること
            t = Vector3.Dot(edge2, q) * inverse;
            return t > 0f;
        }

        private static Vector3 ClosestOnTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 point, out float u, out float v)
        {
            // 面へ投影した点の重心座標を求め、三角形の内側へ収める
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            float d00 = Vector3.Dot(ab, ab);
            float d01 = Vector3.Dot(ab, ac);
            float d11 = Vector3.Dot(ac, ac);
            float d20 = Vector3.Dot(ap, ab);
            float d21 = Vector3.Dot(ap, ac);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 1e-12f)
            {
                // つぶれた三角形は 1 番目の頂点とみなす
                u = v = 0f;
                return a;
            }

            u = Mathf.Clamp01((d11 * d20 - d01 * d21) / denominator);
            v = Mathf.Clamp01((d00 * d21 - d01 * d20) / denominator);
            float sum = u + v;
            if (sum > 1f)
            {
                u /= sum;
                v /= sum;
            }

            return a + ab * u + ac * v;
        }

        private void BuildVertexTriangles()
        {
            // 初回だけ作る
            if (_vertexTriangles != null)
            {
                return;
            }

            _vertexTriangles = new List<int>[Vertices.Length];
            for (int v = 0; v < Vertices.Length; v++)
            {
                _vertexTriangles[v] = new List<int>(6);
            }

            for (int i = 0; i < Triangles.Length; i++)
            {
                _vertexTriangles[Triangles[i]].Add(i / 3);
            }
        }
    }
}
