using System.Collections.Generic;
using NUnit.Framework;
using VRCast.Remote;

namespace VRCast.Tests
{
    public class OscParameterSenderTests
    {
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