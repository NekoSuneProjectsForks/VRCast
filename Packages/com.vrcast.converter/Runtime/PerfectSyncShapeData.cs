using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/perfect_sync_shapes.bin の内容。パーフェクトシンク化で生成した形状（動く頂点だけの移動量）。
    /// 形式（リトルエンディアン）: 識別子 "VCPS"・版・形状数、形状ごとに
    /// メッシュのパス・ARKit 名（BinaryWriter の文字列）・頂点数・（頂点番号, x, y, z）の列。
    /// 移動量はメッシュの座標系（取り込み時の頂点と同じ単位）。
    /// </summary>
    public sealed class PerfectSyncShapeData
    {
        // ファイルの先頭の識別子（"VCPS"）と版
        public const int Magic = 0x53504356;
        public const int CurrentVersion = 1;

        // 信頼できない入力に対する上限
        public const int MaxShapes = 1024;
        public const int MaxVerticesPerShape = 1 << 20;
        public const float MaxDelta = 10000f;

        /// <summary>
        /// 生成した形状の一覧。
        /// </summary>
        public List<PerfectSyncShape> Shapes { get; } = new List<PerfectSyncShape>();

        /// <summary>
        /// 内容を検証し、問題があればエラーメッセージを、無ければ null を返す。
        /// </summary>
        public string Validate()
        {
            // 件数上限
            if (Shapes.Count > MaxShapes)
            {
                return $"shapes must be 0-{MaxShapes} items.";
            }

            foreach (PerfectSyncShape shape in Shapes)
            {
                // 空の要素はエラー、それ以外は形状ごとの検証（問題が無ければ null）
                string error = shape == null ? "A shape is missing." : shape.Validate();
                if (error != null)
                {
                    return error;
                }
            }

            return null;
        }

        /// <summary>
        /// バイナリとして書き出す（検証に通らない内容は例外）。
        /// </summary>
        public byte[] ToBytes()
        {
            // 壊れたファイルを作らないよう書く前に検証する
            string error = Validate();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    // 見出し
                    writer.Write(Magic);
                    writer.Write(CurrentVersion);
                    writer.Write(Shapes.Count);

                    // 形状ごとの名前と動く頂点
                    foreach (PerfectSyncShape shape in Shapes)
                    {
                        writer.Write(shape.Path);
                        writer.Write(shape.Name);
                        writer.Write(shape.Vertices.Length);
                        for (int i = 0; i < shape.Vertices.Length; i++)
                        {
                            writer.Write(shape.Vertices[i]);
                            writer.Write(shape.Deltas[i * 3]);
                            writer.Write(shape.Deltas[i * 3 + 1]);
                            writer.Write(shape.Deltas[i * 3 + 2]);
                        }
                    }
                }

                return stream.ToArray();
            }
        }

        /// <summary>
        /// バイナリから読む。形式・内容が不正なら InvalidDataException。
        /// </summary>
        public static PerfectSyncShapeData FromBytes(byte[] bytes)
        {
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8))
                {
                    // 識別子と版
                    if (reader.ReadInt32() != Magic)
                    {
                        throw new InvalidDataException("Not a perfect sync shape file.");
                    }

                    int version = reader.ReadInt32();
                    if (version < 1 || version > CurrentVersion)
                    {
                        throw new InvalidDataException($"Unsupported version: {version}.");
                    }

                    // 形状数は上限内
                    int count = reader.ReadInt32();
                    if (count < 0 || count > MaxShapes)
                    {
                        throw new InvalidDataException("Too many shapes.");
                    }

                    var data = new PerfectSyncShapeData();
                    for (int s = 0; s < count; s++)
                    {
                        data.Shapes.Add(ReadShape(reader, bytes.Length));
                    }

                    // 最後に内容全体を検証する
                    string error = data.Validate();
                    if (error != null)
                    {
                        throw new InvalidDataException(error);
                    }

                    return data;
                }
            }
            catch (Exception e) when (e is EndOfStreamException || e is FormatException)
            {
                // 途中で切れている・文字列の長さが壊れている
                throw new InvalidDataException("Perfect sync shape file is truncated or corrupt.", e);
            }
        }

        private static PerfectSyncShape ReadShape(BinaryReader reader, long totalBytes)
        {
            // 名前と頂点数（残りのバイト数で表せない数は不正）
            string path = reader.ReadString();
            string name = reader.ReadString();
            int vertexCount = reader.ReadInt32();
            long remaining = totalBytes - reader.BaseStream.Position;
            if (vertexCount < 0 || vertexCount > MaxVerticesPerShape || vertexCount * 16L > remaining)
            {
                throw new InvalidDataException("Invalid vertex count.");
            }

            // 頂点番号と移動量
            var vertices = new int[vertexCount];
            var deltas = new float[vertexCount * 3];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = reader.ReadInt32();
                deltas[i * 3] = reader.ReadSingle();
                deltas[i * 3 + 1] = reader.ReadSingle();
                deltas[i * 3 + 2] = reader.ReadSingle();
            }

            return new PerfectSyncShape(path, name, vertices, deltas);
        }
    }

    /// <summary>
    /// 生成した形状 1 つ（メッシュのパス・ARKit 名・動く頂点の番号と移動量 x, y, z の並び）。
    /// </summary>
    public sealed class PerfectSyncShape
    {
        public string Path { get; }
        public string Name { get; }
        public int[] Vertices { get; }
        public float[] Deltas { get; }

        public PerfectSyncShape(string path, string name, int[] vertices, float[] deltas)
        {
            Path = path ?? string.Empty;
            Name = name ?? string.Empty;
            Vertices = vertices ?? Array.Empty<int>();
            Deltas = deltas ?? Array.Empty<float>();
        }

        /// <summary>
        /// 内容を検証し、問題があればエラーメッセージを、無ければ null を返す。
        /// </summary>
        public string Validate()
        {
            // パスは空（ルート）を許可、名前は必須
            if (!ExpressionSet.IsValidString(Path, true) || !ExpressionSet.IsValidString(Name, false))
            {
                return "A shape name is invalid.";
            }

            // 頂点数と移動量の数が合っていること
            if (Vertices.Length > PerfectSyncShapeData.MaxVerticesPerShape || Deltas.Length != Vertices.Length * 3)
            {
                return $"Shape '{Name}' has invalid vertices.";
            }

            foreach (int vertex in Vertices)
            {
                // 頂点番号は 0 以上（メッシュの頂点数との照合は適用時に行う）
                if (vertex < 0)
                {
                    return $"Shape '{Name}' has an invalid vertex index.";
                }
            }

            foreach (float delta in Deltas)
            {
                // 有限で、ありえない大きさでないこと
                if (float.IsNaN(delta) || delta < -PerfectSyncShapeData.MaxDelta || delta > PerfectSyncShapeData.MaxDelta)
                {
                    return $"Shape '{Name}' has an invalid delta.";
                }
            }

            return null;
        }
    }
}
