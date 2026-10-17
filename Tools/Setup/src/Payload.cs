using System;
using System.IO;
using System.Text;
using VRCast.Platform;

namespace VRCast.Setup
{
    /// <summary>
    /// セットアップ EXE の末尾に連結した配布 zip（オフライン用。通常の BOOTH / Jinxxy 配布では連結せず GitHub から取る）。
    /// 並び: [EXE 本体][zip][zip の大きさ（Int64, リトルエンディアン）][zip の SHA-256（16 進 64 文字, ASCII）][目印 "VRCSETUP"]
    /// </summary>
    internal sealed class Payload
    {
        // 末尾の目印と、末尾の情報全体の長さ（大きさ 8 + SHA-256 64 + 目印 8 バイト）
        private const string Magic = "VRCSETUP";
        private const int HashLength = 64;
        private const int TrailerLength = sizeof(long) + HashLength + 8;

        private Payload(string path, long offset, long length, string sha256)
        {
            Path = path;
            Offset = offset;
            Length = length;
            Sha256 = sha256;
        }

        /// <summary>
        /// zip が入っているファイル（セットアップ EXE 自身）と、zip の開始位置・大きさ・SHA-256。
        /// </summary>
        public string Path { get; }
        public long Offset { get; }
        public long Length { get; }
        public string Sha256 { get; }

        /// <summary>
        /// exePath の末尾から zip の位置を読む。連結されていない・壊れている場合は null。
        /// </summary>
        public static Payload Read(string exePath)
        {
            using (FileStream stream = File.OpenRead(exePath))
            {
                // 末尾の情報が入る大きさが無ければ zip は無い
                if (stream.Length < TrailerLength)
                {
                    return null;
                }

                var trailer = new byte[TrailerLength];
                stream.Seek(-TrailerLength, SeekOrigin.End);
                if (!ReadFully(stream, trailer))
                {
                    return null;
                }

                // 目印が無ければ zip を連結していない EXE（ビルド直後のもの）
                if (Encoding.ASCII.GetString(trailer, TrailerLength - Magic.Length, Magic.Length) != Magic)
                {
                    return null;
                }

                // zip の大きさから開始位置を求め、EXE 本体の範囲に食い込むものは壊れているとみなす
                long length = BitConverter.ToInt64(trailer, 0);
                string sha256 = Encoding.ASCII.GetString(trailer, sizeof(long), HashLength);
                long offset = stream.Length - TrailerLength - length;
                if (length <= 0 || offset <= 0 || !UpdatePackage.IsSha256(sha256))
                {
                    return null;
                }

                return new Payload(exePath, offset, length, sha256);
            }
        }

        /// <summary>
        /// zip の部分だけを読むストリームを開く（閉じると元のファイルも閉じる）。
        /// </summary>
        public Stream Open()
        {
            return new SubStream(File.OpenRead(Path), Offset, Length);
        }

        private static bool ReadFully(Stream stream, byte[] buffer)
        {
            // Read は要求より少なく返すことがあるので、埋まるまで読む
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                {
                    return false;
                }

                total += read;
            }

            return true;
        }

        /// <summary>
        /// 元のストリームの一部（offset から length バイト）だけを見せる読み取り専用のストリーム。
        /// ZipArchive は末尾の中央ディレクトリから読むため、シークできる必要がある。
        /// </summary>
        private sealed class SubStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _offset;
            private readonly long _length;
            private long _position;

            public SubStream(Stream inner, long offset, long length)
            {
                _inner = inner;
                _offset = offset;
                _length = length;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _length;

            public override long Position
            {
                get => _position;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                // 範囲の終わりより先は読ませない
                long remaining = _length - _position;
                if (remaining <= 0)
                {
                    return 0;
                }

                if (count > remaining)
                {
                    count = (int)remaining;
                }

                // 元のストリームの位置を合わせてから読む（ZipArchive は行き来しながら読む）
                _inner.Position = _offset + _position;
                int read = _inner.Read(buffer, offset, count);
                _position += read;
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                // 範囲の中の位置に直し、範囲の前へは動かせない（後ろは Read が 0 を返す）
                long position = origin == SeekOrigin.Begin ? offset
                    : origin == SeekOrigin.Current ? _position + offset
                    : _length + offset;
                if (position < 0)
                {
                    throw new IOException("Seek before the beginning of the package");
                }

                _position = position;
                return _position;
            }

            public override void Flush()
            {
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                // 元のファイルも閉じる
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
