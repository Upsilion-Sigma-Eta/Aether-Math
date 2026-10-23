using System.Text;
using System.Text.Json.Nodes;
using AetherMath.Core;
using AetherMath.Editing;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class WorkspaceStateCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("STATE-RECENT-INDEX", "최근 경로 정규화·대소문자 중복 통합·표시명과 UTC 최근 사용 갱신", RecentIndex);
        yield return new("STATE-RECENT-BOUNDS", "고정 항목 보존·정렬·고정되지 않은 항목 20개 제한", RecentBounds);
        yield return new("STATE-RECENT-METADATA", "최근 사용 시각과 실제 파일 수정 시각 및 누락 상태 분리", RecentMetadata);
        yield return new("STATE-RECENT-MUTATION", "고정·고정 해제·삭제의 무변경 및 없는 경로 no-op", RecentMutation);
        yield return new("STATE-INDEX-PROTECTION", "손상·미래 버전 인덱스 진단, 쓰기 차단, 원본 보존", IndexProtection);
        yield return new("STATE-RECOVERY-ROUNDTRIP", "중첩 Unicode snapshot 복구 저장·목록·열기·삭제 왕복", RecoveryRoundTrip);
        yield return new("STATE-RECOVERY-PROTECTION", "복구 ID·버전·AST 손상 진단, 파일 보존 및 비정상 입력 거부", RecoveryProtection);
        yield return new("STATE-CONCURRENCY", "서비스 인스턴스 간 RMW 직렬화·루트 격리·잠금 및 취소 보존", ConcurrencyAndCancellation);
    }

    private static WorkspaceSnapshot Snapshot()
    {
        var nested = new FractionNode(Guid.NewGuid(), Qa.Text("a<&\"𝑥"),
            new RadicalNode(Guid.NewGuid(), new FractionNode(Guid.NewGuid(), Qa.Text("e\u0301"), Qa.Text("한글🙂"))));
        var matrix = new MatrixNode(Guid.NewGuid(), 2, 2,
            [Qa.Text("α"), Qa.Text("2"), Qa.Text("🙂"), Qa.Text("z")]);
        var workspace = Qa.Workspace(Qa.Sequence(Qa.Text("x="), nested), Qa.Sequence(Qa.Text("M="), matrix),
            new FractionNode(Guid.NewGuid(), Qa.Text("required"), Qa.Text("")));
        return new EditingSession(workspace).CaptureSnapshot();
    }

    private static string IndexPath(WorkspaceStateService service) =>
        Path.Combine(service.RootDirectory, WorkspaceStateService.RecentIndexFileName);

    private static string RecoveryPath(WorkspaceStateService service, Guid id) =>
        Path.Combine(service.RootDirectory, WorkspaceStateService.RecoveryDirectoryName, $"{id:D}.json");

    private static async Task<RecentWorkspaceIndexResult> Read(WorkspaceStateService service) =>
        await service.ReadRecentWorkspacesAsync();

    private static async Task RecentIndex()
    {
        using var directory = new TempDirectory();
        var root = Directory.CreateDirectory(directory.File("state")).FullName;
        var service = new WorkspaceStateService(root);
        var document = directory.File("sample.aethermath");
        await File.WriteAllTextAsync(document, "workspace marker");
        var nonCanonical = Path.Combine(Path.GetDirectoryName(document)!, ".", Path.GetFileName(document));
        var firstUsed = new DateTimeOffset(2042, 4, 5, 18, 20, 0, TimeSpan.FromHours(9));
        await service.RecordSuccessfulUseAsync(nonCanonical, "처음 이름", firstUsed);
        var duplicatePath = Path.GetFullPath(document).ToUpperInvariant();
        await service.RecordSuccessfulUseAsync(duplicatePath, "갱신 이름", firstUsed.AddMinutes(3));

        var index = await Read(service);
        Qa.Check(index.CanWrite && index.Diagnostic is null, "정상 인덱스가 쓰기 가능 상태가 아님");
        Qa.Equal(1, index.Entries.Length, "대소문자 무시 중복 통합");
        var entry = index.Entries.Single();
        Qa.Check(string.Equals(Path.GetFullPath(document), Path.GetFullPath(entry.FullPath), StringComparison.OrdinalIgnoreCase),
            "대소문자 무시 경로 중복 통합");
        Qa.Equal(Path.GetFullPath(entry.FullPath), entry.FullPath, "저장 경로의 전체 경로 정규화");
        Qa.Equal("갱신 이름", entry.DisplayName, "중복 경로의 표시명 갱신");
        Qa.Equal(firstUsed.AddMinutes(3).ToUniversalTime(), entry.LastUsedAtUtc, "최근 사용 UTC 정규화");
        Qa.Equal(TimeSpan.Zero, entry.LastUsedAtUtc.Offset, "최근 사용 offset");
        Qa.Equal(Path.GetFullPath(root), service.RootDirectory, "상태 root 정규화");
    }

    private static async Task RecentBounds()
    {
        using var directory = new TempDirectory();
        var root = Directory.CreateDirectory(directory.File("state")).FullName;
        var service = new WorkspaceStateService(root);
        var firstUse = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var pinnedPaths = new string[2];
        for (var i = 0; i < 2; i++)
        {
            pinnedPaths[i] = directory.File($"pinned-{i}.aethermath");
            await service.RecordSuccessfulUseAsync(pinnedPaths[i], $"고정 {i}", firstUse.AddMinutes(i));
            Qa.Equal(RecentWorkspaceMutationStatus.Applied,
                (await service.SetPinnedAsync(pinnedPaths[i], true)).Status, "고정 항목 준비");
        }

        var unpinnedPaths = new List<string>();
        for (var i = 0; i < 23; i++)
        {
            var path = directory.File($"unpinned-{i:D2}.aethermath");
            unpinnedPaths.Add(path);
            await service.RecordSuccessfulUseAsync(path, $"일반 {i}", firstUse.AddHours(1).AddMinutes(i));
        }

        var index = await Read(service);
        Qa.Equal(22, index.Entries.Length, "고정 2개 + 일반 상한 20개");
        Qa.Equal(2, index.Entries.Count(item => item.IsPinned), "오래된 고정 항목 보존");
        Qa.Equal(20, index.Entries.Count(item => !item.IsPinned), "고정되지 않은 항목 상한");
        Qa.Check(index.Entries.Take(2).All(item => item.IsPinned), "고정 항목 우선 정렬");
        Qa.Equal(Path.GetFullPath(pinnedPaths[1]), Path.GetFullPath(index.Entries[0].FullPath), "고정 항목 최근순 1");
        Qa.Equal(Path.GetFullPath(pinnedPaths[0]), Path.GetFullPath(index.Entries[1].FullPath), "고정 항목 최근순 2");
        foreach (var path in unpinnedPaths.Take(3))
            Qa.Check(index.Entries.All(item => !Path.GetFullPath(item.FullPath).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)),
                "가장 오래된 일반 항목이 상한 밖에 남음: " + path);
        foreach (var path in unpinnedPaths.Skip(3))
            Qa.Check(index.Entries.Any(item => Path.GetFullPath(item.FullPath).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)),
                "최근 일반 항목이 상한 적용 중 소실: " + path);

        var duplicateTimeA = directory.File("tie-a.aethermath");
        var duplicateTimeB = directory.File("tie-b.aethermath");
        var tied = firstUse.AddDays(3);
        await service.RecordSuccessfulUseAsync(duplicateTimeA, "동률 A", tied);
        await service.RecordSuccessfulUseAsync(duplicateTimeB, "동률 B", tied);
        var firstRead = await Read(service);
        var secondRead = await Read(service);
        Qa.Equal(string.Join('|', firstRead.Entries.Select(e => e.FullPath)),
            string.Join('|', secondRead.Entries.Select(e => e.FullPath)), "동률 정렬의 결정성");
        Qa.Check(firstRead.Entries.Take(2).All(item => item.IsPinned), "일반 항목 추가 후 고정 우선 유지");
    }

    private static async Task RecentMetadata()
    {
        using var directory = new TempDirectory();
        var root = Directory.CreateDirectory(directory.File("state")).FullName;
        var service = new WorkspaceStateService(root);
        var available = directory.File("available.aethermath");
        await File.WriteAllTextAsync(available, "metadata fixture");
        var requestedModified = new DateTime(2038, 8, 9, 10, 11, 12, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(available, requestedModified);
        var actualModifiedUtc = File.GetLastWriteTimeUtc(available);
        var usedAt = new DateTimeOffset(2041, 2, 3, 4, 5, 6, TimeSpan.FromHours(-7));
        var missing = directory.File("removed-after-save.aethermath");
        await service.RecordSuccessfulUseAsync(available, "존재 파일", usedAt);
        await service.RecordSuccessfulUseAsync(missing, "누락 파일", usedAt.AddMinutes(1));

        var index = await Read(service);
        var present = index.Entries.Single(item => item.DisplayName == "존재 파일");
        var absent = index.Entries.Single(item => item.DisplayName == "누락 파일");
        Qa.Equal(usedAt.ToUniversalTime(), present.LastUsedAtUtc, "최근 사용 시각은 별도 보존");
        Qa.Check(present.FileModifiedAtUtc.HasValue, "존재 파일 수정 시각 누락");
        Qa.Equal(new DateTimeOffset(actualModifiedUtc), present.FileModifiedAtUtc!.Value, "파일 메타데이터 수정 시각");
        Qa.Check(present.FileModifiedAtUtc != present.LastUsedAtUtc, "파일 수정 시각과 최근 사용 시각이 혼동됨");
        Qa.Equal(RecentWorkspaceFileStatus.Available, present.FileStatus, "존재 파일 상태");
        Qa.Check(!present.IsMissing && present.FileDiagnostic is null, "존재 파일 상태 flag/진단");
        Qa.Equal(RecentWorkspaceFileStatus.Missing, absent.FileStatus, "누락 파일 상태");
        Qa.Check(absent.IsMissing && absent.FileModifiedAtUtc is null, "누락 파일 수정 시각/flag");
        Qa.Check(absent.FileDiagnostic is null, "정상적인 누락 상태에 잘못된 metadata 오류 진단");

        var directoryPath = Directory.CreateDirectory(directory.File("workspace-path-is-a-directory")).FullName;
        await service.RecordSuccessfulUseAsync(directoryPath, "잘못된 대상 형식", usedAt.AddMinutes(2));
        var unavailable = (await Read(service)).Entries.Single(item => item.DisplayName == "잘못된 대상 형식");
        Qa.Equal(RecentWorkspaceFileStatus.MetadataUnavailable, unavailable.FileStatus, "파일이 아닌 경로의 metadata 상태");
        Qa.Check(!unavailable.IsMissing && unavailable.FileModifiedAtUtc is null, "metadata 오류를 missing으로 혼동함");
        Qa.Equal(WorkspaceStateDiagnosticCodes.UnsafePath, unavailable.FileDiagnostic?.Code, "metadata 상태 진단 코드");
    }

    private static async Task RecentMutation()
    {
        using var directory = new TempDirectory();
        var root = Directory.CreateDirectory(directory.File("state")).FullName;
        var service = new WorkspaceStateService(root);
        var target = directory.File("target.aethermath");
        await File.WriteAllTextAsync(target, "recent work");
        var absent = directory.File("never-recorded.aethermath");
        var indexPath = IndexPath(service);

        Qa.Equal(RecentWorkspaceMutationStatus.NotFound, (await service.SetPinnedAsync(absent, true)).Status,
            "없는 경로 고정은 NotFound");
        Qa.Equal(RecentWorkspaceMutationStatus.NotFound, (await service.RemoveRecentAsync(absent)).Status,
            "없는 경로 삭제는 NotFound");
        Qa.Check(!File.Exists(indexPath), "없는 경로 조작이 phantom index를 생성함");

        await service.RecordSuccessfulUseAsync(target, "수식 파일", DateTimeOffset.UtcNow);
        Qa.Equal(RecentWorkspaceMutationStatus.Applied, (await service.SetPinnedAsync(target, true)).Status, "기존 경로 고정");
        Qa.Equal(RecentWorkspaceMutationStatus.Unchanged, (await service.SetPinnedAsync(target, true)).Status, "같은 고정 상태");
        Qa.Equal(RecentWorkspaceMutationStatus.Applied, (await service.SetPinnedAsync(target, false)).Status, "기존 경로 고정 해제");
        Qa.Equal(RecentWorkspaceMutationStatus.Applied, (await service.RemoveRecentAsync(target)).Status, "기존 경로 삭제");
        Qa.Equal(RecentWorkspaceMutationStatus.NotFound, (await service.RemoveRecentAsync(target)).Status, "삭제 재요청");
        Qa.Equal(0, (await Read(service)).Entries.Length, "삭제 뒤 최근 항목");
    }

    private static async Task IndexProtection()
    {
        using var directory = new TempDirectory();
        var malformedRoot = Directory.CreateDirectory(directory.File("malformed-state")).FullName;
        var malformedService = new WorkspaceStateService(malformedRoot);
        var recordedPath = directory.File("kept.aethermath");
        await malformedService.RecordSuccessfulUseAsync(recordedPath, "보존 대상", DateTimeOffset.UtcNow);
        var malformedIndex = IndexPath(malformedService);
        var malformedJson = JsonNode.Parse(await File.ReadAllTextAsync(malformedIndex))!;
        Qa.Check(TrySetProperty(malformedJson, "displayName", JsonValue.Create("")), "인덱스 표시명 필드를 찾지 못함");
        await File.WriteAllTextAsync(malformedIndex, malformedJson.ToJsonString(), new UTF8Encoding(false));
        var malformedBytes = await File.ReadAllBytesAsync(malformedIndex);
        var malformedRead = await Read(malformedService);
        Qa.Check(!malformedRead.CanWrite, "잘못된 인덱스가 쓰기 가능으로 반환됨");
        Qa.Equal(WorkspaceStateDiagnosticCodes.RecentIndexInvalid, malformedRead.Diagnostic?.Code, "잘못된 필드 진단 코드");
        Qa.Equal(RecentWorkspaceMutationStatus.WriteBlocked,
            (await malformedService.SetPinnedAsync(recordedPath, true)).Status, "손상 인덱스 pin 쓰기 차단");
        Qa.Equal(RecentWorkspaceMutationStatus.WriteBlocked,
            (await malformedService.RemoveRecentAsync(recordedPath)).Status, "손상 인덱스 삭제 쓰기 차단");
        Qa.Check(await BytesMatchAsync(malformedBytes, malformedIndex), "손상 인덱스 원본 bytes 변경");

        var futureRoot = Directory.CreateDirectory(directory.File("future-state")).FullName;
        var futureService = new WorkspaceStateService(futureRoot);
        await futureService.RecordSuccessfulUseAsync(recordedPath, "미래 버전", DateTimeOffset.UtcNow);
        var futureIndex = IndexPath(futureService);
        var futureJson = JsonNode.Parse(await File.ReadAllTextAsync(futureIndex))!;
        Qa.Check(TrySetVersion(futureJson, 999), "recent index version 필드를 찾지 못함");
        await File.WriteAllTextAsync(futureIndex, futureJson.ToJsonString(), new UTF8Encoding(false));
        var futureBytes = await File.ReadAllBytesAsync(futureIndex);
        var futureRead = await Read(futureService);
        Qa.Check(!futureRead.CanWrite, "미래 버전 인덱스가 쓰기 가능으로 반환됨");
        Qa.Equal(WorkspaceStateDiagnosticCodes.RecentIndexUnsupportedVersion, futureRead.Diagnostic?.Code, "미래 인덱스 진단 코드");
        Qa.Equal(RecentWorkspaceMutationStatus.WriteBlocked,
            (await futureService.RecordSuccessfulUseAsync(directory.File("new.aethermath"), "금지", DateTimeOffset.UtcNow)).Status,
            "미래 인덱스 변경 쓰기 차단");
        Qa.Check(await BytesMatchAsync(futureBytes, futureIndex), "미래 인덱스 원본 bytes 변경");
    }

    private static async Task RecoveryRoundTrip()
    {
        using var directory = new TempDirectory();
        var root = Directory.CreateDirectory(directory.File("state")).FullName;
        var service = new WorkspaceStateService(root);
        var snapshot = Snapshot();
        var id = Guid.NewGuid();
        var captured = new DateTimeOffset(2044, 6, 7, 8, 9, 10, TimeSpan.FromHours(9));
        var originalPath = Path.GetRelativePath(Environment.CurrentDirectory, directory.File("work\u00f1.aethermath"));
        var expectedOriginalPath = Path.GetFullPath(originalPath);
        var recovery = new WorkspaceRecovery(id, snapshot, captured, originalPath);

        var saved = await service.SaveRecoveryAsync(recovery);
        Qa.Equal(RecoveryWriteStatus.Written, saved.Status, "복구본 저장");
        var canonicalPath = RecoveryPath(service, id);
        Qa.Check(File.Exists(canonicalPath), "정규 GUID 경로에 복구본이 없음");
        var listed = await service.ListRecoveriesAsync();
        var summary = listed.Recoveries.Single(item => item.RecoveryId == id);
        Qa.Equal(snapshot.Workspace.Id, summary.WorkspaceId, "목록 작업 ID");
        Qa.Equal(snapshot.Workspace.Name, summary.WorkspaceName, "목록 작업 이름");
        Qa.Equal(captured.ToUniversalTime(), summary.CapturedAtUtc, "목록 capture 시각 UTC 정규화");
        Qa.Equal(snapshot.Revision, summary.Revision, "목록 revision");
        Qa.Equal(expectedOriginalPath, summary.OriginalPath, "목록 원본 경로 정규화");

        var read = await service.ReadRecoveryAsync(id);
        Qa.Equal(RecoveryReadStatus.Found, read.Status, "복구본 읽기");
        Qa.Check(read.Diagnostic is null && read.Recovery is not null, "정상 복구본 진단/값");
        Qa.Equal(id, read.Recovery!.RecoveryId, "복구 ID");
        Qa.Equal(snapshot.Revision, read.Recovery.Snapshot.Revision, "복구 revision");
        Qa.Equal(snapshot.StateId, read.Recovery.Snapshot.StateId, "복구 state ID");
        Qa.Equal(Qa.Fingerprint(snapshot.Workspace), Qa.Fingerprint(read.Recovery.Snapshot.Workspace), "중첩 Unicode workspace snapshot");
        Qa.Equal(captured.ToUniversalTime(), read.Recovery.CapturedAtUtc, "읽은 capture 시각 UTC 정규화");
        Qa.Equal(expectedOriginalPath, read.Recovery.OriginalPath, "읽은 원본 경로 정규화");
        Qa.Check(Qa.Nodes(read.Recovery.Snapshot.Workspace).OfType<TextNode>().Any(text => text.Text == "한글🙂"), "Unicode leaf 손실");

        var lockedId = Guid.NewGuid();
        await service.SaveRecoveryAsync(new WorkspaceRecovery(lockedId, snapshot, captured.AddMinutes(-1), null));
        var lockedPath = RecoveryPath(service, lockedId);
        await File.WriteAllTextAsync(Path.Combine(root, WorkspaceStateService.RecoveryDirectoryName, "notes.json"), "not a recovery");
        RecoveryListResult listedWithLockedAndUnrelated;
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            listedWithLockedAndUnrelated = await service.ListRecoveriesAsync();
            Qa.Equal(1, listedWithLockedAndUnrelated.Recoveries.Length, "잠긴 개별 파일 뒤의 정상 recovery 계속 반환");
            Qa.Equal(id, listedWithLockedAndUnrelated.Recoveries[0].RecoveryId, "정상 recovery 목록 보존");
            Qa.Check(listedWithLockedAndUnrelated.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WorkspaceStateDiagnosticCodes.RecoveryInvalid &&
                    string.Equals(diagnostic.Path, lockedPath, StringComparison.OrdinalIgnoreCase)),
                "잠긴 개별 recovery 파일 진단 누락");
        }
        Qa.Check(!listedWithLockedAndUnrelated.Recoveries.Any(item => item.RecoveryId == lockedId),
            "잠긴 recovery가 정상 항목처럼 반환됨");
        Qa.Check(!await service.DeleteRecoveryAsync(Guid.NewGuid()), "없는 recovery 삭제 결과");
        Qa.Check(await service.DeleteRecoveryAsync(id), "기존 recovery 삭제 결과");
        Qa.Check(!File.Exists(canonicalPath), "삭제한 canonical recovery 파일이 남음");
        Qa.Check(File.Exists(Path.Combine(root, WorkspaceStateService.RecoveryDirectoryName, "notes.json")),
            "명시 ID 삭제가 임의 파일까지 삭제함");
        Qa.Equal(RecoveryReadStatus.NotFound, (await service.ReadRecoveryAsync(id)).Status, "삭제 후 읽기");
    }

    private static async Task RecoveryProtection()
    {
        using var directory = new TempDirectory();
        var root = Directory.CreateDirectory(directory.File("state")).FullName;
        var service = new WorkspaceStateService(root);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Qa.FailsAsync(() => service.ReadRecoveryAsync(Guid.NewGuid(), canceled.Token), "선취소 recovery read", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => service.ListRecoveriesAsync(canceled.Token), "선취소 recovery list", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => service.SaveRecoveryAsync(new WorkspaceRecovery(Guid.NewGuid(), Snapshot(), DateTimeOffset.UtcNow, null), canceled.Token),
            "선취소 recovery write", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => service.DeleteRecoveryAsync(Guid.NewGuid(), canceled.Token), "선취소 recovery delete", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => service.ReadRecoveryAsync(Guid.Empty), "빈 recovery ID read", typeof(ArgumentException));
        await Qa.FailsAsync(() => service.DeleteRecoveryAsync(Guid.Empty), "빈 recovery ID delete", typeof(ArgumentException));
        await Qa.FailsAsync(() => service.SaveRecoveryAsync(new WorkspaceRecovery(Guid.Empty, Snapshot(), DateTimeOffset.UtcNow, null)),
            "빈 recovery ID write", typeof(ArgumentException));

        var validSnapshot = Snapshot();
        var invalidWorkspace = validSnapshot.Workspace with
        {
            Style = validSnapshot.Workspace.Style with { FontSize = -3 }
        };
        var invalidInputId = Guid.NewGuid();
        var invalidInputSnapshot = validSnapshot with { Workspace = invalidWorkspace };
        await Qa.FailsAsync(() => service.SaveRecoveryAsync(new WorkspaceRecovery(
                invalidInputId, invalidInputSnapshot, DateTimeOffset.UtcNow, null)),
            "신규 invalid AST 저장 거부", typeof(InvalidDataException));
        Qa.Check(!File.Exists(RecoveryPath(service, invalidInputId)), "invalid AST 입력이 recovery 파일을 생성함");

        var mismatchId = Guid.NewGuid();
        await service.SaveRecoveryAsync(new WorkspaceRecovery(mismatchId, validSnapshot, DateTimeOffset.UtcNow, null));
        var mismatchPath = RecoveryPath(service, mismatchId);
        var mismatchJson = JsonNode.Parse(await File.ReadAllTextAsync(mismatchPath))!;
        Qa.Check(TrySetProperty(mismatchJson, "recoveryId", JsonValue.Create(Guid.NewGuid())), "recovery ID 필드를 찾지 못함");
        await File.WriteAllTextAsync(mismatchPath, mismatchJson.ToJsonString(), new UTF8Encoding(false));
        var mismatchBytes = await File.ReadAllBytesAsync(mismatchPath);
        var mismatchRead = await service.ReadRecoveryAsync(mismatchId);
        Qa.Equal(RecoveryReadStatus.Corrupt, mismatchRead.Status, "canonical filename과 envelope ID 불일치");
        Qa.Equal(WorkspaceStateDiagnosticCodes.RecoveryIdentityMismatch, mismatchRead.Diagnostic?.Code, "ID 불일치 진단");
        Qa.Equal(RecoveryWriteStatus.WriteBlocked,
            (await service.SaveRecoveryAsync(new WorkspaceRecovery(mismatchId, validSnapshot, DateTimeOffset.UtcNow, null))).Status,
            "ID 불일치 복구본 덮어쓰기 거부");
        Qa.Check(await BytesMatchAsync(mismatchBytes, mismatchPath), "ID 불일치 복구본 원본 보존");

        var unsupportedId = Guid.NewGuid();
        var unsupportedPath = RecoveryPath(service, unsupportedId);
        await service.SaveRecoveryAsync(new WorkspaceRecovery(unsupportedId, validSnapshot, DateTimeOffset.UtcNow, null));
        var unsupportedJson = JsonNode.Parse(await File.ReadAllTextAsync(unsupportedPath))!;
        Qa.Check(TrySetVersion(unsupportedJson, 999), "recovery version 필드를 찾지 못함");
        await File.WriteAllTextAsync(unsupportedPath, unsupportedJson.ToJsonString(), new UTF8Encoding(false));
        var unsupportedBytes = await File.ReadAllBytesAsync(unsupportedPath);
        var unsupportedRead = await service.ReadRecoveryAsync(unsupportedId);
        Qa.Equal(RecoveryReadStatus.Unsupported, unsupportedRead.Status, "미래 recovery version 상태");
        Qa.Equal(WorkspaceStateDiagnosticCodes.RecoveryUnsupportedVersion, unsupportedRead.Diagnostic?.Code, "미래 recovery 진단");
        Qa.Equal(RecoveryWriteStatus.WriteBlocked,
            (await service.SaveRecoveryAsync(new WorkspaceRecovery(unsupportedId, validSnapshot, DateTimeOffset.UtcNow, null))).Status,
            "미래 recovery 덮어쓰기 거부");
        Qa.Check(await BytesMatchAsync(unsupportedBytes, unsupportedPath), "미래 recovery 원본 보존");

        var invalidAstId = Guid.NewGuid();
        var invalidAstPath = RecoveryPath(service, invalidAstId);
        await service.SaveRecoveryAsync(new WorkspaceRecovery(invalidAstId, validSnapshot, DateTimeOffset.UtcNow, null));
        var invalidAstJson = JsonNode.Parse(await File.ReadAllTextAsync(invalidAstPath))!;
        Qa.Check(TrySetProperty(invalidAstJson, "fontSize", JsonValue.Create(-3)), "recovery snapshot fontSize 필드를 찾지 못함");
        await File.WriteAllTextAsync(invalidAstPath, invalidAstJson.ToJsonString(), new UTF8Encoding(false));
        var invalidAstBytes = await File.ReadAllBytesAsync(invalidAstPath);
        var invalidAstRead = await service.ReadRecoveryAsync(invalidAstId);
        Qa.Equal(RecoveryReadStatus.Corrupt, invalidAstRead.Status, "잘못된 workspace snapshot 상태");
        Qa.Equal(WorkspaceStateDiagnosticCodes.RecoveryInvalid, invalidAstRead.Diagnostic?.Code, "잘못된 workspace snapshot 진단");
        Qa.Equal(RecoveryWriteStatus.WriteBlocked,
            (await service.SaveRecoveryAsync(new WorkspaceRecovery(invalidAstId, validSnapshot, DateTimeOffset.UtcNow, null))).Status,
            "잘못된 AST 복구본 덮어쓰기 거부");
        Qa.Check(await BytesMatchAsync(invalidAstBytes, invalidAstPath), "잘못된 AST 원본 보존");

        var corruptId = Guid.NewGuid();
        var corruptPath = RecoveryPath(service, corruptId);
        await service.SaveRecoveryAsync(new WorkspaceRecovery(corruptId, validSnapshot, DateTimeOffset.UtcNow, null));
        await File.WriteAllTextAsync(corruptPath, "{ broken", new UTF8Encoding(false));
        var corruptBytes = await File.ReadAllBytesAsync(corruptPath);
        var corruptRead = await service.ReadRecoveryAsync(corruptId);
        Qa.Equal(RecoveryReadStatus.Corrupt, corruptRead.Status, "손상 JSON recovery 상태");
        Qa.Equal(WorkspaceStateDiagnosticCodes.RecoveryInvalid, corruptRead.Diagnostic?.Code, "손상 JSON recovery 진단");
        Qa.Equal(RecoveryWriteStatus.WriteBlocked,
            (await service.SaveRecoveryAsync(new WorkspaceRecovery(corruptId, validSnapshot, DateTimeOffset.UtcNow, null))).Status,
            "손상 recovery 덮어쓰기 거부");
        Qa.Check(await BytesMatchAsync(corruptBytes, corruptPath), "손상 recovery 원본 보존");

        var relativePathId = Guid.NewGuid();
        var relativePath = RecoveryPath(service, relativePathId);
        var fullyQualifiedInput = new WorkspaceRecovery(relativePathId, validSnapshot, DateTimeOffset.UtcNow,
            directory.File("qualified-original.aethermath"));
        await service.SaveRecoveryAsync(fullyQualifiedInput);
        var relativePathJson = JsonNode.Parse(await File.ReadAllTextAsync(relativePath))!;
        Qa.Check(TrySetProperty(relativePathJson, "originalPath", JsonValue.Create("relative-original.aethermath")),
            "recovery originalPath 필드를 찾지 못함");
        await File.WriteAllTextAsync(relativePath, relativePathJson.ToJsonString(), new UTF8Encoding(false));
        var relativePathBytes = await File.ReadAllBytesAsync(relativePath);
        var relativePathRead = await service.ReadRecoveryAsync(relativePathId);
        Qa.Equal(RecoveryReadStatus.Corrupt, relativePathRead.Status, "저장된 상대 originalPath 상태");
        Qa.Equal(WorkspaceStateDiagnosticCodes.RecoveryInvalid, relativePathRead.Diagnostic?.Code, "저장된 상대 originalPath 진단");
        Qa.Equal(RecoveryWriteStatus.WriteBlocked,
            (await service.SaveRecoveryAsync(fullyQualifiedInput)).Status, "잘못된 originalPath recovery 덮어쓰기 차단");
        Qa.Check(await BytesMatchAsync(relativePathBytes, relativePath), "잘못된 originalPath recovery 바이트 보존");

        var unsafeId = Guid.NewGuid();
        var unsafeCanonicalPath = RecoveryPath(service, unsafeId);
        Directory.CreateDirectory(unsafeCanonicalPath);
        var unsafeMarker = Path.Combine(unsafeCanonicalPath, "keep.txt");
        await File.WriteAllTextAsync(unsafeMarker, "preserve non-file target");
        var unsafeRead = await service.ReadRecoveryAsync(unsafeId);
        Qa.Equal(RecoveryReadStatus.Corrupt, unsafeRead.Status, "파일 대신 디렉터리인 canonical 경로");
        Qa.Equal(WorkspaceStateDiagnosticCodes.UnsafePath, unsafeRead.Diagnostic?.Code, "위험한 recovery 경로 진단");
        var unsafeSave = await service.SaveRecoveryAsync(new WorkspaceRecovery(unsafeId, validSnapshot, DateTimeOffset.UtcNow, null));
        Qa.Equal(RecoveryWriteStatus.WriteBlocked, unsafeSave.Status, "canonical recovery 디렉터리 덮어쓰기 거부");
        await Qa.FileFailsAsync(() => service.DeleteRecoveryAsync(unsafeId), "canonical recovery 디렉터리 삭제 거부");
        Qa.Equal("preserve non-file target", await File.ReadAllTextAsync(unsafeMarker), "위험 경로 안의 기존 내용 보존");
    }

    private static async Task ConcurrencyAndCancellation()
    {
        using var directory = new TempDirectory();
        var root = Directory.CreateDirectory(directory.File("shared-state")).FullName;
        var otherRoot = Directory.CreateDirectory(directory.File("isolated-state")).FullName;
        var services = Enumerable.Range(0, 4).Select(_ => new WorkspaceStateService(root)).ToArray();
        var other = new WorkspaceStateService(otherRoot);
        var timestamp = new DateTimeOffset(2048, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var paths = Enumerable.Range(0, 16).Select(i => directory.File($"concurrent-{i:D2}.aethermath")).ToArray();
        await Task.WhenAll(paths.Select((path, i) => services[i % services.Length]
            .RecordSuccessfulUseAsync(path, $"항목 {i}", timestamp.AddMinutes(i))));
        var shared = await Read(services[0]);
        Qa.Equal(paths.Length, shared.Entries.Length, "서비스 인스턴스 동시 RMW 항목 수");
        foreach (var path in paths)
            Qa.Check(shared.Entries.Any(entry => Path.GetFullPath(entry.FullPath).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)),
                "동시 RMW 항목 소실: " + path);

        var otherOnlyPath = directory.File("other-root-only.aethermath");
        await other.RecordSuccessfulUseAsync(otherOnlyPath, "분리 루트", timestamp);
        Qa.Equal(1, (await Read(other)).Entries.Length, "별도 root에 기록한 recent 항목 수");
        Qa.Equal(paths.Length, (await Read(services[1])).Entries.Length, "별도 root가 공유 root 인덱스 항목 수를 바꿈");
        Qa.Check((await Read(services[1])).Entries.All(entry =>
            !Path.GetFullPath(entry.FullPath).Equals(Path.GetFullPath(otherOnlyPath), StringComparison.OrdinalIgnoreCase)),
            "분리 root에 기록한 항목이 공유 root에 나타남");

        var indexPath = IndexPath(services[0]);
        var originalIndexBytes = await File.ReadAllBytesAsync(indexPath);
        using var lockHandle = new FileStream(Path.Combine(root, WorkspaceStateService.RootLockFileName),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await Qa.FileFailsAsync(() => services[2].RecordSuccessfulUseAsync(directory.File("blocked.aethermath"), "잠금", timestamp),
            "외부 상태 잠금 충돌");
        Qa.Check(await BytesMatchAsync(originalIndexBytes, indexPath), "잠금 충돌이 recent index를 변경함");

        var indexFilesBeforeReplacementFailure = string.Join('\n', Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order());
        using (new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Qa.FileFailsAsync(() => services[2].RecordSuccessfulUseAsync(paths[0], "교체 실패", timestamp.AddDays(5)),
                "열린 인덱스 파일의 atomic replace 실패");
            Qa.Check(await BytesMatchAsync(originalIndexBytes, indexPath), "atomic replace 실패가 기존 index bytes를 변경함");
        }
        Qa.Equal(indexFilesBeforeReplacementFailure,
            string.Join('\n', Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order()),
            "index atomic replace 실패 뒤 임시 파일 잔류");

        var existingRecovery = new WorkspaceRecovery(Guid.NewGuid(), Snapshot(), timestamp, null);
        // 잠금을 해제한 뒤 유효한 recovery 파일을 만든 다음 선취소·재잠금 경계를 검사한다.
        lockHandle.Dispose();
        Qa.Equal(RecoveryWriteStatus.Written, (await services[0].SaveRecoveryAsync(existingRecovery)).Status, "recovery 원본 준비");
        var recoveryPath = RecoveryPath(services[0], existingRecovery.RecoveryId);
        var originalRecoveryBytes = await File.ReadAllBytesAsync(recoveryPath);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Qa.FailsAsync(() => services[1].ReadRecentWorkspacesAsync(canceled.Token), "선취소 recent read", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => services[1].RecordSuccessfulUseAsync(directory.File("cancelled.aethermath"), "취소", timestamp, canceled.Token),
            "선취소 recent write", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => services[1].SetPinnedAsync(paths[0], true, canceled.Token), "선취소 pin", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => services[1].RemoveRecentAsync(paths[0], canceled.Token), "선취소 recent delete", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => services[1].SaveRecoveryAsync(existingRecovery with { CapturedAtUtc = timestamp.AddDays(1) }, canceled.Token),
            "선취소 recovery write", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => services[1].ListRecoveriesAsync(canceled.Token), "선취소 recovery list", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => services[1].ReadRecoveryAsync(existingRecovery.RecoveryId, canceled.Token), "선취소 recovery read", typeof(OperationCanceledException));
        await Qa.FailsAsync(() => services[1].DeleteRecoveryAsync(existingRecovery.RecoveryId, canceled.Token), "선취소 recovery delete", typeof(OperationCanceledException));
        Qa.Check(await BytesMatchAsync(originalIndexBytes, indexPath), "선취소가 recent index bytes를 변경함");
        Qa.Check(await BytesMatchAsync(originalRecoveryBytes, recoveryPath), "선취소가 recovery bytes를 변경함");

        using var recoveryLock = new FileStream(Path.Combine(root, WorkspaceStateService.RootLockFileName),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await Qa.FileFailsAsync(() => services[3].SaveRecoveryAsync(existingRecovery with { CapturedAtUtc = timestamp.AddDays(2) }),
            "recovery 상태 잠금 충돌");
        Qa.Check(await BytesMatchAsync(originalRecoveryBytes, recoveryPath), "잠금 충돌이 recovery를 변경함");
        recoveryLock.Dispose();

        var recoveryDirectory = Path.GetDirectoryName(recoveryPath)!;
        var recoveryFilesBeforeReplacementFailure = string.Join('\n', Directory.GetFiles(recoveryDirectory, "*", SearchOption.TopDirectoryOnly).Order());
        using (new FileStream(recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Qa.FileFailsAsync(() => services[3].SaveRecoveryAsync(existingRecovery with { CapturedAtUtc = timestamp.AddDays(3) }),
                "열린 recovery 파일의 atomic replace 실패");
            Qa.Check(await BytesMatchAsync(originalRecoveryBytes, recoveryPath), "atomic replace 실패가 recovery bytes를 변경함");
        }
        Qa.Equal(recoveryFilesBeforeReplacementFailure,
            string.Join('\n', Directory.GetFiles(recoveryDirectory, "*", SearchOption.TopDirectoryOnly).Order()),
            "recovery atomic replace 실패 뒤 임시 파일 잔류");
    }

    private static async Task<bool> BytesMatchAsync(byte[] expected, string path)
    {
        var actual = await File.ReadAllBytesAsync(path);
        return expected.SequenceEqual(actual);
    }

    private static bool TrySetProperty(JsonNode node, string name, JsonNode? value)
    {
        if (node is JsonObject obj)
        {
            var key = obj.Select(pair => pair.Key).FirstOrDefault(candidate =>
                candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (key is not null)
            {
                obj[key] = value?.DeepClone();
                return true;
            }
            foreach (var child in obj.Select(pair => pair.Value).Where(child => child is not null))
                if (TrySetProperty(child!, name, value)) return true;
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child is not null && TrySetProperty(child, name, value)) return true;
        }
        return false;
    }

    private static bool TrySetVersion(JsonNode node, int value)
    {
        if (node is JsonObject obj)
        {
            var key = obj.Select(pair => pair.Key).FirstOrDefault(candidate =>
                candidate.Equals("version", StringComparison.OrdinalIgnoreCase) ||
                candidate.EndsWith("Version", StringComparison.OrdinalIgnoreCase));
            if (key is not null)
            {
                obj[key] = value;
                return true;
            }
            foreach (var child in obj.Select(pair => pair.Value).Where(child => child is not null))
                if (TrySetVersion(child!, value)) return true;
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child is not null && TrySetVersion(child, value)) return true;
        }
        return false;
    }
}
