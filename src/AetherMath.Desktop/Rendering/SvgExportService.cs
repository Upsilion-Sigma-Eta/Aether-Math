using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Xml;
using AetherMath.Layout;

namespace AetherMath.Rendering;

public interface ISvgExportService
{
    Task ExportAsync(string path, LayoutResult layout, CancellationToken cancellationToken = default);
}

public sealed class SvgExportService : ISvgExportService
{
    public string ToSvg(LayoutResult layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        RejectIncomplete(layout);

        var builder = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true };
        using (var writer = XmlWriter.Create(builder, settings))
        {
            writer.WriteStartElement("svg", "http://www.w3.org/2000/svg");
            writer.WriteAttributeString("width", Format(layout.Width));
            writer.WriteAttributeString("height", Format(layout.Height));
            writer.WriteAttributeString("viewBox", $"0 0 {Format(layout.Width)} {Format(layout.Height)}");
            writer.WriteAttributeString("role", "img");
            writer.WriteAttributeString("aria-label", "AetherMath 수식 출력");

            foreach (var primitive in layout.Primitives)
            {
                switch (primitive)
                {
                    case GlyphPrimitive glyph:
                        WriteGlyph(writer, glyph);
                        break;
                    case LinePrimitive line:
                        WriteLine(writer, line);
                        break;
                    case PolylinePrimitive polyline:
                        WritePolyline(writer, polyline);
                        break;
                }
            }
            writer.WriteEndElement();
        }
        return builder.ToString();
    }

    public async Task ExportAsync(string path, LayoutResult layout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var svg = ToSvg(layout);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("SVG 출력 경로에 디렉터리가 없습니다.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 16 * 1024, leaveOpen: true);
                await writer.WriteAsync(svg.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void RejectIncomplete(LayoutResult layout)
    {
        var diagnostics = layout.Diagnostics
            .Where(diagnostic => diagnostic.Code == LayoutDiagnosticCode.EmptySlot)
            .ToImmutableArray();
        if (!diagnostics.IsEmpty)
            throw new MathExportException(diagnostics);
    }

    private static void WriteGlyph(XmlWriter writer, GlyphPrimitive glyph)
    {
        writer.WriteStartElement("text", "http://www.w3.org/2000/svg");
        writer.WriteAttributeString("x", Format(glyph.BaselineOrigin.X));
        writer.WriteAttributeString("y", Format(glyph.BaselineOrigin.Y));
        writer.WriteAttributeString("font-family", glyph.FontFamily);
        writer.WriteAttributeString("font-size", Format(glyph.FontSize));
        writer.WriteAttributeString("font-style", glyph.Italic ? "italic" : "normal");
        WriteColor(writer, "fill", "fill-opacity", glyph.Fill);
        writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
        writer.WriteString(glyph.Text);
        writer.WriteEndElement();
    }

    private static void WriteLine(XmlWriter writer, LinePrimitive line)
    {
        writer.WriteStartElement("line", "http://www.w3.org/2000/svg");
        writer.WriteAttributeString("x1", Format(line.Start.X));
        writer.WriteAttributeString("y1", Format(line.Start.Y));
        writer.WriteAttributeString("x2", Format(line.End.X));
        writer.WriteAttributeString("y2", Format(line.End.Y));
        WriteColor(writer, "stroke", "stroke-opacity", line.Stroke);
        writer.WriteAttributeString("stroke-width", Format(line.StrokeWidth));
        writer.WriteEndElement();
    }

    private static void WritePolyline(XmlWriter writer, PolylinePrimitive polyline)
    {
        writer.WriteStartElement(polyline.Closed ? "polygon" : "polyline", "http://www.w3.org/2000/svg");
        writer.WriteAttributeString("points", string.Join(" ", polyline.Points.Select(point =>
            $"{Format(point.X)},{Format(point.Y)}")));
        writer.WriteAttributeString("fill", "none");
        WriteColor(writer, "stroke", "stroke-opacity", polyline.Stroke);
        writer.WriteAttributeString("stroke-width", Format(polyline.StrokeWidth));
        writer.WriteAttributeString("stroke-linecap", "round");
        writer.WriteAttributeString("stroke-linejoin", "round");
        writer.WriteEndElement();
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void WriteColor(XmlWriter writer, string colorAttribute, string opacityAttribute, string color)
    {
        if (color.Length == 9 && color[0] == '#')
        {
            var alpha = byte.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            writer.WriteAttributeString(colorAttribute, $"#{color[3..]}");
            if (alpha != byte.MaxValue)
                writer.WriteAttributeString(opacityAttribute,
                    (alpha / 255d).ToString("0.#########", CultureInfo.InvariantCulture));
            return;
        }

        writer.WriteAttributeString(colorAttribute, color);
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class MathExportException : InvalidOperationException
{
    public MathExportException(ImmutableArray<LayoutDiagnostic> diagnostics)
        : base("수식에 출력할 수 없는 빈 필수 슬롯이 있습니다.") => Diagnostics = diagnostics;

    public ImmutableArray<LayoutDiagnostic> Diagnostics { get; }
}
