using System.Collections.Generic;
using NUnit.Framework;
using VRCast.Remote;
using VRCast.Tracking;

namespace VRCast.Tests
{
    public class OscParameterSenderTests
    {
        [TestCaseSource(nameof(ArKitMappingCases))]
        public void ArKitMappingIndices_MatchBlendshapeNames(int index, string expectedName)
        {
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(index, Is.LessThan(ArKitFace.BlendShapeNames.Length));
            Assert.That(ArKitFace.BlendShapeNames[index], Is.EqualTo(expectedName));
        }

        private static IEnumerable<object[]> ArKitMappingCases()
        {
            yield return new object[] { ArKitFace.EyeBlinkLeft, "eyeBlinkLeft" };
            yield return new object[] { ArKitFace.EyeBlinkRight, "eyeBlinkRight" };
            yield return new object[] { ArKitFace.JawOpen, "jawOpen" };
            yield return new object[] { ArKitFace.MouthSmileLeft, "mouthSmileLeft" };
            yield return new object[] { ArKitFace.MouthSmileRight, "mouthSmileRight" };
            yield return new object[] { ArKitFace.BrowInnerUp, "browInnerUp" };
        }

        [Test]
        public void Start_RejectsIpv6EndpointWithoutThrowing()
        {
            using (var sender = new OscParameterSender())
            {
                Assert.That(sender.Start("::1", 9000), Is.False);
                Assert.That(sender.IsEnabled, Is.False);
                Assert.That(sender.Status, Does.Contain("IPv4"));
            }
        }

        [Test]
        public void SendFloat_RejectsInvalidParameterNamesAndNonFiniteValues()
        {
            using (var sender = new OscParameterSender())
            {
                Assert.That(sender.Start("127.0.0.1", 9000), Is.True);
                Assert.That(sender.SendFloat("Bad/Name", 0.5f), Is.False);
                Assert.That(sender.SendFloat("   ", 0.5f), Is.False);
                Assert.That(sender.SendFloat("Blink", float.NaN), Is.False);
                Assert.That(sender.SendFloat("Blink", float.PositiveInfinity), Is.False);
            }
        }

        [Test]
        public void SendInt_RejectsInvalidParameterNames()
        {
            using (var sender = new OscParameterSender())
            {
                Assert.That(sender.Start("127.0.0.1", 9000), Is.True);
                Assert.That(sender.SendInt("Bad/Name", 1), Is.False);
                Assert.That(sender.SendInt("  ", 1), Is.False);
            }
        }

        [TestCase(1, 0x00, 0x00, 0x00, 0x01)]
        [TestCase(-1, 0xff, 0xff, 0xff, 0xff)]
        [TestCase(256, 0x00, 0x00, 0x01, 0x00)]
        public void CreateInt_UsesOscBigEndianSignedIntegerPayload(int value, int b0, int b1, int b2, int b3)
        {
            byte[] packet = OscParameterPacket.CreateInt("/avatar/parameters/GestureLeft", value);

            Assert.That(packet.Length % 4, Is.EqualTo(0));
            Assert.That(System.Text.Encoding.UTF8.GetString(packet), Does.Contain(",i"));
            Assert.That(packet[packet.Length - 4], Is.EqualTo(b0));
            Assert.That(packet[packet.Length - 3], Is.EqualTo(b1));
            Assert.That(packet[packet.Length - 2], Is.EqualTo(b2));
            Assert.That(packet[packet.Length - 1], Is.EqualTo(b3));
        }

        [Test]
        public void CreateFloat_UsesOscBigEndianFloatPayload()
        {
            byte[] packet = OscParameterPacket.CreateFloat("/x", 1.0f);

            Assert.That(packet.Length % 4, Is.EqualTo(0));
            Assert.That(packet[packet.Length - 4], Is.EqualTo(0x3f));
            Assert.That(packet[packet.Length - 3], Is.EqualTo(0x80));
            Assert.That(packet[packet.Length - 2], Is.EqualTo(0x00));
            Assert.That(packet[packet.Length - 1], Is.EqualTo(0x00));
        }

        [Test]
        public void CreateBool_UsesOscTrueFalseTypeTagsWithoutPayload()
        {
            byte[] truePacket = OscParameterPacket.CreateBool("/x", true);
            byte[] falsePacket = OscParameterPacket.CreateBool("/x", false);

            Assert.That(System.Text.Encoding.UTF8.GetString(truePacket), Does.Contain(",T"));
            Assert.That(System.Text.Encoding.UTF8.GetString(falsePacket), Does.Contain(",F"));
            Assert.That(truePacket.Length, Is.EqualTo(falsePacket.Length));
            Assert.That(truePacket.Length % 4, Is.EqualTo(0));
        }

        [Test]
        public void CreateFloat_EncodesVRChatAvatarParameterMessage()
        {
            byte[] packet = OscParameterPacket.CreateFloat("/avatar/parameters/Blink", 0.75f);
            var messages = new List<OscMessage>();

            OscPacket.Parse(packet, 0, packet.Length, messages);

            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(messages[0].Address, Is.EqualTo("/avatar/parameters/Blink"));
            Assert.That(messages[0].Kind, Is.EqualTo(OscValueKind.Float));
            Assert.That(messages[0].Float, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [TestCase(true, OscValueKind.True)]
        [TestCase(false, OscValueKind.False)]
        public void CreateBool_EncodesOscBoolean(bool value, OscValueKind expected)
        {
            byte[] packet = OscParameterPacket.CreateBool("/avatar/parameters/HandGesture", value);
            var messages = new List<OscMessage>();

            OscPacket.Parse(packet, 0, packet.Length, messages);

            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(messages[0].Address, Is.EqualTo("/avatar/parameters/HandGesture"));
            Assert.That(messages[0].Kind, Is.EqualTo(expected));
        }
    }
}