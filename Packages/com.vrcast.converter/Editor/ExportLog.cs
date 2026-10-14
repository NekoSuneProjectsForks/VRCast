using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// 書き出しの記録（日本語・英語）。結果ウィンドウに一覧で出し、書き出したファイルの横にテキストでも保存する。
    /// 書き出し中は <see cref="Begin"/> で Current に設定され、各処理は静的メソッドで記録する（Console にも英語で出る）。
    /// 同じ間にほかのツール（VRCFury・NDMF 等）が Console に出した警告・エラーも、原文のまま取り込む。
    /// </summary>
    [Serializable]
    public sealed class ExportLog
    {
        public enum Level
        {
            Info,
            Warning,
            Error,
        }

        [Serializable]
        public sealed class Entry
        {
            public Level level;
            public string english;
            public string japanese;

            // パス・名前などの言語によらない補足（複数行可）
            public string detail;

            // ほかのツールが Console に出したもの（原文のまま）
            public bool external;

            public string Message(bool japaneseText)
            {
                return japaneseText && !string.IsNullOrEmpty(japanese) ? japanese : english;
            }
        }

        // Console の取り込みで記録を埋め尽くさないための上限
        private const int MaxExternalEntries = 200;

        // VRCast 自身が Console に出すものの目印（取り込みの対象外にする）
        private const string OwnPrefix = "[VRCast]";

        public List<Entry> entries = new List<Entry>();

        private int _externalCount;

        /// <summary>
        /// 書き出し中の記録先（書き出し中でなければ null）。
        /// </summary>
        public static ExportLog Current { get; private set; }

        /// <summary>
        /// 記録を日本語で表示する（エクスポーターの表示言語が日本語のとき。それ以外は英語）。
        /// </summary>
        public static bool UseJapanese => ExporterLoc.Current == ExporterLoc.Language.Japanese;

        public int Count(Level level)
        {
            int count = 0;
            foreach (Entry entry in entries)
            {
                count += entry.level == level ? 1 : 0;
            }

            return count;
        }

        /// <summary>
        /// この記録を Current にし、Console の取り込みを始める。Dispose で元に戻す。
        /// </summary>
        public IDisposable Begin()
        {
            return new Scope(this);
        }

        public static void Info(string english, string japanese, string detail = null)
        {
            Write(Level.Info, english, japanese, detail);
        }

        public static void Warn(string english, string japanese, string detail = null)
        {
            Write(Level.Warning, english, japanese, detail);
        }

        public static void Error(string english, string japanese, string detail = null)
        {
            Write(Level.Error, english, japanese, detail);
        }

        /// <summary>
        /// Console には出さずに記録だけする（書き出しの外で起きたこと、すでに Console に出ているもの向け）。
        /// </summary>
        public void Add(Level level, string english, string japanese, string detail = null)
        {
            entries.Add(new Entry { level = level, english = english, japanese = japanese, detail = detail ?? string.Empty });
        }

        /// <summary>
        /// 記録をテキストにする（日本語か英語。header はその前に置く）。
        /// </summary>
        public string ToText(bool japaneseText, string header)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(header))
            {
                builder.AppendLine(header).AppendLine();
            }

            foreach (Entry entry in entries)
            {
                builder.Append('[').Append(LevelLabel(entry.level, japaneseText)).Append("] ");
                if (entry.external)
                {
                    builder.Append(japaneseText ? "（ほかのツール）" : "(other tool) ");
                }

                builder.AppendLine(entry.Message(japaneseText));
                if (!string.IsNullOrEmpty(entry.detail))
                {
                    foreach (string line in entry.detail.Split('\n'))
                    {
                        builder.Append("    ").AppendLine(line.TrimEnd('\r'));
                    }
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// テキストにしてファイルへ保存する。保存できなければ null を返す（書き出し自体は成功のまま）。
        /// </summary>
        public string Save(string path, bool japaneseText, string header)
        {
            try
            {
                File.WriteAllText(path, ToText(japaneseText, header), new UTF8Encoding(true));
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{OwnPrefix}[Exporter] Could not save the export log: {e.Message}");
                return null;
            }
        }

        public static string LevelLabel(Level level, bool japaneseText)
        {
            switch (level)
            {
                case Level.Error:
                    return japaneseText ? "エラー" : "Error";
                case Level.Warning:
                    return japaneseText ? "警告" : "Warning";
                default:
                    return japaneseText ? "情報" : "Info";
            }
        }

        private static void Write(Level level, string english, string japanese, string detail)
        {
            Current?.Add(level, english, japanese, detail);

            // Console は問い合わせ時に読みやすいよう英語で出す
            string message = $"{OwnPrefix}[Exporter] {english}" + (string.IsNullOrEmpty(detail) ? string.Empty : "\n" + detail);
            switch (level)
            {
                case Level.Error:
                    Debug.LogError(message);
                    break;
                case Level.Warning:
                    Debug.LogWarning(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }

        private void OnConsoleMessage(string message, string stackTrace, LogType type)
        {
            // ほかのツールの警告・エラーだけを取り込む（VRCast 自身のものは記録済み）
            if (type == LogType.Log || message == null || message.StartsWith(OwnPrefix, StringComparison.Ordinal)
                || _externalCount >= MaxExternalEntries)
            {
                return;
            }

            _externalCount++;
            Level level = type == LogType.Warning ? Level.Warning : Level.Error;
            entries.Add(new Entry { level = level, english = message.Trim(), japanese = string.Empty, detail = string.Empty, external = true });
        }

        private sealed class Scope : IDisposable
        {
            private readonly ExportLog _log;
            private readonly ExportLog _previous;

            public Scope(ExportLog log)
            {
                _log = log;
                _previous = Current;
                Current = log;
                Application.logMessageReceived += log.OnConsoleMessage;
            }

            public void Dispose()
            {
                Application.logMessageReceived -= _log.OnConsoleMessage;
                Current = _previous;
            }
        }
    }
}
