using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバター内の 1 つの BlendShape（path はアバターのルートからの相対パス、空ならルート自身。blendShape が空なら未指定）。
    /// </summary>
    [Serializable]
    public class BlendShapeRef
    {
        public string path = string.Empty;
        public string blendShape = string.Empty;

        /// <summary>
        /// BlendShape を指定しているなら true。
        /// </summary>
        public bool IsSet => !string.IsNullOrEmpty(blendShape);

        /// <summary>
        /// 同じメッシュの同じ BlendShape を指していれば true（未指定同士は false）。
        /// </summary>
        public bool SameAs(BlendShapeRef other)
        {
            return other != null && IsSet && path == other.path && blendShape == other.blendShape;
        }

        /// <summary>
        /// 指定を書き込む（null・空の名前で未指定にする）。
        /// </summary>
        public void Set(string newPath, string newBlendShape)
        {
            path = newPath ?? string.Empty;
            blendShape = newBlendShape ?? string.Empty;
        }

        public BlendShapeRef Clone()
        {
            return new BlendShapeRef { path = path, blendShape = blendShape };
        }
    }

    /// <summary>
    /// アバターごとに覚える、まばたきに使う BlendShape の指定（enabled が false ならアバターのまぶた設定を自動で使う）。
    /// both は両目を閉じる BlendShape、left / right は片目だけ閉じる BlendShape（アバターから見た左右。両方そろったときだけ使う）。
    /// </summary>
    [Serializable]
    public class BlinkShapeOverride
    {
        public bool enabled;
        public BlendShapeRef both = new BlendShapeRef();
        public BlendShapeRef left = new BlendShapeRef();
        public BlendShapeRef right = new BlendShapeRef();

        /// <summary>
        /// 片目用が左右そろっていれば true（ウインクに使う）。
        /// </summary>
        public bool HasWinkPair => left.IsSet && right.IsSet;

        /// <summary>
        /// 何も指定していなければ true。
        /// </summary>
        public bool IsEmpty => !both.IsSet && !left.IsSet && !right.IsSet;

        public BlinkShapeOverride Clone()
        {
            return new BlinkShapeOverride
            {
                enabled = enabled, both = both.Clone(), left = left.Clone(), right = right.Clone(),
            };
        }

        /// <summary>
        /// 読み込んだ値の欠け（旧版の設定・手編集の null）を未指定で補う。
        /// </summary>
        public void Sanitize()
        {
            both = Normalize(both);
            left = Normalize(left);
            right = Normalize(right);
        }

        private static BlendShapeRef Normalize(BlendShapeRef value)
        {
            // null は未指定、null の文字列は空文字へ
            value ??= new BlendShapeRef();
            value.path ??= string.Empty;
            value.blendShape ??= string.Empty;
            return value;
        }
    }
}
