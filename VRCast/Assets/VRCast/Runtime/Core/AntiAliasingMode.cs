namespace VRCast.Core
{
    /// <summary>
    /// アンチエイリアスの方式（高解像度描画は別設定）。Unspecified / 旧 Supersample は Sanitize で直す。
    /// </summary>
    public enum AntiAliasingMode
    {
        Unspecified = 0,
        Off = 1,
        Msaa = 2,
        Fxaa = 3,
        // 以前は方式の一つだった。Sanitize で MSAA + supersample へ移す
        Supersample = 4,
    }
}
