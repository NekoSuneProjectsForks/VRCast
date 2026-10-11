using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/fx_toggles.json の内容。VRChat の FX で表示を ON/OFF しているオブジェクト（衣装・小物の切り替え等）のパス
    /// （アバタールートからの相対パス）。Runtime はオブジェクトの一覧でこれらを目立たせ、絞り込めるようにする。
    /// </summary>
    [Serializable]
    public class FxToggleSet : PathListSet
    {
    }
}
