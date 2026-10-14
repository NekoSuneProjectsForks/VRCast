using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// 改変の適用後の複製で、衣装がアバターの体にうまく付いていない兆候を探して記録する。
    /// ・衣装の腕・脚のボーンの向きがアバターと大きく違う（T ポーズの衣装を A ポーズのアバターに着せた、またはその逆）
    /// ・ボーンが体（Hips の下）に 1 本も無く、体に追従しないメッシュ
    /// 姿勢の違いは、指定されれば衣装のボーンを肩・股関節を軸にアバターの向きへ回して直す（メッシュはボーンの動きで変形するため、
    /// 袖なども一緒にアバターの姿勢へ曲がる）。直さない場合も書き出しは続ける。
    /// </summary>
    public static class OutfitPoseChecker
    {
        // 衣装のボーンの向きがアバターとこれ以上違えば姿勢が合っていないとみなす（度）
        private const float MaxAngle = 15f;

        // 1 つの警告に並べるボーン・メッシュの数の上限
        private const int MaxListed = 12;

        // 向きを比べる腕・脚（付け根 → 中間 → 先端）。先端（手・足）から先は中間の回転についていく
        private static readonly HumanBodyBones[][] Chains =
        {
            new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
            new[] { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
            new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot },
            new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot },
        };

        /// <summary>
        /// 結果。Problems は直さずに警告した問題の数、Fixed は姿勢を直した衣装の数。
        /// </summary>
        public struct Result
        {
            public int Problems;
            public int Fixed;
        }

        // 1 つの衣装（ボーン名の前後の文字列で見分ける）
        private readonly struct Outfit : IEquatable<Outfit>
        {
            public readonly string Prefix;
            public readonly string Suffix;

            public Outfit(string prefix, string suffix)
            {
                Prefix = prefix;
                Suffix = suffix;
            }

            public string BoneName(Transform avatarBone) => Prefix + avatarBone.name + Suffix;

            public bool Equals(Outfit other) => Prefix == other.Prefix && Suffix == other.Suffix;

            public override bool Equals(object obj) => obj is Outfit other && Equals(other);

            public override int GetHashCode() => (Prefix + "\u0001" + Suffix).GetHashCode();
        }

        // 衣装の姿勢の付け直し（from を軸に rotation だけ回し、to へ動かす）
        private readonly struct Rigid
        {
            public readonly Vector3 From;
            public readonly Vector3 To;
            public readonly Quaternion Rotation;

            public Rigid(Vector3 from, Vector3 to, Quaternion rotation)
            {
                From = from;
                To = to;
                Rotation = rotation;
            }

            public Vector3 Apply(Vector3 point) => To + Rotation * (point - From);
        }

        public static Result Check(GameObject root, bool fixPoses)
        {
            var animator = root.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                return default;
            }

            Result result = CheckLimbPoses(root, animator, fixPoses);
            result.Problems += CheckDetachedMeshes(root, animator.GetBoneTransform(HumanBodyBones.Hips));
            return result;
        }

        private static Result CheckLimbPoses(GameObject root, Animator animator, bool fixPoses)
        {
            var result = new Result();
            foreach (Outfit outfit in FindOutfits(animator))
            {
                // 腕・脚ごとに向きの違いを測る
                var differences = new List<string>();
                float worst = 0f;
                foreach (HumanBodyBones[] chain in Chains)
                {
                    for (int i = 0; i + 1 < chain.Length; i++)
                    {
                        float angle = MeasureAngle(animator, outfit, chain[i], chain[i + 1]);
                        if (angle >= MaxAngle)
                        {
                            differences.Add($"{chain[i]}: {angle:F0}°");
                        }

                        worst = Mathf.Max(worst, angle);
                    }
                }

                if (differences.Count == 0)
                {
                    continue;
                }

                string name = OutfitName(root, animator, outfit);
                if (fixPoses)
                {
                    foreach (HumanBodyBones[] chain in Chains)
                    {
                        FixChain(animator, outfit, chain);
                    }

                    result.Fixed++;
                    ExportLog.Info(
                        $"Matched the pose of \"{name}\" to the avatar (arms/legs differed by up to {worst:F0}°). " +
                        "Check the preview to see that it looks right.",
                        $"「{name}」の姿勢をアバターに合わせました（腕・脚の向きが最大 {worst:F0}° 違っていました）。" +
                        "見た目がおかしくないかプレビューで確かめてください。",
                        string.Join("\n", differences));
                    continue;
                }

                result.Problems++;
                ExportLog.Warn(
                    $"The pose of \"{name}\" does not match the avatar (arms/legs differ by up to {worst:F0}°). " +
                    "A T-pose outfit may have been put on an A-pose avatar (or the other way round), " +
                    "so sleeves etc. may float away from the body in the app. Turn on \"Fix outfit poses that do not match " +
                    "the avatar\" and export again, or match the outfit's bones to the avatar's pose in Unity " +
                    "(e.g. with MA Setup Outfit or an outfit fitting tool).",
                    $"「{name}」の姿勢がアバターと合っていません（腕・脚の向きが最大 {worst:F0}° 違います）。" +
                    "T ポーズの衣装を A ポーズのアバターに着せた（またはその逆の）可能性があり、" +
                    "アプリでは袖などが体から浮いて見えることがあります。「アバターと合わない衣装の姿勢を直す」を ON にして書き出し直すか、" +
                    "Unity 上で衣装のボーンをアバターの姿勢に合わせてください（MA の Setup Outfit や着せ替えツールなど）。",
                    string.Join("\n", differences));
            }

            return result;
        }

        private static List<Outfit> FindOutfits(Animator animator)
        {
            // 腕・脚の付け根の直下にある、同じボーン名を含む衣装のボーン（MA・付け替えで統合されたもの）から衣装を見分ける
            var outfits = new List<Outfit>();
            foreach (HumanBodyBones[] chain in Chains)
            {
                Transform bone = animator.GetBoneTransform(chain[0]);
                Transform next = animator.GetBoneTransform(chain[1]);
                if (bone == null)
                {
                    continue;
                }

                foreach (Transform child in bone)
                {
                    int at = child.name.IndexOf(bone.name, StringComparison.OrdinalIgnoreCase);
                    if (at < 0 || child == next)
                    {
                        continue;
                    }

                    var outfit = new Outfit(child.name.Substring(0, at), child.name.Substring(at + bone.name.Length));
                    if (!outfits.Contains(outfit))
                    {
                        outfits.Add(outfit);
                    }
                }
            }

            return outfits;
        }

        private static Transform FindOutfitBone(Transform avatarBone, Outfit outfit)
        {
            return avatarBone == null ? null : avatarBone.Find(outfit.BoneName(avatarBone));
        }

        private static float MeasureAngle(Animator animator, Outfit outfit, HumanBodyBones from, HumanBodyBones to)
        {
            // 衣装とアバターで、関節から次の関節への方向を比べる（ボーンの軸の取り方の違いに影響されない）
            Transform bone = animator.GetBoneTransform(from);
            Transform next = animator.GetBoneTransform(to);
            Transform outfitBone = FindOutfitBone(bone, outfit);
            Transform outfitNext = FindOutfitBone(next, outfit);
            if (outfitBone == null || outfitNext == null)
            {
                return 0f;
            }

            Vector3 avatarDirection = next.position - bone.position;
            Vector3 outfitDirection = outfitNext.position - outfitBone.position;
            return avatarDirection.sqrMagnitude < 1e-8f || outfitDirection.sqrMagnitude < 1e-8f
                ? 0f
                : Vector3.Angle(avatarDirection, outfitDirection);
        }

        private static void FixChain(Animator animator, Outfit outfit, HumanBodyBones[] chain)
        {
            FixChain(Array.ConvertAll(chain, animator.GetBoneTransform), outfit.Prefix, outfit.Suffix);
        }

        /// <summary>
        /// アバターの腕・脚のボーン（付け根 → 中間 → 先端）の直下にある「prefix + ボーン名 + suffix」の衣装のボーンを、
        /// 付け根を軸にアバターの向きへ回す。先端から先の衣装のボーンは先端と同じだけ動かす。
        /// </summary>
        public static void FixChain(Transform[] avatarBones, string prefix, string suffix)
        {
            var outfit = new Outfit(prefix, suffix);

            // 付け根から順に、衣装の関節の位置と向きを控える（動かす前に全部読む）
            int count = avatarBones.Length;
            var outfitBones = new Transform[count];
            var positions = new Vector3[count];
            var rotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                outfitBones[i] = FindOutfitBone(avatarBones[i], outfit);
                if (outfitBones[i] != null)
                {
                    positions[i] = outfitBones[i].position;
                    rotations[i] = outfitBones[i].rotation;
                }
            }

            if (outfitBones[0] == null)
            {
                return;
            }

            // 先端（手・足）から先の衣装のボーン（指など）も、動かす前の姿勢を控える
            var tipBones = new List<(Transform bone, Vector3 position, Quaternion rotation)>();
            Transform tip = avatarBones[count - 1];
            if (tip != null)
            {
                foreach (Transform avatarBone in tip.GetComponentsInChildren<Transform>(true))
                {
                    Transform outfitBone = avatarBone == tip ? null : FindOutfitBone(avatarBone, outfit);
                    if (outfitBone != null)
                    {
                        tipBones.Add((outfitBone, outfitBone.position, outfitBone.rotation));
                    }
                }
            }

            // 付け根は位置を保って回し、それより先は直した親の先へ動かしてから回す（回転は親から積み重ねる）
            var rigid = new Rigid(positions[0], positions[0], Quaternion.identity);
            for (int i = 0; i < count; i++)
            {
                if (outfitBones[i] == null)
                {
                    break;
                }

                Vector3 to = i == 0 ? positions[0] : rigid.Apply(positions[i]);
                Quaternion rotation = rigid.Rotation;
                if (i + 1 < count && outfitBones[i + 1] != null && avatarBones[i + 1] != null)
                {
                    Vector3 outfitDirection = rotation * (positions[i + 1] - positions[i]);
                    Vector3 avatarDirection = avatarBones[i + 1].position - avatarBones[i].position;
                    if (outfitDirection.sqrMagnitude > 1e-8f && avatarDirection.sqrMagnitude > 1e-8f)
                    {
                        rotation = Quaternion.FromToRotation(outfitDirection, avatarDirection) * rotation;
                    }
                }

                rigid = new Rigid(positions[i], to, rotation);
                outfitBones[i].SetPositionAndRotation(to, rotation * rotations[i]);
            }

            // 先端から先は先端と同じ動きにする
            foreach ((Transform bone, Vector3 position, Quaternion rotation) in tipBones)
            {
                bone.SetPositionAndRotation(rigid.Apply(position), rigid.Rotation * rotation);
            }
        }

        private static int CheckDetachedMeshes(GameObject root, Transform hips)
        {
            if (hips == null)
            {
                return 0;
            }

            // ボーンを持つのに、体（Hips とその下）のボーンを 1 本も使っていないメッシュ
            var detached = new List<string>();
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool hasBones = false;
                bool follows = false;
                foreach (Transform bone in renderer.bones)
                {
                    if (bone == null)
                    {
                        continue;
                    }

                    hasBones = true;
                    if (bone == hips || bone.IsChildOf(hips))
                    {
                        follows = true;
                        break;
                    }
                }

                if (hasBones && !follows)
                {
                    detached.Add(ReflectionUtility.PathOf(renderer.transform, root.transform));
                }
            }

            if (detached.Count == 0)
            {
                return 0;
            }

            ExportLog.Warn(
                $"{detached.Count} mesh(es) do not use any of the avatar's body bones and will not follow the body in the app. " +
                "The outfit's armature may not have been merged (check the Merge Armature / Armature Link settings).",
                $"{detached.Count} 個のメッシュがアバターの体のボーンを使っておらず、アプリでは体に追従しません。" +
                "衣装のアーマチュアが統合されていない可能性があります（Merge Armature・Armature Link の設定を確かめてください）。",
                List(detached));
            return 1;
        }

        private static string OutfitName(GameObject root, Animator animator, Outfit outfit)
        {
            // 前後の文字列があればそれを、無ければ衣装のボーンを使うメッシュの名前を衣装の名前にする
            string affix = (outfit.Prefix + outfit.Suffix).Trim(' ', '_', '.', '-', '(', ')');
            if (affix.Length > 0)
            {
                return affix;
            }

            Transform outfitBone = null;
            foreach (HumanBodyBones[] chain in Chains)
            {
                outfitBone = outfitBone != null ? outfitBone : FindOutfitBone(animator.GetBoneTransform(chain[0]), outfit);
            }

            if (outfitBone != null)
            {
                foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (Array.IndexOf(renderer.bones, outfitBone) >= 0)
                    {
                        return renderer.name;
                    }
                }
            }

            return outfit.BoneName(animator.GetBoneTransform(HumanBodyBones.Hips) ?? root.transform);
        }

        private static string List(List<string> items)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < items.Count && i < MaxListed; i++)
            {
                builder.Append(i > 0 ? "\n" : string.Empty).Append(items[i]);
            }

            if (items.Count > MaxListed)
            {
                builder.Append($"\n... (+{items.Count - MaxListed})");
            }

            return builder.ToString();
        }
    }
}
