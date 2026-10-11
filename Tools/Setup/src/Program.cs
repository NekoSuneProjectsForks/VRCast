using System;
using System.IO;
using System.Windows.Forms;

namespace VRCast.Setup
{
    /// <summary>
    /// VRCast のセットアップ EXE（Jinxxy など圧縮ファイルを置けない配布先向け）。
    /// EXE の末尾に連結した配布 zip を、zip と同じ構成で選んだフォルダへ展開する。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 末尾に zip が無い（連結前のビルド出力・途中までのダウンロード）なら案内して終わる
            Payload payload;
            try
            {
                payload = Payload.Read(Application.ExecutablePath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                payload = null;
            }

            if (payload == null)
            {
                MessageBox.Show(Texts.Incomplete, "VRCast", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            // バージョンはビルド時に AssemblyInformationalVersion として埋め込む（Tools/Setup/build.ps1）
            Application.Run(new SetupForm(payload, Application.ProductVersion));
            return 0;
        }
    }
}
