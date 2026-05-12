using System.Globalization;
using System.Xml.Linq;
using TwinCatAutomationKit.Abstractions;

namespace TwinCatAutomationKit.TwinCat;

public sealed class TwinCatScopeConfigurationService
{
    private const string ScopeAssemblyName = "TwinCAT.Measurement.Scope.API.Model";
    private const string ZeroGuid = "00000000-0000-0000-0000-000000000000";

    public ScopeConfigurationResult EnsureConfiguration(EnsureScopeConfigurationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredText(request.ConfigurationFilePath, nameof(request.ConfigurationFilePath));
        ValidateRequiredText(request.ScopeName, nameof(request.ScopeName));

        string configurationFilePath = Path.GetFullPath(request.ConfigurationFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(configurationFilePath)
            ?? throw new InvalidOperationException($"Unable to resolve Scope configuration directory for '{configurationFilePath}'."));

        XDocument document = File.Exists(configurationFilePath)
            ? XDocument.Load(configurationFilePath, LoadOptions.PreserveWhitespace)
            : CreateBaseScopeDocument(request);
        XElement root = document.Root ?? throw new InvalidOperationException($"Scope configuration '{configurationFilePath}' is empty.");
        if (!string.Equals(root.Name.LocalName, "ScopeProject", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Scope configuration root must be ScopeProject: {configurationFilePath}");
        }

        root.SetAttributeValue("AssemblyName", ScopeAssemblyName);
        SetOrCreate(root, "Name", request.ScopeName);
        SetOrCreate(root, "MainServer", request.MainServer);
        SetOrCreate(root, "RecordTime", request.RecordTime.ToString(CultureInfo.InvariantCulture));
        SetOrCreate(root, "StopMode", request.StopMode);

        XElement rootMembers = GetOrCreate(root, "SubMember");
        XElement dataPool = EnsureNamedMember(rootMembers, "DataPool", "DataPool", sortPriority: 0);
        XElement trigger = EnsureNamedMember(rootMembers, "Trigger", "Trigger", sortPriority: 10000);
        SetOrCreate(trigger, "Title", "MeasurementMemberBase");
        XElement chart = EnsureChart(rootMembers, request.ChartName);

        XElement acquisitionMembers = GetOrCreate(dataPool, "SubMember");
        XElement axisGroup = EnsureAxisGroup(chart);
        XElement chartMembers = GetOrCreate(axisGroup, "SubMember");

        if (request.ReplaceChannels)
        {
            acquisitionMembers.Elements().Where(IsAdsAcquisition).Remove();
            chartMembers.Elements().Where(IsScopeChannel).Remove();
        }

        Dictionary<string, string> acquisitionGuids = acquisitionMembers.Elements()
            .Where(IsAdsAcquisition)
            .Select(element => new
            {
                Name = ChildValue(element, "Name"),
                Guid = ChildValue(element, "Guid")
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Guid))
            .ToDictionary(item => item.Name!, item => item.Guid!, StringComparer.OrdinalIgnoreCase);

        foreach (ScopeAdsChannelDefinition channel in request.AdsChannels ?? Array.Empty<ScopeAdsChannelDefinition>())
        {
            ValidateScopeAdsChannel(channel);
            XElement acquisition = acquisitionMembers.Elements()
                .FirstOrDefault(element => IsAdsAcquisition(element) &&
                    string.Equals(ChildValue(element, "Name"), channel.Name, StringComparison.OrdinalIgnoreCase))
                ?? CreateAdsAcquisition(channel.Name);
            if (acquisition.Parent is null)
            {
                acquisitionMembers.Add(acquisition);
            }

            ApplyAdsChannel(acquisition, channel);
            acquisitionGuids[channel.Name] = ChildValue(acquisition, "Guid")
                ?? throw new InvalidOperationException($"Generated Scope acquisition '{channel.Name}' has no Guid.");
        }

        int chartIndex = 0;
        foreach (ScopeChartChannelDefinition channel in request.ChartChannels ?? Array.Empty<ScopeChartChannelDefinition>())
        {
            ValidateRequiredText(channel.Name, nameof(channel.Name));
            ValidateRequiredText(channel.AcquisitionName, nameof(channel.AcquisitionName));
            if (!acquisitionGuids.TryGetValue(channel.AcquisitionName, out string? acquisitionGuid))
            {
                throw new InvalidOperationException(
                    $"Scope chart channel '{channel.Name}' references unknown acquisition '{channel.AcquisitionName}'.");
            }

            XElement chartChannel = chartMembers.Elements()
                .FirstOrDefault(element => IsScopeChannel(element) &&
                    string.Equals(ChildValue(element, "Name"), channel.Name, StringComparison.OrdinalIgnoreCase))
                ?? CreateChartChannel(channel.Name);
            if (chartChannel.Parent is null)
            {
                chartMembers.Add(chartChannel);
            }

            ApplyChartChannel(chartChannel, channel, acquisitionGuid, chartIndex++);
        }

        SetOrCreate(dataPool, "Title", "MeasurementMemberBase");
        document.Save(configurationFilePath);

        int adsCount = document.Descendants().Count(IsAdsAcquisition);
        int chartChannelCount = document.Descendants().Count(IsScopeChannel);
        return new ScopeConfigurationResult(
            configurationFilePath,
            request.ScopeName,
            adsCount,
            chartChannelCount,
            $"Scope configuration '{request.ScopeName}' has {adsCount} ADS channel(s) and {chartChannelCount} chart channel(s).");
    }

    public ScopeConfigurationShapeResult AssertConfigurationShape(AssertScopeConfigurationShapeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredText(request.ConfigurationFilePath, nameof(request.ConfigurationFilePath));
        string configurationFilePath = Path.GetFullPath(request.ConfigurationFilePath);
        XDocument document = XDocument.Load(configurationFilePath, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ?? throw new InvalidOperationException($"Scope configuration '{configurationFilePath}' is empty.");

        string? scopeName = ChildValue(root, "Name");
        XElement? chart = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "YTChart");
        string? chartName = chart is null ? null : ChildValue(chart, "Name");
        List<XElement> adsChannels = document.Descendants().Where(IsAdsAcquisition).ToList();
        List<XElement> chartChannels = document.Descendants().Where(IsScopeChannel).ToList();
        List<string> errors = [];

        if (request.ExpectedScopeName is not null && !string.Equals(scopeName, request.ExpectedScopeName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Scope name expected '{request.ExpectedScopeName}' but was '{scopeName ?? "(missing)"}'.");
        }

        if (request.ExpectedChartName is not null && !string.Equals(chartName, request.ExpectedChartName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Chart name expected '{request.ExpectedChartName}' but was '{chartName ?? "(missing)"}'.");
        }

        if (request.ExpectedAdsChannelCount.HasValue && adsChannels.Count != request.ExpectedAdsChannelCount.Value)
        {
            errors.Add($"ADS channel count expected {request.ExpectedAdsChannelCount.Value} but was {adsChannels.Count}.");
        }

        if (request.ExpectedChartChannelCount.HasValue && chartChannels.Count != request.ExpectedChartChannelCount.Value)
        {
            errors.Add($"Chart channel count expected {request.ExpectedChartChannelCount.Value} but was {chartChannels.Count}.");
        }

        foreach (ScopeConfigurationChannelShape expected in request.AdsChannels ?? Array.Empty<ScopeConfigurationChannelShape>())
        {
            XElement? channel = adsChannels.FirstOrDefault(element =>
                string.Equals(ChildValue(element, "Name"), expected.Name, StringComparison.OrdinalIgnoreCase));
            if (channel is null)
            {
                errors.Add($"ADS channel '{expected.Name}' was not found.");
                continue;
            }

            if (expected.SymbolName is not null &&
                !string.Equals(ChildValue(channel, "SymbolName"), expected.SymbolName, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"ADS channel '{expected.Name}' SymbolName expected '{expected.SymbolName}' but was '{ChildValue(channel, "SymbolName") ?? "(missing)"}'.");
            }
        }

        foreach (ScopeConfigurationChannelShape expected in request.ChartChannels ?? Array.Empty<ScopeConfigurationChannelShape>())
        {
            XElement? channel = chartChannels.FirstOrDefault(element =>
                string.Equals(ChildValue(element, "Name"), expected.Name, StringComparison.OrdinalIgnoreCase));
            if (channel is null)
            {
                errors.Add($"Chart channel '{expected.Name}' was not found.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(expected.AcquisitionName))
            {
                string? acquisitionGuid = channel.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "AcquisitionInterpreter")
                    ?.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "AcquisitionGUID")
                    ?.Value;
                XElement? acquisition = adsChannels.FirstOrDefault(element =>
                    string.Equals(ChildValue(element, "Guid"), acquisitionGuid, StringComparison.OrdinalIgnoreCase));
                if (acquisition is null ||
                    !string.Equals(ChildValue(acquisition, "Name"), expected.AcquisitionName, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Chart channel '{expected.Name}' acquisition expected '{expected.AcquisitionName}' but was '{ChildValue(acquisition ?? new XElement("missing"), "Name") ?? "(missing)"}'.");
                }
            }
        }

        bool succeeded = errors.Count == 0;
        string summary = succeeded
            ? $"Scope configuration shape matched: {adsChannels.Count} ADS channel(s), {chartChannels.Count} chart channel(s)."
            : "Scope configuration shape mismatch: " + string.Join("; ", errors);
        return new ScopeConfigurationShapeResult(
            succeeded,
            configurationFilePath,
            scopeName,
            chartName,
            adsChannels.Count,
            chartChannels.Count,
            errors,
            summary);
    }

    private static XDocument CreateBaseScopeDocument(EnsureScopeConfigurationRequest request)
    {
        XElement root = ScopeElement(
            "ScopeProject",
            Element("AutoRestartRecord", "false"),
            Element("AutoSaveMode", "None"),
            Element("Comment", string.Empty),
            Element("DisplayColor", "Black"),
            Element("Guid", NewGuid()),
            Element("HeadlessServer", string.Empty),
            Element("HeadlessServerConnectionId", ZeroGuid),
            Element("KeepPreviousExports", "true"),
            Element("KeepPreviousImageExports", "true"),
            Element("MainServer", request.MainServer),
            Element("Name", request.ScopeName),
            Element("RecordTime", request.RecordTime.ToString(CultureInfo.InvariantCulture)),
            Element("SortPriority", "100"),
            Element("StopMode", request.StopMode),
            Element("SubMember"),
            Element("SynchronisationMode", "Default"),
            Element("TargetConnectionIds", string.Empty),
            Element("Title", "MeasurementMemberBase"),
            Element("UseAutoSave", "false"),
            Element("UseFileStore", "true"),
            Element("Version", "1.0.0.5"),
            Element("ViewDetailLevel", "Default"));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static XElement EnsureNamedMember(XElement parent, string elementName, string name, int sortPriority)
    {
        XElement? member = parent.Elements()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, elementName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ChildValue(element, "Name"), name, StringComparison.OrdinalIgnoreCase));
        if (member is null)
        {
            member = ScopeElement(
                elementName,
                Element("Comment", string.Empty),
                Element("DisplayColor", "Black"),
                Element("Guid", NewGuid()),
                Element("Name", name),
                Element("SortPriority", sortPriority.ToString(CultureInfo.InvariantCulture)),
                Element("SubMember"),
                Element("Title", "MeasurementMemberBase"));
            parent.Add(member);
        }

        return member;
    }

    private static XElement EnsureChart(XElement parent, string chartName)
    {
        XElement? chart = parent.Elements()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "YTChart", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ChildValue(element, "Name"), chartName, StringComparison.OrdinalIgnoreCase));
        if (chart is null)
        {
            chart = ScopeElement(
                "YTChart",
                Element("AnchorTimestamp", "0"),
                Element("AssignedCore", "0"),
                Element("AutoStart", "true"),
                Element("Comment", string.Empty),
                Element("ConnectedTimeMemberGuid", ZeroGuid),
                Element("DefaultDisplayWidth", "100000000"),
                Element("DisplayColor", "-13750738"),
                Element("DisplayOverwriteMode", "false"),
                Element("Guid", NewGuid()),
                Element("Name", chartName),
                Element("ShowImageChart", "false"),
                Element("SortPriority", "10"),
                Element("SubMember"),
                Element("TimeOffset", "0"),
                Element("TimeRefreshMode", "Explicit"),
                Element("Title", "MeasurementMemberBase"),
                Element("TriggerGroupOffset", "0"));
            parent.Add(chart);
        }

        return chart;
    }

    private static XElement EnsureAxisGroup(XElement chart)
    {
        XElement chartMembers = GetOrCreate(chart, "SubMember");
        XElement? axisGroup = chartMembers.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "AxisGroup", StringComparison.OrdinalIgnoreCase));
        if (axisGroup is null)
        {
            axisGroup = ScopeElement(
                "AxisGroup",
                Element("ChannelRelatedGuid", ZeroGuid),
                Element("Comment", string.Empty),
                Element("DisplayColor", "-14737633"),
                Element("Enabled", "true"),
                Element("Guid", NewGuid()),
                Element("Name", "Axis Group"),
                Element("ShowTitle", "False"),
                Element("SortPriority", "10"),
                Element("SubMember"),
                Element("Title", "Axis Group"));
            chartMembers.Add(axisGroup);
        }

