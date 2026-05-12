using System.Globalization;
using System.Xml;
using TwinCatAutomationKit.Abstractions;

namespace TwinCatAutomationKit.TwinCat;

public sealed class TwinCatEtherCatDeviceDescriptionService
{
    public AssertEtherCatProductRevisionsResult AssertProductRevisions(AssertEtherCatProductRevisionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<EtherCatProductRevisionRequirement> requirements = BuildRequirements(request);
        if (requirements.Count == 0)
        {
            throw new InvalidOperationException("At least one ProductRevision or Items entry is required.");
        }

        IReadOnlyList<string> searchDirectories = ResolveSearchDirectories(request.SearchDirectories);
        Dictionary<string, List<EtherCatProductTypeRecord>> index = BuildProductIndex(searchDirectories, request.IncludeHiddenTypes, out int scannedFileCount);
        List<EtherCatProductRevisionAssertion> assertions = [];

        foreach (EtherCatProductRevisionRequirement requirement in requirements)
        {
            EtherCatProductRevisionAssertion assertion = AssertProductRevision(requirement, index);
            assertions.Add(assertion);
        }

        int matched = assertions.Count(item => item.Succeeded);
        int missing = assertions.Count - matched;
        bool succeeded = missing == 0;
        return new AssertEtherCatProductRevisionsResult(
            succeeded,
            assertions.Count,
            matched,
            missing,
            scannedFileCount,
            searchDirectories,
            assertions,
            succeeded
                ? $"EtherCAT product revision guard matched {matched} item(s)."
                : $"EtherCAT product revision guard found {missing} missing item(s) out of {assertions.Count}.");
    }

    private static List<EtherCatProductRevisionRequirement> BuildRequirements(AssertEtherCatProductRevisionsRequest request)
    {
        List<EtherCatProductRevisionRequirement> requirements = [];
        foreach (string item in request.ProductRevisions ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                requirements.Add(new EtherCatProductRevisionRequirement(item.Trim()));
            }
        }

        foreach (EtherCatProductRevisionRequirement item in request.Items ?? Array.Empty<EtherCatProductRevisionRequirement>())
        {
            if (string.IsNullOrWhiteSpace(item.ProductRevision))
            {
                throw new InvalidOperationException("EtherCAT product revision requirement ProductRevision must not be empty.");
            }

            requirements.Add(item);
        }

