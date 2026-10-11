using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバターごとに覚える 1 つのパーティクルのまとまりの表示の ON/OFF と、利用者が付けた名前。
    /// path はアバターのルートからの相対パス（空ならルート自身）。name が空なら Unity 上の名前を表示する。
    /// </summary>
    [Serializable]
    public class ParticleToggle
    {
        public string path = string.Empty;
        public bool enabled = true;
        public string name = string.Empty;
    }
}
