using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバターごとに覚える 1 つのパーティクルの表示の ON/OFF。
    /// path はアバターのルートからの相対パス（空ならルート自身）。
    /// </summary>
    [Serializable]
    public class ParticleToggle
    {
        public string path = string.Empty;
        public bool enabled = true;
    }
}
