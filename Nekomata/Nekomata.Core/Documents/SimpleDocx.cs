using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Nekomata.Core.Documents;

/// <summary>One paragraph: a Word style ("Title", "Subtitle", "Heading1", "Heading2", "Normal") and its text.</summary>
public sealed record DocBlock(string Style, string Text, bool PageBreakBefore = false);

/// <summary>
/// Writes and reads plain Word documents (.docx) without any library: titles, headings and paragraphs with the built-in
/// style names, so they open and edit normally in Word and can be read back by style. Lines inside a paragraph are kept
/// as line breaks.
/// </summary>
public static class SimpleDocx
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static byte[] Write(IEnumerable<DocBlock> blocks)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>""");
            Add(zip, "_rels/.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
            Add(zip, "word/_rels/document.xml.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Add(zip, "word/styles.xml", Styles);
            var body = new StringBuilder();
            foreach (var block in blocks)
            {
                body.Append("<w:p>");
                if (block.Style != "Normal") body.Append($"<w:pPr><w:pStyle w:val=\"{block.Style}\"/></w:pPr>");
                if (block.PageBreakBefore) body.Append("<w:r><w:br w:type=\"page\"/></w:r>");
                var lines = block.Text.Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    body.Append("<w:r>");
                    if (i > 0) body.Append("<w:br/>");
                    body.Append($"<w:t xml:space=\"preserve\">{SecurityElement.Escape(lines[i])}</w:t></w:r>");
                }
                body.Append("</w:p>");
            }
            Add(zip, "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                body + "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
        }
        return memory.ToArray();
    }

    /// <summary>Every paragraph with its style id (Word's own ids, e.g. "Heading1"; "Normal" when none) and text.</summary>
    public static List<DocBlock> Read(Stream docx)
    {
        using var zip = new ZipArchive(docx, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("That isn't a Word document.");
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        var styleNames = ReadStyleNames(zip);
        var blocks = new List<DocBlock>();
        foreach (var p in doc.Descendants(W + "p"))
        {
            var styleId = p.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value ?? "Normal";
            // Word may localise or rename style ids; map through the style's name ("heading 1" → Heading1).
            if (styleNames.TryGetValue(styleId, out var name)) styleId = name;
            var text = new StringBuilder();
            foreach (var node in p.Descendants())
            {
                if (node.Name == W + "t") text.Append(node.Value);
                else if (node.Name == W + "tab") text.Append('\t');
                else if (node.Name == W + "br" && (string?)node.Attribute(W + "type") is null or "textWrapping") text.Append('\n');
            }
            var numbered = p.Element(W + "pPr")?.Element(W + "numPr") is not null;
            blocks.Add(new DocBlock(styleId, (numbered ? "• " : "") + text.ToString()));
        }
        return blocks;
    }

    private static Dictionary<string, string> ReadStyleNames(ZipArchive zip)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entry = zip.GetEntry("word/styles.xml");
        if (entry is null) return map;
        using var stream = entry.Open();
        foreach (var style in XDocument.Load(stream).Descendants(W + "style"))
        {
            var id = style.Attribute(W + "styleId")?.Value;
            var name = style.Element(W + "name")?.Attribute(W + "val")?.Value?.ToLowerInvariant();
            if (id is null || name is null) continue;
            var canonical = name switch
            {
                "title" => "Title", "subtitle" => "Subtitle", "heading 1" => "Heading1", "heading 2" => "Heading2", "normal" => "Normal",
                _ => null
            };
            if (canonical is not null) map[id] = canonical;
        }
        return map;
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string Styles =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">""" +
        """<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/><w:sz w:val="22"/><w:lang w:val="en-GB"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="276" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>""" +
        """<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>""" +
        """<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:spacing w:after="80"/></w:pPr><w:rPr><w:b/><w:color w:val="3B2E7E"/><w:sz w:val="40"/></w:rPr></w:style>""" +
        """<w:style w:type="paragraph" w:styleId="Subtitle"><w:name w:val="Subtitle"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:spacing w:after="240"/></w:pPr><w:rPr><w:i/><w:color w:val="666666"/><w:sz w:val="20"/></w:rPr></w:style>""" +
        """<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="240" w:after="80"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:color w:val="5B47C7"/><w:sz w:val="28"/></w:rPr></w:style>""" +
        """<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="200" w:after="60"/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:sz w:val="24"/></w:rPr></w:style>""" +
        """</w:styles>""";
}
