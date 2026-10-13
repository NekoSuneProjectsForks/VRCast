using System;
using System.Collections.Generic;
using System.IO;

namespace VRCast.Remote
{
    /// <summary>
    /// 外部操作でアバターを切り替えるときの対象探し。任意のパスは受け付けず、一度 VRCast で読み込んだアバター
    /// （設定に記録のあるもの）の中から、ファイル名（拡張子の有無は問わない）で探す。
    /// </summary>
    public static class RemoteAvatars
    {
        /// <summary>
        /// 一覧に出す名前（拡張子を除いたファイル名）。
        /// </summary>
        public static string NameOf(string path)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// paths（新しい順）から name に合うパスを返す（無ければ null）。
        /// 拡張子付きのファイル名の一致を優先し、次に拡張子を除いた名前の一致（どちらも大文字・小文字を区別しない）。
        /// exists でファイルが無いものは飛ばす。
        /// </summary>
        public static string Find(IReadOnlyList<string> paths, string name, Func<string, bool> exists)
        {
            if (paths == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string target = name.Trim();
            string found = FindBy(paths, target, exists, FileNameOf);
            return found ?? FindBy(paths, target, exists, NameOf);
        }

        /// <summary>
        /// 切り替えに使える名前の一覧（paths の並び、ファイルが無いものを除く）。
        /// 拡張子を除いた名前で別のファイルに当たる（同名の別アバターがある）ものは拡張子付きの名前にし、それでも当たらないものは出さない。
        /// </summary>
        public static List<string> Names(IReadOnlyList<string> paths, Func<string, bool> exists)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                if (!exists(path))
                {
                    continue;
                }

                string name = NameOf(path);
                if (name.Length == 0 || Find(paths, name, exists) != path)
                {
                    name = FileNameOf(path);
                    if (name.Length == 0 || Find(paths, name, exists) != path)
                    {
                        continue;
                    }
                }

                if (seen.Add(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private static string FileNameOf(string path)
        {
            try
            {
                return Path.GetFileName(path) ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private static string FindBy(IReadOnlyList<string> paths, string target, Func<string, bool> exists,
            Func<string, string> key)
        {
            foreach (string path in paths)
            {
                if (string.Equals(key(path), target, StringComparison.OrdinalIgnoreCase) && exists(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
