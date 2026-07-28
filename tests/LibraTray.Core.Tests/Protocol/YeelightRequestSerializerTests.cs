using System.Text;
using LibraTray.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Protocol;

[TestClass]
public sealed class YeelightRequestSerializerTests
{
    [TestMethod]
    public void SerializeProducesCompactCrlfTerminatedRequest()
    {
        var request = new YeelightRequest(
            42,
            "get_prop",
            new object?[] { "power", 7, true, null });

        string serialized = Encoding.UTF8.GetString(
            YeelightRequestSerializer.Serialize(request));

        Assert.AreEqual(
            """{"id":42,"method":"get_prop","params":["power",7,true,null]}""" + "\r\n",
            serialized);
    }

    [TestMethod]
    [DataRow(0, "get_prop")]
    [DataRow(-1, "get_prop")]
    [DataRow(1, "")]
    [DataRow(1, " set_power")]
    [DataRow(1, "set-power")]
    [DataRow(1, "1method")]
    public void SerializeInvalidRequestIsRejected(int id, string method)
    {
        var request = new YeelightRequest(id, method);

        Assert.Throws<ArgumentException>(
            () => YeelightRequestSerializer.Serialize(request));
    }

    [TestMethod]
    public void SerializeOversizedRequestIsRejected()
    {
        string oversized = new(
            'x',
            CrlfMessageFramer.DefaultMaximumFrameBytes);
        var request = new YeelightRequest(1, "set_value", [oversized]);

        Assert.ThrowsExactly<YeelightFrameTooLargeException>(
            () => YeelightRequestSerializer.Serialize(request));
    }
}
