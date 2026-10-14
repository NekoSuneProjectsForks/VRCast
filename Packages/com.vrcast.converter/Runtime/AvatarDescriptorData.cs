using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/descriptor.json の内容。VRCAvatarDescriptor のうち Runtime で使う設定（リップシンク・まぶた）。
    /// </summary>
    [Serializable]
    public class AvatarDescriptorData : IMetadata
    {
        // VRChat の Viseme 数（sil, PP, FF, TH, DD, kk, CH, SS, nn, RR, aa, E, I, O, U）
        public const int VisemeCount = 15;

        // 母音の Viseme（aa = あ、E = え、I = い、O = お、U = う）のインデックス。aa は口を開く代表としても使う
        public const int VisemeAa = 10;
        public const int VisemeE = 11;
        public const int VisemeI = 12;
        public const int VisemeO = 13;
        public const int VisemeU = 14;

        public LipSyncData lipSync = new LipSyncData();
        public EyelidData eyelids = new EyelidData();

        public string Validate()
        {
            // 各ブロックの欠落
            if (lipSync == null || eyelids == null)
            {
                return "lipSync and eyelids are required.";
            }

            // モードは既知の値のみ
            if (lipSync.mode != LipSyncData.ModeNone && lipSync.mode != LipSyncData.ModeVisemeBlendShape
                && lipSync.mode != LipSyncData.ModeJawFlapBlendShape && lipSync.mode != LipSyncData.ModeVisemeAnimation)
            {
                return $"Unknown lipSync.mode '{lipSync.mode}'.";
            }

            // パスと BlendShape 名は空可・長さ上限あり
            if (!ExpressionSet.IsValidString(lipSync.meshPath, true)
                || !ExpressionSet.IsValidString(lipSync.mouthOpenBlendShape, true)
                || !ExpressionSet.IsValidString(eyelids.meshPath, true)
                || !ExpressionSet.IsValidString(eyelids.winkLeftBlendShape, true)
                || !ExpressionSet.IsValidString(eyelids.winkRightBlendShape, true))
            {
                return "Descriptor contains an invalid string.";
            }

            // まばたき BlendShape は上限件数まで、名前は必須
            if (eyelids.blinkBlendShapes == null || eyelids.blinkBlendShapes.Length > EyelidData.MaxBlinkBlendShapes)
            {
                return $"eyelids.blinkBlendShapes must have 0-{EyelidData.MaxBlinkBlendShapes} items.";
            }

            foreach (string shape in eyelids.blinkBlendShapes)
            {
                if (!ExpressionSet.IsValidString(shape, false))
                {
                    return "eyelids.blinkBlendShapes contains an invalid name.";
                }
            }

            // Viseme 名は 0 件または 15 件
            if (lipSync.visemes == null || (lipSync.visemes.Length != 0 && lipSync.visemes.Length != VisemeCount))
            {
                return $"lipSync.visemes must have 0 or {VisemeCount} items.";
            }

            foreach (string viseme in lipSync.visemes)
            {
                if (!ExpressionSet.IsValidString(viseme, true))
                {
                    return "lipSync.visemes contains an invalid name.";
                }
            }

            return ValidateVisemePoses();
        }

        private string ValidateVisemePoses()
        {
            // Viseme ごとの口の形は 0 件または 15 件（visemeAnimation 方式では 15 件必須）
            VisemePose[] poses = lipSync.visemePoses;
            if (poses == null || (poses.Length != 0 && poses.Length != VisemeCount)
                || (lipSync.mode == LipSyncData.ModeVisemeAnimation && poses.Length != VisemeCount))
            {
                return $"lipSync.visemePoses must have 0 or {VisemeCount} items.";
            }

            foreach (VisemePose pose in poses)
            {
                if (pose == null || pose.values == null || pose.values.Length > VisemePose.MaxValues)
                {
                    return $"lipSync.visemePoses items must have 0-{VisemePose.MaxValues} values.";
                }

                // パスは空（ルート）を許可、BlendShape 名は必須、重みは 0〜100
                foreach (BlendShapeValue value in pose.values)
                {
                    if (value == null || !ExpressionSet.IsValidString(value.path, true)
                        || !ExpressionSet.IsValidString(value.blendShape, false)
                        || float.IsNaN(value.weight) || value.weight < 0f || value.weight > 100f)
                    {
                        return "lipSync.visemePoses contains an invalid blend shape value.";
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// リップシンク設定。mode に応じて visemes・mouthOpenBlendShape・visemePoses のどれかを使う。
    /// </summary>
    [Serializable]
    public class LipSyncData
    {
        // 対応するモード（ジョーボーン等は未対応のため none として書き出す）。
        // visemeAnimation は FX の Viseme パラメーターで口を動かすアバター向けで、Viseme ごとの BlendShape の組を使う
        public const string ModeNone = "none";
        public const string ModeVisemeBlendShape = "visemeBlendShape";
        public const string ModeJawFlapBlendShape = "jawFlapBlendShape";
        public const string ModeVisemeAnimation = "visemeAnimation";

        public string mode = ModeNone;
        public string meshPath = string.Empty;
        public string[] visemes = Array.Empty<string>();
        public string mouthOpenBlendShape = string.Empty;
        public VisemePose[] visemePoses = Array.Empty<VisemePose>();
    }

    /// <summary>
    /// 1 つの Viseme で動かす BlendShape と、その Viseme を出し切ったときの重み（0〜100、複数メッシュ可）。
    /// </summary>
    [Serializable]
    public class VisemePose
    {
        // 1 つの Viseme で動かす BlendShape 数の上限
        public const int MaxValues = 64;

        public BlendShapeValue[] values = Array.Empty<BlendShapeValue>();
    }

    /// <summary>
    /// まぶた設定。blinkBlendShapes（左右別の場合は複数）が空ならまばたき不可。
    /// winkLeft / winkRightBlendShape は片目だけ閉じる BlendShape（アバターから見た左右、両方揃った場合のみ。空ならウインク不可）。
    /// blinkBlendShapes と同名でもよい（左右別のまばたき BlendShape をウインクにも使う場合）。
    /// </summary>
    [Serializable]
    public class EyelidData
    {
        // 1 つのメッシュでまばたきに使う BlendShape 数の上限
        public const int MaxBlinkBlendShapes = 4;

        public string meshPath = string.Empty;
        public string[] blinkBlendShapes = Array.Empty<string>();
        public string winkLeftBlendShape = string.Empty;
        public string winkRightBlendShape = string.Empty;
    }
}
