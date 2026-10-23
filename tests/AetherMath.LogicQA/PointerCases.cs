using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;

namespace AetherMath.LogicQA;

internal static class PointerCases
{
    private static Window? _headlessWindow;
    private static bool _runningHeadlessChild;

    public static IEnumerable<QaCase> All()
    {
        yield return new("PTR-01", "headless visual tree의 합성 Ctrl+클릭으로 구조 선택 후 삭제·undo",
            () => RunHeadlessProcessAsync("PTR-01"));
        yield return new("PTR-02", "headless visual tree의 합성 포인터 누름·놓기에서 caret와 캡처 수명주기",
            () => RunHeadlessProcessAsync("PTR-02"));
        yield return new("PTR-03", "headless visual tree의 합성 capture-loss 이벤트가 드래그 상태를 정리",
            () => RunHeadlessProcessAsync("PTR-03"));
        yield return new("PTR-04", "visual-tree detach·재부착과 Session 교체가 잡힌 포인터를 해제",
            () => RunHeadlessProcessAsync("PTR-04"));
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (caseId is not ("PTR-01" or "PTR-02" or "PTR-03" or "PTR-04"))
        {
            Console.Error.WriteLine("알 수 없는 headless 포인터 시나리오 ID: " + caseId);
            return 2;
        }

        _runningHeadlessChild = true;
        try
        {
            switch (caseId)
            {
                case "PTR-01": ControlClickStructureSelection(); break;
                case "PTR-02": PointerCaptureAndRelease(); break;
                case "PTR-03": PointerCaptureLost(); break;
                case "PTR-04": AttachmentAndSessionReleaseCapture(); break;
            }
            Console.WriteLine("PASS " + caseId);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {caseId}\n{exception}");
            return 1;
        }
        finally
        {
            try { _headlessWindow?.Close(); }
            finally
            {
                _headlessWindow = null;
                _runningHeadlessChild = false;
            }
        }
    }

