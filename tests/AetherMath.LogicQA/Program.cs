using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using AetherMath.Desktop;

namespace AetherMath.LogicQA;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 전용 자식 분기만 Headless 플랫폼을 초기화한다. 일반 QA는 창을 만들거나 표시하지 않는다.
        if (args.Length == 2 && args[0] == "--row-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--group-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--lifecycle-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--library-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--template-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--palette-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--structure-clear-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--properties-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--typesetting-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--output-preview-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--text-role-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--dfrac-pin-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--group-row-spacing-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--thin-math-space-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--bracket-kind-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--latex-copy-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--latex-highlight-ui-headless")
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else if (args.Length == 2 && args[0] == "--pointer-headless")
        {
            AppBuilder.Configure<QaApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
        else
        {
            AppBuilder.Configure<QaApplication>().UsePlatformDetect().SetupWithoutStarting();
        }
        using var shutdown = new CancellationTokenSource();
        int exitCode = 1;
        Dispatcher.UIThread.Post(async () =>
        {
            try { exitCode = await RunCasesAsync(args); }
            catch (Exception exception) { Console.Error.WriteLine("실행기 실패: " + exception); }
            finally { shutdown.Cancel(); }
        });
        try { Dispatcher.UIThread.MainLoop(shutdown.Token); }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        return exitCode;
    }

    private static async Task<int> RunCasesAsync(string[] args)
    {
        if (args.Length == 2 && args[0] == "--pointer-headless")
            return await PointerCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--row-ui-headless")
            return await RowUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--group-ui-headless")
            return await GroupUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--lifecycle-ui-headless")
            return await LifecycleUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--library-ui-headless")
            return await LibraryUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--template-ui-headless")
            return await TemplateUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--palette-ui-headless")
            return await PaletteUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--structure-clear-ui-headless")
            return await StructureClearUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--properties-ui-headless")
            return await PropertiesUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--typesetting-ui-headless")
            return await TypesettingUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--output-preview-ui-headless")
            return await OutputPreviewUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--text-role-ui-headless")
            return await TextRoleUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--dfrac-pin-ui-headless")
            return await DfracPinUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--group-row-spacing-ui-headless")
            return await GroupRowSpacingUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--thin-math-space-ui-headless")
            return await ThinMathSpaceUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--bracket-kind-ui-headless")
            return await BracketKindUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--latex-copy-ui-headless")
            return await LatexCopyUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--latex-highlight-ui-headless")
            return await LatexHighlightUiInteractionCases.RunHeadlessChildAsync(args[1]);

        if (args.Length == 2 && args[0] == "--font-probe")
        {
            FontProbeCases.RunChild(args[1]);
            return 0;
        }

        var cases = EditingCases.All()
            .Concat(PersistenceCases.All())
            .Concat(LayoutSvgCases.All())
            .Concat(ViewModelCases.All())
            .Concat(ClipboardCases.All())
            .Concat(RowEditingCases.All())
            .Concat(RowUiInteractionCases.All())
            .Concat(GroupEditingCases.All())
            .Concat(GroupUiInteractionCases.All())
            .Concat(LifecycleCases.All())
            .Concat(LifecycleUiInteractionCases.All())
            .Concat(WorkspaceStateCases.All())
            .Concat(WorkspaceStateIntegrationCases.All())
            .Concat(LibraryUiInteractionCases.All())
            .Concat(TemplateEditingCases.All())
            .Concat(TemplateUiInteractionCases.All())
            .Concat(PointerCases.All())
            .Concat(FontProbeCases.All())
            .Concat(OffscreenRenderCases.All())
            .Concat(SymbolPaletteCases.All())
            .Concat(PaletteUiInteractionCases.All())
            .Concat(StructureClearCases.All())
            .Concat(StructureClearUiInteractionCases.All())
            .Concat(PropertiesCases.All())
            .Concat(PropertiesUiInteractionCases.All())
            .Concat(TypesettingCases.All())
            .Concat(TypesettingUiInteractionCases.All())
            .Concat(OutputPreviewCases.All())
            .Concat(OutputPreviewUiInteractionCases.All())
            .Concat(TextRoleCases.All())
            .Concat(TextRoleUiInteractionCases.All())
            .Concat(DfracPinCases.All())
            .Concat(DfracPinUiInteractionCases.All())
            .Concat(GroupRowSpacingCases.All())
            .Concat(GroupRowSpacingUiInteractionCases.All())
            .Concat(ThinMathSpaceCases.All())
            .Concat(ThinMathSpaceUiInteractionCases.All())
            .Concat(BracketKindCases.All())
            .Concat(BracketKindUiInteractionCases.All())
            .Concat(LatexCopyCases.All())
            .Concat(LatexCopyUiInteractionCases.All())
            .Concat(LatexHighlightCases.All())
            .Concat(LatexHighlightUiInteractionCases.All())
            .ToArray();
        var selected = args.Length == 0 ? cases : cases.Where(c => args.Contains(c.Id, StringComparer.Ordinal)).ToArray();
        if (selected.Length == 0 || args.Any(id => cases.All(c => c.Id != id)))
        {
            Console.Error.WriteLine("알 수 없는 시나리오 ID. 사용 가능: " + string.Join(", ", cases.Select(c => c.Id)));
            return 2;
        }
        int failures = 0;
        foreach (var test in selected)
        {
            try
            {
                await test.RunAsync();
                Console.WriteLine($"PASS {test.Id} {test.Description}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Id} {test.Description}\n{exception}");
            }
        }
        Console.WriteLine($"RESULT 전체={selected.Length} 합격={selected.Length - failures} 실패={failures}");
        return failures == 0 ? 0 : 1;
    }
}

public sealed class QaApplication : Application { }
internal sealed record QaCase(string Id, string Description, Func<Task> RunAsync)
{
    public QaCase(string id, string description, Action run)
        : this(id, description, () => { run(); return Task.CompletedTask; }) { }
}
