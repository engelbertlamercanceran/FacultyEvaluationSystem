using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace FacultyEvalSystem.Services;

/// <summary>
/// Minimal reader for the first worksheet of an .xlsx file — enough for the
/// Registrar's masterlist and the faculty workload summary, without taking a
/// dependency on a spreadsheet library. Returns every row (including blank ones,
/// so row numbers line up with Excel) as an array of cell text by column.
/// </summary>
public static class XlsxReader
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static List<string?[]> ReadFirstSheet(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var sharedStrings = ReadSharedStrings(zip);
        var sheetPath = FindFirstSheetPath(zip);
        var entry = zip.GetEntry(sheetPath)
                    ?? throw new InvalidDataException("The workbook has no worksheet.");

        var rows = new List<string?[]>();
        using var s = entry.Open();
        using var reader = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = false });

        var current = new List<string?>();
        int currentRow = 0;
        string? cellRef = null, cellType = null;
        System.Text.StringBuilder? value = null;
        bool inValue = false, inInline = false, inText = false;

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    switch (reader.LocalName)
                    {
                        case "row":
                            var r = reader.GetAttribute("r");
                            int rowNumber = r is not null ? int.Parse(r, CultureInfo.InvariantCulture) : currentRow + 1;
                            // Excel omits empty rows; keep row numbers aligned
                            while (rows.Count < rowNumber - 1) rows.Add([]);
                            current = [];
                            currentRow = rowNumber;
                            if (reader.IsEmptyElement) rows.Add([]);
                            break;
                        case "c":
                            cellRef = reader.GetAttribute("r");
                            cellType = reader.GetAttribute("t");
                            value = null;
                            if (reader.IsEmptyElement) cellRef = null;
                            break;
                        case "v":
                            inValue = !reader.IsEmptyElement;
                            value ??= new();
                            break;
                        case "is":
                            inInline = !reader.IsEmptyElement;
                            value ??= new();
                            break;
                        case "t" when inInline:
                            inText = !reader.IsEmptyElement;
                            break;
                    }
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                case XmlNodeType.Whitespace:
                    if (inValue || inText) value?.Append(reader.Value);
                    break;

                case XmlNodeType.EndElement:
                    switch (reader.LocalName)
                    {
                        case "v": inValue = false; break;
                        case "t": inText = false; break;
                        case "is": inInline = false; break;
                        case "c":
                            if (cellRef is not null)
                            {
                                var col = ColumnIndex(cellRef);
                                while (current.Count <= col) current.Add(null);
                                current[col] = Resolve(value?.ToString(), cellType, sharedStrings);
                            }
                            cellRef = null;
                            inInline = inValue = inText = false;
                            break;
                        case "row":
                            rows.Add([.. current]);
                            break;
                    }
                    break;
            }
        }

        return rows;
    }

    public static List<string?[]> ReadFirstSheet(string path)
    {
        using var fs = File.OpenRead(path);
        return ReadFirstSheet(fs);
    }

    private static string? Resolve(string? value, string? type, List<string> sharedStrings)
    {
        if (value is null) return null;
        return type switch
        {
            "s" when int.TryParse(value, out var i) && i >= 0 && i < sharedStrings.Count => sharedStrings[i],
            "b" => value == "1" ? "TRUE" : "FALSE",
            _ => value
        };
    }

    /// <summary>"AB12" -> 27 (zero based).</summary>
    private static int ColumnIndex(string cellRef)
    {
        int col = 0;
        foreach (var ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return col - 1;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return list;

        using var s = entry.Open();
        using var reader = XmlReader.Create(s);
        System.Text.StringBuilder? sb = null;
        bool inPhonetic = false, inText = false;

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.LocalName == "si")
                    {
                        sb = new System.Text.StringBuilder();
                        if (reader.IsEmptyElement) { list.Add(""); sb = null; }
                    }
                    else if (reader.LocalName == "rPh") inPhonetic = !reader.IsEmptyElement;   // phonetic hints: skip
                    else if (reader.LocalName == "t") inText = !reader.IsEmptyElement && !inPhonetic;
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                case XmlNodeType.Whitespace:
                    if (inText) sb?.Append(reader.Value);
                    break;

                case XmlNodeType.EndElement:
                    if (reader.LocalName == "t") inText = false;
                    else if (reader.LocalName == "rPh") inPhonetic = false;
                    else if (reader.LocalName == "si" && sb is not null)
                    {
                        list.Add(sb.ToString());
                        sb = null;
                    }
                    break;
            }
        }

        return list;
    }

    private static string FindFirstSheetPath(ZipArchive zip)
    {
        var workbook = zip.GetEntry("xl/workbook.xml");
        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbook is null || rels is null) return "xl/worksheets/sheet1.xml";

        var wbDoc = new XmlDocument();
        using (var s = workbook.Open()) wbDoc.Load(s);
        var ns = new XmlNamespaceManager(wbDoc.NameTable);
        ns.AddNamespace("m", MainNs);
        ns.AddNamespace("r", RelNs);

        var sheet = wbDoc.SelectSingleNode("//m:sheets/m:sheet", ns) as XmlElement;
        var relId = sheet?.GetAttribute("id", RelNs);
        if (string.IsNullOrEmpty(relId)) return "xl/worksheets/sheet1.xml";

        var relDoc = new XmlDocument();
        using (var s = rels.Open()) relDoc.Load(s);

        foreach (XmlElement rel in relDoc.GetElementsByTagName("Relationship"))
        {
            if (rel.GetAttribute("Id") != relId) continue;
            var target = rel.GetAttribute("Target");
            if (target.StartsWith('/')) return target.TrimStart('/');
            return "xl/" + target;
        }

        return "xl/worksheets/sheet1.xml";
    }
}
