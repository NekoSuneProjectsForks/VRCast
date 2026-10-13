using NUnit.Framework;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// 入力元ごとの判定（特に iPhone と MediaPipe の併用）を検証する。
    /// </summary>
    public class TrackingSourceInfoTests
    {
        [TestCase(TrackingSource.MediaPipeVmc, TrackingSource.Vmc)]
        [TestCase(TrackingSource.MediaPipeIFacialMocap, TrackingSource.IFacialMocap)]
        [TestCase(TrackingSource.MediaPipe, TrackingSource.MediaPipe)]
        [TestCase(TrackingSource.Vmc, TrackingSource.Vmc)]
        public void NetworkSourceOf_MapsHybridToItsPhoneApp(TrackingSource source, TrackingSource expected)
        {
            // 併用は顔を受け取る外部アプリ側、それ以外はそのまま
            Assert.That(TrackingSourceInfo.NetworkSourceOf(source), Is.EqualTo(expected));
        }

        [TestCase(TrackingSource.MediaPipeVmc)]
        [TestCase(TrackingSource.MediaPipeIFacialMocap)]
        public void Hybrid_LaunchesMediaPipeAndTracksArms(TrackingSource source)
        {
            // 併用は同梱の MediaPipe を起動して腕・手を受け取り、顔の ARKit 値も使える
            Assert.That(TrackingSourceInfo.IsHybrid(source), Is.True);
            Assert.That(TrackingSourceInfo.UsesBundledTracker(source), Is.True);
            Assert.That(TrackingSourceInfo.BundledTrackerOf(source), Is.EqualTo(TrackingSource.MediaPipe));
            Assert.That(TrackingSourceInfo.HasArms(source), Is.True);
            Assert.That(TrackingSourceInfo.HasArKit(source), Is.True);

            // 自分の同梱版は無く（ビルドで二重にコピーしない）、VMC の全身は腕と取り合うため使わない
            Assert.That(TrackingSourceInfo.IsBundledTracker(source), Is.False);
            Assert.That(TrackingSourceInfo.HasFullBody(source), Is.False);
        }

        [Test]
        public void PortOf_UsesThePortOfEachChannel()
        {
            var settings = new AppSettings { trackingPort = 12000, vmcPort = 39540 };

            // 受信口ごとに、VMC は VMC のポート、iFacialMocap は固定、MediaPipe は同梱トラッカーのポート
            Assert.That(TrackingSourceInfo.PortOf(settings, TrackingSource.Vmc), Is.EqualTo(39540));
            Assert.That(TrackingSourceInfo.PortOf(settings, TrackingSource.IFacialMocap),
                Is.EqualTo(AppSettings.IFacialMocapPort));
            Assert.That(TrackingSourceInfo.PortOf(settings, TrackingSource.MediaPipe), Is.EqualTo(12000));
        }

        [Test]
        public void ReceivesFromNetwork_OnlyPhoneApps()
        {
            // LAN から受けるのは外部アプリの受信口だけ（同梱トラッカーは 127.0.0.1）
            Assert.That(TrackingSourceInfo.ReceivesFromNetwork(TrackingSource.Vmc), Is.True);
            Assert.That(TrackingSourceInfo.ReceivesFromNetwork(TrackingSource.IFacialMocap), Is.True);
            Assert.That(TrackingSourceInfo.ReceivesFromNetwork(TrackingSource.MediaPipe), Is.False);
        }
    }
}
