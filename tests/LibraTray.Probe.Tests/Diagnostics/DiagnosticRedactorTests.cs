using System.Net;
using System.Text;
using System.Text.Json;
using LibraTray.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Probe.Tests.Diagnostics;

[TestClass]
public sealed class DiagnosticRedactorTests
{
    private static readonly int[][] NoOpSensitiveIndexes =
    [
        [],
        [-1, 2, int.MaxValue],
    ];

    [TestMethod]
    [DataRow("peer=192.0.2.42:55443", "192.0.2.42")]
    [DataRow("mac=02:11:22:33:44:55", "02:11:22:33:44:55")]
    [DataRow("bssid=02-11-22-33-44-55", "02-11-22-33-44-55")]
    [DataRow("mac=0211.2233.4455", "0211.2233.4455")]
    [DataRow(
        "id: 0x000000001234abcd",
        "0x000000001234abcd")]
    [DataRow(
        "name: Fixture Desk Light",
        "Fixture Desk Light")]
    [DataRow(
        "ssid: Fixture Wireless Network",
        "Fixture Wireless Network")]
    [DataRow(
        "path=C:\\FixtureProfiles\\SampleUser\\probe.jsonl",
        "C:\\FixtureProfiles\\SampleUser\\probe.jsonl")]
    [DataRow(
        "path=E:/FixtureProfiles/SampleUser/probe.jsonl",
        "E:/FixtureProfiles/SampleUser/probe.jsonl")]
    [DataRow(
        "peer=[fe80::a3b:4c5d:6e7f:8a9b]:55443",
        "fe80::a3b:4c5d:6e7f:8a9b")]
    public void RedactRemovesSensitiveDiagnosticValues(
        string input,
        string sensitiveValue)
    {
        string redacted = DiagnosticRedactor.Redact(input);

        Assert.IsFalse(
            redacted.Contains(sensitiveValue, StringComparison.OrdinalIgnoreCase),
            "The sensitive fixture value remained in the redacted output.");
        StringAssert.Contains(redacted, "[REDACTED");
    }

