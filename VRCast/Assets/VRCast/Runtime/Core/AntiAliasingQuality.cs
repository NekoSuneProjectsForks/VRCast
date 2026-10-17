namespace VRCast.Core
{
    /// <summary>
    /// アンチエイリアスの強さ。MSAA ではサンプル数、高解像度描画では内部解像度の倍率に使う。
    /// </summary>
    public enum AntiAliasingQuality
    {
        Off = 0,
        x2 = 2,
        x4 = 4,
        x8 = 8,
    }
}
