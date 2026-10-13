using System;
using System.IO;

namespace VRCast.Platform
{
    /// <summary>
    /// フォルダをエクスプローラーで開く。日本語を含むパスでも確実に開けるよう、URL ではなくエクスプローラーにパスを渡す
    /// （IL2CPP の Process.Start は起動できないため CreateProcessW で起動する）。
    /// </summary>
    public static class FolderOpener
    {
        /// <summary>
        /// 存在するフォルダを開く。開けなければ false（理由は error）。
        /// </summary>
        public static bool Open(string folder, out string error)
        {
            // 無い・空のフォルダは開かない
            error = null;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                error = "Folder not found: " + folder;
                return false;
            }

            try
            {
                // Windows フォルダの explorer.exe にパスを引用符付きで渡す
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                NativeProcess.Start(explorer, $"\"{folder}\"", null).Dispose();
                return true;
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                // 起動できなかった（権限・ポリシー等）
                error = e.Message;
                return false;
            }
        }
    }
}