        return axisGroup;
    }

    private static XElement CreateAdsAcquisition(string name) =>
        ScopeElement(
            "AdsAcquisition",
            Element("Guid", NewGuid()),
            Element("Name", name),
            Element("SubMember"),
            Element("Title", "MeasurementMemberBase"));

    private static void ApplyAdsChannel(XElement acquisition, ScopeAdsChannelDefinition channel)
    {
        SetOrCreate(acquisition, "AmsNetId", channel.AmsNetId);
        SetOrCreate(acquisition, "Area", "Local");
        SetOrCreate(acquisition, "ArrayLength", "0");
        SetOrCreate(acquisition, "BaseSampleTime", channel.BaseSampleTime.ToString(CultureInfo.InvariantCulture));
        SetOrCreate(acquisition, "ChannelStyleInformation", string.Empty);
        SetOrCreate(acquisition, "Comment", string.Empty);
        SetOrCreate(acquisition, "CompressionMode", "Uncompressed");
        SetOrCreate(acquisition, "ContextMask", "0");
        EnsureDataAccess(acquisition);
        SetOrCreate(acquisition, "DataType", channel.DataType);
        SetOrCreate(acquisition, "DisplayColor", channel.DisplayColor);
        SetOrCreate(acquisition, "Enabled", channel.Enabled ? "true" : "false");
        SetOrCreate(acquisition, "FileHandle", "0");
        SetOrCreate(acquisition, "ForceOversampling", "false");
        SetOrCreate(acquisition, "IndexGroup", channel.IndexGroup.ToString(CultureInfo.InvariantCulture));
        SetOrCreate(acquisition, "IndexOffset", channel.IndexOffset.ToString(CultureInfo.InvariantCulture));
        SetOrCreate(acquisition, "IsEvent", "false");
        SetOrCreate(acquisition, "IsHistorical", "false");
        SetOrCreate(acquisition, "IsTimeline", "false");
        SetOrCreate(acquisition, "Name", channel.Name);
        SetOrCreate(acquisition, "Oversample", "0");
        SetOrCreate(acquisition, "SaveOption", "IncludeDataInSVDX");
        SetOrCreate(acquisition, "ServerHandle", "0");
        SetOrCreate(acquisition, "SortPriority", "10");
        SetOrCreate(acquisition, "SubAdsAcquisition", string.Empty);
        SetOrCreate(acquisition, "SymbolBased", "true");
        SetOrCreate(acquisition, "SymbolName", channel.SymbolName);
        SetOrCreate(acquisition, "TargetPort", channel.TargetPort.ToString(CultureInfo.InvariantCulture));
        SetOrCreate(acquisition, "TimeOffset", "0");
        SetOrCreate(acquisition, "Title", "MeasurementMemberBase");
        SetOrCreate(acquisition, "UseLocalServer", "true");
        SetOrCreate(acquisition, "UseTaskSampleTime", "true");
        SetOrCreate(acquisition, "UTF8Encoding", "false");
        SetOrCreate(acquisition, "VariableSize", channel.VariableSize.ToString(CultureInfo.InvariantCulture));
    }

    private static void EnsureDataAccess(XElement acquisition)
    {
        XElement dataAccess = GetOrCreate(acquisition, "DataAccess");
        XElement mode = GetOrCreate(dataAccess, "DataAccessMode");
        SetOrCreate(mode, "Source", "TwinCAT");
        SetOrCreate(mode, "Protocoll", "ADS");
        SetOrCreate(mode, "Format", "TcBinary");
        SetOrCreate(mode, "TimeContext", "Present");
        XElement range = GetOrCreate(mode, "TimeTangeInfo");
        SetOrCreate(range, "StartTimeStamp", "0");
        SetOrCreate(range, "EndTimeStamp", "0");
    }

    private static XElement CreateChartChannel(string name) =>
        ScopeElement(
            "Channel",
            Element("Guid", NewGuid()),
            Element("Name", name),
            Element("SubMember"),
            Element("Title", "MeasurementMemberBase"));

    private static void ApplyChartChannel(XElement chartChannel, ScopeChartChannelDefinition channel, string acquisitionGuid, int index)
    {
        SetOrCreate(chartChannel, "Comment", string.Empty);
        SetOrCreate(chartChannel, "DisplayColor", channel.DisplayColor);
        SetOrCreate(chartChannel, "Enabled", "true");
        SetOrCreate(chartChannel, "Name", channel.Name);
        SetOrCreate(chartChannel, "SortPriority", channel.SortPriority.ToString(CultureInfo.InvariantCulture));
        SetOrCreate(chartChannel, "Title", "MeasurementMemberBase");

        XElement subMember = GetOrCreate(chartChannel, "SubMember");
        XElement interpreter = subMember.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "AcquisitionInterpreter", StringComparison.OrdinalIgnoreCase))
            ?? ScopeElement("AcquisitionInterpreter", Element("Guid", NewGuid()));
        if (interpreter.Parent is null)
        {
            subMember.AddFirst(interpreter);
        }

        SetOrCreate(interpreter, "AcquisitionGUID", acquisitionGuid);
        SetOrCreate(interpreter, "ArrayIndex", "0");
        SetOrCreate(interpreter, "BitMask", "18446744073709551615");
        SetOrCreate(interpreter, "Comment", string.Empty);
        SetOrCreate(interpreter, "DisplayColor", "Black");
        SetOrCreate(interpreter, "Name", "Y: " + channel.Name);
        SetOrCreate(interpreter, "Offset", "0");
        SetOrCreate(interpreter, "ScaleFactor", "1");
        SetOrCreate(interpreter, "ShortInfo", string.Empty);
        SetOrCreate(interpreter, "SortPriority", "2");
        SetOrCreate(interpreter, "Title", "MeasurementMemberBase");
        SetOrCreate(interpreter, "Usage", "Y");

        XElement style = subMember.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "ChannelStyle", StringComparison.OrdinalIgnoreCase))
            ?? ScopeElement("ChannelStyle", Element("Guid", NewGuid()));
        if (style.Parent is null)
        {
            subMember.Add(style);
        }

        SetOrCreate(style, "Comment", string.Empty);
        SetOrCreate(style, "DisplayColor", "Black");
        SetOrCreate(style, "Name", index == 0 ? "Channel Style" : $"Channel Style ({index})");
        SetOrCreate(style, "SortPriority", "100");
        SetOrCreate(style, "Title", "MeasurementMemberBase");
        SetOrCreate(style, "Visible", "true");
        XElement styleMembers = GetOrCreate(style, "SubMember");
        XElement series = styleMembers.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "SeriesStyle", StringComparison.OrdinalIgnoreCase))
            ?? ScopeElement("SeriesStyle", Element("Guid", NewGuid()));
        if (series.Parent is null)
        {
            styleMembers.AddFirst(series);
        }

        SetOrCreate(series, "Antialias", "true");
        SetOrCreate(series, "Comment", string.Empty);
        SetOrCreate(series, "DisplayColor", channel.DisplayColor);
        SetOrCreate(series, "FillColor", "838893568");
        SetOrCreate(series, "FillMode", "None");
        SetOrCreate(series, "LineWidth", channel.LineWidth.ToString(CultureInfo.InvariantCulture));
        SetOrCreate(series, "MarkColor", channel.DisplayColor);
        SetOrCreate(series, "MarkSize", "2");
        SetOrCreate(series, "MarkState", "Auto");
        SetOrCreate(series, "Name", index == 0 ? "Series Style" : $"Series Style ({index})");
        SetOrCreate(series, "SeriesType", "Line");
        SetOrCreate(series, "SortPriority", "100");
        SetOrCreate(series, "Title", "MeasurementMemberBase");
    }

    private static XElement ScopeElement(string localName, params object[] content) =>
        new(localName, new XAttribute("AssemblyName", ScopeAssemblyName), content);

    private static XElement Element(string localName) => new(localName);

    private static XElement Element(string localName, string value) => new(localName, value);

    private static XElement GetOrCreate(XElement parent, string localName)
    {
        XElement? child = parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
        if (child is null)
        {
            child = Element(localName);
            parent.Add(child);
        }

        return child;
    }

    private static void SetOrCreate(XElement parent, string localName, string value) =>
        GetOrCreate(parent, localName).Value = value;

    private static string? ChildValue(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;

    private static bool IsAdsAcquisition(XElement element) =>
        string.Equals(element.Name.LocalName, "AdsAcquisition", StringComparison.OrdinalIgnoreCase);

    private static bool IsScopeChannel(XElement element) =>
        string.Equals(element.Name.LocalName, "Channel", StringComparison.OrdinalIgnoreCase);

    private static void ValidateScopeAdsChannel(ScopeAdsChannelDefinition channel)
    {
        ValidateRequiredText(channel.Name, nameof(channel.Name));
        ValidateRequiredText(channel.SymbolName, nameof(channel.SymbolName));
        ValidateRequiredText(channel.AmsNetId, nameof(channel.AmsNetId));
        ValidateRequiredText(channel.DataType, nameof(channel.DataType));
    }

    private static void ValidateRequiredText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} must not be empty.");
        }
    }

    private static string NewGuid() => Guid.NewGuid().ToString("D");
}
