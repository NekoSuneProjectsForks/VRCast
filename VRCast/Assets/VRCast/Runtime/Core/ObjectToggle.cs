using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバターごとに覚える 1 つのオブジェクトの表示の ON/OFF（読込時の状態から切り替えたものだけ）。
    /// path はアバターのルートからの相対パス（ルート自身は対象外）。
    /// </summary>
    [Serializable]
    public class ObjectToggle
    {
        public string path = string.Empty;
        public bool active = true;
    }
}
