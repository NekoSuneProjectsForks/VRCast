namespace VRCast.Core
{
    /// <summary>
    /// 画面のアンチエイリアス（MSAA のサンプル数。仮想カメラ等で MSAA が効かないときは FXAA も併用する）。
    /// </summary>
    public enum AntiAliasingQuality
    {
        Off = 0,
        x2 = 2,
        x4 = 4,
        x8 = 8,
    }
}
