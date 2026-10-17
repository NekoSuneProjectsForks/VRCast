namespace VRCast.Core
{
    /// <summary>
    /// アンチエイリアスの方式。Unspecified は古い設定ファイル用（Sanitize で MSAA 等へ直す）。
    /// </summary>
    public enum AntiAliasingMode
    {
        Unspecified = 0,
        Off = 1,
        Msaa = 2,
        Fxaa = 3,
        Supersample = 4,
    }
}
