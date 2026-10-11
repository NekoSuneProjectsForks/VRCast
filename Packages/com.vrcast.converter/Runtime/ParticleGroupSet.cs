using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/particle_groups.json の内容。VRChat の FX で表示を ON/OFF しているオブジェクトのうち、
    /// パーティクルを含むもののパス（アバタールートからの相対パス）。Runtime はこの単位でパーティクルをまとめて切り替える。
    /// </summary>
    [Serializable]
    public class ParticleGroupSet : PathListSet
    {
    }
}
