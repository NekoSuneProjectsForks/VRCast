using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// BlendShape を持つメッシュを、頂点を読み書きできる複製に差し替える
    /// （アプリのパーフェクトシンク化が頂点を読み、BlendShape を足すため）。
    /// 元の FBX の取り込み設定は変えず、書き出し用の一時フォルダに複製を保存する。
    /// </summary>
    public static class ReadableMeshCopier
    {
        // 複製したメッシュのファイル名の接頭辞
        private const string AssetPrefix = "readable_";

        /// <summary>
        /// root 以下の SkinnedMeshRenderer のうち BlendShape を持つメッシュを複製に差し替え、差し替えた数を返す。
        /// folder は保存先（書き出しの一時フォルダ）。
        /// </summary>
        public static int Apply(GameObject root, string folder)
        {
            // 同じメッシュを複数のレンダラーが使っていても複製は 1 つにする
            var copies = new Dictionary<Mesh, Mesh>();
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // BlendShape の無いメッシュ（体・服など）はファイルを大きくしないよう対象外
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0)
                {
                    continue;
                }

                // 初めて見るメッシュだけ複製して保存する
                if (!copies.TryGetValue(mesh, out Mesh copy))
                {
                    copy = CreateCopy(mesh, folder, copies.Count);
                    copies[mesh] = copy;
                }

                // 複製へ差し替え、焼き込み済みの BlendShape の値を戻す（番号は同じ）
                var weights = new float[mesh.blendShapeCount];
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] = renderer.GetBlendShapeWeight(i);
                }

                renderer.sharedMesh = copy;
                for (int i = 0; i < weights.Length; i++)
                {
                    renderer.SetBlendShapeWeight(i, weights[i]);
                }
            }

            return copies.Count;
        }

        private static Mesh CreateCopy(Mesh source, string folder, int number)
        {
            // エディタでは読み書きできないメッシュも複製できる
            Mesh copy = Object.Instantiate(source);
            copy.name = source.name;

            // 同じ名前のメッシュが複数あっても衝突しないよう連番を付けて保存する
            string path = $"{folder}/{AssetPrefix}{number}.asset";
            AssetDatabase.CreateAsset(copy, path);

            // 取り込み設定の Read/Write に当たる値を有効にする（AssetBundle の読み込み後も頂点を読めるように）
            var serialized = new SerializedObject(copy);
            SerializedProperty readable = serialized.FindProperty("m_IsReadable");
            if (readable != null)
            {
                readable.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return copy;
        }
    }
}
