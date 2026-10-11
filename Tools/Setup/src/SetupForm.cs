using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using VRCast.Platform;

namespace VRCast.Setup
{
    /// <summary>
    /// 展開先の選択と進み具合の表示。展開は別スレッドで行い、終わるまで閉じられないようにする。
    /// </summary>
    internal sealed class SetupForm : Form
    {
        // 文章の折り返し幅（文字の高さの倍数。表示倍率に合わせて広がる）
        private const int WidthInLines = 32;

        private readonly Payload _payload;
        private readonly TextBox _folder;
        private readonly Button _browse;
        private readonly CheckBox _openFolder;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private readonly Button _install;
        private readonly Button _cancel;
        private bool _busy;
        private int _lastReport = int.MinValue;

        public SetupForm(Payload payload, string version)
        {
            _payload = payload;

            // OS の表示倍率・言語に合ったフォントで、中身に合わせた大きさの固定ダイアログにする
            Text = Texts.Title(version);
            Font = SystemFonts.MessageBoxFont;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Font.Height);
            int width = Font.Height * WidthInLines;

            // 2 列（入力欄・参照ボタン）の表に縦に並べる
            var layout = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(width, 0),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddWide(layout, new Label { Text = Texts.Intro, AutoSize = true, MaximumSize = new Size(width, 0) });

            // 展開先（既定は EXE の場所）と参照ボタン
            _folder = new TextBox { Text = DefaultFolder(), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            _browse = new Button { Text = Texts.Browse, AutoSize = true };
            _browse.Click += OnBrowse;
            layout.Controls.Add(_folder, 0, layout.RowCount);
            layout.Controls.Add(_browse, 1, layout.RowCount);
            layout.RowCount++;

            AddWide(layout, new Label
            {
                Text = Texts.Note,
                AutoSize = true,
                MaximumSize = new Size(width, 0),
                ForeColor = SystemColors.GrayText,
            });

            // 完了後にフォルダを開くか（VRCast は起動しない。展開するだけ）
            _openFolder = new CheckBox { Text = Texts.OpenFolder, AutoSize = true, Checked = true };
            AddWide(layout, _openFolder);

            // 進み具合と状態
            _progress = new ProgressBar { Anchor = AnchorStyles.Left | AnchorStyles.Right, Maximum = 100 };
            _status = new Label { AutoSize = true, MaximumSize = new Size(width, 0) };
            AddWide(layout, _progress);
            AddWide(layout, _status);

            // 右寄せのボタン
            _install = new Button { Text = Texts.Install, AutoSize = true };
            _cancel = new Button { Text = Texts.Cancel, AutoSize = true };
            _install.Click += OnInstall;

            // DialogResult はモーダル表示でしか閉じないため、キャンセル・閉じるは明示的に閉じる
            _cancel.Click += (sender, e) => Close();
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
            };
            buttons.Controls.Add(_cancel);
            buttons.Controls.Add(_install);
            AddWide(layout, buttons);

            Controls.Add(layout);
            AcceptButton = _install;
            CancelButton = _cancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 展開の途中で閉じると中途半端なファイルが残るため、終わるまで閉じない
            if (_busy)
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        private static void AddWide(TableLayoutPanel layout, Control control)
        {
            // 2 列にまたがる行を末尾に足す
            layout.Controls.Add(control, 0, layout.RowCount);
            layout.SetColumnSpan(control, 2);
            layout.RowCount++;
        }

        private static string DefaultFolder()
        {
            // 既定は EXE と同じ場所（zip を展開したときと同じ感覚）。ブラウザーの一時フォルダから直接開かれたらデスクトップ
            string folder = Path.GetDirectoryName(Application.ExecutablePath) ?? string.Empty;
            string temp = Path.GetFullPath(Path.GetTempPath());
            return folder.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : folder;
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = Texts.BrowseDescription,
                SelectedPath = _folder.Text,
                ShowNewFolderButton = true,
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _folder.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnInstall(object sender, EventArgs e)
        {
            // 展開先を作って書き込めるか確かめる（Program Files 等は管理者権限が無いので書き込めない）
            string parent = PrepareFolder(_folder.Text.Trim());
            if (parent == null)
            {
                MessageBox.Show(this, Texts.NotWritable, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 更新のときは、VRCast が終わっていないとファイルを差し替えられない
            while (Installer.IsInstalled(parent))
            {
                string running = Installer.FindRunning(parent);
                if (running == null)
                {
                    break;
                }

                if (MessageBox.Show(this, Texts.Running(running), Text, MessageBoxButtons.RetryCancel,
                        MessageBoxIcon.Warning) != DialogResult.Retry)
                {
                    return;
                }
            }

            // 画面の操作を止めて、別スレッドで展開する（完了時の処理は画面のスレッドで行う）
            SetBusy(true);
            bool openFolder = _openFolder.Checked;
            var installer = new Installer(_payload);
            Task.Run(() => installer.Run(parent, Report))
                .ContinueWith(task => OnFinished(task, parent, openFolder),
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static string PrepareFolder(string folder)
        {
            // 空欄・不正なパス・作れない・書き込めない場合は null
            if (folder.Length == 0)
            {
                return null;
            }

            try
            {
                string full = Path.GetFullPath(folder);
                Directory.CreateDirectory(full);
                return UpdatePackage.CanWrite(full) ? full : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                || e is NotSupportedException)
            {
                return null;
            }
        }

        private void Report(InstallStage stage, double progress)
        {
            // ファイルごとに呼ばれるので、表示が変わるとき（1% 単位・段階の切り替え）だけ画面へ送る
            bool extracting = stage == InstallStage.Extracting;
            int percent = extracting ? (int)(progress * 100) : 0;
            int key = (int)stage * 1000 + percent;
            if (key == _lastReport)
            {
                return;
            }

            _lastReport = key;
            BeginInvoke(new Action(() =>
            {
                // 確認中・入れ替え中は終わりが分からないので流れる表示、展開中は割合
                _progress.Style = extracting ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                _progress.Value = Math.Max(0, Math.Min(100, percent));
                _status.Text = stage == InstallStage.Verifying ? Texts.Verifying
                    : extracting ? Texts.Extracting : Texts.Updating;
            }));
        }

        private void OnFinished(Task<bool> task, string parent, bool openFolder)
        {
            SetBusy(false);

            // 失敗: 理由を出して、もう一度押せるようにする
            if (task.IsFaulted)
            {
                Exception error = task.Exception?.GetBaseException();
                string message = error is SetupException ? error.Message : Texts.Failed(error?.Message ?? string.Empty);
                _status.Text = string.Empty;
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 0;
                _lastReport = int.MinValue;
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (openFolder)
            {
                Try(() => Installer.OpenFolder(parent));
            }

            // 完了の案内（新しく展開した / 入れ替えた）を残して、閉じるボタンだけにする
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 100;
            _status.Text = task.Result ? Texts.Updated : Texts.Done;
            _install.Visible = false;
            _cancel.Text = Texts.Close;
            _folder.Enabled = false;
            _browse.Enabled = false;
            _openFolder.Enabled = false;
            AcceptButton = _cancel;
        }

        private void SetBusy(bool busy)
        {
            // 展開中は入力・ボタンを止める
            _busy = busy;
            _folder.Enabled = !busy;
            _browse.Enabled = !busy;
            _openFolder.Enabled = !busy;
            _install.Enabled = !busy;
            _cancel.Enabled = !busy;
        }

        private void Try(Action action)
        {
            // フォルダを開くのに失敗しても展開は済んでいるので、理由だけ伝える
            try
            {
                action();
            }
            catch (Win32Exception e)
            {
                MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
