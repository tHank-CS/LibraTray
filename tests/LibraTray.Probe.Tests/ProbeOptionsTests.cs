using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Probe.Tests;

[TestClass]
public sealed class ProbeOptionsTests
{
    private static readonly string[] ExpectedProperties =
    [
        "power",
        "bg_bright",
        "A_1",
    ];

    [TestMethod]
    public void ParseSafeWriteRequiresStandaloneExplicitConfirmationFlag()
    {
        string[] required =
        [
            "safe-write",
            "--host",
            "127.0.0.1",
            "--method",
            "set_bright",
            "--value",
            "50",
        ];

        ProbeOptions unconfirmed = ProbeOptions.Parse(required);
        ProbeOptions confirmed = ProbeOptions.Parse([.. required, "--confirm-write"]);

        Assert.IsFalse(unconfirmed.ConfirmWrite);
        Assert.IsTrue(confirmed.ConfirmWrite);
        Assert.AreEqual("set_bright", confirmed.SafeWriteMethod);
        Assert.AreEqual("50", confirmed.SafeWriteValue);
        Assert.ThrowsExactly<ArgumentException>(
            () => ProbeOptions.Parse([.. required, "--confirm-write=true"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => ProbeOptions.Parse(
                [.. required, "--confirm-write", "--confirm-write"]));
    }

    [TestMethod]
    [DataRow("--port", "1", 1)]
    [DataRow("--port", "65535", 65535)]
    [DataRow("--discovery-port", "1", 1)]
    [DataRow("--discovery-port", "65535", 65535)]
    public void ParseAcceptsPortBoundaries(string option, string value, int expected)
    {
        ProbeOptions parsed = ProbeOptions.Parse(
            ["get-props", "--host", "127.0.0.1", option, value]);

        int actual = option == "--port" ? parsed.Port : parsed.DiscoveryPort;

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow("--port", "0")]
    [DataRow("--port", "65536")]
    [DataRow("--port", "-1")]
    [DataRow("--port", "1.5")]
    [DataRow("--discovery-port", "0")]
    [DataRow("--discovery-port", "65536")]
    [DataRow("--discovery-port", "-1")]
    [DataRow("--discovery-port", "1.5")]
    public void ParseRejectsValuesOutsidePortBoundaries(string option, string value)
    {
        ProbeOptions parsed = ProbeOptions.Parse(
            ["get-props", "--host", "127.0.0.1", option, value]);

        Assert.ThrowsExactly<ArgumentException>(
            () =>
            {
                _ = option == "--port" ? parsed.Port : parsed.DiscoveryPort;
            });
    }

    [TestMethod]
    [DataRow("1", 1)]
    [DataRow("300", 300)]
    public void ParseAcceptsTimeoutBoundaries(string value, int expectedSeconds)
    {
        ProbeOptions parsed = ProbeOptions.Parse(
            ["discover", "--timeout-seconds", value]);

        Assert.AreEqual(TimeSpan.FromSeconds(expectedSeconds), parsed.Timeout);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("301")]
    [DataRow("-1")]
    [DataRow("1.5")]
    public void ParseRejectsValuesOutsideTimeoutBoundaries(string value)
    {
        ProbeOptions parsed = ProbeOptions.Parse(
            ["discover", "--timeout-seconds", value]);

        Assert.ThrowsExactly<ArgumentException>(() => _ = parsed.Timeout);
    }

    [TestMethod]
    public void ParseListenDurationZeroMeansUntilCancelled()
    {
        ProbeOptions indefinitely = ProbeOptions.Parse(
            ["listen", "--host", "127.0.0.1", "--listen-seconds", "0"]);
        ProbeOptions bounded = ProbeOptions.Parse(
            ["listen", "--host", "127.0.0.1", "--listen-seconds", "86400"]);

        Assert.IsNull(indefinitely.ListenDuration);
        Assert.AreEqual(TimeSpan.FromDays(1), bounded.ListenDuration);
    }

    [TestMethod]
    [DataRow("-1")]
    [DataRow("86401")]
    public void ParseRejectsValuesOutsideListenDurationBoundaries(string value)
    {
        ProbeOptions parsed = ProbeOptions.Parse(
            ["listen", "--host", "127.0.0.1", "--listen-seconds", value]);

        Assert.ThrowsExactly<ArgumentException>(() => _ = parsed.ListenDuration);
    }

    [TestMethod]
    public void ParseAcceptsOnlyProtocolIdentifiersForPropertyNames()
    {
        ProbeOptions valid = ProbeOptions.Parse(
            ["get-props", "--host", "127.0.0.1", "--props", "power,bg_bright,A_1"]);
        ProbeOptions startsWithDigit = ProbeOptions.Parse(
            ["get-props", "--host", "127.0.0.1", "--props", "1power"]);
        ProbeOptions punctuation = ProbeOptions.Parse(
            ["get-props", "--host", "127.0.0.1", "--props", "bg-bright"]);
        ProbeOptions tooLong = ProbeOptions.Parse(
            ["get-props", "--host", "127.0.0.1", "--props", new string('a', 129)]);

        CollectionAssert.AreEqual(
            ExpectedProperties,
            valid.Properties.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => _ = startsWithDigit.Properties);
        Assert.ThrowsExactly<ArgumentException>(() => _ = punctuation.Properties);
        Assert.ThrowsExactly<ArgumentException>(() => _ = tooLong.Properties);
    }

    [TestMethod]
    public void DefaultPropertiesCoverKnownMainAndAmbientResearchSet()
    {
        ProbeOptions parsed = ProbeOptions.Parse(
            ["get-props", "--host", "127.0.0.1"]);

        string[] properties = parsed.Properties.ToArray();

        Assert.HasCount(17, properties);
        CollectionAssert.Contains(properties, "hue");
        CollectionAssert.Contains(properties, "sat");
        CollectionAssert.Contains(properties, "color_mode");
        CollectionAssert.Contains(properties, "bg_hue");
        CollectionAssert.Contains(properties, "bg_sat");
        CollectionAssert.Contains(properties, "bg_lmode");
    }

    [TestMethod]
    public void PropertiesRejectDuplicatesAndCountsAboveBound()
    {
        ProbeOptions duplicate = ProbeOptions.Parse(
            ["get-props", "--host", "127.0.0.1", "--props", "power,POWER"]);
        string tooManyProperties = string.Join(
            ',',
            Enumerable.Range(0, ProbeOptions.MaximumProperties + 1)
                .Select(index => $"p{index}"));
        ProbeOptions tooMany = ProbeOptions.Parse(
            ["get-props", "--host", "127.0.0.1", "--props", tooManyProperties]);

        Assert.ThrowsExactly<ArgumentException>(() => _ = duplicate.Properties);
        Assert.ThrowsExactly<ArgumentException>(() => _ = tooMany.Properties);
    }

    [TestMethod]
    [DataRow("--host=")]
    [DataRow("--method=")]
    [DataRow("--value=")]
    [DataRow("--props=")]
    public void ParseRejectsExplicitlyEmptyValues(string token)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ProbeOptions.Parse(["safe-write", token]));
    }

    [TestMethod]
    public void ParseRejectsUnknownCommandsOptionsAndMissingValues()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ProbeOptions.Parse(["unknown-command"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => ProbeOptions.Parse(["discover", "--unknown-option"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => ProbeOptions.Parse(["get-props", "--host"]));
    }

    [TestMethod]
    [DataRow("token")]
    [DataRow("accessToken")]
    [DataRow("access_token")]
    [DataRow("refresh-token")]
    [DataRow("device_token")]
    [DataRow("wifi_password_hash")]
    [DataRow("sessionPasswdDigest")]
    [DataRow("password")]
    [DataRow("authorization")]
    [DataRow("apiKey")]
    [DataRow("clientSecret")]
    [DataRow("credential")]
    [DataRow("cookie")]
    public void PropertiesRejectCredentialLikeNames(string property)
    {
        ProbeOptions parsed = ProbeOptions.Parse(
            ["get-props", "--host", "127.0.0.1", "--props", property]);

        Assert.ThrowsExactly<ArgumentException>(() => _ = parsed.Properties);
    }
}
