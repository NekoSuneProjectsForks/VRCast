using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/particle_groups.json の内容。VRChat の FX で表示を ON/OFF しているオブジェクトのうち、
    /// パーティクルを含むもののパス（アバタールートからの相対パス）。Runtime はこの単位でパーティクルをまとめて切り替える。
    /// </summary>
    [Serializable]
    public class ParticleGroupSet : IMetadata
    {
        // 信頼できない入力に対する上限
        public const int MaxPaths = 1024;

        public string[] paths = Array.Empty<string>();

        /// <summary>
        /// 内容を検証し、問題があればエラーメッセージを、無ければ null を返す。
        /// </summary>
        public string Validate()
        {
            // 配列欠落と件数上限
            if (paths == null || paths.Length > MaxPaths)
            {
                return $"paths must be 0-{MaxPaths} items.";
            }

            foreach (string path in paths)
            {
                // ルート自身（空）は切り替えの単位にしない
                if (!ExpressionSet.IsValidString(path, false))
                {
                    return "A particle group path is invalid.";
                }
            }

            return null;
        }
    }
}
