using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using AetherMath.Desktop;

namespace AetherMath.LogicQA;

internal static class FontProbeCases
{
    private const string NormalFirst = "normal-first";
    private const string ItalicFirst = "italic-first";
    private const string ObliqueFirst = "oblique-first";
    private const double FontSize = 32;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IEnumerable<QaCase> All()
    {
        yield return new QaCase("FONT-PROBE-ORDERS", "신선한 프로세스의 글꼴 resolve·shaping 순서 비교", VerifyOrdersAsync);
    }

    public static void RunChild(string order)
    {
        if (order is not (NormalFirst or ItalicFirst or ObliqueFirst))
            throw new ArgumentException("지원하지 않는 글꼴 탐색 순서입니다: " + order, nameof(order));

        // 제품과 동일한 resolver 등록 경계를 각 자식 프로세스에서 새로 연다.
        _ = new AvaloniaMathTextMetrics();
        var styles = order switch
        {
            NormalFirst => new[] { FontStyle.Normal, FontStyle.Italic, FontStyle.Oblique },
            ItalicFirst => new[] { FontStyle.Italic, FontStyle.Normal, FontStyle.Oblique },
            _ => new[] { FontStyle.Oblique, FontStyle.Normal, FontStyle.Italic }
        };
        var observations = ImmutableArray.CreateBuilder<FontRequestObservation>(6);

        foreach (var route in new[] { FontRoute.System, FontRoute.ProductResolver })
        foreach (var style in styles)
            observations.Add(Observe(route, style));

        var artifacts = OffscreenRenderCases.CreateArtifacts("font-" + order);
        Qa.Check(artifacts.GlyphRequests.Any(glyph => glyph.Italic) && artifacts.GlyphRequests.Any(glyph => !glyph.Italic),
            "제품 렌더 요청에 직립·기울임 glyph run이 모두 포함되지 않았습니다.");

        var report = new FontProbeReport(order, "after-normal-face-warmup", observations.MoveToImmutable(), artifacts);
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
    }

    private static FontRequestObservation Observe(FontRoute route, FontStyle requestedStyle)
    {
        var fontFamily = route == FontRoute.ProductResolver
            ? MathFontFamilyResolver.Resolve(CambriaMathTypefaceCollection.FamilyName)
            : new FontFamily(CambriaMathTypefaceCollection.FamilyName);
        var typeface = new Typeface(fontFamily, requestedStyle, FontWeight.Normal, FontStretch.Normal);
        using var layout = new TextLayout(OffscreenRenderCases.ProbeText, typeface, FontSize, Brushes.Transparent,
            textWrapping: TextWrapping.NoWrap,
            flowDirection: FlowDirection.LeftToRight,
            maxWidth: double.PositiveInfinity,
            maxHeight: double.PositiveInfinity);

        var runs = layout.TextLines
            .SelectMany(line => line.TextRuns.OfType<ShapedTextRun>())
            .Select(ObserveRun)
            .ToImmutableArray();
        Qa.Check(!runs.IsDefaultOrEmpty, $"{route}/{requestedStyle}에서 shaped glyph run을 얻지 못했습니다.");

        var width = layout.WidthIncludingTrailingWhitespace;
        var height = layout.Height;
        var baseline = layout.Baseline;
        var signature = Signature(route, requestedStyle, width, height, baseline, runs);
        return new FontRequestObservation(route.ToString(), requestedStyle.ToString(), fontFamily.Name,
            width, height, baseline, runs, signature);
    }

