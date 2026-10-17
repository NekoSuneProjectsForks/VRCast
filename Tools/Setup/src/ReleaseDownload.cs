using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using VRCast.Platform;

namespace VRCast.Setup
{
    /// <summary>
    /// GitHub Releases の最新の配布 zip を取る（アプリの自動更新と同じ version.json を優先し、無ければ GitHub API）。
    /// </summary>
    internal static class ReleaseDownload
    {
        // アプリの更新通知と同じ版情報（zip の URL・大きさ・SHA-256）
        private const string VersionJsonUrl = "https://coffin299.github.io/VRCast/version.json";

        // version.json がまだ無い・古いときの控え（latest の資産名 VRCast-*-win64.zip）
        private const string LatestApiUrl = "https://api.github.com/repos/coffin299/VRCast/releases/latest";
        private const string UserAgent = "VRCast-Setup";

        /// <summary>
        /// 取得した配布 zip（一時ファイル）と、正しさの確認に使う SHA-256（API だけのときは空）。
        /// </summary>
        public readonly struct Result
        {
            public readonly string ZipPath;
            public readonly string Sha256;
            public readonly string Version;

            public Result(string zipPath, string sha256, string version)
            {
                ZipPath = zipPath;
                Sha256 = sha256 ?? string.Empty;
                Version = version ?? string.Empty;
            }
        }

        /// <summary>
        /// 最新の配布 zip を作業フォルダへ保存する。progress はバイト数（取得済み / 全体。全体不明なら 0）。
        /// </summary>
        public static Result Fetch(Action<long, long> progress)
        {
            // TLS 1.2（古い既定のままだと GitHub に繋がらないことがある）
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            if (TryFromVersionJson(out string zipUrl, out long size, out string sha256, out string version))
            {
                string path = Download(zipUrl, size, progress);
                return new Result(path, sha256, version);
            }

            if (TryFromGitHubApi(out zipUrl, out size, out version))
            {
                string path = Download(zipUrl, size, progress);
                return new Result(path, string.Empty, version);
            }

            throw new SetupException(Texts.DownloadFailed(string.Empty));
        }

        private static bool TryFromVersionJson(out string zipUrl, out long size, out string sha256, out string version)
        {
            zipUrl = null;
            size = 0;
            sha256 = null;
            version = null;
            try
            {
                string json = DownloadString(VersionJsonUrl);
                var document = ReadJson<VersionDocument>(json);
                if (document?.Update == null
                    || !UpdatePackage.IsValid(document.Update.ZipUrl, document.Update.Size, document.Update.Sha256))
                {
                    return false;
                }

                zipUrl = document.Update.ZipUrl;
                size = document.Update.Size;
                sha256 = document.Update.Sha256;
                version = document.Version ?? string.Empty;
                return true;
            }
            catch (Exception e) when (e is WebException || e is IOException || e is SerializationException
                || e is InvalidDataException || e is ArgumentException)
            {
                return false;
            }
        }

        private static bool TryFromGitHubApi(out string zipUrl, out long size, out string version)
        {
            zipUrl = null;
            size = 0;
            version = null;
            try
            {
                string json = DownloadString(LatestApiUrl);
                // latest の VRCast-*-win64.zip の URL（大きさは分からなければ 0 のまま。SHA-256 は version.json 側で取る）
                Match url = Regex.Match(json,
                    "\"browser_download_url\"\\s*:\\s*\"(https://github\\.com/coffin299/VRCast/releases/download/v([^/]+)/VRCast-\\2-win64\\.zip)\"");
                if (!url.Success)
                {
                    return false;
                }

                zipUrl = url.Groups[1].Value;
                version = url.Groups[2].Value;
                Match sizeMatch = Regex.Match(json,
                    "\"name\"\\s*:\\s*\"VRCast-" + Regex.Escape(version) + "-win64\\.zip\"[\\s\\S]{0,400}?\"size\"\\s*:\\s*([0-9]+)");
                if (sizeMatch.Success)
                {
                    size = long.Parse(sizeMatch.Groups[1].Value);
                }

                return UpdatePackage.IsAllowedUrl(zipUrl);
            }
            catch (Exception e) when (e is WebException || e is IOException || e is FormatException
                || e is OverflowException || e is ArgumentException)
            {
                return false;
            }
        }

        private static string Download(string url, long expectedSize, Action<long, long> progress)
        {
            string folder = Path.Combine(UpdatePackage.WorkFolder, "setup-download");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "VRCast-download.zip");
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            try
            {
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                    using (Stream remote = client.OpenRead(url))
                    using (FileStream local = File.Create(path))
                    {
                        // 全体の大きさが不明なら応答ヘッダーを使う
                        long total = expectedSize;
                        if (total <= 0 && long.TryParse(client.ResponseHeaders?.Get("Content-Length"), out long length))
                        {
                            total = length;
                        }

                        var buffer = new byte[81920];
                        long done = 0;
                        int read;
                        while ((read = remote.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            local.Write(buffer, 0, read);
                            done += read;
                            progress?.Invoke(done, total);
                        }

                        if (expectedSize > 0 && done != expectedSize)
                        {
                            throw new SetupException(Texts.Damaged);
                        }

                        progress?.Invoke(done, done > 0 ? done : 1);
                    }
                }
            }
            catch (SetupException)
            {
                TryDelete(path);
                throw;
            }
            catch (Exception e) when (e is WebException || e is IOException || e is UnauthorizedAccessException)
            {
                TryDelete(path);
                throw new SetupException(Texts.DownloadFailed(e.Message));
            }

            return path;
        }

        private static string DownloadString(string url)
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                client.Encoding = Encoding.UTF8;
                return client.DownloadString(url);
            }
        }

        private static T ReadJson<T>(string json) where T : class
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                return serializer.ReadObject(stream) as T;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }

        [DataContract]
        private sealed class VersionDocument
        {
            [DataMember(Name = "version")]
            public string Version { get; set; }

            [DataMember(Name = "update")]
            public UpdateSection Update { get; set; }
        }

        [DataContract]
        private sealed class UpdateSection
        {
            [DataMember(Name = "zipUrl")]
            public string ZipUrl { get; set; }

            [DataMember(Name = "size")]
            public long Size { get; set; }

            [DataMember(Name = "sha256")]
            public string Sha256 { get; set; }
        }
    }
}
