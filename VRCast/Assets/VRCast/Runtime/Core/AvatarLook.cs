using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバターごとに覚える見た目の設定（ライト・アバターの明るさ・輪郭線の太さ・待機ポーズ・向き）。
    /// 範囲の補正は適用後の AppSettings.Sanitize が行う。
    /// </summary>
    [Serializable]
    public struct AvatarLook
    {
        public float lightIntensity;
        public float lightYaw;
        public float lightPitch;
        public float lightTemperature;
        public float ambientIntensity;
        public float avatarBrightness;
        public float poseArmDown;
        public float poseElbowBend;
        public float avatarYaw;

        // 輪郭線の太さ。以前の版の記録には無く 0 と読まれるため、hasOutlineWidth が false なら既定（1）として扱う
        public bool hasOutlineWidth;
        public float outlineWidth;

        // 腕の向きの微調整。以前の版の記録には無いため、hasArmAdjust が false なら既定（開き 12 度・前後とひねり 0）として扱う
        public bool hasArmAdjust;
        public float poseArmSpread;
        public float poseArmForwardLeft;
        public float poseArmForwardRight;
        public float poseArmTwistLeft;
        public float poseArmTwistRight;

        // 最新ライティング（Fill / Rim / IBL / 影 / 映え）。以前の版には無いため hasModernLighting が false なら既定のまま残す
        public bool hasModernLighting;
        public float fillIntensity;
        public float fillYaw;
        public float fillPitch;
        public float fillTemperature;
        public float rimIntensity;
        public float rimYaw;
        public float rimPitch;
        public float rimTemperature;
        public float iblIntensity;
        public IblPreset iblPreset;
        public bool softShadows;
        public float shadowStrength;
        public float shadowSoftness;
        public bool bloomEnabled;
        public float bloomIntensity;
        public float bloomThreshold;
        public bool tonemapEnabled;
        public float tonemapStrength;

        /// <summary>
        /// 現在の設定から取り出す。
        /// </summary>
        public static AvatarLook From(AppSettings settings)
        {
            return new AvatarLook
            {
                lightIntensity = settings.lightIntensity,
                lightYaw = settings.lightYaw,
                lightPitch = settings.lightPitch,
                lightTemperature = settings.lightTemperature,
                ambientIntensity = settings.ambientIntensity,
                avatarBrightness = settings.avatarBrightness,
                poseArmDown = settings.poseArmDown,
                poseElbowBend = settings.poseElbowBend,
                avatarYaw = settings.avatarYaw,
                hasOutlineWidth = true,
                outlineWidth = settings.outlineWidth,
                hasArmAdjust = true,
                poseArmSpread = settings.poseArmSpread,
                poseArmForwardLeft = settings.poseArmForwardLeft,
                poseArmForwardRight = settings.poseArmForwardRight,
                poseArmTwistLeft = settings.poseArmTwistLeft,
                poseArmTwistRight = settings.poseArmTwistRight,
                hasModernLighting = true,
                fillIntensity = settings.fillIntensity,
                fillYaw = settings.fillYaw,
                fillPitch = settings.fillPitch,
                fillTemperature = settings.fillTemperature,
                rimIntensity = settings.rimIntensity,
                rimYaw = settings.rimYaw,
                rimPitch = settings.rimPitch,
                rimTemperature = settings.rimTemperature,
                iblIntensity = settings.iblIntensity,
                iblPreset = settings.iblPreset,
                softShadows = settings.softShadows,
                shadowStrength = settings.shadowStrength,
                shadowSoftness = settings.shadowSoftness,
                bloomEnabled = settings.bloomEnabled,
                bloomIntensity = settings.bloomIntensity,
                bloomThreshold = settings.bloomThreshold,
                tonemapEnabled = settings.tonemapEnabled,
                tonemapStrength = settings.tonemapStrength,
            };
        }

        /// <summary>
        /// 設定へ書き戻し、範囲外の値を補正する（反映は呼び出し側）。
        /// </summary>
        public void ApplyTo(AppSettings settings)
        {
            settings.lightIntensity = lightIntensity;
            settings.lightYaw = lightYaw;
            settings.lightPitch = lightPitch;
            settings.lightTemperature = lightTemperature;
            settings.ambientIntensity = ambientIntensity;
            settings.avatarBrightness = avatarBrightness;
            settings.poseArmDown = poseArmDown;
            settings.poseElbowBend = poseElbowBend;
            settings.avatarYaw = avatarYaw;
            // 記録の無い以前の版の見た目はマテリアルのままの太さにする
            settings.outlineWidth = hasOutlineWidth ? outlineWidth : 1f;
            // 記録の無い以前の版の腕の向きは既定にする
            settings.poseArmSpread = hasArmAdjust ? poseArmSpread : AppSettings.DefaultPoseArmSpread;
            settings.poseArmForwardLeft = hasArmAdjust ? poseArmForwardLeft : 0f;
            settings.poseArmForwardRight = hasArmAdjust ? poseArmForwardRight : 0f;
            settings.poseArmTwistLeft = hasArmAdjust ? poseArmTwistLeft : 0f;
            settings.poseArmTwistRight = hasArmAdjust ? poseArmTwistRight : 0f;

            if (hasModernLighting)
            {
                settings.fillIntensity = fillIntensity;
                settings.fillYaw = fillYaw;
                settings.fillPitch = fillPitch;
                settings.fillTemperature = fillTemperature;
                settings.rimIntensity = rimIntensity;
                settings.rimYaw = rimYaw;
                settings.rimPitch = rimPitch;
                settings.rimTemperature = rimTemperature;
                settings.iblIntensity = iblIntensity;
                settings.iblPreset = iblPreset;
                settings.softShadows = softShadows;
                settings.shadowStrength = shadowStrength;
                settings.shadowSoftness = shadowSoftness;
                settings.bloomEnabled = bloomEnabled;
                settings.bloomIntensity = bloomIntensity;
                settings.bloomThreshold = bloomThreshold;
                settings.tonemapEnabled = tonemapEnabled;
                settings.tonemapStrength = tonemapStrength;
            }

            settings.Sanitize();
        }

        /// <summary>
        /// 全要素が有限値なら true（壊れた設定ファイルの値を使わないため）。
        /// </summary>
        public bool IsFinite => IsFiniteValue(lightIntensity) && IsFiniteValue(lightYaw) && IsFiniteValue(lightPitch)
            && IsFiniteValue(lightTemperature) && IsFiniteValue(ambientIntensity) && IsFiniteValue(avatarBrightness)
            && IsFiniteValue(poseArmDown) && IsFiniteValue(poseElbowBend) && IsFiniteValue(avatarYaw)
            && IsFiniteValue(outlineWidth) && IsFiniteValue(poseArmSpread) && IsFiniteValue(poseArmForwardLeft)
            && IsFiniteValue(poseArmForwardRight) && IsFiniteValue(poseArmTwistLeft) && IsFiniteValue(poseArmTwistRight)
            && (!hasModernLighting || (IsFiniteValue(fillIntensity) && IsFiniteValue(fillYaw) && IsFiniteValue(fillPitch)
                && IsFiniteValue(fillTemperature) && IsFiniteValue(rimIntensity) && IsFiniteValue(rimYaw)
                && IsFiniteValue(rimPitch) && IsFiniteValue(rimTemperature) && IsFiniteValue(iblIntensity)
                && IsFiniteValue(shadowStrength) && IsFiniteValue(shadowSoftness) && IsFiniteValue(bloomIntensity)
                && IsFiniteValue(bloomThreshold) && IsFiniteValue(tonemapStrength)));

        /// <summary>
        /// 全要素が同じなら true（変化したときだけ記録するための比較）。
        /// </summary>
        public bool SameAs(AvatarLook other)
        {
            return lightIntensity == other.lightIntensity && lightYaw == other.lightYaw
                && lightPitch == other.lightPitch && lightTemperature == other.lightTemperature
                && ambientIntensity == other.ambientIntensity && avatarBrightness == other.avatarBrightness
                && poseArmDown == other.poseArmDown && poseElbowBend == other.poseElbowBend
                && avatarYaw == other.avatarYaw && hasOutlineWidth == other.hasOutlineWidth
                && outlineWidth == other.outlineWidth && hasArmAdjust == other.hasArmAdjust
                && poseArmSpread == other.poseArmSpread && poseArmForwardLeft == other.poseArmForwardLeft
                && poseArmForwardRight == other.poseArmForwardRight && poseArmTwistLeft == other.poseArmTwistLeft
                && poseArmTwistRight == other.poseArmTwistRight
                && hasModernLighting == other.hasModernLighting
                && fillIntensity == other.fillIntensity && fillYaw == other.fillYaw && fillPitch == other.fillPitch
                && fillTemperature == other.fillTemperature && rimIntensity == other.rimIntensity
                && rimYaw == other.rimYaw && rimPitch == other.rimPitch && rimTemperature == other.rimTemperature
                && iblIntensity == other.iblIntensity && iblPreset == other.iblPreset
                && softShadows == other.softShadows && shadowStrength == other.shadowStrength
                && shadowSoftness == other.shadowSoftness && bloomEnabled == other.bloomEnabled
                && bloomIntensity == other.bloomIntensity && bloomThreshold == other.bloomThreshold
                && tonemapEnabled == other.tonemapEnabled && tonemapStrength == other.tonemapStrength;
        }

        private static bool IsFiniteValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
