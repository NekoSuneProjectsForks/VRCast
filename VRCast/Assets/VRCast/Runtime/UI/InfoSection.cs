using UnityEngine;
using VRCast.Core;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// 情報タブ（このアプリについて・アップデートの確認・ヘルプ、クレジット、デバッグログ）。
    /// 長いデバッグログの一覧は最後に置く。
    /// </summary>
    public class InfoSection
    {
        private readonly AppSettings _settings;
        private readonly UpdateChecker _updates;
        private readonly CreditsSection _credits;
        private readonly LogSection _log;

        public InfoSection(AppSettings settings, UpdateChecker updates, CreditsSection credits, LogSection log)
        {
            _settings = settings;
            _updates = updates;
            _credits = credits;
            _log = log;
        }

        public void Draw()
        {
            DrawAbout();
            DrawUpdates();
            DrawHelp();
            _credits.Draw();
            _log.Draw();
        }

        private static void DrawAbout()
        {
            GuiControls.BeginCard(Loc.T("About", "このアプリについて", "이 앱에 대하여", "关于本应用", "關於本應用程式"));
            GuiControls.Hint($"VRCast {Application.version}");
            GuiControls.Hint(Loc.T(
                "Tab key: show / hide this panel (the background is transparent in OBS while hidden)",
                "Tab キー: このパネルの表示 / 非表示（隠している間は OBS で背景も透過）",
                "Tab 키: 이 패널 표시 / 숨기기 (숨긴 동안에는 OBS에서 배경도 투명)",
                "Tab 键：显示 / 隐藏此面板（隐藏期间 OBS 中背景也会透明）",
                "Tab 鍵：顯示 / 隱藏此面板（隱藏期間 OBS 中背景也會透明）"));
            GuiControls.Hint(Loc.T("Settings are saved when the app closes", "設定は終了時に保存されます",
                "설정은 종료할 때 저장됩니다", "设置会在退出时保存", "設定會在結束時儲存"));
            GuiControls.EndCard();
        }

        private void DrawUpdates()
        {
            GuiControls.BeginCard(Loc.T("Updates", "アップデート", "업데이트", "更新", "更新"));

            // ON にしたらその場で確認する（OFF の間は通信しない）
            bool check = GUILayout.Toggle(_settings.checkForUpdates, Loc.T(
                "Check for updates at startup", "起動時に新しいバージョンを確認する", "시작할 때 새 버전 확인",
                "启动时检查新版本", "啟動時檢查新版本"));
            if (check != _settings.checkForUpdates)
            {
                _settings.checkForUpdates = check;
                if (check)
                {
                    _updates.Check();
                }
            }

            GuiControls.Hint(Loc.T(
                "Connects to the VRCast website (coffin299.github.io) only to read the latest version number. "
                + "The update itself is downloaded from GitHub only when you press \"Update now\"",
                "最新のバージョン番号を読むためだけに VRCast の Web サイト（coffin299.github.io）へ接続します。"
                + "更新そのものは「今すぐ更新」を押したときだけ GitHub からダウンロードします",
                "최신 버전 번호를 읽기 위해서만 VRCast 웹사이트(coffin299.github.io)에 접속합니다. "
                + "업데이트 자체는 「지금 업데이트」를 눌렀을 때만 GitHub에서 다운로드합니다",
                "仅为读取最新版本号而连接 VRCast 网站（coffin299.github.io）。更新本身仅在按下“立即更新”时从 GitHub 下载",
                "僅為讀取最新版本號而連線 VRCast 網站（coffin299.github.io）。更新本身僅在按下「立即更新」時從 GitHub 下載"));
            GuiControls.Hint(UpdateStatus());

            // 新しいバージョンがあればここからも開ける
            if (_updates.IsUpdateAvailable)
            {
                UpdateDownloadButtons.Draw(_updates);
            }

            GuiControls.EndCard();
        }

        private string UpdateStatus()
        {
            // 確認の状態を表示言語で返す
            switch (_updates.State)
            {
                case UpdateChecker.CheckState.Checking:
                    return Loc.T("Checking...", "確認中...", "확인 중...", "正在检查...", "正在檢查...");
                case UpdateChecker.CheckState.Failed:
                    return Loc.T("Could not check (offline?)", "確認できませんでした（オフライン？）",
                        "확인하지 못했습니다 (오프라인?)", "无法检查（是否离线？）", "無法檢查（是否離線？）");
                case UpdateChecker.CheckState.Done:
                    return _updates.IsUpdateAvailable
                        ? Loc.T("New version", "新しいバージョン", "새 버전", "新版本", "新版本") + $": {_updates.LatestVersion}"
                        : Loc.T("You are using the latest version", "最新のバージョンです", "최신 버전입니다",
                            "已是最新版本", "已是最新版本");
                default:
                    return Loc.T("Not checked", "未確認", "확인 안 함", "未检查", "未檢查");
            }
        }

        private static void DrawHelp()
        {
            GuiControls.BeginCard(Loc.T("Help", "ヘルプ", "도움말", "帮助", "說明"));
            GuiControls.Hint(Loc.T("Step-by-step guide and troubleshooting (opens in your browser)",
                "使い方の手順とトラブルシューティング（ブラウザで開きます）",
                "사용 방법과 문제 해결 (브라우저에서 열립니다)",
                "使用步骤与故障排除（在浏览器中打开）",
                "使用步驟與疑難排解（在瀏覽器中開啟）"));

            if (GUILayout.Button(Loc.T("Open help", "ヘルプを開く", "도움말 열기", "打开帮助", "開啟說明")))
            {
                HelpPage.Open();
            }

            GuiControls.EndCard();
        }
    }
}
