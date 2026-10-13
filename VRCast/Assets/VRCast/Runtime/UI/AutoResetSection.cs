using UnityEngine;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// 操作・連携タブの自動リセットのカード（ON/OFF・間隔・対象・次のリセットまでの時間）。
    /// </summary>
    public class AutoResetSection
    {
        private readonly AutoResetScheduler _scheduler;
        private readonly AppSettings _settings;

        public AutoResetSection(AutoResetScheduler scheduler, AppSettings settings)
        {
            _scheduler = scheduler;
            _settings = settings;
        }

        public void Draw()
        {
            GuiControls.BeginCard(Loc.T("Auto reset", "自動リセット", "자동 초기화", "自动重置", "自動重設"));
            GuiControls.Hint(Loc.T(
                "Resets at a fixed interval, like when tracking first starts. The head and gaze are reset when you hold your head still facing the front, so looking away is never taken as the front.",
                "一定時間ごとに、起動したときと同じ状態へ戻します。顔の向き・視線は、正面付近で頭を止めたときに取り直すので、よそ見している向きが正面になることはありません。",
                "일정 시간마다 처음 시작했을 때와 같은 상태로 되돌립니다. 얼굴 방향·시선은 정면 근처에서 머리를 멈췄을 때 다시 잡으므로 다른 곳을 보는 방향이 정면이 되지 않습니다.",
                "每隔一定时间恢复到刚启动时的状态。头部朝向和视线会在头部于正面附近静止时重新校准，因此不会把看向别处的方向当作正面。",
                "每隔一定時間恢復到剛啟動時的狀態。頭部朝向和視線會在頭部於正面附近靜止時重新校準，因此不會把看向別處的方向當作正面。"));

            // ON/OFF（変えたら今から 1 間隔後に予約し直す）
            bool enabled = GUILayout.Toggle(_settings.autoResetEnabled,
                Loc.T("Reset automatically", "自動でリセットする", "자동으로 초기화", "自动重置", "自動重設"));
            if (enabled != _settings.autoResetEnabled)
            {
                _settings.autoResetEnabled = enabled;
                _scheduler.Restart();
            }

            // OFF の間は細かい設定を出さない
            if (!_settings.autoResetEnabled)
            {
                GuiControls.EndCard();
                return;
            }

            // 間隔（分、整数。変えたら今から数え直す）
            float minutes = Mathf.Round(GuiControls.Slider(
                Loc.T("Interval (min)", "間隔（分）", "간격 (분)", "间隔（分钟）", "間隔（分鐘）"),
                _settings.autoResetIntervalMinutes, AppSettings.MinAutoResetIntervalMinutes,
                AppSettings.MaxAutoResetIntervalMinutes, "F0"));
            if (!Mathf.Approximately(minutes, _settings.autoResetIntervalMinutes))
            {
                _settings.autoResetIntervalMinutes = minutes;
                _scheduler.Restart();
            }

            // 対象（顔の向きは視線も一緒に取り直す）
            _settings.autoResetHead = GUILayout.Toggle(_settings.autoResetHead,
                Loc.T("Head (with gaze)", "顔の向き（視線も）", "얼굴 방향 (시선 포함)", "头部朝向（含视线）", "頭部朝向（含視線）"));
            _settings.autoResetGaze = GUILayout.Toggle(_settings.autoResetGaze,
                Loc.T("Gaze", "視線", "시선", "视线", "視線"));
            _settings.autoResetExpression = GUILayout.Toggle(_settings.autoResetExpression,
                Loc.T("Expression (back to neutral)", "表情（ニュートラルへ）", "표정 (무표정으로)", "表情（恢复无表情）",
                    "表情（恢復無表情）"));

            // 状況（正面を待っているか、次のリセットまでの時間）
            if (_scheduler.IsWaitingForFront)
            {
                GuiControls.Hint(Loc.T("Waiting for you to face the front and hold still…",
                    "正面を向いて頭を止めるのを待っています…", "정면을 보고 머리를 멈추기를 기다리는 중…",
                    "正在等待您面向正面并保持静止…", "正在等待您面向正面並保持靜止…"));
            }

            float seconds = _scheduler.SecondsUntilNext;
            if (seconds >= 0f)
            {
                int total = Mathf.CeilToInt(seconds);
                GuiControls.Hint(Loc.T("Next reset in ", "次のリセットまで ", "다음 초기화까지 ", "距下次重置 ",
                    "距下次重設 ") + $"{total / 60}:{total % 60:00}");
            }

            GuiControls.EndCard();
        }
    }
}
