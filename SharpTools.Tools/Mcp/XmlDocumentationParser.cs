using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SharpTools.Tools.Mcp.Models;

namespace SharpTools.Tools.Mcp;

/// <summary>
/// Parser for XML documentation comments
/// </summary>
internal static class XmlDocumentationParser
{
    /// <summary>
    /// Parse XML documentation string into structured format
    /// </summary>
    public static XmlDocumentation? ParseXmlDocumentation(string? xmlDocString)
    {
        if (string.IsNullOrWhiteSpace(xmlDocString))
            return null;

        try
        {
            var doc = new XmlDocumentation
            {
                RawXml = xmlDocString
            };

            // Wrap in root element if necessary
            var xmlContent = xmlDocString.Trim();
            if (!xmlContent.StartsWith("<"))
                return doc;

            if (!xmlContent.StartsWith("<doc>") && !xmlContent.StartsWith("<?xml"))
            {
                xmlContent = $"<doc>{xmlContent}</doc>";
            }

            var xdoc = XDocument.Parse(xmlContent);

            // Parse summary
            var summaryElement = xdoc.Descendants("summary").FirstOrDefault();
            if (summaryElement != null)
            {
                doc.Summary = CleanXmlText(summaryElement.Value);
            }

            // Parse remarks
            var remarksElement = xdoc.Descendants("remarks").FirstOrDefault();
            if (remarksElement != null)
            {
                doc.Remarks = CleanXmlText(remarksElement.Value);
            }

            // Parse returns
            var returnsElement = xdoc.Descendants("returns").FirstOrDefault();
            if (returnsElement != null)
            {
                doc.Returns = CleanXmlText(returnsElement.Value);
            }

            // Parse parameters
            foreach (var paramElement in xdoc.Descendants("param"))
            {
                var name = paramElement.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    doc.Parameters.Add(new XmlParameterDoc
                    {
                        Name = name,
                        Description = CleanXmlText(paramElement.Value)
                    });
                }
            }

            // Parse exceptions
            foreach (var exceptionElement in xdoc.Descendants("exception"))
            {
                var cref = exceptionElement.Attribute("cref")?.Value;
                if (!string.IsNullOrEmpty(cref))
                {
                    // Extract type name from cref (e.g., "T:System.ArgumentNullException" -> "ArgumentNullException")
                    var typeName = cref.StartsWith("T:") ? cref.Substring(2) : cref;
                    if (typeName.Contains('.'))
                    {
                        typeName = typeName.Split('.').Last();
                    }

                    doc.Exceptions.Add(new XmlExceptionDoc
                    {
                        Type = typeName,
                        Description = CleanXmlText(exceptionElement.Value)
                    });
                }
            }

            // Parse examples
            foreach (var exampleElement in xdoc.Descendants("example"))
            {
                var example = CleanXmlText(exampleElement.Value);
                if (!string.IsNullOrWhiteSpace(example))
                {
                    doc.Examples.Add(example);
                }
            }

            // Parse seealso
            foreach (var seeAlsoElement in xdoc.Descendants("seealso"))
            {
                var cref = seeAlsoElement.Attribute("cref")?.Value;
                if (!string.IsNullOrEmpty(cref))
                {
                    doc.SeeAlso.Add(cref);
                }
            }

            return doc;
        }
        catch
        {
            // If parsing fails, return basic documentation with raw XML
            return new XmlDocumentation
            {
                RawXml = xmlDocString,
                Summary = ExtractBasicSummary(xmlDocString)
            };
        }
    }

    private static string CleanXmlText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove extra whitespace and normalize line breaks
        text = Regex.Replace(text, @"\s+", " ");
        text = text.Trim();

        return text;
    }

    private static string? ExtractBasicSummary(string xmlDocString)
    {
        // Try to extract summary using regex as fallback
        var match = Regex.Match(xmlDocString, @"<summary>(.*?)</summary>", RegexOptions.Singleline);
        if (match.Success)
        {
            return CleanXmlText(match.Groups[1].Value);
        }

        // If no XML tags, just return the cleaned text
        if (!xmlDocString.Contains("<"))
        {
            return CleanXmlText(xmlDocString);
        }

        return null;
    }
}