    private static ShapedRunObservation ObserveRun(ShapedTextRun run)
    {
        var glyphRun = run.GlyphRun;
        var glyphTypeface = glyphRun.GlyphTypeface;
        var platformTypeface = glyphTypeface.PlatformTypeface;
        var faceNames = string.Join(" | ", glyphTypeface.FaceNames
            .OrderBy(pair => pair.Key.Name, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key.Name}={pair.Value}"));
        var glyphData = string.Join(";", glyphRun.GlyphInfos.Select(info =>
            string.Create(CultureInfo.InvariantCulture,
                $"{info.GlyphIndex}:{info.GlyphCluster}:{info.GlyphAdvance:R}:{info.GlyphOffset.X:R},{info.GlyphOffset.Y:R}")));

        var platformFontHash = TryHashPlatformFont(platformTypeface);
        var outline = CaptureOutline(glyphRun);
        var ink = glyphRun.InkBounds;
        return new ShapedRunObservation(
            run.Text.ToString(),
            glyphTypeface.FamilyName,
            glyphTypeface.TypographicFamilyName,
            faceNames,
            glyphTypeface.Style.ToString(),
            glyphTypeface.Weight.ToString(),
            glyphTypeface.Stretch.ToString(),
            glyphTypeface.FontSimulations.ToString(),
            platformTypeface.GetType().FullName ?? platformTypeface.GetType().Name,
            platformTypeface.FamilyName,
            platformTypeface.Style.ToString(),
            platformTypeface.Weight.ToString(),
            platformTypeface.Stretch.ToString(),
            platformTypeface.FontSimulations.ToString(),
            platformFontHash,
            glyphRun.Characters.ToString(),
            glyphData,
            FormatRect(ink),
            outline.Bounds,
            outline.RasterSha256);
    }

    private static OutlineObservation CaptureOutline(GlyphRun glyphRun)
    {
        var geometry = glyphRun.BuildGeometry();
        var bounds = geometry.Bounds;
        if (!double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0)
            return new OutlineObservation(FormatRect(bounds), "empty");

        const int margin = 2;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width) + margin * 2);
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height) + margin * 2);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            using (context.PushTransform(Matrix.CreateTranslation(margin - bounds.X, margin - bounds.Y)))
                context.DrawGeometry(Brushes.Black, null, geometry);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        var digest = Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        return new OutlineObservation(FormatRect(bounds), digest);
    }

    private static string? TryHashPlatformFont(IPlatformTypeface platformTypeface)
    {
        if (!platformTypeface.TryGetStream(out var stream) || stream is null)
            return null;
        using (stream)
            return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string FormatRect(Rect rect) => string.Create(CultureInfo.InvariantCulture,
        $"{rect.X:R},{rect.Y:R},{rect.Width:R},{rect.Height:R}");

    private static string Signature(FontRoute route, FontStyle requestedStyle, double width, double height, double baseline,
        ImmutableArray<ShapedRunObservation> runs)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Route = route.ToString(),
            RequestedStyle = requestedStyle.ToString(),
            Width = width,
            Height = height,
            Baseline = baseline,
            Runs = runs
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static async Task VerifyOrdersAsync()
    {
        var normalFirst = await RunChildProcessAsync(NormalFirst);
        PersistReport(normalFirst);
        var italicFirst = await RunChildProcessAsync(ItalicFirst);
        PersistReport(italicFirst);
        var obliqueFirst = await RunChildProcessAsync(ObliqueFirst);
        PersistReport(obliqueFirst);
        VerifyReport(normalFirst, NormalFirst);
        VerifyReport(italicFirst, ItalicFirst);
        VerifyReport(obliqueFirst, ObliqueFirst);
        var reports = new[] { normalFirst, italicFirst, obliqueFirst };
        PersistComparison(reports);

        foreach (var report in reports)
        {
            Qa.Equal("after-normal-face-warmup", report.InitializationPhase, "resolver 초기화 단계");
            VerifyProductFaceSemantics(report);
        }

        foreach (var referenceProbe in normalFirst.Probes.Where(probe => probe.Route == FontRoute.ProductResolver.ToString()))
        {
            foreach (var report in reports.Skip(1))
            {
                var otherByKey = report.Probes.ToDictionary(Key, StringComparer.Ordinal);
                Qa.Check(otherByKey.TryGetValue(Key(referenceProbe), out var otherProbe),
                    "제품 경로 순서 비교 대상 probe가 다른 프로세스 결과에 없습니다: " + Key(referenceProbe));
                Qa.Equal(referenceProbe.SignatureSha256, otherProbe!.SignatureSha256,
                    "요청 순서에 따른 제품 경로 face·glyph 윤곽 변화 " + report.Order + "/" + Key(referenceProbe));
            }
        }

        foreach (var report in reports.Skip(1))
        {
            Qa.Equal(normalFirst.Artifacts.PngSha256, report.Artifacts.PngSha256,
                "컨트롤 PNG 프로세스 순서 일치 " + report.Order);
            Qa.Equal(normalFirst.Artifacts.SvgSha256, report.Artifacts.SvgSha256,
                "SVG 프로세스 순서 일치 " + report.Order);
            Qa.Equal(normalFirst.Artifacts.ExternalSvgPngSha256, report.Artifacts.ExternalSvgPngSha256,
                "Firefox headless SVG 출력 프로세스 순서 일치 " + report.Order);
        }
    }

    private static void VerifyReport(FontProbeReport report, string expectedOrder)
    {
        Qa.Equal(expectedOrder, report.Order, "자식 글꼴 요청 순서");
        Qa.Equal(6, report.Probes.Length, "route/style probe 수");
        foreach (var route in Enum.GetNames<FontRoute>())
        foreach (var style in new[] { FontStyle.Normal.ToString(), FontStyle.Italic.ToString(), FontStyle.Oblique.ToString() })
        {
            var probe = report.Probes.SingleOrDefault(item => item.Route == route && item.RequestedStyle == style);
            Qa.Check(probe is not null, $"자식 결과에 {route}/{style} 요청이 없습니다.");
            Qa.Check(probe!.Runs.Length > 0, $"자식 결과에 {route}/{style} shaped run이 없습니다.");
            Qa.Check(probe.Runs.All(run => run.Text.Length > 0 && run.GlyphData.Length > 0),
                $"자식 결과에 {route}/{style} glyph id가 없습니다.");
        }
        Qa.Check(report.Artifacts.PngSha256.Length == 64 && report.Artifacts.SvgSha256.Length == 64 &&
                 report.Artifacts.ExternalSvgPngSha256.Length == 64,
            "오프스크린 이미지/SVG 해시가 누락되었습니다.");
    }

    private static void VerifyProductFaceSemantics(FontProbeReport report)
    {
        var normal = FindProductProbe(report, FontStyle.Normal);
        var italic = FindProductProbe(report, FontStyle.Italic);
        var oblique = FindProductProbe(report, FontStyle.Oblique);

        foreach (var run in normal.Runs)
        {
            Qa.Equal(CambriaMathTypefaceCollection.FamilyName, run.GlyphFamilyName,
                "제품 Normal glyph family " + report.Order);
            Qa.Equal(CambriaMathTypefaceCollection.FamilyName, run.PlatformFamilyName,
                "제품 Normal platform family " + report.Order);
            Qa.Equal(FontStyle.Normal.ToString(), run.PlatformStyle,
                "제품 Normal platform style " + report.Order);
            Qa.Equal(FontSimulations.None.ToString(), run.GlyphSimulations,
                "제품 Normal glyph simulations " + report.Order);
            Qa.Equal(FontSimulations.None.ToString(), run.PlatformSimulations,
                "제품 Normal platform simulations " + report.Order);
        }

        foreach (var run in italic.Runs.Concat(oblique.Runs))
        {
            Qa.Equal(CambriaMathTypefaceCollection.FamilyName, run.GlyphFamilyName,
                "제품 Italic/Oblique glyph family " + report.Order);
            Qa.Equal(CambriaMathTypefaceCollection.FamilyName, run.PlatformFamilyName,
                "제품 Italic/Oblique platform family " + report.Order);
            Qa.Equal(FontStyle.Oblique.ToString(), run.PlatformStyle,
                "제품 Italic/Oblique platform style " + report.Order);
            Qa.Equal(FontSimulations.None.ToString(), run.GlyphSimulations,
                "제품 Italic/Oblique glyph simulations " + report.Order);
            Qa.Equal(FontSimulations.None.ToString(), run.PlatformSimulations,
                "제품 Italic/Oblique platform simulations " + report.Order);
        }

        Qa.Check(italic.Runs.SequenceEqual(oblique.Runs),
            "제품 Italic 요청이 native Oblique와 같은 glyph face·윤곽을 사용하지 않습니다. " + report.Order);
        Qa.Equal(italic.Width, oblique.Width, "제품 Italic/Oblique 폭 " + report.Order);
        Qa.Equal(italic.Height, oblique.Height, "제품 Italic/Oblique 높이 " + report.Order);
        Qa.Equal(italic.Baseline, oblique.Baseline, "제품 Italic/Oblique 기준선 " + report.Order);
        Qa.Check(normal.Runs.Length == italic.Runs.Length &&
                 normal.Runs.Zip(italic.Runs).All(pair => pair.First.Text == pair.Second.Text &&
                                                          pair.First.OutlineRasterSha256 != pair.Second.OutlineRasterSha256),
            "제품 Italic face가 실제 glyph 윤곽을 기울이지 않습니다. " + report.Order);
    }

    private static FontRequestObservation FindProductProbe(FontProbeReport report, FontStyle style) =>
        report.Probes.Single(probe => probe.Route == FontRoute.ProductResolver.ToString() &&
                                      probe.RequestedStyle == style.ToString());

    private static async Task<FontProbeReport> RunChildProcessAsync(string order)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("현재 프로세스 실행 경로가 없습니다.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("dotnet host의 QA 어셈블리 경로를 찾을 수 없습니다.");
            startInfo.ArgumentList.Add(assemblyPath);
        }
        startInfo.ArgumentList.Add("--font-probe");
        startInfo.ArgumentList.Add(order);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("신선한 글꼴 probe 프로세스를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{order} 글꼴 probe 프로세스가 90초 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Qa.Equal(0, process.ExitCode, $"{order} 글꼴 probe 자식 프로세스 종료 코드; stderr={stderr}");
        var jsonLines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Qa.Equal(1, jsonLines.Length, $"{order} 자식 프로세스 JSON 출력 줄 수; stdout={stdout}; stderr={stderr}");
        return JsonSerializer.Deserialize<FontProbeReport>(jsonLines[0], JsonOptions)
            ?? throw new InvalidOperationException(order + " 자식 프로세스 결과 JSON이 비어 있습니다.");
    }

    private static string Key(FontRequestObservation observation) => observation.Route + "/" + observation.RequestedStyle;

    private static void PersistReport(FontProbeReport report)
    {
        var path = Path.Combine(report.Artifacts.Directory, $"font-probe-{report.Order}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
    }

    private static void PersistComparison(FontProbeReport[] reports)
    {
        var root = Directory.GetParent(reports[0].Artifacts.Directory)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("글꼴 probe evidence 루트를 찾지 못했습니다.");
        var path = Path.Combine(root, $"order-comparison-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
        var reference = reports[0];
        var comparison = new
        {
            Reports = reports.Select(report => new
            {
                report.Order,
                report.InitializationPhase,
                report.Artifacts.Directory,
                report.Artifacts.PngSha256,
                report.Artifacts.SvgSha256,
                report.Artifacts.ExternalSvgPngSha256
            }),
            ProbeSignatures = reference.Probes.Select(probe => new
            {
                Key = Key(probe),
                SignaturesByOrder = reports.ToDictionary(report => report.Order,
                    report => report.Probes.Single(other => Key(other) == Key(probe)).SignatureSha256,
                    StringComparer.Ordinal)
            }),
            SystemRouteSignaturesAreDiagnosticOnly = true
        };
        File.WriteAllText(path, JsonSerializer.Serialize(comparison,
            new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
    }

    private enum FontRoute
    {
        System,
        ProductResolver
    }
}

internal sealed record FontProbeReport(
    string Order,
    string InitializationPhase,
    ImmutableArray<FontRequestObservation> Probes,
    OffscreenArtifacts Artifacts);

internal sealed record FontRequestObservation(
    string Route,
    string RequestedStyle,
    string RequestedFamily,
    double Width,
    double Height,
    double Baseline,
    ImmutableArray<ShapedRunObservation> Runs,
    string SignatureSha256);

internal sealed record ShapedRunObservation(
    string Text,
    string GlyphFamilyName,
    string TypographicFamilyName,
    string FaceNames,
    string GlyphStyle,
    string GlyphWeight,
    string GlyphStretch,
    string GlyphSimulations,
    string PlatformType,
    string PlatformFamilyName,
    string PlatformStyle,
    string PlatformWeight,
    string PlatformStretch,
    string PlatformSimulations,
    string? PlatformFontSha256,
    string Characters,
    string GlyphData,
    string InkBounds,
    string OutlineBounds,
    string OutlineRasterSha256);

internal sealed record OutlineObservation(string Bounds, string RasterSha256);
