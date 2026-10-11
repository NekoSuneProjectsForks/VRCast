namespace VRCast.Core
{
    /// <summary>
    /// トラッキングのまばたきの左右をそろえるか（settings.json には数値で保存するため並び順を変えない）。
    /// そろえるとウインクにはならないが、両目のまばたきはできる。
    /// </summary>
    public enum BlinkSyncMode
    {
        // そろえない（左右別。ウインクできる）
        None = 0,

        // 常にそろえる
        Always = 1,

        // 横を向いたときだけそろえる（顔の奥側の目が閉じて見えやすいため）
        WhenTurned = 2,
    }
}
