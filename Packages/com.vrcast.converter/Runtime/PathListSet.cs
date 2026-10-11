using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// アバタールートからの相対パスの一覧だけを持つ metadata の共通部分（件数の上限とパスの検証）。
    /// </summary>
    [Serializable]
    public abstract class PathListSet : IMetadata
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
                // ルート自身（空）は対象にしない
                if (!ExpressionSet.IsValidString(path, false))
                {
                    return "A path is invalid.";
                }
            }

            return null;
        }
    }
}
