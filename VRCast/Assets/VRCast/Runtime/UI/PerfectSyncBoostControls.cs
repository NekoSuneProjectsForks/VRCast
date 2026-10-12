using UnityEngine;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// パーフェクトシンクの「口・頬の動きを強調」の設定（ON/OFF・組ごとの倍率・頬のふくらみの推定のしきい値と今の値）。
    /// トラッキングタブと、パーフェクトシンク化の「実際のトラッキングで試す」で同じものを出す。
    /// </summary>
    public static class PerfectSyncBoostControls
    {
        /// <summary>
        /// 口・頬の強調の設定値の控え（試しに変えた値を保存せずに戻すため）。
        /// </summary>
        public readonly struct Snapshot
        {
            private readonly bool _enabled;
            private readonly float _pucker;
            private readonly float _cheekPuff;
            private readonly float _lips;
            private readonly float _cheekStart;
            private readonly float _cheekFull;

            private Snapshot(AppSettings settings)
            {
                _enabled = settings.trackingPerfectSyncBoost;
                _pucker = settings.trackingBoostPucker;
                _cheekPuff = settings.trackingBoostCheekPuff;
                _lips = settings.trackingBoostLips;
                _cheekStart = settings.trackingCheekPuffStart;
                _cheekFull = settings.trackingCheekPuffFull;
            }

            /// <summary>
            /// 今の設定値を控える。
            /// </summary>
            public static Snapshot Capture(AppSettings settings)
            {
                return new Snapshot(settings);
            }

            /// <summary>
            /// 控えた値へ設定を戻す。
            /// </summary>
            public void Restore(AppSettings settings)
            {
                settings.trackingPerfectSyncBoost = _enabled;
                settings.trackingBoostPucker = _pucker;
                settings.trackingBoostCheekPuff = _cheekPuff;
                settings.trackingBoostLips = _lips;
                settings.trackingCheekPuffStart = _cheekStart;
                settings.trackingCheekPuffFull = _cheekFull;
            }

            /// <summary>
            /// 今の設定値が控えと同じなら true（変えていない）。
            /// </summary>
            public bool Matches(AppSettings settings)
            {
                return Equals(Capture(settings));
            }
        }

        /// <summary>
        /// 設定を描く。driver があれば頬の幅の今の広がりと推定値を出す（しきい値を合わせる目安）。
        /// </summary>
        public static void Draw(AppSettings settings, FaceTrackingDriver driver)
        {
            // 口・頬の強調の ON/OFF（パーフェクトシンクで動かしている間だけ効く）
            settings.trackingPerfectSyncBoost = GUILayout.Toggle(
                settings.trackingPerfectSyncBoost,
                Loc.T("Boost mouth and cheeks", "口・頬の動きを強調", "입·볼 움직임 강조", "增强嘴部和脸颊动作", "增強嘴部和臉頰動作"));
            GuiControls.Hint(Loc.T(
                "Only while perfect sync is driving the face. Strengthens pouting, puffed cheeks and lip movements that the camera tends to pick up weakly.",
                "パーフェクトシンクで顔を動かしている間だけ効きます。カメラでは弱く出やすい、ふくれっ面（口をとがらせる）・頬をふくらませる・唇の動きを強めます。",
                "퍼펙트 싱크로 얼굴을 움직이는 동안에만 적용됩니다. 카메라에서 약하게 잡히기 쉬운 입 내밀기·볼 부풀리기·입술 움직임을 강하게 합니다.",
                "仅在完美同步驱动面部时生效。增强摄像头容易识别偏弱的嘟嘴、鼓腮和嘴唇动作。",
                "僅在完美同步驅動臉部時生效。增強攝影機容易辨識偏弱的嘟嘴、鼓腮和嘴唇動作。"));

            // 無効時は倍率・しきい値を出さない
            if (!settings.trackingPerfectSyncBoost)
            {
                return;
            }

            DrawGains(settings);
            DrawCheekEstimate(settings, driver);
        }

        private static void DrawGains(AppSettings settings)
        {
            // 組ごとの倍率（1 = そのまま）
            float max = AppSettings.MaxPerfectSyncBoost;
            settings.trackingBoostPucker = GuiControls.Slider(
                Loc.T("Pout", "ふくれっ面", "입 내밀기", "嘟嘴", "嘟嘴"),
                settings.trackingBoostPucker, 1f, max, "F1");
            settings.trackingBoostCheekPuff = GuiControls.Slider(
                Loc.T("Puffed cheeks", "頬をふくらませる", "볼 부풀리기", "鼓腮", "鼓腮"),
                settings.trackingBoostCheekPuff, 1f, max, "F1");
            settings.trackingBoostLips = GuiControls.Slider(
                Loc.T("Lips", "唇", "입술", "嘴唇", "嘴唇"),
                settings.trackingBoostLips, 1f, max, "F1");
            GuiControls.Hint(Loc.T(
                "Raise a value if the shape barely moves, lower it if it moves too easily.",
                "動きが小さければ値を上げ、すぐ動きすぎるなら下げてください。",
                "움직임이 작으면 값을 올리고, 너무 쉽게 움직이면 낮추세요.",
                "动作太小时调高，太容易动时调低。",
                "動作太小時調高，太容易動時調低。"));
        }

        private static void DrawCheekEstimate(AppSettings settings, FaceTrackingDriver driver)
        {
            // 頬のふくらみの推定のしきい値（頬の幅が真顔より何 % 広がったら出し始め、何 % で最大か）
            GUILayout.Label(Loc.T("Puffed cheek detection (camera / MediaPipe only)",
                "頬のふくらみの検出（カメラ・MediaPipe のみ）", "볼 부풀리기 감지 (카메라·MediaPipe만)",
                "鼓腮检测（仅限摄像头・MediaPipe）", "鼓腮偵測（僅限攝影機・MediaPipe）"));
            float max = AppSettings.MaxCheekWidenPercent;
            settings.trackingCheekPuffStart = GuiControls.Slider(
                "  " + Loc.T("Starts at (%)", "出し始め（%）", "시작 (%)", "开始 (%)", "開始 (%)"),
                settings.trackingCheekPuffStart, 0f, max - AppSettings.MinCheekWidenSpan, "F1");
            settings.trackingCheekPuffFull = GuiControls.Slider(
                "  " + Loc.T("Full at (%)", "最大（%）", "최대 (%)", "最大 (%)", "最大 (%)"),
                settings.trackingCheekPuffFull, 0f, max, "F1");

            // 最大は出し始めより必ず大きくする（逆転すると推定が反転するため）
            settings.trackingCheekPuffFull = Mathf.Max(settings.trackingCheekPuffFull,
                settings.trackingCheekPuffStart + AppSettings.MinCheekWidenSpan);

            // 今の広がりと推定値（トラッキング中だけ。真顔と頬をふくらませたときの値を見てしきい値を決める）
            if (driver != null && driver.IsTracking)
            {
                float widen = driver.CheekWiden * 100f;
                float puff = driver.CheekPuffEstimate;
                GuiControls.Status(Loc.T($"Now: {widen:F1}% wider, puff {puff:F2}",
                    $"今: {widen:F1}% 広い、ふくらみ {puff:F2}", $"현재: {widen:F1}% 넓음, 부풀림 {puff:F2}",
                    $"当前: 宽 {widen:F1}%，鼓腮 {puff:F2}", $"目前: 寬 {widen:F1}%，鼓腮 {puff:F2}"), puff > 0f);
            }

            GuiControls.Hint(Loc.T(
                "Measures how much wider your lower cheeks are than your neutral face. Keep a neutral face for a few seconds after starting. Set Starts at a little above the value with a neutral face, and Full at about the value with puffed cheeks.",
                "頬の下側の幅が真顔よりどれだけ広いかで測ります。開始後の数秒は真顔にしておいてください。「出し始め」は真顔のときの値より少し上に、「最大」は頬をふくらませたときの値くらいにします。",
                "볼 아래쪽 폭이 무표정일 때보다 얼마나 넓은지로 측정합니다. 시작 후 몇 초 동안 무표정을 유지하세요. '시작'은 무표정일 때 값보다 조금 위로, '최대'는 볼을 부풀렸을 때 값 정도로 설정합니다.",
                "根据脸颊下侧比面无表情时宽多少来测量。开始后的几秒请保持面无表情。“开始”设为比面无表情时的数值稍高，“最大”设为鼓腮时的数值左右。",
                "依臉頰下側比面無表情時寬多少來測量。開始後的幾秒請保持面無表情。「開始」設為比面無表情時的數值稍高，「最大」設為鼓腮時的數值左右。"));
        }
    }
}
