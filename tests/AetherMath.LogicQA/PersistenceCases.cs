using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AetherMath.Core;
using AetherMath.Editing;
using AetherMath.Persistence;
using AetherMath.Desktop;
using AetherMath.Layout;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class PersistenceCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("IO-01", "중첩·Unicode·논리 행·정렬·ID·빈 슬롯 flag JSON 왕복", RoundTrip);
        yield return new("IO-02", "기존 파일 원자 교체와 마지막 성공 내용", ReplaceFile);
        yield return new("IO-03", "Windows 독점 잠금 저장실패의 기존 파일·dirty 보존", LockedFile);
        yield return new("IO-04", "부모 경로가 파일인 저장실패의 데이터 보존", InvalidParent);
        yield return new("IO-05", "손상·스키마·ID·슬롯 flag·행렬 오류 파일 거절", InvalidDocuments);
        yield return new("IO-CANCEL", "취소된 저장의 원본·임시파일 안전성", CancelSave);
        yield return new("IO-SNAPSHOT", "편집 중 이전 snapshot 저장은 그 버전만 기록", SaveSnapshot);
    }

    private static EquationWorkspace Sample()
    {
        var fraction = new FractionNode(Guid.NewGuid(), Qa.Text("a<&\"𝑥"),
            new RadicalNode(Guid.NewGuid(), new FractionNode(Guid.NewGuid(), Qa.Text("e\u0301"), Qa.Text("한글🙂"))));
        var script = new ScriptNode(Guid.NewGuid(), Qa.Text("y"), Qa.Text("i"), Qa.Text("2"));
        var matrix = new MatrixNode(Guid.NewGuid(), 2, 2, [Qa.Text("1"), Qa.Text("2"), Qa.Text("3"), Qa.Text("4")]);
        return Qa.Workspace(Qa.Sequence(Qa.Text("x="), fraction, script), Qa.Sequence(Qa.Text("longer="), matrix),
            new FractionNode(Guid.NewGuid(), Qa.Text("required"), Qa.Text("")));
    }

    private static string Listing(TempDirectory directory) => string.Join('\n', Directory.GetFileSystemEntries(directory.Path).Order());

    private static async Task RoundTrip()
    {
        using var directory = new TempDirectory();
        var service = new WorkspaceFileService(); var session = new EditingSession(Sample());
        var path = directory.File("원본.aethermath"); var snapshot = session.CaptureSnapshot();
        await service.SaveAsync(path, snapshot); session.MarkSaved(snapshot);
        Qa.Check(!session.IsDirty, "저장 기준 상태");
        Qa.Check(JsonNode.Parse(await File.ReadAllTextAsync(path, Encoding.UTF8)) is JsonObject, "실제 JSON 객체 파일 아님");
        var reopened = await service.OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(snapshot.Workspace), Qa.Fingerprint(reopened), "원본 왕복 전체 필드");
        Qa.Check(Qa.Nodes(reopened).OfType<TextNode>().Any(n => n.IsEmptySlot && n.Text == ""), "필수 빈 슬롯 flag 손실");
        var incompleteLayout = MathLayoutEngine.Layout(reopened, new AvaloniaMathTextMetrics());
        Qa.Throws<MathExportException>(() => new SvgExportService().ToSvg(incompleteLayout), "정합한 미완성 원본 재열기는 성공하되 출력 거절");
        var newSession = new EditingSession(reopened);
        var leaf = Qa.Nodes(reopened).OfType<TextNode>().Single(n => n.Text == "한글🙂");
        Qa.Select(newSession, leaf.Id, 0, 2); newSession.InsertText("재편집");
        Qa.OnlyTextChanged(reopened, newSession.Workspace, leaf.Id, "재편집🙂");
        Qa.Equal(1, Directory.GetFiles(directory.Path).Length, "저장 잔여 임시파일");
    }

    private static async Task ReplaceFile()
    {
        using var directory = new TempDirectory();
        var service = new WorkspaceFileService(); var session = new EditingSession(Sample());
        var path = directory.File("replace.aethermath");
        await service.SaveAsync(path, session.CaptureSnapshot());
        var oldBytes = await File.ReadAllBytesAsync(path);
        var leaf = Qa.Nodes(session.Workspace).OfType<TextNode>().First();
        Qa.Caret(session, leaf.Id, 0); session.InsertText("수정");
        var snapshot = session.CaptureSnapshot(); await service.SaveAsync(path, snapshot); session.MarkSaved(snapshot);
        var replacedBytes = await File.ReadAllBytesAsync(path);
        Qa.Check(!oldBytes.SequenceEqual(replacedBytes), "기존 파일 내용 미교체");
        Qa.Equal(Qa.Fingerprint(session.Workspace), Qa.Fingerprint(await service.OpenAsync(path)), "마지막 성공 내용");
        Qa.Check(!session.IsDirty, "교체 후 dirty");
        Qa.Equal(1, Directory.GetFiles(directory.Path).Length, "교체 후 잔여 파일");
    }

    private static async Task LockedFile()
    {
        Qa.Check(OperatingSystem.IsWindows(), "Windows FileShare.None 원자 교체 제한을 검증합니다.");
        using var directory = new TempDirectory();
        var service = new WorkspaceFileService(); var session = new EditingSession(Sample());
        var path = directory.File("locked.aethermath"); var saved = session.CaptureSnapshot();
        await service.SaveAsync(path, saved); session.MarkSaved(saved);
        var oldBytes = await File.ReadAllBytesAsync(path); var files = Listing(directory);
        var leaf = Qa.Nodes(session.Workspace).OfType<TextNode>().First();
        Qa.Caret(session, leaf.Id, 0); session.InsertText("unsaved");
        var before = Qa.Fingerprint(session.Workspace); var revision = session.Revision;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Qa.FileFailsAsync(() => service.SaveAsync(path, session.CaptureSnapshot()), "독점 잠금 저장");
        var bytesAfterFailedSave = await File.ReadAllBytesAsync(path);
        Qa.Check(oldBytes.SequenceEqual(bytesAfterFailedSave), "실패가 기존 바이트 변경");
        Qa.Equal(files, Listing(directory), "실패 후 임시파일");
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "실패 후 메모리");
        Qa.Equal(revision, session.Revision, "실패 후 revision");
        Qa.Check(session.IsDirty, "실패가 dirty 해제");
        session.Undo(); Qa.Check(!session.IsDirty, "실패 후 저장 기준 보존");
    }

    private static async Task InvalidParent()
    {
        using var directory = new TempDirectory();
        var service = new WorkspaceFileService(); var session = new EditingSession(Sample());
        var parent = directory.File("not-a-directory"); await File.WriteAllTextAsync(parent, "keep");
        var leaf = Qa.Nodes(session.Workspace).OfType<TextNode>().First();
        Qa.Caret(session, leaf.Id, 0); session.InsertText("변경");
        var before = Qa.Fingerprint(session.Workspace);
        await Qa.FileFailsAsync(() => service.SaveAsync(System.IO.Path.Combine(parent, "file.aethermath"), session.CaptureSnapshot()), "파일인 부모");
        Qa.Equal("keep", await File.ReadAllTextAsync(parent), "부모 파일 보존");
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "실패 뒤 모델");
        Qa.Check(session.IsDirty, "실패 뒤 dirty");
        Qa.Equal(1, Directory.GetFiles(directory.Path).Length, "실패 뒤 부분 파일");
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var property in obj)
                foreach (var child in Objects(property.Value)) yield return child;
        }
        else if (node is JsonArray array)
            foreach (var item in array)
                foreach (var child in Objects(item)) yield return child;
    }
    private static string Key(JsonObject obj, string name) => obj.Select(p => p.Key).Single(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static bool Has(JsonObject obj, string name) => obj.Any(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static JsonObject Find(JsonNode json, string property) => Objects(json).First(o => Has(o, property));

    private static async Task InvalidDocuments()
    {
        using var directory = new TempDirectory(); var service = new WorkspaceFileService();
        var validPath = directory.File("valid.aethermath");
        await service.SaveAsync(validPath, new EditingSession(Sample()).CaptureSnapshot());
        var valid = await File.ReadAllTextAsync(validPath);
        var mutations = new (string Label, Action<JsonNode> Mutate)[]
        {
            ("unsupported-schema", root => { var o = Find(root, "SchemaVersion"); o[Key(o, "SchemaVersion")] = 999; }),
            ("missing-groups", root => { var o = Find(root, "Groups"); o.Remove(Key(o, "Groups")); }),
            ("duplicate-node-id", root =>
            {
                var texts = Objects(root).Where(o => Has(o, "Text")).Take(2).ToArray();
                Qa.Equal(2, texts.Length, "저장 JSON text leaf 수");
                texts[1][Key(texts[1], "Id")] = texts[0][Key(texts[0], "Id")]!.DeepClone();
            }),
            ("bad-matrix-count", root => { var o = Objects(root).Single(x => Has(x, "Cells") && Has(x, "Columns")); o[Key(o, "Columns")] = 3; }),
            ("null-fraction-slot", root => { var o = Find(root, "Denominator"); o[Key(o, "Denominator")] = null; }),
            ("null-text", root => { var o = Find(root, "Text"); o[Key(o, "Text")] = null; }),
            ("invalid-font-size", root => { var o = Find(root, "FontSize"); o[Key(o, "FontSize")] = -5; }),
            ("required-empty-slot-unflagged", root =>
            {
                var empty = Objects(root).Single(o => Has(o, "Text") && o[Key(o, "Text")]!.GetValue<string>() == "");
                empty[Key(empty, "IsEmptySlot")] = false;
            })
        };
        foreach (var mutation in mutations)
        {
            var json = JsonNode.Parse(valid)!; mutation.Mutate(json);
            await Reject(mutation.Label, json.ToJsonString());
        }
        await Reject("broken-json", "{not valid"); await Reject("null-root", "null");

        async Task Reject(string label, string contents)
        {
            var path = directory.File(label + ".aethermath"); await File.WriteAllTextAsync(path, contents, new UTF8Encoding(false));
            var bytes = await File.ReadAllBytesAsync(path);
            await Qa.FailsAsync(async () => { await service.OpenAsync(path); }, label + " 열기", typeof(JsonException), typeof(InvalidDataException));
            var bytesAfterRejectedOpen = await File.ReadAllBytesAsync(path);
            Qa.Check(bytes.SequenceEqual(bytesAfterRejectedOpen), label + " 원본 바이트 변경");
        }
    }

    private static async Task CancelSave()
    {
        using var directory = new TempDirectory(); var service = new WorkspaceFileService();
        var session = new EditingSession(Sample()); var path = directory.File("cancel.aethermath");
        await service.SaveAsync(path, session.CaptureSnapshot()); var bytes = await File.ReadAllBytesAsync(path); var listing = Listing(directory);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Qa.FailsAsync(() => service.SaveAsync(path, session.CaptureSnapshot(), cancelled.Token), "취소된 저장", typeof(OperationCanceledException));
        var bytesAfterCancelledSave = await File.ReadAllBytesAsync(path);
        Qa.Check(bytes.SequenceEqual(bytesAfterCancelledSave), "취소가 기존 파일 변경");
        Qa.Equal(listing, Listing(directory), "취소 잔여 파일");
    }

    private static async Task SaveSnapshot()
    {
        using var directory = new TempDirectory(); var service = new WorkspaceFileService();
        var session = new EditingSession(Sample()); var old = session.CaptureSnapshot();
        var leaf = Qa.Nodes(session.Workspace).OfType<TextNode>().First();
        Qa.Caret(session, leaf.Id, 0); session.InsertText("나중 편집");
        var path = directory.File("snapshot.aethermath"); await service.SaveAsync(path, old); session.MarkSaved(old);
        Qa.Equal(Qa.Fingerprint(old.Workspace), Qa.Fingerprint(await service.OpenAsync(path)), "snapshot 원본 고정");
        Qa.Check(session.IsDirty, "저장 중 새 편집이 저장 완료 처리됨");
    }
}