    [TestMethod]
    public void RedactHandlesJsonKeysEscapedValuesAndCompressedIpv6()
    {
        const string DeviceId = "fixture-device-0001";
        const string DeviceName = "Fixture \\\"Quoted\\\" Light";
        const string Ssid = "Fixture \\\\ Wireless";
        const string Path = "C:\\\\FixtureProfiles\\\\SampleUser\\\\probe.jsonl";
        const string Ipv6 = "2001:db8:12::34";
        const string Input =
            $$"""
            {"deviceId":"{{DeviceId}}","name":"{{DeviceName}}","ssid":"{{Ssid}}","path":"{{Path}}","peer":"[{{Ipv6}}]:55443"}
            """;

        string redacted = DiagnosticRedactor.Redact(Input);

        Assert.IsFalse(redacted.Contains(DeviceId, StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains(DeviceName, StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains(Ssid, StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains(Path, StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains(Ipv6, StringComparison.Ordinal));
        StringAssert.Contains(redacted, "[REDACTED");
    }

    [TestMethod]
    [DataRow(
        """{"device_token":"fixture-composite-token-secret"}""",
        "fixture-composite-token-secret")]
    [DataRow(
        "wifi_password_hash: fixture-composite-password-secret",
        "fixture-composite-password-secret")]
    [DataRow(
        """{"auth.token":"fixture-dotted-token-secret"}""",
        "fixture-dotted-token-secret")]
    [DataRow(
        """{"auth\u002etoken":"fixture-escaped-token-secret"}""",
        "fixture-escaped-token-secret")]
    [DataRow(
        "auth.token: fixture-dotted-header-secret",
        "fixture-dotted-header-secret")]
    [DataRow(
        "{\"to\\u006ben\":\"fixture-malformed-escaped-token-secret\"",
        "fixture-malformed-escaped-token-secret")]
    [DataRow(
        "Authorization: Bearer fixture-authorization-secret",
        "fixture-authorization-secret")]
    [DataRow(
        "HTTP/1.1 200 OK\r\nAuthorization: Bearer fixture-crlf-authorization-secret\r\n\r\n",
        "fixture-crlf-authorization-secret")]
    [DataRow(
        """{"api\u004bey":"fixture-api-key-secret"}""",
        "fixture-api-key-secret")]
    [DataRow(
        """{"clientSecret":"fixture-client-secret"}""",
        "fixture-client-secret")]
    [DataRow(
        "Set-Cookie: fixture-cookie-secret",
        "fixture-cookie-secret")]
    [DataRow(
        "X-Session-Cookie: fixture-session-cookie-secret",
        "fixture-session-cookie-secret")]
    [DataRow(
        """{"auth\u0043ookie":"fixture-escaped-cookie-secret"}""",
        "fixture-escaped-cookie-secret")]
    [DataRow(
        "authorization=Bearer fixture-bearer-assignment-secret",
        "fixture-bearer-assignment-secret")]
    public void CredentialRedactionHandlesCompositePropertyNames(
        string input,
        string secret)
    {
        string redacted = DiagnosticRedactor.RedactCredentials(input);

        Assert.IsFalse(redacted.Contains(secret, StringComparison.Ordinal));
        StringAssert.Contains(redacted, "[REDACTED-SECRET");
    }

    [TestMethod]
    public void CredentialRedactionHandlesDuplicateDecodedPropertyNames()
    {
        const string FirstSecret = "fixture-first-duplicate-token";
        const string SecondSecret = "fixture-second-duplicate-token";
        const string SafeValue = "fixture-safe-duplicate-value";
        const string Input =
            """{"token":"fixture-first-duplicate-token","\u0074oken":"fixture-second-duplicate-token","name":"fixture-safe-duplicate-value"}""";

        string redacted = DiagnosticRedactor.RedactCredentials(Input);

        Assert.IsFalse(redacted.Contains(FirstSecret, StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains(SecondSecret, StringComparison.Ordinal));
        StringAssert.Contains(redacted, SafeValue);
        Assert.AreEqual(
            2,
            redacted.Split("[REDACTED-SECRET]", StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    [DataRow(
        """{"id":1,"error":{"message":"{\"auth.token\":\"fixture-nested-json-token\"}"}}""",
        "fixture-nested-json-token")]
    [DataRow(
        """{"id":1,"error":{"message":"auth.token: fixture-nested-header-token"}}""",
        "fixture-nested-header-token")]
    [DataRow(
        """{"id":1,"error":{"message":"access_token=fixture-nested-assignment-token"}}""",
        "fixture-nested-assignment-token")]
    [DataRow(
        """{"id":1,"error":{"message":"{\"token\":[\"fixture-nested-container-token-a\",\"fixture-nested-container-token-b\"]"}}""",
        "fixture-nested-container-token-a")]
    [DataRow(
        """{"id":1,"error":{"message":"{\"token\":[\"fixture-nested-container-token-a\",\"fixture-nested-container-token-b\"]"}}""",
        "fixture-nested-container-token-b")]
    [DataRow(
        """{"id":1,"error":{"message":"{\"token\":\n[\"fixture-multiline-container-token-a\",\"fixture-multiline-container-token-b\"]"}}""",
        "fixture-multiline-container-token-a")]
    [DataRow(
        """{"id":1,"error":{"message":"{\"token\":\n[\"fixture-multiline-container-token-a\",\"fixture-multiline-container-token-b\"]"}}""",
        "fixture-multiline-container-token-b")]
    public void CredentialRedactionHandlesCredentialsInNestedStringPayloads(
        string input,
        string secret)
    {
        string redacted = DiagnosticRedactor.RedactCredentials(input);

        AssertSensitiveValueRemoved(redacted, secret);
        StringAssert.Contains(redacted, "[REDACTED-SECRET");
    }

    [TestMethod]
    public void RedactHandlesEscapedSensitiveJsonPropertyNames()
    {
        const string Ssid = "fixture-escaped-ssid";
        const string DeviceId = "fixture-escaped-device-id";
        const string DeviceName = "fixture-escaped-device-name";
        const string HostName = "fixture-escaped-host-name";
        const string SafeValue = "fixture-escaped-safe-value";
        const string Input =
            """{"\u0073sid":"fixture-escaped-ssid","device\u0049d":"fixture-escaped-device-id","na\u006de":"fixture-escaped-device-name","host\u006eame":"fixture-escaped-host-name","power":"fixture-escaped-safe-value"}""";

        string redacted = DiagnosticRedactor.Redact(Input);

        AssertSensitiveValueRemoved(redacted, Ssid);
        AssertSensitiveValueRemoved(redacted, DeviceId);
        AssertSensitiveValueRemoved(redacted, DeviceName);
        AssertSensitiveValueRemoved(redacted, HostName);
        StringAssert.Contains(redacted, SafeValue);
    }

    [TestMethod]
    public void RedactConservativelyHandlesMalformedNestedSensitiveContainers()
    {
        const string FirstSsid = "fixture-nested-ssid-a";
        const string SecondSsid = "fixture-nested-ssid-b";
        const string DeviceId = "fixture-assignment-device-id";
        const string Input =
            """{"message":"{\"ssid\":[\"fixture-nested-ssid-a\",\"fixture-nested-ssid-b\"]","details":"device_id=fixture-assignment-device-id"}""";

        string redacted = DiagnosticRedactor.Redact(Input);

        AssertSensitiveValueRemoved(redacted, FirstSsid);
        AssertSensitiveValueRemoved(redacted, SecondSsid);
        AssertSensitiveValueRemoved(redacted, DeviceId);
        StringAssert.Contains(redacted, "[REDACTED");
    }

    [TestMethod]
    public void RedactHandlesAssignmentValuesContainingSpaces()
    {
        const string Ssid = "Fixture Home Wireless";
        const string DeviceId = "fixture device identifier";
        const string Input =
            "ssid=Fixture Home Wireless&device_id=fixture device identifier";

        string redacted = DiagnosticRedactor.Redact(Input);

        AssertSensitiveValueRemoved(redacted, Ssid);
        AssertSensitiveValueRemoved(redacted, DeviceId);
        StringAssert.Contains(redacted, "[REDACTED-SENSITIVE]");
        StringAssert.Contains(redacted, "[REDACTED-DEVICE-ID]");
    }

    [TestMethod]
    public void RedactRemovesCurrentHostAndUserWithoutPersistingFixtureSecrets()
    {
        string hostName = Dns.GetHostName();
        string userName = Environment.UserName;
        string input = $"host={hostName}; user={userName}";

        string redacted = DiagnosticRedactor.Redact(input);

        Assert.IsFalse(
            redacted.Contains(hostName, StringComparison.OrdinalIgnoreCase),
            "The current host name remained in the redacted output.");
        Assert.IsFalse(
            redacted.Contains(userName, StringComparison.OrdinalIgnoreCase),
            "The current user name remained in the redacted output.");
        StringAssert.Contains(redacted, "[REDACTED");
    }

    [TestMethod]
    public void RedactSensitiveResultValuesHandlesEscapesUnicodeAndNoOpIndexes()
    {
        const string SensitiveResult = "Fixture \"Desk\" \\ 路径灯";
        const string SafeResult = "fixture-safe-status";
        string rawJson = JsonSerializer.Serialize(
            new
            {
                id = 7,
                result = new[] { SensitiveResult, SafeResult },
            });

        string redacted = DiagnosticRedactor.RedactSensitiveResultValues(rawJson, [0]);
        using (JsonDocument document = JsonDocument.Parse(redacted))
        {
            JsonElement results = document.RootElement.GetProperty("result");
            string? firstResult = results[0].GetString();

            Assert.AreNotEqual(SensitiveResult, firstResult);
            StringAssert.Contains(firstResult, "[REDACTED");
            Assert.AreEqual(SafeResult, results[1].GetString());
            Assert.AreEqual(7, document.RootElement.GetProperty("id").GetInt32());
        }

        foreach (int[] indexes in NoOpSensitiveIndexes)
        {
            string unchanged = DiagnosticRedactor.RedactSensitiveResultValues(
                rawJson,
                indexes);
            using JsonDocument document = JsonDocument.Parse(unchanged);
            JsonElement results = document.RootElement.GetProperty("result");

            Assert.AreEqual(SensitiveResult, results[0].GetString());
            Assert.AreEqual(SafeResult, results[1].GetString());
            Assert.AreEqual(7, document.RootElement.GetProperty("id").GetInt32());
        }
    }

    [TestMethod]
    public void RedactSensitiveResultValuesRedactsEveryDuplicateResultArray()
    {
        const string FirstSecret = "fixture-duplicate-result-a";
        const string SecondSecret = "fixture-duplicate-result-b";
        const string Raw =
            """{"id":1,"result":["fixture-duplicate-result-a","safe-a"],"result":["fixture-duplicate-result-b","safe-b"]}""";

        string redacted = DiagnosticRedactor.RedactSensitiveResultValues(Raw, [0]);
        using JsonDocument document = JsonDocument.Parse(redacted);
        JsonProperty[] results = document.RootElement
            .EnumerateObject()
            .Where(static property => property.NameEquals("result"))
            .ToArray();

        Assert.HasCount(2, results);
        AssertSensitiveValueRemoved(redacted, FirstSecret);
        AssertSensitiveValueRemoved(redacted, SecondSecret);
        Assert.IsTrue(results.All(property =>
            property.Value[0].GetString() == "[REDACTED-SENSITIVE]"));
    }

    [TestMethod]
    public async Task LoggerSanitizesStructuredKeysAndRawPayloadBeforeWritingJsonl()
    {
        const string DeviceId = "fixture-device-0002";
        const string GenericId = "fixture-generic-device-id";
        const string DeviceName = "Fixture North Light";
        const string Ssid = "Fixture Lab Wireless";
        const string Bssid = "02:21:32:43:54:65";
        const string Ipv4 = "198.51.100.23";
        const string Ipv6 = "fd00:12::34";
        const string AbsolutePath = "C:\\FixtureProfiles\\SampleUser\\probe.jsonl";
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"LibraTray-Probe-Redaction-{Guid.NewGuid():N}.jsonl");

        try
        {
            await using (DiagnosticLogger logger = DiagnosticLogger.Create(path, redact: true))
            {
                await logger.WriteAsync(
                    "fixture",
                    new Dictionary<string, object?>
                    {
                        ["deviceId"] = DeviceId,
                        ["id"] = GenericId,
                        ["reportedName"] = DeviceName,
                        ["raw"] =
                            $"ssid: {Ssid}\n"
                            + $"bssid: {Bssid}\n"
                            + $"endpoint: {Ipv4}:55443\n"
                            + $"ipv6: [{Ipv6}]:55443\n"
                            + $"path: {AbsolutePath}",
                    });
            }

            string line = await File.ReadAllTextAsync(path);
            using JsonDocument document = JsonDocument.Parse(line);

            Assert.AreEqual(
                "fixture",
                document.RootElement.GetProperty("Event").GetString());
            JsonElement data = document.RootElement.GetProperty("Data");
            string deviceId = data.GetProperty("deviceId").GetString() ?? string.Empty;
            string genericId = data.GetProperty("id").GetString() ?? string.Empty;
            string deviceName =
                data.GetProperty("reportedName").GetString() ?? string.Empty;
            string raw = data.GetProperty("raw").GetString() ?? string.Empty;

            AssertSensitiveValueRemoved(deviceId, DeviceId);
            AssertSensitiveValueRemoved(genericId, GenericId);
            AssertSensitiveValueRemoved(deviceName, DeviceName);
            AssertSensitiveValueRemoved(raw, Ssid);
            AssertSensitiveValueRemoved(raw, Bssid);
            AssertSensitiveValueRemoved(raw, Ipv4);
            AssertSensitiveValueRemoved(raw, Ipv6);
            AssertSensitiveValueRemoved(raw, AbsolutePath);
            StringAssert.Contains(raw, "[REDACTED");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task RawModeStillRemovesCredentialsButPreservesDeviceDiagnostics()
    {
        const string Token = "fixture-token-that-must-never-be-logged";
        const string Password = "fixture-password-that-must-never-be-logged";
        const string RefreshToken = "fixture-refresh-token";
        const string DeviceName = "Fixture Raw Device";
        const string Address = "192.0.2.99";
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"LibraTray-Probe-Raw-Mode-{Guid.NewGuid():N}.jsonl");

        try
        {
            await using (DiagnosticLogger logger = DiagnosticLogger.Create(
                path,
                redact: false))
            {
                logger.RegisterSensitiveValue(
                    Token,
                    replacement: "[REDACTED-SECRET]",
                    alwaysRedact: true);
                await logger.WriteAsync(
                    "fixture",
                    new Dictionary<string, object?>
                    {
                        ["token"] = Token,
                        ["raw"] =
                            $"{{\"password\":\"{Password}\","
                            + $"\"refreshToken\":\"{RefreshToken}\","
                            + $"\"name\":\"{DeviceName}\","
                            + $"\"endpoint\":\"{Address}\"}},"
                            + $" result=[{Token}]",
                    });
            }

            string line = await File.ReadAllTextAsync(path);

            AssertSensitiveValueRemoved(line, Token);
            AssertSensitiveValueRemoved(line, Password);
            AssertSensitiveValueRemoved(line, RefreshToken);
            StringAssert.Contains(line, DeviceName);
            StringAssert.Contains(line, Address);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task RawModeAlwaysRedactsCompositeStructuredCredentialKeys()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"LibraTray-Probe-Structured-Secrets-{Guid.NewGuid():N}.jsonl");
        var secrets = new Dictionary<string, string>
        {
            ["device_token"] = "fixture-structured-device-token",
            ["wifi_password_hash"] = "fixture-structured-password-hash",
            ["sessionPasswdDigest"] = "fixture-structured-passwd-digest",
        };

        try
        {
            await using (DiagnosticLogger logger = DiagnosticLogger.Create(
                path,
                redact: false))
            {
                await logger.WriteAsync(
                    "fixture",
                    secrets.ToDictionary(
                        static pair => pair.Key,
                        static pair => (object?)pair.Value));
            }

            string line = await File.ReadAllTextAsync(path);
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement data = document.RootElement.GetProperty("Data");

            foreach ((string key, string secret) in secrets)
            {
                AssertSensitiveValueRemoved(line, secret);
                Assert.AreEqual(
                    "[REDACTED-SECRET]",
                    data.GetProperty(key).GetString());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task LoggerRefusesToAppendToAnExistingFile()
    {
        const string ExistingContent = "existing-sensitive-session";
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"LibraTray-Probe-Existing-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, ExistingContent);

        try
        {
            Assert.ThrowsExactly<IOException>(
                () => DiagnosticLogger.Create(path, redact: true));
            Assert.AreEqual(ExistingContent, await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ConsoleEscapingNeutralizesTerminalControlSequences()
    {
        const string Input = "prefix\u001b]52;c;payload\u0007\u009b31m\r\n\ttext";

        string escaped = ProbeOutput.EscapeForConsole(Input);

        Assert.IsFalse(escaped.Any(character =>
            char.IsControl(character)
            && character is not ('\r' or '\n' or '\t')));
        StringAssert.Contains(escaped, "\\u001B");
        StringAssert.Contains(escaped, "\\u0007");
        StringAssert.Contains(escaped, "\\u009B");
        StringAssert.Contains(escaped, "\r\n\ttext");
    }

    [TestMethod]
    public void ProtocolResultFormattingAlwaysHidesCredentialResultIndexes()
    {
        const string Secret = "fixture-token-that-must-not-reach-console";
        const string SafeValue = "on";
        const string Raw =
            """{"id":7,"result":["fixture-token-that-must-not-reach-console","on"]}""";
        var response = (YeelightSuccessResponse)YeelightMessageParser.Parse(
            Encoding.UTF8.GetBytes(Raw));

        string formatted = ProbeOutput.FormatSuccessResults(response, [0]);

        Assert.IsFalse(formatted.Contains(Secret, StringComparison.Ordinal));
        StringAssert.Contains(formatted, "[REDACTED-SECRET]");
        StringAssert.Contains(formatted, SafeValue);
    }

    private static void AssertSensitiveValueRemoved(string output, string sensitiveValue)
    {
        Assert.IsFalse(
            output.Contains(sensitiveValue, StringComparison.OrdinalIgnoreCase),
            "A sensitive fixture value remained in the JSONL output.");
    }
}