        return requirements;
    }

    private static IReadOnlyList<string> ResolveSearchDirectories(IReadOnlyList<string>? requested)
    {
        List<string> directories = [];
        foreach (string directory in requested ?? DefaultEtherCatSearchDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory));
            if (Directory.Exists(fullPath) && !directories.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                directories.Add(fullPath);
            }
        }

        if (directories.Count == 0)
        {
            throw new InvalidOperationException("No EtherCAT device description search directories exist on this machine.");
        }

        return directories;
    }

    private static Dictionary<string, List<EtherCatProductTypeRecord>> BuildProductIndex(
        IReadOnlyList<string> searchDirectories,
        bool includeHiddenTypes,
        out int scannedFileCount)
    {
        Dictionary<string, List<EtherCatProductTypeRecord>> index = new(StringComparer.OrdinalIgnoreCase);
        scannedFileCount = 0;
        foreach (string directory in searchDirectories)
        {
            foreach (string filePath in Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly))
            {
                scannedFileCount++;
                foreach (EtherCatProductTypeRecord record in ReadProductTypeRecords(filePath, includeHiddenTypes))
                {
                    if (!index.TryGetValue(record.ProductRevision, out List<EtherCatProductTypeRecord>? records))
                    {
                        records = [];
                        index[record.ProductRevision] = records;
                    }

                    records.Add(record);
                }
            }
        }

        return index;
    }

    private static IEnumerable<EtherCatProductTypeRecord> ReadProductTypeRecords(string filePath, bool includeHiddenTypes)
    {
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        using XmlReader reader = XmlReader.Create(filePath, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            bool hiddenType = reader.LocalName.Equals("HideType", StringComparison.OrdinalIgnoreCase);
            if (!reader.LocalName.Equals("Type", StringComparison.OrdinalIgnoreCase) && !hiddenType)
            {
                continue;
            }

            if (hiddenType && !includeHiddenTypes)
            {
                continue;
            }

            string? productCode = reader.GetAttribute("ProductCode");
            string? revisionNo = reader.GetAttribute("RevisionNo");
            string? productRevision = reader.GetAttribute("ProductRevision");
            string? typeName = null;
            if (!reader.IsEmptyElement)
            {
                typeName = reader.ReadElementContentAsString()?.Trim();
            }

            productRevision = NormalizeProductRevision(productRevision, typeName, revisionNo);
            if (string.IsNullOrWhiteSpace(productRevision))
            {
                continue;
            }

            yield return new EtherCatProductTypeRecord(
                productRevision,
                productCode,
                revisionNo,
                typeName,
                filePath,
                hiddenType);
        }
    }

    private static EtherCatProductRevisionAssertion AssertProductRevision(
        EtherCatProductRevisionRequirement requirement,
        Dictionary<string, List<EtherCatProductTypeRecord>> index)
    {
        string requestedRevision = requirement.ProductRevision.Trim();
        if (!index.TryGetValue(requestedRevision, out List<EtherCatProductTypeRecord>? matches))
        {
            return new EtherCatProductRevisionAssertion(
                requestedRevision,
                false,
                ErrorMessage: "ProductRevision was not found in the scanned EtherCAT device description files.");
        }

        EtherCatProductTypeRecord? match = matches.FirstOrDefault(item =>
            MatchesOptionalField(item.ProductCode, requirement.ProductCode) &&
            MatchesOptionalField(item.RevisionNo, requirement.RevisionNo));

        if (match is null)
        {
            return new EtherCatProductRevisionAssertion(
                requestedRevision,
                false,
                ProductCode: requirement.ProductCode,
                RevisionNo: requirement.RevisionNo,
                ErrorMessage: "ProductRevision exists, but ProductCode/RevisionNo constraints did not match.");
        }

        return new EtherCatProductRevisionAssertion(
            requestedRevision,
            true,
            match.ProductCode,
            match.RevisionNo,
            match.TypeName,
            match.SourceFilePath,
            match.HiddenType);
    }

    private static bool MatchesOptionalField(string? actual, string? expected) =>
        string.IsNullOrWhiteSpace(expected) ||
        string.Equals(NormalizeHex(actual), NormalizeHex(expected), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeProductRevision(string? productRevision, string? typeName, string? revisionNo)
    {
        if (!string.IsNullOrWhiteSpace(productRevision))
        {
            return productRevision.Trim();
        }

        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        string trimmedType = typeName.Trim();
        if (trimmedType.Contains('-'))
        {
            return trimmedType;
        }

        int? revision = TryParseHighWordRevision(revisionNo);
        return revision is null
            ? trimmedType
            : $"{trimmedType}-0000-{revision.Value.ToString("0000", CultureInfo.InvariantCulture)}";
    }

    private static int? TryParseHighWordRevision(string? revisionNo)
    {
        if (string.IsNullOrWhiteSpace(revisionNo))
        {
            return null;
        }

        string normalized = NormalizeHex(revisionNo) ?? string.Empty;
        if (!long.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value))
        {
            return null;
        }

        long highWord = (value >> 16) & 0xffff;
        return highWord <= 0 ? null : (int)highWord;
    }

    private static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string trimmed = value.Trim();
        return trimmed.StartsWith("#x", StringComparison.OrdinalIgnoreCase)
            ? trimmed[2..]
            : trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? trimmed[2..]
                : trimmed;
    }

    private sealed record EtherCatProductTypeRecord(
        string ProductRevision,
        string? ProductCode,
        string? RevisionNo,
        string? TypeName,
        string SourceFilePath,
        bool HiddenType);

    private static readonly IReadOnlyList<string> DefaultEtherCatSearchDirectories =
    [
        @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Config\Io\EtherCAT",
        @"C:\ProgramData\Beckhoff\TwinCAT\3.1\Config\Io\EtherCAT"
    ];
}
