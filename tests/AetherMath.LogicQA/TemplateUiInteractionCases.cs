using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;

namespace AetherMath.LogicQA;

internal static class TemplateUiInteractionCases
{
    private const string CaseId = "TEMPLATE-UI";
    private static readonly TemplateBinding[] Templates =
    [
        new("InsertFractionButton", "분수 템플릿 삽입", "InsertFractionCommand", MathTemplateKind.Fraction),
        new("InsertRootButton", "근호 템플릿 삽입", "InsertRootCommand", MathTemplateKind.Radical),
        new("InsertSuperscriptButton", "위첨자 템플릿 삽입", "InsertSuperscriptCommand", MathTemplateKind.Superscript),
        new("InsertMatrixButton", "2행 2열 행렬 템플릿 삽입", "InsertMatrixCommand", MathTemplateKind.Matrix),
        new("InsertSubscriptButton", "아래첨자 템플릿 삽입", "InsertSubscriptCommand", MathTemplateKind.Subscript),
        new("InsertIntegralButton", "적분 템플릿 삽입", "InsertIntegralCommand", MathTemplateKind.Integral),
        new("InsertSummationButton", "합 템플릿 삽입", "InsertSummationCommand", MathTemplateKind.Summation),
        new("InsertLimitButton", "극한 템플릿 삽입", "InsertLimitCommand", MathTemplateKind.Limit),
        new("InsertBracketsButton", "소괄호 템플릿 삽입", "InsertBracketsCommand", MathTemplateKind.Brackets),
        new("InsertCasesButton", "두 행 경우 나누기 템플릿 삽입", "InsertCasesCommand", MathTemplateKind.Cases)
    ];
    private static MainWindow? _window;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "10개 수식 템플릿 버튼의 접근성·공통 세션 삽입·슬롯 편집과 최소/기본 크기 화면의 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (caseId != CaseId)
        {
            Console.Error.WriteLine("알 수 없는 템플릿 UI headless 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("템플릿 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
            return 2;
        }

        try
        {
            await RunWindowChecksAsync();
            Console.WriteLine("PASS " + CaseId);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {CaseId}\n{exception}");
            return 1;
        }
        finally
        {
            try
            {
                await CloseOwnedWindowAsync(_window);
            }
            finally
            {
                _window = null;
            }
        }
    }

