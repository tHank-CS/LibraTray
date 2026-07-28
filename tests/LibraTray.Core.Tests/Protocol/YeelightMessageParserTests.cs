using System.Text;
using LibraTray.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Protocol;

[TestClass]
public sealed class YeelightMessageParserTests
{
    [TestMethod]
    public void ParseSuccessResponseIgnoresUnknownFields()
    {
        YeelightMessage message = Parse(
            """{"id":7,"result":["ok",23],"future":{"x":true}}""");

        var response = message as YeelightSuccessResponse;
        Assert.IsNotNull(response);
        Assert.AreEqual(7, response.Id);
        Assert.HasCount(2, response.Results);
        Assert.AreEqual("ok", response.Results[0].GetString());
        Assert.AreEqual(23, response.Results[1].GetInt32());
    }

    [TestMethod]
    public void ParseErrorResponseParsesCodeMessageAndOptionalData()
    {
        YeelightMessage message = Parse(
            """{"id":8,"error":{"code":-5000,"message":"failed","data":{"retry":false},"future":1},"ignored":true}""");

        var response = message as YeelightErrorResponse;
        Assert.IsNotNull(response);
        Assert.AreEqual(8, response.Id);
        Assert.AreEqual(-5000, response.Code);
        Assert.AreEqual("failed", response.Message);
        Assert.IsTrue(response.Data.HasValue);
        Assert.IsFalse(response.Data.Value.GetProperty("retry").GetBoolean());
    }

    [TestMethod]
    public void ParsePropsNotificationPreservesUnknownProperties()
    {
        YeelightMessage message = Parse(
            """{"method":"props","params":{"power":"on","future_prop":123},"future":true}""");

        var notification = message as YeelightPropsNotification;
        Assert.IsNotNull(notification);
        Assert.AreEqual("on", notification.Properties["power"].GetString());
        Assert.AreEqual(123, notification.Properties["future_prop"].GetInt32());
    }

    [TestMethod]
    public void ParseUnknownNotificationIsRetainedInsteadOfRejected()
    {
        YeelightMessage message = Parse(
            """{"method":"future_event","params":{"value":1},"another":"field"}""");

        var notification = message as YeelightUnknownNotification;
        Assert.IsNotNull(notification);
        Assert.AreEqual("future_event", notification.Method);
        Assert.IsTrue(notification.Parameters.HasValue);
        Assert.AreEqual(
            1,
            notification.Parameters.Value.GetProperty("value").GetInt32());
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("[]")]
    [DataRow("""{"id":"not-an-integer","result":[]}""")]
    [DataRow("""{"id":1,"result":"not-an-array"}""")]
    [DataRow("""{"method":"props","params":[]}""")]
    public void ParseInvalidMessageIsRejected(string json)
    {
        Assert.ThrowsExactly<YeelightProtocolException>(() => Parse(json));
    }

    [TestMethod]
    public void ParseOversizedMessageIsRejectedBeforeJsonParsing()
    {
        byte[] oversized = new byte[
            CrlfMessageFramer.DefaultMaximumFrameBytes + 1];

        Assert.ThrowsExactly<YeelightFrameTooLargeException>(
            () => YeelightMessageParser.Parse(oversized));
    }

    private static YeelightMessage Parse(string json) =>
        YeelightMessageParser.Parse(Encoding.UTF8.GetBytes(json));
}
