using System.Collections.Generic;
using UnityEngine;
using VRCast.PerfectSync;

namespace VRCast.Tests
{
    /// <summary>
    /// パーフェクトシンク化のテスト用の平らな顔（z = 0 の格子。アバターは +Z 向きなので、アバターの左は -X）。
    /// </summary>
    public static class PerfectSyncTestFace
    {
        // 格子の範囲と間隔（目印がちょうど格子の点に乗る値）
        public const float MinX = -1f;
        public const float MinY = -1.1f;
        public const float Step = 0.05f;
        public const int Columns = 41;
        public const int Rows = 41;

        /// <summary>
        /// 格子の頂点・前向きの法線・三角形。
        /// </summary>
        public static FaceMeshGeometry CreateGeometry()
        {
            var vertices = new Vector3[Columns * Rows];
            var normals = new Vector3[vertices.Length];
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    vertices[y * Columns + x] = new Vector3(MinX + x * Step, MinY + y * Step, 0f);
                    normals[y * Columns + x] = Vector3.forward;
                }
            }

            // 格子の四角を 2 つの三角形に分ける
            var triangles = new List<int>();
            for (int y = 0; y < Rows - 1; y++)
            {
                for (int x = 0; x < Columns - 1; x++)
                {
                    int i = y * Columns + x;
                    triangles.AddRange(new[] { i, i + Columns, i + 1, i + 1, i + Columns, i + Columns + 1 });
                }
            }

            return new FaceMeshGeometry(vertices, normals, triangles.ToArray());
        }

        /// <summary>
        /// 最小の段階の目印の位置（メッシュの座標系）。
        /// </summary>
        public static Dictionary<LandmarkId, Vector3> MinimalPoints()
        {
            return new Dictionary<LandmarkId, Vector3>
            {
                { LandmarkId.EyeUpperLeft, new Vector3(-0.35f, 0.35f, 0f) },
                { LandmarkId.EyeUpperRight, new Vector3(0.35f, 0.35f, 0f) },
                { LandmarkId.EyeLowerLeft, new Vector3(-0.35f, 0.15f, 0f) },
                { LandmarkId.EyeLowerRight, new Vector3(0.35f, 0.15f, 0f) },
                { LandmarkId.BrowMidLeft, new Vector3(-0.35f, 0.55f, 0f) },
                { LandmarkId.BrowMidRight, new Vector3(0.35f, 0.55f, 0f) },
                { LandmarkId.MouthCornerLeft, new Vector3(-0.2f, -0.45f, 0f) },
                { LandmarkId.MouthCornerRight, new Vector3(0.2f, -0.45f, 0f) },
                { LandmarkId.LipUpper, new Vector3(0f, -0.4f, 0f) },
                { LandmarkId.LipLower, new Vector3(0f, -0.5f, 0f) },
                { LandmarkId.Chin, new Vector3(0f, -0.85f, 0f) },
            };
        }

        /// <summary>
        /// 目印の位置の法線（平らな顔なので全部前向き）。
        /// </summary>
        public static Dictionary<LandmarkId, Vector3> Normals(IEnumerable<LandmarkId> ids)
        {
            var normals = new Dictionary<LandmarkId, Vector3>();
            foreach (LandmarkId id in ids)
            {
                normals[id] = Vector3.forward;
            }

            return normals;
        }

        /// <summary>
        /// 位置に最も近い格子の頂点の番号。
        /// </summary>
        public static int VertexAt(Vector3 position)
        {
            int x = Mathf.RoundToInt((position.x - MinX) / Step);
            int y = Mathf.RoundToInt((position.y - MinY) / Step);
            return y * Columns + x;
        }
    }
}
