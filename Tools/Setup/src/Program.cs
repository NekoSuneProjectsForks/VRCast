using System;
using System.IO;
using System.Windows.Forms;

namespace VRCast.Setup
{
    /// <summary>
    /// VRCast のセットアップ EXE（BOOTH / Jinxxy 向け）。
    /// 末尾に zip が連結されていればそれを使い、無ければ GitHub Releases の最新版をダウンロードして展開する。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 末尾に zip があればオフライン展開、無ければ起動時に GitHub から取る（軽い配布用）
            Payload payload;
            try
            {
                payload = Payload.Read(Application.ExecutablePath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                payload = null;
            }

            // バージョンはビルド時に AssemblyInformationalVersion として埋め込む（Tools/Setup/build.ps1）
            Application.Run(new SetupForm(payload, Application.ProductVersion));
            return 0;
        }
    }
}
