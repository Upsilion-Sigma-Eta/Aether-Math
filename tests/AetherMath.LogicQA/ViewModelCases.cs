using System.Text.Json.Nodes;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class ViewModelCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("VM-SAVE", "실제 저장 성공/잠금·경로실패의 dirty와 현재경로", SaveFailure);
        yield return new("VM-OPEN", "열기 실패 보존과 dirty valid-open의 명시적 Discard", OpenFailure);
        yield return new("VM-EXPORT", "필수 빈 슬롯 출력실패의 기존 SVG·편집 상태 보존", ExportFailure);
        yield return new("VM-COMMAND", "공개 삽입·undo/redo 명령의 모델변경과 focus 요청", Commands);
    }

    private static async Task SaveFailure()
    {
        using var directory = new TempDirectory(); var vm = new MainWindowViewModel();
        Qa.Equal<string?>(null, vm.CurrentPath, "초기 경로");
        var leaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, leaf.Id, 0); vm.Session.InsertText("첫 저장");
        var path = directory.File("saved.aethermath"); await vm.SaveToPathAsync(path);
        Qa.Equal(path, vm.CurrentPath, "성공 저장 경로");
        Qa.Check(!vm.Session.IsDirty, "성공 저장 dirty");
        var oldBytes = await File.ReadAllBytesAsync(path);
        var session = vm.Session; session.InsertText("미저장");
        var before = Qa.Fingerprint(session.Workspace); var revision = session.Revision;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) await vm.SaveToPathAsync(path);
        AssertFailed(path);
        var bytesAfterFailedSave = await File.ReadAllBytesAsync(path);
        Qa.Check(oldBytes.SequenceEqual(bytesAfterFailedSave), "VM 잠금실패 기존 파일");
        var parent = directory.File("file-parent"); await File.WriteAllTextAsync(parent, "keep");
        await vm.SaveToPathAsync(System.IO.Path.Combine(parent, "new.aethermath"));
        AssertFailed(path);
        Qa.Equal("keep", await File.ReadAllTextAsync(parent), "VM 실패 부모파일 보존");
        session.Undo(); Qa.Check(!session.IsDirty, "실패 후 이전 저장 state 유지");

        void AssertFailed(string currentPath)
        {
            Qa.Check(ReferenceEquals(session, vm.Session), "저장실패가 Session 교체");
            Qa.Equal(currentPath, vm.CurrentPath, "저장실패 CurrentPath");
            Qa.Equal(before, Qa.Fingerprint(vm.Session.Workspace), "저장실패 원본");
            Qa.Equal(revision, vm.Session.Revision, "저장실패 revision"); Qa.Check(vm.Session.IsDirty, "저장실패가 dirty 해제");
        }
    }

    private static async Task OpenFailure()
    {
        using var directory = new TempDirectory(); var vm = new MainWindowViewModel();
        var path = directory.File("active.aethermath"); await vm.SaveToPathAsync(path);
        var valid = await File.ReadAllTextAsync(path);
        var leaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, leaf.Id, 0); vm.Session.InsertText("미저장 원본");
        Qa.Select(vm.Session, leaf.Id, 0, 2);
        var session = vm.Session; var before = Qa.Fingerprint(session.Workspace);
        var caret = session.Caret; var selection = session.Selection; var revision = session.Revision;
        var invalid = directory.File("broken.aethermath"); await File.WriteAllTextAsync(invalid, "{broken");
        var unsupported = directory.File("future.aethermath"); var json = JsonNode.Parse(valid)!.AsObject();
        var schemaKey = json.Select(p => p.Key).Single(k => k.Equals("SchemaVersion", StringComparison.OrdinalIgnoreCase));
        json[schemaKey] = 999; await File.WriteAllTextAsync(unsupported, json.ToJsonString());
        foreach (var badPath in new[] { invalid, unsupported, directory.File("missing.aethermath") })
        {
            byte[]? bytes = File.Exists(badPath) ? await File.ReadAllBytesAsync(badPath) : null;
            Qa.Equal(WorkspaceActionRequestResult.Failed, await vm.OpenFromPathAsync(badPath), "잘못된 Open 결과");
            Qa.Check(ReferenceEquals(session, vm.Session), "열기실패가 활성 Session 교체");
            Qa.Equal(path, vm.CurrentPath, "열기실패 CurrentPath");
            Qa.Equal(before, Qa.Fingerprint(vm.Session.Workspace), "열기실패 메모리");
            Qa.Equal(caret, vm.Session.Caret, "열기실패 caret"); Qa.Equal(selection, vm.Session.Selection, "열기실패 selection");
            Qa.Equal(revision, vm.Session.Revision, "열기실패 revision"); Qa.Check(vm.Session.IsDirty, "열기실패 dirty");
            if (bytes is not null)
            {
                var bytesAfterFailedOpen = await File.ReadAllBytesAsync(badPath);
                Qa.Check(bytes.SequenceEqual(bytesAfterFailedOpen), "열기실패 입력파일 변경");
            }
            else Qa.Check(!File.Exists(badPath), "없는 파일 열기로 파일이 생성됨");
        }
        var canUndo = session.CanUndo; var canRedo = session.CanRedo;
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired,
            await vm.OpenFromPathAsync(path), "dirty valid Open은 확인을 요구");
        Qa.Equal(WorkspaceActionKind.OpenWorkspace, vm.PendingWorkspaceAction, "dirty valid Open pending 종류");
        Qa.Check(vm.IsPendingWorkspaceActionVisible, "dirty valid Open 확인 상태가 보이지 않음");
        Qa.Check(ReferenceEquals(session, vm.Session), "Discard 전 dirty valid Open이 Session을 바꿈");
        Qa.Equal(before, Qa.Fingerprint(vm.Session.Workspace), "Discard 전 dirty valid Open이 workspace를 바꿈");
        Qa.Equal(path, vm.CurrentPath, "Discard 전 dirty valid Open이 경로를 바꿈");
        Qa.Equal(caret, vm.Session.Caret, "Discard 전 dirty valid Open이 caret를 바꿈");
        Qa.Equal(selection, vm.Session.Selection, "Discard 전 dirty valid Open이 selection을 바꿈");
        Qa.Equal(revision, vm.Session.Revision, "Discard 전 dirty valid Open이 revision을 바꿈");
        Qa.Equal(canUndo, vm.Session.CanUndo, "Discard 전 dirty valid Open이 undo history를 바꿈");
        Qa.Equal(canRedo, vm.Session.CanRedo, "Discard 전 dirty valid Open이 redo history를 바꿈");
        Qa.Check(vm.Session.IsDirty, "Discard 전 dirty valid Open이 dirty를 지움");
        Qa.Equal(WorkspaceActionResolutionResult.Completed, vm.DiscardPendingWorkspaceAction(), "명시적 Open Discard");
        Qa.Equal(Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(path)), Qa.Fingerprint(vm.Session.Workspace), "성공 열기 저장원본");
        Qa.Equal(path, vm.CurrentPath, "성공 열기 경로"); Qa.Check(!vm.Session.IsDirty, "성공 열기 dirty");
    }

    private static async Task ExportFailure()
    {
        using var directory = new TempDirectory(); var vm = new MainWindowViewModel();
        var path = directory.File("original.svg"); await File.WriteAllTextAsync(path, "keep SVG");
        var id = vm.Session.InsertFraction(); Qa.Check(id.HasValue, "VM 세션 분수 삽입");
        var before = Qa.Fingerprint(vm.Session.Workspace); var revision = vm.Session.Revision;
        await vm.ExportSvgToPathAsync(path);
        Qa.Equal("keep SVG", await File.ReadAllTextAsync(path), "VM 미완성 출력 기존 파일");
        Qa.Equal(before, Qa.Fingerprint(vm.Session.Workspace), "VM 미완성 출력 원본");
        Qa.Equal(revision, vm.Session.Revision, "VM 미완성 출력 revision");
        Qa.Check(vm.Session.IsDirty, "VM 출력실패가 편집을 저장 처리");
        Qa.Equal<string?>(null, vm.CurrentPath, "SVG 출력은 원본 저장경로가 아님");
        Qa.Equal(1, Directory.GetFiles(directory.Path).Length, "VM 출력실패 임시파일");
    }

    private static void Commands()
    {
        foreach (var kind in new[] { "fraction", "root", "matrix", "script" })
        {
            var vm = new MainWindowViewModel(); var before = Qa.Fingerprint(vm.Session.Workspace);
            var originalIds = Qa.Nodes(vm.Session.Workspace).Select(n => n.Id).ToHashSet();
            bool focused = false; vm.EditorFocusRequested += (_, _) => focused = true;
            var command = kind switch
            {
                "fraction" => vm.InsertFractionCommand, "root" => vm.InsertRootCommand,
                "matrix" => vm.InsertMatrixCommand, _ => vm.InsertSuperscriptCommand
            };
            Qa.Check(command.CanExecute(null), kind + " 명령 사용불가"); command.Execute(null);
            Qa.Check(focused && vm.Session.IsDirty, kind + " 실제 삽입/focus");
            Qa.Check(before != Qa.Fingerprint(vm.Session.Workspace), kind + " 명령 모델 무변경");
            var expectedType = kind switch
            {
                "fraction" => typeof(FractionNode), "root" => typeof(RadicalNode),
                "matrix" => typeof(MatrixNode), _ => typeof(ScriptNode)
            };
            Qa.Check(Qa.Nodes(vm.Session.Workspace).Any(n => !originalIds.Contains(n.Id) && n.GetType() == expectedType), kind + " 명령의 실제 구조 종류 삽입");
            Qa.Check(vm.UndoCommand.CanExecute(null), kind + " undo 명령 비활성"); vm.UndoCommand.Execute(null);
            Qa.Equal(before, Qa.Fingerprint(vm.Session.Workspace), kind + " 명령 undo");
            Qa.Check(vm.RedoCommand.CanExecute(null), kind + " redo 명령 비활성"); vm.RedoCommand.Execute(null);
            Qa.Check(vm.Session.IsDirty, kind + " 명령 redo");
        }
    }
}