    private static void ControlClickStructureSelection()
    {
        var numerator = Qa.Text("a+b");
        var denominator = Qa.Text("c+d");
        var fraction = new FractionNode(Guid.NewGuid(), numerator, denominator);
        var workspace = Qa.Workspace(fraction);
        var session = new EditingSession(workspace);
        Qa.Caret(session, numerator.Id, 1);
        var control = CreateAttachedControl(session);
        var layout = MathLayoutEngine.Layout(workspace, new AvaloniaMathTextMetrics(), new MathLayoutOptions(FontSize: 30));
        var click = layout.Nodes.Single(node => node.NodeId == numerator.Id).CaretStops[1].Position;
        Qa.Equal<Guid?>(fraction.Id, layout.HitTestSelectionTarget(click), "합성 click의 구조 대상 선검증");

        var pointer = NewPointer();
        var pressed = Press(control, pointer, click, KeyModifiers.Control);
        InvokeControlEvent(control, "OnPointerPressed", pressed);

        Qa.Check(pressed.Handled, "Ctrl+클릭 이벤트가 처리되지 않음");
        Qa.Check(session.IsNodeSelected, "Ctrl+클릭이 전체 구조 선택을 만들지 않음");
        Qa.Equal<Guid?>(fraction.Id, session.SelectedNodeId, "Ctrl+클릭 전체 선택 대상");
        Qa.Equal(new MathCaret(numerator.Id, 1), session.Caret, "Ctrl+클릭이 caret를 이동함");
        Qa.Check(pointer.Captured is null, "Ctrl+클릭이 일반 드래그 포인터를 캡처함");

        var before = Qa.Fingerprint(session.Workspace);
        Qa.Check(session.DeleteSelection(), "Ctrl+클릭 선택 구조 삭제 실패");
        Qa.Check(!session.IsNodeSelected, "구조 삭제 뒤 전체 선택 잔존");
        session.Undo();
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "구조 삭제 undo 원본 구조/ID");
        Qa.Check(session.IsNodeSelected, "구조 삭제 undo 뒤 전체 선택 복원 실패");
        Qa.Equal<Guid?>(fraction.Id, session.SelectedNodeId, "구조 삭제 undo 선택 대상");
    }

    private static void PointerCaptureAndRelease()
    {
        var text = Qa.Text("xy");
        var session = new EditingSession(Qa.Workspace(text));
        var control = CreateAttachedControl(session);
        var layout = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics(), new MathLayoutOptions(FontSize: 30));
        var click = layout.Nodes.Single(node => node.NodeId == text.Id).CaretStops[1].Position;
        var pointer = NewPointer();

        var pressed = Press(control, pointer, click, KeyModifiers.None);
        InvokeControlEvent(control, "OnPointerPressed", pressed);
        Qa.Check(pressed.Handled, "일반 포인터 누름 이벤트가 처리되지 않음");
        Qa.Equal(new MathCaret(text.Id, 1), session.Caret, "일반 포인터 누름 caret 위치");
        Qa.Check(ReferenceEquals(control, pointer.Captured), "일반 포인터 누름이 control을 캡처하지 않음");

        var released = new PointerReleasedEventArgs(control, pointer, control,
            new Point(click.X, click.Y), 2,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left);
        InvokeControlEvent(control, "OnPointerReleased", released);
        Qa.Check(released.Handled, "일반 포인터 놓기 이벤트가 처리되지 않음");
        Qa.Check(pointer.Captured is null, "포인터 놓기 후 capture가 남아 있음");
    }

    private static void AttachmentAndSessionReleaseCapture()
    {
        var text = Qa.Text("xy");
        var session = new EditingSession(Qa.Workspace(text));
        var control = CreateAttachedControl(session);
        var layout = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics(), new MathLayoutOptions(FontSize: 30));
        var click = layout.Nodes.Single(node => node.NodeId == text.Id).CaretStops[1].Position;

        var detachedPointer = NewPointer();
        var pressed = Press(control, detachedPointer, click, KeyModifiers.None);
        InvokeControlEvent(control, "OnPointerPressed", pressed);
        Qa.Check(ReferenceEquals(control, detachedPointer.Captured), "detach 경계 사전 capture 실패");
        DetachFromHeadlessRoot(control);
        Qa.Check(detachedPointer.Captured is null, "detach 뒤 pointer capture 잔존");

        ReattachToHeadlessRoot(control);
        var replacedPointer = NewPointer();
        var secondPress = Press(control, replacedPointer, click, KeyModifiers.None);
        InvokeControlEvent(control, "OnPointerPressed", secondPress);
        Qa.Check(ReferenceEquals(control, replacedPointer.Captured), "Session 교체 경계 사전 capture 실패");

        var replacementText = Qa.Text("new");
        var replacement = new EditingSession(Qa.Workspace(replacementText));
        control.Session = replacement;
        Qa.Check(ReferenceEquals(replacement, control.Session), "Session 교체 실패");
        Qa.Check(replacedPointer.Captured is null, "Session 교체 뒤 pointer capture 잔존");
    }

    private static void PointerCaptureLost()
    {
        var text = Qa.Text("xy");
        var session = new EditingSession(Qa.Workspace(text));
        var control = CreateAttachedControl(session);
        var layout = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics(), new MathLayoutOptions(FontSize: 30));
        var click = layout.Nodes.Single(node => node.NodeId == text.Id).CaretStops[1].Position;
        var pointer = NewPointer();

        var pressed = Press(control, pointer, click, KeyModifiers.None);
        InvokeControlEvent(control, "OnPointerPressed", pressed);
        Qa.Check(ReferenceEquals(control, pointer.Captured), "capture-loss 시험의 포인터 누름 capture 실패");

        pointer.Capture(null); // 플랫폼이 capture를 잃은 상태를 합성한다.
        var captureLost = new PointerCaptureLostEventArgs(control, pointer);
        InvokeControlEvent(control, "OnPointerCaptureLost", captureLost);
        var released = Release(control, pointer, click);
        InvokeControlEvent(control, "OnPointerReleased", released);
        Qa.Check(!released.Handled, "capture loss 뒤 이전 capture가 포인터 놓기를 소비함");
        Qa.Check(pointer.Captured is null, "capture loss 뒤 포인터가 다시 캡처됨");
    }

    private static PointerReleasedEventArgs Release(MathEditorControl control, IPointer pointer, MathPoint point) =>
        new(control, pointer, control, new Point(point.X, point.Y), 2,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left);

    private static PointerPressedEventArgs Press(
        MathEditorControl control, IPointer pointer, MathPoint point, KeyModifiers modifiers) =>
        new(control, pointer, control, new Point(point.X, point.Y), 1,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            modifiers, 1);

    private static Avalonia.Input.Pointer NewPointer() => new(1, PointerType.Mouse, isPrimary: true);

    private static MathEditorControl CreateAttachedControl(EditingSession session)
    {
        if (!_runningHeadlessChild)
            throw new InvalidOperationException("PTR 입력 이벤트는 전용 headless child에서만 실행할 수 있습니다.");

        var control = new MathEditorControl { Session = session };
        _headlessWindow = new Window
        {
            Width = 1200,
            Height = 800,
            Content = control
        };
        // 이 Show는 --pointer-headless 전용 프로세스에서만 호출된다. 일반 QA 프로세스는
        // UsePlatformDetect 경로를 유지하며 Window를 생성하거나 표시하지 않는다.
        var platformHandle = _headlessWindow.TryGetPlatformHandle();
        Qa.Check(platformHandle is not null && platformHandle.HandleDescriptor == "STUB" && platformHandle.Handle == IntPtr.Zero,
            "headless Window.Show를 호출하기 전에 STUB/0 플랫폼 handle을 확인하지 못해 표시를 차단함");
        _headlessWindow.Show();
        platformHandle = _headlessWindow.TryGetPlatformHandle();
        Qa.Check(platformHandle is not null && platformHandle.HandleDescriptor == "STUB" && platformHandle.Handle == IntPtr.Zero,
            "표시 뒤 headless window handle이 STUB/0이 아님");
        ArrangeHeadlessRoot(control);
        Qa.Check(control.IsAttachedToVisualTree(), "headless control이 실제 visual tree에 연결되지 않음");
        return control;
    }

    private static void DetachFromHeadlessRoot(MathEditorControl control)
    {
        var window = _headlessWindow ?? throw new InvalidOperationException("headless root가 없습니다.");
        Qa.Check(ReferenceEquals(window.Content, control), "detach 전 control이 headless root의 content가 아님");
        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        Qa.Check(!control.IsAttachedToVisualTree(), "content 제거 후 control이 visual tree에서 분리되지 않음");
    }

    private static void ReattachToHeadlessRoot(MathEditorControl control)
    {
        var window = _headlessWindow ?? throw new InvalidOperationException("headless root가 없습니다.");
        Qa.Check(window.Content is null, "재부착 전 headless root가 비어 있지 않음");
        window.Content = control;
        ArrangeHeadlessRoot(control);
        Qa.Check(control.IsAttachedToVisualTree(), "content 재설정 후 control이 visual tree에 재부착되지 않음");
    }

    private static void ArrangeHeadlessRoot(MathEditorControl control)
    {
        Dispatcher.UIThread.RunJobs();
        var available = new Size(1200, 800);
        control.Measure(available);
        control.Arrange(new Rect(new Point(0, 0), available));
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task RunHeadlessProcessAsync(string caseId)
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
        startInfo.ArgumentList.Add("--pointer-headless");
        startInfo.ArgumentList.Add(caseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException(caseId + " headless PTR 자식 프로세스를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(caseId + " headless PTR 자식 프로세스가 60초 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Qa.Equal(0, process.ExitCode, caseId + " headless PTR 자식 종료 코드; stdout=" + stdout + "; stderr=" + stderr);
        var passLines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line == "PASS " + caseId)
            .ToArray();
        Qa.Equal(1, passLines.Length, caseId + " headless PTR 합격 출력; stdout=" + stdout + "; stderr=" + stderr);
    }

    private static void InvokeControlEvent(MathEditorControl control, string methodName, object eventArgs)
    {
        var method = typeof(MathEditorControl).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MathEditorControl).FullName, methodName);
        method.Invoke(control, [eventArgs]);
    }

}
