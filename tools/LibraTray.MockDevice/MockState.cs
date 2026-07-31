using System.Globalization;
using System.Text.Json;
using LibraTray.Core.Identity;

namespace LibraTray.MockDevice;

internal sealed class MockState
{
    public const string SupportedMethods =
        "get_prop set_power set_bright set_ct_abx set_rgb "
        + "bg_set_power bg_set_bright bg_set_rgb";

    private readonly object _sync = new();
    private readonly Dictionary<string, string> _properties;
    private readonly string _model;
    private readonly string _name;

    public MockState(string model, string name)
    {
        _model = model;
        _name = name;
        _properties = CreateInitialProperties();
    }

    public void Reset()
    {
        lock (_sync)
        {
            _properties.Clear();
            foreach ((string name, string value) in CreateInitialProperties())
            {
                _properties.Add(name, value);
            }
        }
    }

    private Dictionary<string, string> CreateInitialProperties()
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["power"] = "on",
            ["bright"] = "50",
            ["ct"] = "4000",
            ["rgb"] = "16777215",
            ["name"] = _name,
            ["model"] = _model,
            ["fw_ver"] = "100",
            ["bg_power"] = "on",
            ["bg_bright"] = "40",
            ["bg_ct"] = "4000",
            ["bg_rgb"] = "16777215",
            ["bg_hue"] = "0",
            ["bg_sat"] = "0",
            ["bg_lmode"] = "0",
        };

        if (ProductIdentityMapper.IsSupportedInternalModel(_model))
        {
            properties["main_power"] = "on";
        }

        return properties;
    }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_sync)
        {
            return new Dictionary<string, string>(_properties, StringComparer.Ordinal);
        }
    }

    public MockReply Execute(MockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_sync)
        {
            return request.Method switch
            {
                "get_prop" => GetProperties(request.Parameters),
                "set_power" => SetPower(
                    ProductIdentityMapper.IsSupportedInternalModel(_model)
                        ? "main_power"
                        : "power",
                    request.Parameters),
                "set_bright" => SetInteger(
                    "bright",
                    request.Parameters,
                    minimum: 1,
                    maximum: 100),
                "set_ct_abx" => SetInteger(
                    "ct",
                    request.Parameters,
                    minimum: 1_700,
                    maximum: 6_500),
                "set_rgb" => SetInteger(
                    "rgb",
                    request.Parameters,
                    minimum: 0,
                    maximum: 16_777_215),
                "bg_set_power" => SetPower("bg_power", request.Parameters),
                "bg_set_bright" => SetInteger(
                    "bg_bright",
                    request.Parameters,
                    minimum: 1,
                    maximum: 100),
                "bg_set_rgb" => SetInteger(
                    "bg_rgb",
                    request.Parameters,
                    minimum: 0,
                    maximum: 16_777_215),
                _ => MockReply.Fail(-1, $"unsupported method: {request.Method}"),
            };
        }
    }

    private MockReply GetProperties(IReadOnlyList<JsonElement> parameters)
    {
        var results = new List<string>(parameters.Count);
        foreach (JsonElement parameter in parameters)
        {
            if (parameter.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(parameter.GetString()))
            {
                return MockReply.Fail(-5001, "get_prop expects string property names");
            }

            string property = parameter.GetString()!;
            results.Add(_properties.TryGetValue(property, out string? value)
                ? value
                : string.Empty);
        }

        return MockReply.Ok(results);
    }

    private MockReply SetPower(
        string property,
        IReadOnlyList<JsonElement> parameters)
    {
        if (parameters.Count == 0
            || parameters[0].ValueKind != JsonValueKind.String)
        {
            return MockReply.Fail(-5001, "power command expects on or off");
        }

        string? value = parameters[0].GetString();
        if (value is not ("on" or "off"))
        {
            return MockReply.Fail(-5001, "power value must be on or off");
        }

        _properties[property] = value;
        if (ProductIdentityMapper.IsSupportedInternalModel(_model)
            && property is "main_power" or "bg_power")
        {
            _properties["power"] =
                _properties["main_power"] == "on"
                || _properties["bg_power"] == "on"
                    ? "on"
                    : "off";
        }

        return MockReply.Ok(["ok"]);
    }

    private MockReply SetInteger(
        string property,
        IReadOnlyList<JsonElement> parameters,
        int minimum,
        int maximum)
    {
        if (parameters.Count == 0
            || !parameters[0].TryGetInt32(out int value)
            || value < minimum
            || value > maximum)
        {
            return MockReply.Fail(
                -5001,
                $"value must be an integer from "
                + $"{minimum.ToString(CultureInfo.InvariantCulture)} to "
                + maximum.ToString(CultureInfo.InvariantCulture));
        }

        _properties[property] = value.ToString(CultureInfo.InvariantCulture);
        return MockReply.Ok(["ok"]);
    }
}

internal sealed record MockReply(
    bool IsSuccess,
    IReadOnlyList<string> Results,
    int ErrorCode,
    string ErrorMessage)
{
    public static MockReply Ok(IReadOnlyList<string> results) =>
        new(
            IsSuccess: true,
            results,
            ErrorCode: 0,
            ErrorMessage: string.Empty);

    public static MockReply Fail(int code, string message) =>
        new(
            IsSuccess: false,
            Results: [],
            code,
            message);
}
