using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class OffscreenRenderCases
{
    internal const string ProbeText = "xA012345=+−√π";
    private const string FirefoxExecutable = @"C:\Program Files\Mozilla Firefox\firefox.exe";

    public static IEnumerable<QaCase> All()
    {
        yield return new QaCase("FONT-OFFSCREEN", "수식 컨트롤과 같은 레이아웃의 화면 외 PNG·SVG 생성", VerifyArtifacts);
    }

    internal static OffscreenArtifacts CreateArtifacts(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (!string.Equals(Path.GetFileName(label), label, StringComparison.Ordinal))
            throw new ArgumentException("아티팩트 라벨은 단일 파일 이름이어야 합니다.", nameof(label));
        var directory = Path.Combine(FindRepositoryRoot(), "doc", "verification", "artifacts", "font-probe",
            $"{label}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pngPath = Path.Combine(directory, "equation.png");
        var svgPath = Path.Combine(directory, "equation.svg");
        var externalSvgPngPath = Path.Combine(directory, "equation-firefox.png");

        var workspace = Qa.Workspace(Qa.Text(ProbeText));
        var session = new EditingSession(workspace);
        var metrics = new AvaloniaMathTextMetrics();
        var layout = MathLayoutEngine.Layout(workspace, metrics);
        var editor = new MathEditorControl { Session = session };
        editor.Measure(new Size(layout.Width, Math.Max(layout.Height, 360)));
        editor.Arrange(new Rect(0, 0, layout.Width, Math.Max(layout.Height, 360)));

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(layout.Width));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(Math.Max(layout.Height, 360)));
        using (var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96)))
        {
            using (var drawingContext = bitmap.CreateDrawingContext())
                editor.Render(drawingContext);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var svg = new SvgExportService().ToSvg(layout);
        File.WriteAllText(svgPath, svg, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        RenderSvgHeadless(svgPath, externalSvgPngPath, pixelWidth, pixelHeight);
        return new OffscreenArtifacts(directory, pngPath, svgPath, layout.Width, layout.Height, pixelWidth, pixelHeight,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pngPath))),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(svg))),
            externalSvgPngPath,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(externalSvgPngPath))),
            layout.Primitives.OfType<GlyphPrimitive>().Select(glyph => new RenderedGlyphRequest(
                glyph.Text, glyph.FontFamily, glyph.Italic, glyph.FontSize)).ToImmutableArray());
    }

    private static void VerifyArtifacts()
    {
        var artifacts = CreateArtifacts("standalone");
        var png = File.ReadAllBytes(artifacts.PngPath);
        Qa.Check(png.Length > 64, "오프스크린 PNG가 비어 있거나 지나치게 작습니다.");
        Qa.Check(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG 서명이 올바르지 않습니다.");
        Qa.Equal(artifacts.PixelWidth, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), "PNG 너비");
        Qa.Equal(artifacts.PixelHeight, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)), "PNG 높이");
        var externalPng = File.ReadAllBytes(artifacts.ExternalSvgPngPath);
        Qa.Check(externalPng.Length > 64 && externalPng.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "Firefox headless SVG 소비 결과가 비어 있거나 PNG가 아닙니다.");

        var svg = XDocument.Load(artifacts.SvgPath);
        var root = svg.Root ?? throw new InvalidOperationException("오프스크린 SVG에 루트 요소가 없습니다.");
        Qa.Equal("svg", root.Name.LocalName, "SVG 루트");
        Qa.Near(artifacts.LayoutWidth, double.Parse((string?)root.Attribute("width") ?? "NaN", System.Globalization.CultureInfo.InvariantCulture), "SVG 너비");
        Qa.Near(artifacts.LayoutHeight, double.Parse((string?)root.Attribute("height") ?? "NaN", System.Globalization.CultureInfo.InvariantCulture), "SVG 높이");
        var glyphElements = root.Descendants().Where(element => element.Name.LocalName == "text").ToArray();
        Qa.Equal(ProbeText, string.Concat(glyphElements.Select(element => element.Value)), "SVG 글리프 원문");
        Qa.Equal(artifacts.GlyphRequests.Length, glyphElements.Length, "SVG glyph run 수");
        for (var index = 0; index < glyphElements.Length; index++)
            Qa.Equal(artifacts.GlyphRequests[index].Italic ? "italic" : "normal",
                (string?)glyphElements[index].Attribute("font-style"), $"SVG glyph-style {index}");
        Qa.Check(artifacts.GlyphRequests.Any(glyph => glyph.Italic) && artifacts.GlyphRequests.Any(glyph => !glyph.Italic),
            "실측 컨트롤 요청에 직립·기울임 run이 모두 포함되지 않았습니다.");
    }

    private static void RenderSvgHeadless(string svgPath, string pngPath, int pixelWidth, int pixelHeight)
    {
        if (!File.Exists(FirefoxExecutable))
            throw new FileNotFoundException("외부 SVG headless 소비기 Firefox를 찾을 수 없습니다.", FirefoxExecutable);

        using var profile = new TempDirectory();
        var startInfo = new ProcessStartInfo
        {
            FileName = FirefoxExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--no-remote");
        startInfo.ArgumentList.Add("--profile");
        startInfo.ArgumentList.Add(profile.Path);
        startInfo.ArgumentList.Add("--screenshot");
        startInfo.ArgumentList.Add(pngPath);
        startInfo.ArgumentList.Add("--window-size");
        startInfo.ArgumentList.Add($"{pixelWidth},{pixelHeight}");
        startInfo.ArgumentList.Add(new Uri(Path.GetFullPath(svgPath)).AbsoluteUri);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Firefox headless SVG 소비기를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(90000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("Firefox headless SVG 소비기가 90초 안에 끝나지 않았습니다.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Firefox headless SVG 소비 실패 (exit={process.ExitCode}). stdout={stdout}; stderr={stderr}");
        if (!File.Exists(pngPath))
            throw new InvalidOperationException($"Firefox가 SVG 화면 캡처를 만들지 않았습니다. stdout={stdout}; stderr={stderr}");
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AetherMath.sln")))
                return current.FullName;
        throw new DirectoryNotFoundException("AetherMath.sln을 기준으로 저장소 루트를 찾지 못했습니다.");
    }
}

internal sealed record OffscreenArtifacts(
    string Directory,
    string PngPath,
    string SvgPath,
    double LayoutWidth,
    double LayoutHeight,
    int PixelWidth,
    int PixelHeight,
    string PngSha256,
    string SvgSha256,
    string ExternalSvgPngPath,
    string ExternalSvgPngSha256,
    ImmutableArray<RenderedGlyphRequest> GlyphRequests);

internal sealed record RenderedGlyphRequest(string Text, string FontFamily, bool Italic, double FontSize);
