using System;
using UnityEngine;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 生成で使う重み（なめらかな減衰）と、折れ線の補間。
    /// </summary>
    public static class FaceShapeMath
    {
        /// <summary>
        /// 0〜1 に収めて、両端でなめらかにつながる曲線（smoothstep）を通す。
        /// </summary>
        public static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// 距離 distance が 0 で 1、radius 以上で 0 になる、なめらかな減衰。
        /// </summary>
        public static float Falloff(float distance, float radius)
        {
            return radius <= 0f ? 0f : Smooth01(1f - distance / radius);
        }

        /// <summary>
        /// 中心 center・半径 rx（横）, ry（縦）の楕円の中で 1 に近く、外周で 0 になる重み（局所座標の x, y だけを使う）。
        /// </summary>
        public static float Ellipse(Vector3 point, Vector3 center, float rx, float ry)
        {
            float dx = (point.x - center.x) / Mathf.Max(rx, 1e-9f);
            float dy = (point.y - center.y) / Mathf.Max(ry, 1e-9f);
            return Falloff(Mathf.Sqrt(dx * dx + dy * dy), 1f);
        }

        /// <summary>
        /// 奥行き z が基準 front より depth 以上奥なら 0、depth の半分より手前なら 1（後頭部・耳の裏を動かさない）。
        /// </summary>
        public static float FrontMask(float z, float front, float depth)
        {
            return Smooth01((z - (front - depth)) / Mathf.Max(depth * 0.5f, 1e-9f));
        }
    }

    /// <summary>
    /// x を鍵とする折れ線（鍵は昇順にそろえる）。範囲外は端の値。
    /// </summary>
    public sealed class Polyline
    {
        private readonly float[] _keys;
        private readonly float[] _values;

        public Polyline(float[] keys, float[] values)
        {
            // 鍵の昇順に並べ替えて持つ
            _keys = (float[])keys.Clone();
            _values = (float[])values.Clone();
            Array.Sort(_keys, _values);
        }

        /// <summary>
        /// x での値（隣り合う点の間は直線で補間）。
        /// </summary>
        public float Evaluate(float x)
        {
            // 範囲外は端の値
            if (x <= _keys[0])
            {
                return _values[0];
            }

            int last = _keys.Length - 1;
            if (x >= _keys[last])
            {
                return _values[last];
            }

            // x を挟む区間で補間
            for (int i = 1; i <= last; i++)
            {
                if (x <= _keys[i])
                {
                    float span = _keys[i] - _keys[i - 1];
                    float t = span > 1e-9f ? (x - _keys[i - 1]) / span : 0f;
                    return Mathf.Lerp(_values[i - 1], _values[i], t);
                }
            }

            return _values[last];
        }
    }
}
