namespace VRCast.Core
{
    /// <summary>
    /// ライティングの系統。レガシーは 2.x 相当（太陽光 1 灯 + 平坦な環境光）、最新は三点・影・IBL・ポスト。
    /// </summary>
    public enum LightingSystem
    {
        Legacy = 0,
        Modern = 1,
    }
}