    private static async Task RunHeadlessProcessAsync()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("현재 프로세스 실행 경로가 없습니다.");
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
        startInfo.ArgumentList.Add("--template-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("템플릿 UI headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("템플릿 UI headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"템플릿 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("템플릿 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow(viewModel)
        {
            Width = 1060,
            Height = 700,
            CanResize = true
        };
        _window = window;
        EnsureStubHandle(window, "Show 전");
        window.Show();
        EnsureStubHandle(window, "Show 후");
        await DrainLayoutAsync(window);

        AssertTemplateBindings(window, viewModel);

        await CaptureAtEdgesAsync(window, minimum: true);
        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "1360×880 재배치 후");
        await CaptureAtEdgesAsync(window, minimum: false);
        await SetScrollEdgeAsync(window, top: true);

        var defaultSession = viewModel.Session;
        await ClickAsync(window, Find<Button>(window, "NewWorkspaceButton"));
        await DrainLayoutAsync(window);
        var session = viewModel.Session;
        Qa.Check(!ReferenceEquals(defaultSession, session), "새 작업 버튼이 빈 workspace를 만들지 않았습니다.");
        Qa.Check(ReferenceEquals(session, Find<MathEditorControl>(window, "MathEditorSurface").Session),
            "MainWindow의 수식 편집기가 ViewModel의 새 Session을 사용하지 않습니다.");
        var initialFingerprint = Qa.Fingerprint(session.Workspace);
        var initialCaret = session.Caret;
        Qa.Check(Qa.Leaf(session, initialCaret.NodeId) is { IsEmptySlot: true, Text.Length: 0 },
            "새 빈 작업의 시작 caret가 기본 빈 슬롯을 가리키지 않습니다.");
        Qa.Check(Find<MathEditorControl>(window, "MathEditorSurface").IsFocused,
            "빈 작업 생성 후 managed editor focus가 복원되지 않았습니다.");

        foreach (var template in Templates)
        {
            var button = Find<Button>(window, template.ButtonName);
            var scroll = Find<ScrollViewer>(window, "TemplateToolsScrollViewer");
            await BringButtonIntoViewAsync(window, scroll, button);
            EnsureStubHandle(window, template.ButtonName + " 합성 입력 전");
            Qa.Check(Contains(BoundsInWindow(scroll, window), BoundsInWindow(button, window)),
                $"{template.ButtonName}가 스크롤 viewport에 완전히 들어오지 않았습니다.");

            var before = Qa.Fingerprint(session.Workspace);
            Qa.Equal(initialFingerprint, before, template.Kind + " 실행 전 workspace 원상태");
            var priorIds = Qa.Nodes(session.Workspace).Select(node => node.Id).ToHashSet();
            var priorRevision = session.Revision;
            await ClickAsync(window, button);
            await DrainLayoutAsync(window);

            Qa.Check(ReferenceEquals(session, viewModel.Session), template.Kind + " 삽입이 기존 Session을 교체했습니다.");
            Qa.Check(session.Revision > priorRevision, template.Kind + " 버튼이 Session을 변경하지 않았습니다.");
            Qa.Check(Find<MathEditorControl>(window, "MathEditorSurface").IsFocused,
                template.Kind + " 삽입 뒤 managed editor focus가 복원되지 않았습니다.");
            var insertedFingerprint = Qa.Fingerprint(session.Workspace);
            Qa.Check(!string.Equals(before, insertedFingerprint, StringComparison.Ordinal),
                template.Kind + " 버튼이 workspace 구조를 바꾸지 않았습니다.");

            var wrapper = Qa.Nodes(session.Workspace)
                .SingleOrDefault(node => !priorIds.Contains(node.Id) && MatchesKind(node, template.Kind));
            Qa.Check(wrapper is not null, template.Kind + " 삽입 후 해당 구조 wrapper node를 찾지 못했습니다.");
            AssertTemplateShape(wrapper!, template.Kind);
            var caretLeaf = Qa.Leaf(session, session.Caret.NodeId);
            Qa.Check(caretLeaf.IsEmptySlot && caretLeaf.Text.Length == 0,
                template.Kind + " 삽입 뒤 caret가 새 빈 슬롯으로 이동하지 않았습니다.");

            var expectedSlotOrder = ExpectedSlotIds(wrapper!);
            var enumeratedSlotOrder = Qa.Walk(wrapper!).OfType<TextNode>().Select(node => node.Id).ToArray();
            Qa.Check(expectedSlotOrder.SequenceEqual(enumeratedSlotOrder),
                template.Kind + " AST의 논리 슬롯 순서와 검증 traversal 순서가 다릅니다.");
            var caretBeforeNavigation = session.Caret;
            var revisionAfterInsert = session.Revision;
            Qa.Check(session.TrySetCaret(new MathCaret(expectedSlotOrder[0], 0)),
                template.Kind + " 첫 슬롯 caret를 설정하지 못했습니다.");
            for (var index = 1; index < expectedSlotOrder.Length; index++)
            {
                await SendKeyAsync(window, Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                Qa.Equal(expectedSlotOrder[index], session.Caret.NodeId,
                    template.Kind + " Tab 슬롯 이동 " + index);
            }
            for (var index = expectedSlotOrder.Length - 2; index >= 0; index--)
            {
                await SendKeyAsync(window, Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                Qa.Equal(expectedSlotOrder[index], session.Caret.NodeId,
                    template.Kind + " Shift+Tab 슬롯 이동 " + index);
            }
            Qa.Equal(revisionAfterInsert, session.Revision, template.Kind + " 슬롯 순회 중 revision 불변");
            Qa.Equal(insertedFingerprint, Qa.Fingerprint(session.Workspace),
                template.Kind + " 슬롯 순회 중 AST 불변");
            Qa.Check(session.TrySetCaret(caretBeforeNavigation), template.Kind + " 삽입 당시 caret 복원 실패");

            window.KeyTextInput("x");
            await DrainLayoutAsync(window);
            Qa.Equal("x", Qa.Leaf(session, session.Caret.NodeId).Text,
                template.Kind + "의 빈 슬롯에서 합성 텍스트 입력");
            Qa.Check(!Qa.Nodes(session.Workspace).OfType<TextNode>().Any(node => node.IsEmptySlot && node.Text == "x"),
                template.Kind + " 입력 텍스트가 빈 슬롯 상태로 남았습니다.");
            var editedFingerprint = Qa.Fingerprint(session.Workspace);
            Qa.Check(editedFingerprint != insertedFingerprint, template.Kind + " 슬롯 입력이 workspace에 반영되지 않았습니다.");

            SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
            await DrainLayoutAsync(window);
            Qa.Equal(insertedFingerprint, Qa.Fingerprint(session.Workspace), template.Kind + " 슬롯 텍스트 Ctrl+Z");
            Qa.Check(Qa.Leaf(session, session.Caret.NodeId) is { IsEmptySlot: true, Text.Length: 0 },
                template.Kind + " 입력 undo가 편집 가능한 빈 슬롯을 복원하지 않았습니다.");
            SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
            await DrainLayoutAsync(window);
            Qa.Equal(initialFingerprint, Qa.Fingerprint(session.Workspace), template.Kind + " 템플릿 삽입 Ctrl+Z");
            Qa.Equal(initialCaret, session.Caret, template.Kind + " 삽입 undo 뒤 원래 caret 복원");

            SendShortcut(window, Key.Y, PhysicalKey.Y, "y");
            await DrainLayoutAsync(window);
            Qa.Equal(insertedFingerprint, Qa.Fingerprint(session.Workspace), template.Kind + " 템플릿 삽입 Ctrl+Y");
            SendShortcut(window, Key.Y, PhysicalKey.Y, "y");
            await DrainLayoutAsync(window);
            Qa.Equal(editedFingerprint, Qa.Fingerprint(session.Workspace), template.Kind + " 슬롯 텍스트 Ctrl+Y");
            SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
            SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
            await DrainLayoutAsync(window);
            Qa.Equal(initialFingerprint, Qa.Fingerprint(session.Workspace), template.Kind + " 정리 후 원본 workspace 복원");
            Qa.Check(ReferenceEquals(session, viewModel.Session), template.Kind + " 실행 사이에 Session이 교체되었습니다.");
        }
        Qa.Equal(initialFingerprint, Qa.Fingerprint(session.Workspace), "템플릿 UI 시나리오 종료 workspace");
        await CaptureCompletedTemplateExamplesAsync(window, session);
    }

    private static async Task CaptureCompletedTemplateExamplesAsync(MainWindow window, EditingSession originalSession)
    {
        EnsureStubHandle(window, "완성 템플릿 시각 증거 생성 전");
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        Qa.Check(ReferenceEquals(editor.Session, originalSession),
            "시각 증거 생성 전 실제 편집기 Session이 UI 시나리오 Session과 다릅니다.");
        var originalMargin = editor.Margin;
        var originalBounds = editor.Bounds;

        var sample = CreateCompletedTemplateWorkspace();
        var workspace = sample.Workspace;
        var session = new EditingSession(workspace);
        var metrics = new AvaloniaMathTextMetrics();
        var layout = MathLayoutEngine.Layout(workspace, metrics,
            new MathLayoutOptions(FontSize: workspace.Style.FontSize));
        Qa.Equal(10, layout.LogicalRows.Length, "완성 템플릿 시각 증거 행 수");
        var width = Math.Max(1, (int)Math.Ceiling(layout.Width));
        var height = Math.Max(360, (int)Math.Ceiling(layout.Height));
        try
        {
            editor.SetCurrentValue(MathEditorControl.SessionProperty, session);
            editor.SetCurrentValue(Layoutable.MarginProperty, new Thickness(0));
            editor.Measure(new Size(width, height));
            editor.Arrange(new Rect(0, 0, width, height));
            var snapshotBounds = editor.Bounds;
            Qa.Near(width, snapshotBounds.Width, "템플릿 snapshot 직전 실제 editor bounds 너비", 0.5);
            Qa.Near(height, snapshotBounds.Height, "템플릿 snapshot 직전 실제 editor bounds 높이", 0.5);
            EnsureStubHandle(window, "완성 템플릿 오프스크린 렌더 직전");

            var repositoryRoot = FindRepositoryRoot();
            var directory = Path.Combine(repositoryRoot, "doc", "verification", "artifacts", "p2-templates",
                $"template-examples-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var pngPath = Path.Combine(directory, "template-examples.png");
            var boundsPath = Path.Combine(directory, "template-examples-bounds.json");
            using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
            {
                using (var drawingContext = bitmap.CreateDrawingContext())
                {
                    drawingContext.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    editor.Render(drawingContext);
                }
                using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                bitmap.Save(output, PngBitmapEncoderOptions.Default);
            }

            var rows = workspace.Groups.SelectMany(group => group.Rows).ToArray();
            var visualRows = rows.Select((row, index) =>
            {
                var rowLayout = layout.LogicalRows[index];
                var rootLayout = layout.Nodes.Single(node => node.NodeId == row.Root.Id);
                return new
                {
                    index = index + 1,
                    template = sample.Kinds[index].ToString(),
                    rootType = row.Root.GetType().Name,
                    rowBounds = new { rowLayout.Bounds.X, rowLayout.Bounds.Y, rowLayout.Bounds.Width, rowLayout.Bounds.Height },
                    rootBounds = new { rootLayout.Bounds.X, rootLayout.Bounds.Y, rootLayout.Bounds.Width, rootLayout.Bounds.Height }
                };
            }).ToArray();
            foreach (var row in visualRows)
            {
                Qa.Check(MatchesKind(rows[row.index - 1].Root, sample.Kinds[row.index - 1]),
                    $"완성 템플릿 {row.index}행의 종류 metadata와 실제 구조가 다릅니다.");
                Qa.Check(row.rowBounds.X >= 0 && row.rowBounds.Y >= 0 &&
                         row.rowBounds.X + row.rowBounds.Width <= layout.Width + 0.5 &&
                         row.rowBounds.Y + row.rowBounds.Height <= layout.Height + 0.5,
                    $"완성 템플릿 {row.template} 논리 행 bounds가 전체 레이아웃 밖에 있습니다.");
                Qa.Check(row.rootBounds.X >= 0 && row.rootBounds.Y >= 0 &&
                         row.rootBounds.X + row.rootBounds.Width <= layout.Width + 0.5 &&
                         row.rootBounds.Y + row.rootBounds.Height <= layout.Height + 0.5,
                    $"완성 템플릿 {row.template} 구조 bounds가 전체 레이아웃 밖에 있습니다.");
            }

            var pngBytes = File.ReadAllBytes(pngPath);
            Qa.Check(pngBytes.Length > 64 && pngBytes.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "완성 템플릿 오프스크린 캡처가 유효한 PNG가 아닙니다.");
            var evidence = new
            {
                renderedBy = nameof(MathEditorControl),
                evidencePurpose = "검토용 오프스크린 시각 증거이며 제품 내보내기 출력이 아닙니다.",
                background = "#FFFFFF",
                workspace = workspace.Name,
                windowSize = new { window.Width, window.Height },
                pixelSize = new { width, height },
                layoutSize = new { layout.Width, layout.Height },
                renderCoordinateSpace = "MathEditorControl 로컬 좌표 (0,0)에서 직접 Render",
                temporaryMargin = new { Left = 0d, Top = 0d, Right = 0d, Bottom = 0d },
                originalMargin = new { originalMargin.Left, originalMargin.Top, originalMargin.Right, originalMargin.Bottom },
                originalVisualTreeBounds = new
                {
                    originalBounds.X, originalBounds.Y, originalBounds.Width, originalBounds.Height
                },
                controlBoundsAtRender = new
                {
                    snapshotBounds.X, snapshotBounds.Y, snapshotBounds.Width, snapshotBounds.Height
                },
                rowCount = rows.Length,
                rows = visualRows,
                pngSha256 = Convert.ToHexString(SHA256.HashData(pngBytes))
            };
            File.WriteAllText(boundsPath, JsonSerializer.Serialize(evidence, new JsonSerializerOptions
            {
                WriteIndented = true
            }), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"TEMPLATE-UI artifact {pngPath}");
            Console.WriteLine($"TEMPLATE-UI artifact {boundsPath}");
        }
        finally
        {
            editor.SetCurrentValue(MathEditorControl.SessionProperty, originalSession);
            editor.SetCurrentValue(Layoutable.MarginProperty, originalMargin);
            Qa.Check(ReferenceEquals(editor.Session, originalSession),
                "오프스크린 시각 증거 생성 뒤 ViewModel에 바인딩된 원 Session identity가 복원되지 않았습니다.");
            editor.InvalidateMeasure();
            editor.InvalidateArrange();
            window.InvalidateMeasure();
            window.InvalidateArrange();
            await DrainLayoutAsync(window);
            Qa.Equal(originalMargin, editor.Margin, "오프스크린 이후 editor 원래 margin 복원");
            Qa.Equal(originalBounds, editor.Bounds, "오프스크린 이후 editor 원래 visual-tree bounds 복원");
            EnsureStubHandle(window, "완성 템플릿 시각 증거 복원 후");
        }
    }

    private static CompletedTemplateSample CreateCompletedTemplateWorkspace()
    {
        static TextNode Text(string value) => new(Guid.NewGuid(), value);
        static SequenceNode Sequence(params MathNode[] nodes) => new(Guid.NewGuid(), [.. nodes]);

        var formulas = new (MathTemplateKind Kind, MathNode Root)[]
        {
            (MathTemplateKind.Fraction, new FractionNode(Guid.NewGuid(), Sequence(Text("a"), Text("+"), Text("1")),
                Sequence(Text("b"), Text("+"), Text("2")))),
            (MathTemplateKind.Radical, new RadicalNode(Guid.NewGuid(), Sequence(Text("x"), Text("+"), Text("1")))),
            (MathTemplateKind.Superscript, new ScriptNode(Guid.NewGuid(), Text("x"), null, Text("2"))),
            (MathTemplateKind.Subscript, new ScriptNode(Guid.NewGuid(), Text("a"), Text("n"), null)),
            (MathTemplateKind.Integral, new IntegralNode(Guid.NewGuid(), Text("t"), Text("0"), Text("1"), Text("t"))),
            (MathTemplateKind.Summation, new SummationNode(Guid.NewGuid(), Text("i"), Text("i=1"), Text("n"))),
            (MathTemplateKind.Limit, new LimitNode(Guid.NewGuid(), Sequence(Text("x"), Text("→"), Text("0")),
                new FractionNode(Guid.NewGuid(), Text("sin(x)"), Text("x")))),
            (MathTemplateKind.Matrix, new MatrixNode(Guid.NewGuid(), 2, 2, [Text("1"), Text("0"), Text("0"), Text("1")])),
            (MathTemplateKind.Brackets, new BracketsNode(Guid.NewGuid(), MathBracketKind.Parentheses,
                Sequence(Text("x"), Text("+"), Text("1")))),
            (MathTemplateKind.Cases, new CasesNode(Guid.NewGuid(),
            [
                new CaseRow(Text("1"), Sequence(Text("x"), Text(">"), Text("0"))),
                new CaseRow(Text("0"), Text("otherwise"))
            ]))
        };
        var workspace = new EquationWorkspace(Guid.NewGuid(), 1, "P2 템플릿 시각 표본",
            [new EquationGroup(Guid.NewGuid(), [.. formulas.Select(formula => new EquationRow(Guid.NewGuid(), formula.Root))],
                AlignmentMode.None, 14)], new MathStyle(30, "Cambria Math", "#25364D"));
        return new CompletedTemplateSample(workspace, formulas.Select(formula => formula.Kind).ToArray());
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AetherMath.sln")))
                return current.FullName;
        throw new DirectoryNotFoundException("AetherMath.sln을 기준으로 저장소 루트를 찾지 못했습니다.");
    }

    private sealed record CompletedTemplateSample(EquationWorkspace Workspace, MathTemplateKind[] Kinds);

    private static async Task CaptureAtEdgesAsync(MainWindow window, bool minimum)
    {
        await SetScrollEdgeAsync(window, top: true);
        RowShellVisualCases.CaptureAndVerify(window,
            minimum ? "templates-min-top" : "templates-default-top", "p2-templates");
        await SetScrollEdgeAsync(window, top: false);
        RowShellVisualCases.CaptureAndVerify(window,
            minimum ? "templates-min-bottom" : "templates-default-bottom", "p2-templates");
    }

    private static void AssertTemplateBindings(MainWindow window, MainWindowViewModel viewModel)
    {
        Qa.Equal(10, Templates.Select(template => template.ButtonName).Distinct(StringComparer.Ordinal).Count(),
            "템플릿 버튼 이름 고유 개수");
        foreach (var template in Templates)
        {
            var button = Find<Button>(window, template.ButtonName);
            var property = typeof(MainWindowViewModel).GetProperty(template.CommandName)
                ?? throw new InvalidOperationException("ViewModel 템플릿 명령 속성을 찾지 못했습니다: " + template.CommandName);
            var command = property.GetValue(viewModel) as ICommand
                ?? throw new InvalidOperationException("ViewModel 템플릿 속성이 ICommand가 아닙니다: " + template.CommandName);
            Qa.Check(ReferenceEquals(button.Command, command),
                template.ButtonName + "가 실제 ViewModel command에 연결되지 않았습니다.");
            Qa.Equal(template.ButtonName, AutomationProperties.GetAutomationId(button) ?? string.Empty,
                template.ButtonName + " AutomationId");
            Qa.Equal(template.AutomationName, AutomationProperties.GetName(button) ?? string.Empty,
                template.ButtonName + " 한국어 접근성 이름");
            var tooltip = ToolTip.GetTip(button)?.ToString() ?? string.Empty;
            Qa.Check(tooltip.Contains("빈", StringComparison.Ordinal) &&
                     (tooltip.Contains("선택", StringComparison.Ordinal) || tooltip.Contains("감싸", StringComparison.Ordinal)),
                template.ButtonName + "에 선택 적용/빈 슬롯 동작 안내가 없습니다.");
            Qa.Check(button.CommandParameter is null, template.ButtonName + "에 예기치 않은 CommandParameter가 있습니다.");
            Qa.Check(command.CanExecute(null) && button.IsEffectivelyEnabled,
                template.ButtonName + "의 command 상태와 UI 상태가 다릅니다.");
        }
    }

    private static void AssertTemplateShape(MathNode wrapper, MathTemplateKind kind)
    {
        Qa.Check(MatchesKind(wrapper, kind), $"{kind}에 잘못된 wrapper node 형식이 있습니다: {wrapper.GetType().Name}.");
        var leaves = Qa.Walk(wrapper).OfType<TextNode>().ToArray();
        Qa.Check(leaves.Length > 0 && leaves.All(node => node.IsEmptySlot && node.Text.Length == 0),
            $"{kind} 템플릿의 초기 slot이 빈 편집 가능 텍스트가 아닙니다.");

        switch (wrapper)
        {
            case FractionNode fraction:
                Qa.Check(fraction.Numerator is TextNode && fraction.Denominator is TextNode,
                    "분수의 분자/분모 기본 slot 구조가 맞지 않습니다.");
                break;
            case RadicalNode radical:
                Qa.Check(radical.Radicand is TextNode, "근호 기본 피제곱근 slot이 맞지 않습니다.");
                break;
            case ScriptNode script when kind == MathTemplateKind.Superscript:
                Qa.Check(script.Subscript is null && script.Superscript is TextNode,
                    "위첨자 템플릿 슬롯 구성이 맞지 않습니다.");
                break;
            case ScriptNode script when kind == MathTemplateKind.Subscript:
                Qa.Check(script.Subscript is TextNode && script.Superscript is null,
                    "아래첨자 템플릿 슬롯 구성이 맞지 않습니다.");
                break;
            case MatrixNode matrix:
                Qa.Equal(2, matrix.Rows, "행렬 템플릿 행 수");
                Qa.Equal(2, matrix.Columns, "행렬 템플릿 열 수");
                Qa.Equal(4, matrix.Cells.Length, "행렬 템플릿 빈 셀 수");
                break;
            case IntegralNode integral:
                Qa.Check(integral.LowerLimit is TextNode && integral.UpperLimit is TextNode &&
                         integral.Integrand is TextNode && integral.DifferentialVariable is TextNode,
                    "적분의 상한/하한/적분식/미분변수 slot 구성이 맞지 않습니다.");
                break;
            case SummationNode sum:
                Qa.Check(sum.LowerLimit is TextNode && sum.UpperLimit is TextNode && sum.Body is TextNode,
                    "합의 상한/하한/항 slot 구성이 맞지 않습니다.");
                break;
            case LimitNode limit:
                Qa.Check(limit.Approach is TextNode && limit.Body is TextNode,
                    "극한 접근값/식 slot 구성이 맞지 않습니다.");
                break;
            case BracketsNode brackets:
                Qa.Equal(MathBracketKind.Parentheses, brackets.Kind, "기본 괄호 종류");
                Qa.Check(brackets.Content is TextNode, "괄호 내용 slot 구성이 맞지 않습니다.");
                break;
            case CasesNode cases:
                Qa.Equal(2, cases.Rows.Length, "경우 나누기 초기 행 수");
                Qa.Check(cases.Rows.All(row => row.Expression is TextNode && row.Condition is TextNode),
                    "경우 나누기 각 행의 식/조건 slot 구성이 맞지 않습니다.");
                break;
            default:
                throw new InvalidOperationException("템플릿 구조 검사가 처리하지 않는 node입니다: " + wrapper.GetType().Name);
        }
    }

    private static bool MatchesKind(MathNode node, MathTemplateKind kind) => kind switch
    {
        MathTemplateKind.Fraction => node is FractionNode,
        MathTemplateKind.Radical => node is RadicalNode,
        MathTemplateKind.Superscript => node is ScriptNode { Superscript: not null, Subscript: null },
        MathTemplateKind.Matrix => node is MatrixNode,
        MathTemplateKind.Subscript => node is ScriptNode { Subscript: not null, Superscript: null },
        MathTemplateKind.Integral => node is IntegralNode,
        MathTemplateKind.Summation => node is SummationNode,
        MathTemplateKind.Limit => node is LimitNode,
        MathTemplateKind.Brackets => node is BracketsNode,
        MathTemplateKind.Cases => node is CasesNode,
        _ => false
    };

    private static Guid[] ExpectedSlotIds(MathNode wrapper) => wrapper switch
    {
        FractionNode fraction => [SlotId(fraction.Numerator), SlotId(fraction.Denominator)],
        RadicalNode radical => [SlotId(radical.Radicand)],
        ScriptNode script => new[] { script.Base, script.Subscript, script.Superscript }
            .OfType<MathNode>().Select(SlotId).ToArray(),
        MatrixNode matrix => matrix.Cells.Select(SlotId).ToArray(),
        IntegralNode integral =>
        [
            SlotId(integral.LowerLimit ?? throw new InvalidOperationException("적분 하한 slot이 없습니다.")),
            SlotId(integral.UpperLimit ?? throw new InvalidOperationException("적분 상한 slot이 없습니다.")),
            SlotId(integral.Integrand), SlotId(integral.DifferentialVariable)
        ],
        SummationNode sum =>
        [
            SlotId(sum.LowerLimit ?? throw new InvalidOperationException("합 하한 slot이 없습니다.")),
            SlotId(sum.UpperLimit ?? throw new InvalidOperationException("합 상한 slot이 없습니다.")),
            SlotId(sum.Body)
        ],
        LimitNode limit => [SlotId(limit.Approach), SlotId(limit.Body)],
        BracketsNode brackets => [SlotId(brackets.Content)],
        CasesNode cases => cases.Rows
            .SelectMany(row => new[] { row.Expression, row.Condition })
            .Select(SlotId).ToArray(),
        _ => throw new InvalidOperationException("템플릿 슬롯 순서 검사가 처리하지 않는 node입니다: " + wrapper.GetType().Name)
    };

    private static Guid SlotId(MathNode node) => node is TextNode text
        ? text.Id
        : throw new InvalidOperationException("템플릿 슬롯이 TextNode가 아닙니다: " + node.GetType().Name);

    private static async Task BringButtonIntoViewAsync(MainWindow window, ScrollViewer scroll, Button button)
    {
        await DrainLayoutAsync(window);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var origin = button.TranslatePoint(new Point(0, 0), scroll);
            Qa.Check(origin is Point, "템플릿 버튼의 ScrollViewer 기준 좌표를 얻지 못했습니다.");
            var top = origin!.Value.Y;
            var bottom = top + button.Bounds.Height;
            var nextOffset = scroll.Offset.Y;
            if (top < 0)
                nextOffset += top;
            else if (bottom > scroll.Viewport.Height)
                nextOffset += bottom - scroll.Viewport.Height;
            else
                break;

            var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(nextOffset, 0, maximum));
            await DrainLayoutAsync(window);
        }

        Qa.Check(Contains(BoundsInWindow(scroll, window), BoundsInWindow(button, window)),
            $"{AutomationProperties.GetAutomationId(button)}를 스크롤해도 버튼 전체가 viewport 안에 오지 않습니다.");
    }

    private static async Task SetScrollEdgeAsync(MainWindow window, bool top)
    {
        var scroll = Find<ScrollViewer>(window, "TemplateToolsScrollViewer");
        await DrainLayoutAsync(window);
        var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, top ? 0 : maximum);
        await DrainLayoutAsync(window);
    }

    private static async Task ClickAsync(MainWindow window, Button button)
    {
        EnsureStubHandle(window, "템플릿 버튼 합성 입력 전");
        Qa.Check(button.IsEffectivelyEnabled, "비활성 템플릿 버튼을 합성 클릭하려 했습니다.");
        var origin = button.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "템플릿 버튼의 창 기준 좌표를 얻지 못했습니다.");
        var point = origin!.Value + new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
        window.MouseMove(point, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static void SendShortcut(Window window, Key key, PhysicalKey physicalKey, string symbol)
    {
        EnsureStubHandle(window, "템플릿 undo/redo 합성 입력 전");
        window.KeyPress(key, RawInputModifiers.Control, physicalKey, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task SendKeyAsync(Window window, Key key, RawInputModifiers modifiers,
        PhysicalKey physicalKey, string symbol)
    {
        EnsureStubHandle(window, "템플릿 Tab 키 합성 입력 전");
        window.KeyPress(key, modifiers, physicalKey, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, symbol);
        await DrainLayoutAsync(window);
    }

    private static Rect BoundsInWindow(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "템플릿 컨트롤의 창 기준 bounds를 얻지 못했습니다.");
        return new Rect(origin!.Value, control.Bounds.Size);
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static async Task DrainLayoutAsync(Window window)
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            Dispatcher.UIThread.RunJobs();
            window.InvalidateMeasure();
            window.InvalidateArrange();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void EnsureStubHandle(Window window, string phase)
    {
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is not null && handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            $"{phase}: STUB/0 headless handle을 확인하지 못해 입력/시각 검증을 중단했습니다.");
    }

    private static async Task CloseOwnedWindowAsync(MainWindow? window)
    {
        if (window is null)
            return;

        if (window.DataContext is MainWindowViewModel viewModel && viewModel.IsPendingWorkspaceActionVisible)
            viewModel.CancelPendingWorkspaceAction();
        window.Close();
        if (window.DataContext is MainWindowViewModel closingViewModel &&
            closingViewModel.IsPendingWorkspaceActionVisible &&
            closingViewModel.PendingWorkspaceAction == WorkspaceActionKind.CloseWindow)
            closingViewModel.DiscardPendingWorkspaceAction();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "템플릿 UI가 소유한 headless 창이 정상 닫히지 않았습니다.");
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "템플릿 UI child 종료 뒤 실제 native window handle이 남았습니다.");
        Dispatcher.UIThread.RunJobs();
    }

    private static Button Find<Button>(Control root, string name) where Button : Control =>
        root.FindControl<Button>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name}을 찾지 못했습니다.");

    private sealed record TemplateBinding(string ButtonName, string AutomationName, string CommandName, MathTemplateKind Kind);
}
