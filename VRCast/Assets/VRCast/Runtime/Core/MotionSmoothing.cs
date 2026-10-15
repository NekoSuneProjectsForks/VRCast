namespace VRCast.Core
{
    /// <summary>
    /// トラッキングの動きのなめらかさ（settings.json には数値で保存するため並び順を変えない）。
    /// 高いほど頭・腕・手・表情の揺れや段差を強く抑えてぬるぬる動き、そのぶんカメラより遅れる。
    /// </summary>
    public enum MotionSmoothing
    {
        // 最低: カメラへの追従を最優先（既定）
        Lowest = 0,

        // 低
        Low = 1,

        // 中: 2.2.1 までと同じ追従の仕方
        Medium = 2,

        // 高
        High = 3,

        // 最高: 遅れは大きいが最もぬるぬる動く
        Highest = 4,
    }
}
