using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// まばたきの .anim の読み込みの失敗理由。
    /// </summary>
    public enum BlinkAnimationError
    {
        None,
        ReadFailed,
        TooLarge,
        NotText,
        NotAnimationClip,
        Malformed,
        NoBlendShapes,
    }

    /// <summary>
    /// .anim（Unity のテキスト形式の AnimationClip）から読んだ、自動まばたき用の BlendShape のカーブ。
    /// ビルドでは .anim を Unity の機能で読み込めないため、テキストを読んで BlendShape（SkinnedMeshRenderer の blendShape.名前）のカーブだけを使う。
    /// 長さのあるクリップはそのまま再生し、長さの無いクリップ（1 コマだけの目を閉じた形）は、閉じた形として自動まばたきの動きに合わせて出す。
    /// </summary>
    public sealed class BlinkAnimation
    {
        // 読み込むファイルの大きさの上限（バイト）
        public const long MaxFileBytes = 16 * 1024 * 1024;

        // バイナリ形式かを調べる先頭の長さ（バイト）
        private const int BinaryProbeBytes = 8192;

        // SkinnedMeshRenderer の classID と、BlendShape のカーブの attribute の接頭辞
        private const int SkinnedMeshRendererClassId = 137;
        private const string BlendShapePrefix = "blendShape.";

        // JSON に書けない無限大の傾き（一定の値を保つキー）を読むための置き換えと、読み戻すときのしきい値
        private static readonly Regex InfinityValue = new Regex(@":[ \t]+(-?)Infinity\b");
        private static readonly Regex NaNValue = new Regex(@":[ \t]+NaN\b");
        private const float InfinityStandIn = 1e38f;

        /// <summary>
        /// 1 つの BlendShape のカーブ（path はアバターのルートからの相対パス、値は 0〜100 の重み）。
        /// </summary>
        public sealed class Track
        {
            public string Path { get; }
            public string BlendShape { get; }
            public AnimationCurve Curve { get; }

            public Track(string path, string blendShape, AnimationCurve curve)
            {
                Path = path;
                BlendShape = blendShape;
                Curve = curve;
            }
        }

        private readonly List<Track> _tracks;

        public IReadOnlyList<Track> Tracks => _tracks;

        /// <summary>
        /// クリップの長さ（秒。最後のキーの時刻）。
        /// </summary>
        public float Length { get; }

        /// <summary>
        /// 長さの無い（目を閉じた形だけの）クリップなら true。
        /// </summary>
        public bool IsPose => Length <= 0f;

        private BlinkAnimation(List<Track> tracks, float length)
        {
            _tracks = tracks;
            Length = length;
        }

        /// <summary>
        /// 1 回のまばたきの長さ（秒）。形だけのクリップは閉じる・閉じたまま・開くの時間の合計。
        /// </summary>
        public float Duration(float close, float hold, float open)
        {
            return IsPose ? close + hold + open : Length;
        }

        /// <summary>
        /// まばたき開始から elapsed 秒の、track の BlendShape の重み（0〜100）。
        /// </summary>
        public float Sample(Track track, float elapsed, float close, float hold, float open)
        {
            return IsPose
                ? track.Curve.Evaluate(0f) * BlinkController.Evaluate(elapsed, close, hold, open)
                : track.Curve.Evaluate(elapsed);
        }

        /// <summary>
        /// .anim ファイルを読む。
        /// </summary>
        public static bool TryLoad(string path, out BlinkAnimation animation, out BlinkAnimationError error)
        {
            animation = null;
            byte[] bytes;
            try
            {
                if (new FileInfo(path).Length > MaxFileBytes)
                {
                    error = BlinkAnimationError.TooLarge;
                    return false;
                }

                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                || e is NotSupportedException)
            {
                error = BlinkAnimationError.ReadFailed;
                return false;
            }

            // バイナリ形式で保存された .anim（Unity の Asset Serialization が Force Binary）は読めない
            int probe = Math.Min(bytes.Length, BinaryProbeBytes);
            for (int i = 0; i < probe; i++)
            {
                if (bytes[i] == 0)
                {
                    error = BlinkAnimationError.NotText;
                    return false;
                }
            }

            return TryParse(Encoding.UTF8.GetString(bytes), out animation, out error);
        }

        /// <summary>
        /// .anim のテキストを読む。BlendShape のカーブが 1 つも無ければ false。
        /// </summary>
        public static bool TryParse(string text, out BlinkAnimation animation, out BlinkAnimationError error)
        {
            animation = null;
            if (string.IsNullOrEmpty(text))
            {
                error = BlinkAnimationError.Malformed;
                return false;
            }

            // Unity の YAML の見出し（%YAML・%TAG・--- !u!74 &7400000）を除き、無限大・NaN を JSON で読める値へ
            var body = new StringBuilder(text.Length);
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("%", StringComparison.Ordinal) && !trimmed.StartsWith("---", StringComparison.Ordinal))
                {
                    body.Append(line).Append('\n');
                }
            }

            string yaml = NaNValue.Replace(InfinityValue.Replace(body.ToString(), ": ${1}1e38"), ": 0");
            if (!SimpleYaml.TryToJson(yaml, out string json))
            {
                error = BlinkAnimationError.Malformed;
                return false;
            }

            // 一番外側が AnimationClip でなければアニメーションではない（JsonUtility は無いキーも空のオブジェクトで埋める）
            if (!JsonObjectText.TryParseObject(json, out var root) || !root.Exists(member => member.Key == "AnimationClip"))
            {
                error = BlinkAnimationError.NotAnimationClip;
                return false;
            }

            AnimDocument document;
            try
            {
                document = JsonUtility.FromJson<AnimDocument>(json);
            }
            catch (ArgumentException)
            {
                error = BlinkAnimationError.Malformed;
                return false;
            }

            if (document == null || document.AnimationClip == null)
            {
                error = BlinkAnimationError.NotAnimationClip;
                return false;
            }

            // 実行時用のカーブ（m_FloatCurves）を使い、無ければエディター用（m_EditorCurves）を使う
            List<AnimFloatCurve> curves = document.AnimationClip.m_FloatCurves;
            List<Track> tracks = ReadTracks(curves, out float length);
            if (tracks.Count == 0)
            {
                tracks = ReadTracks(document.AnimationClip.m_EditorCurves, out length);
            }

            if (tracks.Count == 0)
            {
                error = BlinkAnimationError.NoBlendShapes;
                return false;
            }

            animation = new BlinkAnimation(tracks, length);
            error = BlinkAnimationError.None;
            return true;
        }

        private static List<Track> ReadTracks(List<AnimFloatCurve> curves, out float length)
        {
            var tracks = new List<Track>();
            var seen = new HashSet<(string, string)>();
            length = 0f;
            if (curves == null)
            {
                return tracks;
            }

            foreach (AnimFloatCurve entry in curves)
            {
                // SkinnedMeshRenderer の BlendShape のカーブだけ（同じ BlendShape は最初のもの）
                if (entry == null || entry.classID != SkinnedMeshRendererClassId || entry.attribute == null
                    || !entry.attribute.StartsWith(BlendShapePrefix, StringComparison.Ordinal)
                    || entry.curve?.m_Curve == null || entry.curve.m_Curve.Count == 0)
                {
                    continue;
                }

                string path = entry.path ?? string.Empty;
                string shape = entry.attribute.Substring(BlendShapePrefix.Length);
                if (shape.Length == 0 || !seen.Add((path, shape)))
                {
                    continue;
                }

                var keys = new Keyframe[entry.curve.m_Curve.Count];
                for (int i = 0; i < keys.Length; i++)
                {
                    AnimKey key = entry.curve.m_Curve[i];
                    keys[i] = new Keyframe(key.time, key.value, RestoreInfinity(key.inSlope), RestoreInfinity(key.outSlope),
                        key.inWeight, key.outWeight)
                    {
                        weightedMode = (WeightedMode)key.weightedMode,
                    };
                    length = Mathf.Max(length, key.time);
                }

                tracks.Add(new Track(path, shape, new AnimationCurve(keys)));
            }

            return tracks;
        }

        private static float RestoreInfinity(float slope)
        {
            // 置き換えた無限大を戻す（Unity のカーブは無限大の傾きを一定の値として扱う）
            return slope >= InfinityStandIn * 0.5f ? float.PositiveInfinity
                : slope <= -InfinityStandIn * 0.5f ? float.NegativeInfinity
                : slope;
        }

        // .anim の中身のうち使う部分（JsonUtility で読む。名前は Unity の YAML のキーと同じ）
#pragma warning disable 0649
        [Serializable]
        private class AnimDocument
        {
            public AnimClipData AnimationClip;
        }

        [Serializable]
        private class AnimClipData
        {
            public List<AnimFloatCurve> m_FloatCurves;
            public List<AnimFloatCurve> m_EditorCurves;
        }

        [Serializable]
        private class AnimFloatCurve
        {
            public AnimCurveData curve;
            public string attribute;
            public string path;
            public int classID;
        }

        [Serializable]
        private class AnimCurveData
        {
            public List<AnimKey> m_Curve;
        }

        [Serializable]
        private class AnimKey
        {
            public float time;
            public float value;
            public float inSlope;
            public float outSlope;
            public int weightedMode;
            public float inWeight;
            public float outWeight;
        }
#pragma warning restore 0649
    }
}
