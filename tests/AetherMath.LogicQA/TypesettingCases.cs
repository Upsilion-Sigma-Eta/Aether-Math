using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class TypesettingCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("TYPE-CASCADE", "Display/Text/Script 상속 출처와 node override 우선순위", ResolveCascade);
        yield return new("TYPE-APPLY-RESET", "네 번째 조판 속성의 combined apply·호환 wrapper·reset·history", ApplyResetAndHistory);
        yield return new("TYPE-JSON", "schema v1 Display 기본값·override 왕복·잘못된 enum 원본 보존", JsonCompatibility);
        yield return new("TYPE-FRACTION", "Display 분수의 level0 Text 전이와 compact 분수의 level+1 전이", FractionTransitions);
        yield return new("TYPE-NESTED", "Script 축소와 Matrix/Cases의 compact 동일 level", NestedStructureTransitions);
        yield return new("TYPE-DEPTH", "0.71 깊이 축소·1px 하한·명시 크기의 자식 상대 전이", DepthAndExplicitSize);
        yield return new("TYPE-OPERATORS", "큰 연산자 한계 배치·적분 옆 한계·hit-test 및 모델 불변", OperatorLayoutAndHitTest);
        yield return new("TYPE-LATEX-SVG", "scope LaTeX 조판 명령과 동일 LayoutResult의 SVG 소비", LatexAndSvgConsumers);
    }

    private static void ResolveCascade()
    {
        var workspaceLeaf = Qa.Text("workspace");
        var groupLeaf = Qa.Text("group");
        var rowLeaf = Qa.Text("row");
        var nodeLeaf = Qa.Text("node") with { StyleOverride = Override(MathTypesettingStyle.ScriptScript) };
        var group = Group([Row(groupLeaf), Row(rowLeaf) with { StyleOverride = Override(MathTypesettingStyle.Script) },
            Row(nodeLeaf)]) with { StyleOverride = Override(MathTypesettingStyle.Text) };
        var defaults = Group([Row(workspaceLeaf)]);
        var workspace = Workspace([group, defaults], new MathStyle(30, "Face", "#112233")
        {
            TypesettingStyle = MathTypesettingStyle.Display
        });
        var session = new EditingSession(workspace);

        AssertResolved(session, workspaceLeaf.Id, MathTypesettingStyle.Display,
            MathStyleSourceKind.WorkspaceDefault, workspace.Id, "workspace 기본값");
        AssertResolved(session, groupLeaf.Id, MathTypesettingStyle.Text,
            MathStyleSourceKind.Group, group.Id, "group override");
        AssertResolved(session, rowLeaf.Id, MathTypesettingStyle.Script,
            MathStyleSourceKind.Row, group.Rows[1].Id, "row override");
        AssertResolved(session, nodeLeaf.Id, MathTypesettingStyle.ScriptScript,
            MathStyleSourceKind.Node, nodeLeaf.Id, "node override");

        var legacyPreset = new MathStyle(18, "Legacy Face", "#334455");
        Qa.Equal(MathTypesettingStyle.Display, legacyPreset.TypesettingStyle,
            "기존 3인자 MathStyle 생성자의 기본 조판 스타일");
    }

    private static void ApplyResetAndHistory()
    {
        var targetNode = Qa.Text("target");
        var row = Row(targetNode);
        var workspace = Workspace([Group([row])]);
        var session = new EditingSession(workspace);
        Qa.Caret(session, targetNode.Id, 3);
        session.MarkSaved(session.CaptureSnapshot());
        var beforeFingerprint = Qa.Fingerprint(session.Workspace);
        var beforeStateId = session.CaptureSnapshot().StateId;
        var beforeCaret = session.Caret;
        var beforeSelection = session.Selection;
        var beforeRevision = session.Revision;
        var target = Capture(session, MathPropertyScope.SelectionNode);
        var patch = new MathPropertiesPatch(
            new MathStylePatch(FontSize: 36, FontFamily: "Property Face", Foreground: "#80AABBCC"),
            MathTypesettingStyle.Script);

        var applied = session.ApplyMathProperties(target, patch);
        Qa.Equal(MathPropertyChangeStatus.Applied, applied.Status, "4필드 combined Apply");
        Qa.Equal(beforeRevision + 1, session.Revision, "combined Apply 단일 commit");
        Qa.Equal(beforeCaret, session.Caret, "조판 Apply caret 보존");
        Qa.Equal(beforeSelection, session.Selection, "조판 Apply selection 보존");
        Qa.Equal(new MathStyleOverride(36, "Property Face", "#80AABBCC")
        {
            TypesettingStyle = MathTypesettingStyle.Script
        }, Qa.Node(session, targetNode.Id).StyleOverride, "네 필드 모두 node override에 저장");
        var afterFingerprint = Qa.Fingerprint(session.Workspace);
        Qa.Check(afterFingerprint != beforeFingerprint, "combined Apply가 fingerprint를 바꾸지 않음");

        var revisionAfterApply = session.Revision;
        var same = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode), patch);
        Qa.Equal(MathPropertyChangeStatus.NoChange, same.Status, "저장된 명시값과 동일한 patch");
        Qa.Equal(revisionAfterApply, session.Revision, "동일 patch revision 변경");
        var invalidNull = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode),
            new MathPropertiesPatch(null!, MathTypesettingStyle.Text));
        Qa.Equal(MathPropertyChangeStatus.InvalidValue, invalidNull.Status, "null Style patch 거절");
        var invalidEnum = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode),
            new MathPropertiesPatch(new MathStylePatch(), (MathTypesettingStyle)999));
        Qa.Equal(MathPropertyChangeStatus.InvalidValue, invalidEnum.Status, "무효 조판 enum 거절");
        Qa.Equal(afterFingerprint, Qa.Fingerprint(session.Workspace), "잘못된 patch 모델 변경");
        Qa.Equal(revisionAfterApply, session.Revision, "잘못된 patch revision 변경");

        session.Undo();
        Qa.Equal(beforeFingerprint, Qa.Fingerprint(session.Workspace), "combined Apply undo 구조/스타일");
        Qa.Equal(beforeStateId, session.CaptureSnapshot().StateId, "combined Apply undo saved StateId");
        Qa.Equal(beforeCaret, session.Caret, "combined Apply undo caret");
        Qa.Equal(beforeSelection, session.Selection, "combined Apply undo selection");
        Qa.Check(!session.IsDirty && session.CanRedo, "combined Apply undo dirty/redo 상태");
        session.Redo();
        Qa.Equal(afterFingerprint, Qa.Fingerprint(session.Workspace), "combined Apply redo 네 필드");
        Qa.Equal(beforeCaret, session.Caret, "combined Apply redo caret");
        Qa.Equal(beforeSelection, session.Selection, "combined Apply redo selection");
        Qa.Check(session.IsDirty && session.CanUndo && !session.CanRedo, "combined Apply redo history 상태");

        var legacyWrapper = session.ApplyMathStyle(Capture(session, MathPropertyScope.SelectionNode),
            new MathStylePatch(Foreground: "#CCDDEE"));
        Qa.Equal(MathPropertyChangeStatus.Applied, legacyWrapper.Status, "3필드 호환 ApplyMathStyle");
        var wrapperOverride = Qa.Node(session, targetNode.Id).StyleOverride!;
        Qa.Equal(MathTypesettingStyle.Script, wrapperOverride.TypesettingStyle,
            "기존 3필드 wrapper가 조판 override를 덮음");
        Qa.Equal("Property Face", wrapperOverride.FontFamily, "기존 wrapper가 미변경 글꼴을 잃음");

        var resetTarget = Capture(session, MathPropertyScope.SelectionNode);
        var resetRevision = session.Revision;
        var reset = session.ResetMathStyleOverride(resetTarget);
        Qa.Equal(MathPropertyChangeStatus.Applied, reset.Status, "4필드 override 전체 reset");
        Qa.Equal(resetRevision + 1, session.Revision, "4필드 Reset 단일 commit");
        Qa.Equal<MathStyleOverride?>(null, Qa.Node(session, targetNode.Id).StyleOverride,
            "조판 포함 all-null override 정규화");
        session.Undo();
        Qa.Equal(MathTypesettingStyle.Script, Qa.Node(session, targetNode.Id).StyleOverride?.TypesettingStyle,
            "Reset undo가 조판 override 복구");

        var workspaceSession = new EditingSession(Workspace([Group([Row(Qa.Text("root"))])]));
        var workspaceTarget = Capture(workspaceSession, MathPropertyScope.WorkspaceDefault);
        var workspaceApply = workspaceSession.ApplyMathProperties(workspaceTarget,
            new MathPropertiesPatch(new MathStylePatch(), MathTypesettingStyle.Text));
        Qa.Equal(MathPropertyChangeStatus.Applied, workspaceApply.Status, "WorkspaceDefault 조판 preset Apply");
        Qa.Equal(MathTypesettingStyle.Text, workspaceSession.Workspace.Style.TypesettingStyle,
            "Workspace MathStyle.TypesettingStyle 갱신");
    }

    private static async Task JsonCompatibility()
    {
        using var directory = new TempDirectory();
        var service = new WorkspaceFileService();
        var leaf = Qa.Text("roundtrip") with { StyleOverride = Override(MathTypesettingStyle.ScriptScript) };
        var row = Row(leaf) with { StyleOverride = Override(MathTypesettingStyle.Script) };
        var group = Group([row]) with
        {
            StyleOverride = new MathStyleOverride(FontSize: 24)
            {
                TypesettingStyle = MathTypesettingStyle.Text
            }
        };
        var rowWithStyle = row with
        {
            StyleOverride = new MathStyleOverride(Foreground: "#AABBCC")
            {
                TypesettingStyle = MathTypesettingStyle.Script
            }
        };
        group = group with { Rows = [rowWithStyle] };
        var workspace = Workspace([group], new MathStyle(30, "Face", "#123456")
        {
            TypesettingStyle = MathTypesettingStyle.Display
        });
        var path = directory.File("typesetting.aethermath");
        await service.SaveAsync(path, new EditingSession(workspace).CaptureSnapshot());
        var reopened = await service.OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(workspace), Qa.Fingerprint(reopened), "root 및 모든 override 조판 스타일 왕복");
        Qa.Equal(1, reopened.SchemaVersion, "조판 스타일 도입 뒤 schema 버전");

        var legacyJson = JsonNode.Parse(await File.ReadAllTextAsync(path)) ?? throw new InvalidOperationException("JSON root 없음");
        RemovePropertyRecursively(legacyJson, "typesettingStyle");
        var legacyPath = directory.File("legacy-v1.aethermath");
        await File.WriteAllTextAsync(legacyPath, legacyJson.ToJsonString());
        var legacy = await service.OpenAsync(legacyPath);
        Qa.Equal(1, legacy.SchemaVersion, "legacy schema version");
        Qa.Equal(MathTypesettingStyle.Display, legacy.Style.TypesettingStyle, "legacy root preset 기본값");
        Qa.Equal<MathTypesettingStyle?>(null, legacy.Groups[0].StyleOverride?.TypesettingStyle,
            "legacy group preset이 명시값으로 오인됨");
        Qa.Equal(24d, legacy.Groups[0].StyleOverride?.FontSize,
            "legacy group의 기존 글꼴 크기 손실");
        Qa.Equal("#AABBCC", legacy.Groups[0].Rows[0].StyleOverride?.Foreground,
            "legacy row의 기존 전경색 손실");
        Qa.Equal<MathTypesettingStyle?>(null, legacy.Groups[0].Rows[0].StyleOverride?.TypesettingStyle,
            "legacy row preset이 명시값으로 오인됨");
        Qa.Equal<MathTypesettingStyle?>(null,
            Qa.Nodes(legacy).OfType<TextNode>().Single().StyleOverride?.TypesettingStyle,
            "legacy node preset이 명시값으로 오인됨");

        var invalidJson = JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject
            ?? throw new InvalidOperationException("저장된 JSON 객체 없음");
        invalidJson["style"]!["typesettingStyle"] = 999;
        var invalidPath = directory.File("unknown-typesetting.aethermath");
        await File.WriteAllTextAsync(invalidPath, invalidJson.ToJsonString());
        var originalBytes = await File.ReadAllBytesAsync(invalidPath);
        await Qa.FailsAsync(() => service.OpenAsync(invalidPath), "무효 조판 enum 거절",
            typeof(InvalidDataException), typeof(System.Text.Json.JsonException));
        var bytesAfterRead = await File.ReadAllBytesAsync(invalidPath);
        Qa.Check(originalBytes.SequenceEqual(bytesAfterRead), "무효 enum open이 원본 bytes를 변경");
    }

    private static void FractionTransitions()
    {
        var displayNumerator = Qa.Text("displaynum");
        var displayDenominator = Qa.Text("displayden");
        var displayFraction = new FractionNode(Guid.NewGuid(), displayNumerator, displayDenominator)
            with { StyleOverride = new MathStyleOverride(FontSize: 34) { TypesettingStyle = MathTypesettingStyle.Display } };
        var textNumerator = Qa.Text("textnum");
        var textDenominator = Qa.Text("textden");
        var textFraction = new FractionNode(Guid.NewGuid(), textNumerator, textDenominator)
            with { StyleOverride = new MathStyleOverride(FontSize: 34) { TypesettingStyle = MathTypesettingStyle.Text } };
        var workspace = Workspace([Group([Row(displayFraction), Row(textFraction)])],
            new MathStyle(30, "Test Face", "#203040") { TypesettingStyle = MathTypesettingStyle.Display });
        var layout = MathLayoutEngine.Layout(workspace, new DeterministicMetrics());

        Qa.Near(34, GlyphSize(layout, displayNumerator.Id), "Display 분수 numerator의 level0 Text 크기");
        Qa.Near(34, GlyphSize(layout, displayDenominator.Id), "Display 분수 denominator의 level0 Text 크기");
        Qa.Near(34 * 0.71, GlyphSize(layout, textNumerator.Id), "Text 분수 numerator의 level+1 크기");
        Qa.Near(34 * 0.71, GlyphSize(layout, textDenominator.Id), "Text 분수 denominator의 level+1 크기");
        Qa.Check(Node(layout, displayNumerator.Id).Bounds.Y < Node(layout, displayDenominator.Id).Bounds.Y,
            "분수 numerator/denominator의 세로 배치가 뒤바뀜");
        Qa.Check(layout.Primitives.OfType<LinePrimitive>().Any(), "fraction bar primitive 없음");
    }

    private static void NestedStructureTransitions()
    {
        var scriptBase = Qa.Text("script-base");
        var scriptChild = Qa.Text("script-child");
        var script = new ScriptNode(Guid.NewGuid(), scriptBase, scriptChild, null);
        var matrixA = Qa.Text("matrix-a");
        var matrixB = Qa.Text("matrix-b");
        var matrix = new MatrixNode(Guid.NewGuid(), 1, 2, [matrixA, matrixB]);
        var caseExpr = Qa.Text("case-expr");
        var caseCondition = Qa.Text("case-condition");
        var cases = new CasesNode(Guid.NewGuid(), [new CaseRow(caseExpr, caseCondition)]);
        var workspace = Workspace([Group([Row(Qa.Sequence(script, matrix, cases))])],
            new MathStyle(40, "Test Face", "#203040") { TypesettingStyle = MathTypesettingStyle.Display });
        var layout = MathLayoutEngine.Layout(workspace, new DeterministicMetrics());

        Qa.Near(40, GlyphSize(layout, scriptBase.Id), "Script base 크기");
        Qa.Near(40 * 0.71, GlyphSize(layout, scriptChild.Id), "Script 자식 compact level+1 크기");
        Qa.Near(40, GlyphSize(layout, matrixA.Id), "Display matrix cell compact 전이의 동일 level");
        Qa.Near(40, GlyphSize(layout, matrixB.Id), "Matrix 모든 cell 동일 level");
        Qa.Near(40, GlyphSize(layout, caseExpr.Id), "Display cases expression compact 전이의 동일 level");
        Qa.Near(40, GlyphSize(layout, caseCondition.Id), "Display cases condition compact 전이의 동일 level");
        Qa.Check(Node(layout, matrix.Id).Bounds.Contains(Center(Node(layout, matrixA.Id).Bounds)),
            "행렬 bounds가 셀 bounds를 포함하지 않음");
        Qa.Check(Node(layout, cases.Id).Bounds.Contains(Center(Node(layout, caseExpr.Id).Bounds)),
            "Cases bounds가 expression bounds를 포함하지 않음");

        AssertLargeScriptChildBounds(includeSubscript: false, includeSuperscript: true);
        AssertLargeScriptChildBounds(includeSubscript: true, includeSuperscript: false);
        AssertLargeScriptChildBounds(includeSubscript: true, includeSuperscript: true);
    }

    private static void DepthAndExplicitSize()
    {
        var ownerText = Qa.Text("explicit-owner");
        var scriptBase = Qa.Text("explicit-base");
        var scriptChild = Qa.Text("explicit-child");
        var childScript = new ScriptNode(Guid.NewGuid(), scriptBase, scriptChild, null);
        var explicitRoot = Qa.Sequence(ownerText, childScript) with
        {
            StyleOverride = new MathStyleOverride(FontSize: 19)
        };

        MathNode deeplyNested = Qa.Text("depth-floor");
        for (var index = 0; index < 15; index++)
            deeplyNested = new ScriptNode(Guid.NewGuid(), Qa.Text("depth-base-" + index), deeplyNested, null);

        var workspace = Workspace([Group([Row(explicitRoot), Row(deeplyNested)])],
            new MathStyle(30, "Test Face", "#203040") { TypesettingStyle = MathTypesettingStyle.Text });
        var layout = MathLayoutEngine.Layout(workspace, new DeterministicMetrics());

        Qa.Near(19, GlyphSize(layout, ownerText.Id), "명시 FontSize가 대상 node에서 절대값");
        Qa.Near(19, GlyphSize(layout, scriptBase.Id), "명시 FontSize 자식 Script의 base 크기");
        Qa.Near(19 * 0.71, GlyphSize(layout, scriptChild.Id), "명시 FontSize 이후 Script 자식 상대 전이");

        var anchorOwnerText = Qa.Text("anchor-owner");
        var innerScriptBase = Qa.Text("preset-reset-base");
        var innerScriptChild = Qa.Text("preset-reset-subscript");
        var innerTextPreset = new ScriptNode(Guid.NewGuid(), innerScriptBase, innerScriptChild, null)
        {
            StyleOverride = Override(MathTypesettingStyle.Text)
        };
        var anchoredSequence = Qa.Sequence(anchorOwnerText, innerTextPreset) with
        {
            StyleOverride = new MathStyleOverride(FontSize: 19)
            {
                TypesettingStyle = MathTypesettingStyle.Script
            }
        };
        var contextWorkspace = Workspace([Group([Row(anchoredSequence)])],
            new MathStyle(30, "Test Face", "#203040")
            {
                TypesettingStyle = MathTypesettingStyle.Display
            });
        var contextLayout = MathLayoutEngine.Layout(contextWorkspace, new DeterministicMetrics());
        Qa.Near(19, GlyphSize(contextLayout, innerScriptBase.Id),
            "Script 크기 앵커 안에서 자식 Text preset이 대상 node 크기를 유지");
        Qa.Near(19 * 0.71, GlyphSize(contextLayout, innerScriptChild.Id),
            "자식 Text preset이 상속된 크기 앵커를 재설정해 하위 첨자를 한 단계 축소");

        Qa.Near(1, GlyphSize(layout, Qa.Nodes(workspace).OfType<TextNode>().Single(node => node.Text == "depth-floor").Id),
            "깊은 수학 조판 축소의 1px 하한");
        Qa.Check(layout.Nodes.All(node => double.IsFinite(node.Bounds.X) && double.IsFinite(node.Bounds.Y) &&
            double.IsFinite(node.Bounds.Width) && double.IsFinite(node.Bounds.Height) && node.Bounds.Width >= 0 && node.Bounds.Height > 0),
            "깊은 축소가 유효하지 않은 node bounds를 생성");

        const double largeWorkspaceFontSize = 1_500_001;
        var largeWorkspaceText = Qa.Text("large-default");
        var largeWorkspace = Workspace([Group([Row(largeWorkspaceText)])],
            new MathStyle(largeWorkspaceFontSize, "Test Face", "#203040")
            {
                TypesettingStyle = MathTypesettingStyle.Display
            });
        var largeWorkspaceLayout = MathLayoutEngine.Layout(largeWorkspace, new DeterministicMetrics());
        Qa.Near(largeWorkspaceFontSize, GlyphSize(largeWorkspaceLayout, largeWorkspaceText.Id),
            "유효한 큰 workspace 글꼴 크기가 임의 상한으로 잘림");
        Qa.Check(double.IsFinite(largeWorkspaceLayout.Width) && double.IsFinite(largeWorkspaceLayout.Height),
            "큰 workspace 글꼴 크기가 비유한 전체 bounds 생성");
    }

    private static void OperatorLayoutAndHitTest()
    {
        var display = OperatorFixture(MathTypesettingStyle.Display);
        var compact = OperatorFixture(MathTypesettingStyle.Text);
        var displayStress = OperatorFixture(MathTypesettingStyle.Display, stressBounds: true);
        var compactStress = OperatorFixture(MathTypesettingStyle.Text, stressBounds: true);
        AssertOperatorLayout(display, isDisplay: true);
        AssertOperatorLayout(compact, isDisplay: false);
        AssertOperatorLayout(displayStress, isDisplay: true);
        AssertOperatorLayout(compactStress, isDisplay: false);

        Qa.Check(Node(display.Layout, display.Sum.Id).Bounds.Height > Node(compact.Layout, compact.Sum.Id).Bounds.Height,
            "Text compact 합 행 높이가 Display 행보다 줄지 않음");
        Qa.Check(display.Layout.Height > compact.Layout.Height,
            "Text compact 연산자 fixture 전체 높이가 Display보다 줄지 않음");

        foreach (var style in new[] { MathTypesettingStyle.Display, MathTypesettingStyle.Text })
        {
            AssertSingleBoundOperator(style, upperOnly: true);
            AssertSingleBoundOperator(style, upperOnly: false);
        }

        var displaySumGlyph = Glyph(display.Layout, display.Sum.Id, "∑");
        var compactSumGlyph = Glyph(compact.Layout, compact.Sum.Id, "∑");
        var displayIntegralGlyph = Glyph(display.Layout, display.Integral.Id, "∫");
        var compactIntegralGlyph = Glyph(compact.Layout, compact.Integral.Id, "∫");
        Qa.Check(displaySumGlyph.FontSize > compactSumGlyph.FontSize,
            "Display 합 연산자가 compact 크기보다 크지 않음");
        Qa.Check(displayIntegralGlyph.FontSize > compactIntegralGlyph.FontSize,
            "Display 적분 연산자가 compact 크기보다 크지 않음");

        foreach (var fixture in new[] { display, compact })
        {
            var before = Qa.Fingerprint(fixture.Session.Workspace);
            var revision = fixture.Session.Revision;
            foreach (var text in fixture.TextNodes)
            {
                var stop = Node(fixture.Layout, text.Id).CaretStops.First();
                Qa.Equal(new MathCaret(text.Id, stop.TextElementOffset), fixture.Layout.HitTest(stop.Position),
                    "조판별 구조 자식 caret hit-test " + text.Text);
            }
            Qa.Equal(before, Qa.Fingerprint(fixture.Session.Workspace), "layout/hit-test가 workspace 변경");
            Qa.Equal(revision, fixture.Session.Revision, "layout/hit-test가 revision 변경");
        }
    }

    private static void LatexAndSvgConsumers()
    {
        var firstPrefix = Qa.Text("lhs=");
        var firstNumerator = Qa.Text("n");
        var firstDenominator = Qa.Text("d");
        var explicitlyStyledFraction = new FractionNode(Guid.NewGuid(), firstNumerator, firstDenominator)
        {
            StyleOverride = Override(MathTypesettingStyle.ScriptScript)
        };
        var firstSuffix = Qa.Text("+tail");
        var firstRow = Row(Qa.Sequence(firstPrefix, explicitlyStyledFraction, firstSuffix))
            with { StyleOverride = Override(MathTypesettingStyle.Script) };

        var secondPrefix = Qa.Text("rhs=");
        var secondNumerator = Qa.Text("u");
        var secondDenominator = Qa.Text("v");
        var inheritedFraction = new FractionNode(Guid.NewGuid(), secondNumerator, secondDenominator);
        var secondRow = Row(Qa.Sequence(secondPrefix, inheritedFraction));
        var alignedGroup = Group([firstRow, secondRow]) with
        {
            Alignment = AlignmentMode.Equals,
            StyleOverride = Override(MathTypesettingStyle.Text)
        };
        var workspace = Workspace([alignedGroup], new MathStyle(26, "Test Face", "#203040")
        {
            TypesettingStyle = MathTypesettingStyle.Display
        });

        const string expectedLatex = "\\begin{aligned}" +
            "{\\scriptstyle lhs}&={\\scriptstyle }" +
            "{\\scriptscriptstyle \\frac{{\\scriptscriptstyle n}}{{\\scriptscriptstyle d}}}" +
            "{\\scriptstyle +tail} \\\\" +
            "{\\textstyle rhs}&={\\textstyle }" +
            "{\\textstyle \\frac{{\\scriptstyle u}}{{\\scriptstyle v}}}" +
            "\\end{aligned}";
        var latex = LatexSerializer.ToLatex(workspace);
        Qa.Equal(expectedLatex, latex,
            "aligned 행별 preset·외부 정렬자·구조 자동 전이·명시 node scope와 다음 형제 격리의 정확한 LaTeX");
        var styleDiagnostics = LatexSerializer.GetStyleDiagnostics(workspace);
        Qa.Equal(3, styleDiagnostics.Length,
            "기존 크기/글꼴/색 미표현 진단 수가 Typesetting 도입으로 바뀜");
        Qa.Check(styleDiagnostics.All(diagnostic => diagnostic.OwnerId == workspace.Id &&
                diagnostic.Scope == MathPropertyScope.WorkspaceDefault),
            "표현 가능한 Typesetting preset이 미표현 속성 진단으로 오인됨");

        var defaultDisplayRow = Workspace([Group([Row(Qa.Text("x=1"))])]);
        Qa.Equal("{\\displaystyle x=1}", LatexSerializer.ToLatex(defaultDisplayRow),
            "기본 Display preset의 LaTeX 조각 보존");
        var defaultDisplayFraction = new FractionNode(Guid.NewGuid(), Qa.Text("a"), Qa.Text("b"));
        Qa.Equal("{\\displaystyle \\frac{{\\textstyle a}}{{\\textstyle b}}}",
            LatexSerializer.ToLatex(Workspace([Group([Row(defaultDisplayFraction)])])),
            "기본 Display 분수와 자동 Text 자식 전이의 정확한 LaTeX");

        var layoutBefore = Qa.Fingerprint(workspace);
        var layout = MathLayoutEngine.Layout(workspace, new DeterministicMetrics());
        var svg = new SvgExportService().ToSvg(layout);
        var document = XDocument.Parse(svg);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var svgText = document.Descendants(ns + "text").ToArray();
        var glyphs = layout.Primitives.OfType<GlyphPrimitive>().ToArray();
        Qa.Equal(glyphs.Length, svgText.Length, "동일 LayoutResult glyph SVG 개수");
        Qa.Equal(string.Join("|", glyphs.Select(glyph => glyph.Text)),
            string.Join("|", svgText.Select(element => element.Value)), "동일 LayoutResult SVG glyph 문서 순서");
        for (var index = 0; index < glyphs.Length; index++)
        {
            Qa.Near(glyphs[index].FontSize,
                double.Parse((string?)svgText[index].Attribute("font-size") ?? "NaN", CultureInfo.InvariantCulture),
                "SVG가 LayoutResult의 effective font size를 사용 " + index);
            Qa.Equal(glyphs[index].FontFamily, (string?)svgText[index].Attribute("font-family"),
                "SVG가 LayoutResult의 effective font family를 사용 " + index);
        }
        Qa.Equal(layoutBefore, Qa.Fingerprint(workspace), "LaTeX/layout/SVG가 workspace 또는 style을 변경");
    }

    private static void AssertResolved(EditingSession session, Guid nodeId, MathTypesettingStyle expected,
        MathStyleSourceKind originKind, Guid originId, string label)
    {
        Qa.Caret(session, nodeId, 0);
        Qa.Check(session.TryCaptureMathPropertyTarget(MathPropertyScope.SelectionNode, out var target) && target is not null,
            label + " target 캡처");
        var resolved = session.ResolveMathStyle(target!) ?? throw new InvalidOperationException(label + " style resolve 실패");
        Qa.Equal(expected, resolved.TypesettingStyle.Value, label + " effective value");
        Qa.Equal(originKind, resolved.TypesettingStyle.Origin.Kind, label + " origin kind");
        Qa.Equal(originId, resolved.TypesettingStyle.Origin.OwnerId, label + " origin owner");
    }

    private static OperatorFixtureData OperatorFixture(MathTypesettingStyle style, bool stressBounds = false)
    {
        var sumLower = Qa.Text(stressBounds ? new string('i', 18) : "i=0");
        var sumUpper = Qa.Text(stressBounds ? new string('n', 24) : "n") with
        {
            StyleOverride = stressBounds ? new MathStyleOverride(FontSize: 76) : null
        };
        var sumBody = Qa.Text(stressBounds ? "sumBodyWithSeveralCharacters" : "sumBody");
        var sum = new SummationNode(Guid.NewGuid(), sumBody, sumLower, sumUpper);
        var limitApproach = Qa.Text("x→0");
        var limitBody = Qa.Text("limitBody");
        var limit = new LimitNode(Guid.NewGuid(), limitApproach, limitBody);
        var intLower = Qa.Text("a");
        var intUpper = Qa.Text("b");
        var intBody = Qa.Text("intBody");
        var intVariable = Qa.Text("x");
        var integral = new IntegralNode(Guid.NewGuid(), intBody, intLower, intUpper, intVariable);
        var roots = new MathNode[] { sum, limit, integral };
        var workspace = Workspace([Group(roots.Select(Row).ToArray())],
            new MathStyle(40, "Test Face", "#203040") { TypesettingStyle = style });
        var session = new EditingSession(workspace);
        var layout = MathLayoutEngine.Layout(workspace, new DeterministicMetrics());
        return new OperatorFixtureData(layout, session, sum, limit, integral, stressBounds,
            [sumLower, sumUpper, sumBody, limitApproach, limitBody, intLower, intUpper, intBody, intVariable]);
    }

    private static void AssertSingleBoundOperator(MathTypesettingStyle style, bool upperOnly)
    {
        var longBound = Qa.Text(new string(upperOnly ? 'u' : 'l', 20)) with
        {
            StyleOverride = new MathStyleOverride(FontSize: 82)
        };
        var body = Qa.Text("single-bound-body");
        var sum = new SummationNode(Guid.NewGuid(), body,
            upperOnly ? null : longBound,
            upperOnly ? longBound : null);
        var workspace = Workspace([Group([Row(sum)])], new MathStyle(40, "Test Face", "#203040")
        {
            TypesettingStyle = style
        });
        var layout = MathLayoutEngine.Layout(workspace, new DeterministicMetrics());
        var sumBounds = Node(layout, sum.Id).Bounds;
        var boundBounds = Node(layout, longBound.Id).Bounds;
        var bodyBounds = Node(layout, body.Id).Bounds;
        var glyphBounds = GlyphBounds(Glyph(layout, sum.Id, "∑"), new DeterministicMetrics());
        var layoutBounds = new MathRect(0, 0, layout.Width, layout.Height);

        Qa.Check(ContainsRect(sumBounds, boundBounds) && ContainsRect(sumBounds, bodyBounds) &&
                 ContainsRect(sumBounds, glyphBounds),
            $"{style} {(upperOnly ? "상한" : "하한")}만 있는 큰 한계의 부모 bounds 포함");
        Qa.Check(ContainsRect(layoutBounds, sumBounds),
            $"{style} {(upperOnly ? "상한" : "하한")}만 있는 전체 layout bounds 포함");
        Qa.Check(!RectanglesOverlap(glyphBounds, boundBounds) && !RectanglesOverlap(bodyBounds, boundBounds),
            $"{style} {(upperOnly ? "상한" : "하한")}만 있는 큰 한계와 operator/body의 비겹침");
        if (style == MathTypesettingStyle.Display)
        {
            Qa.Check(upperOnly
                    ? boundBounds.Bottom <= glyphBounds.Y
                    : boundBounds.Y >= glyphBounds.Bottom,
                $"Display {(upperOnly ? "상한" : "하한")}만 있는 한계가 operator 위·아래에 배치되지 않음");
            Qa.Near(CenterX(glyphBounds), CenterX(boundBounds),
                $"Display {(upperOnly ? "상한" : "하한")}만 있는 한계의 operator 중앙 정렬", 5);
        }
        else
        {
            Qa.Check(boundBounds.X > glyphBounds.Right,
                $"{style} {(upperOnly ? "상한" : "하한")}만 있는 한계의 측면 배치");
        }
    }

    private static void AssertLargeScriptChildBounds(bool includeSubscript, bool includeSuperscript)
    {
        var @base = Qa.Text("script-base");
        var subscript = includeSubscript
            ? Qa.Text("large-subscript") with { StyleOverride = new MathStyleOverride(FontSize: 512) }
            : null;
        var superscript = includeSuperscript
            ? Qa.Text("large-superscript") with { StyleOverride = new MathStyleOverride(FontSize: 512) }
            : null;
        var script = new ScriptNode(Guid.NewGuid(), @base, subscript, superscript);
        var workspace = Workspace([Group([Row(script)])], new MathStyle(30, "Test Face", "#203040")
        {
            TypesettingStyle = MathTypesettingStyle.Display
        });
        var layout = MathLayoutEngine.Layout(workspace, new DeterministicMetrics());
        var scriptBounds = Node(layout, script.Id).Bounds;
        var baseBounds = Node(layout, @base.Id).Bounds;
        var layoutBounds = new MathRect(0, 0, layout.Width, layout.Height);

        Qa.Check(ContainsRect(scriptBounds, baseBounds),
            "큰 첨자 layout이 Script 부모에서 base를 잘라냄");
        Qa.Check(ContainsRect(layoutBounds, scriptBounds),
            "큰 첨자 Script 전체 bounds가 layout 경계를 벗어남");
        if (subscript is not null)
        {
            var subBounds = Node(layout, subscript.Id).Bounds;
            Qa.Check(ContainsRect(scriptBounds, subBounds),
                "512px 아래첨자 bounds가 Script 부모에서 잘림");
            Qa.Check(baseBounds.Right <= subBounds.X && !RectanglesOverlap(subBounds, baseBounds),
                "512px 아래첨자가 base 오른쪽에 배치되지 않았거나 2D bounds가 겹침");
        }
        if (superscript is not null)
        {
            var supBounds = Node(layout, superscript.Id).Bounds;
            Qa.Check(ContainsRect(scriptBounds, supBounds),
                "512px 위첨자 bounds가 Script 부모에서 잘림");
            Qa.Check(baseBounds.Right <= supBounds.X && !RectanglesOverlap(supBounds, baseBounds),
                "512px 위첨자가 base 오른쪽에 배치되지 않았거나 2D bounds가 겹침");
        }
        if (subscript is not null && superscript is not null)
        {
            var subBounds = Node(layout, subscript.Id).Bounds;
            var supBounds = Node(layout, superscript.Id).Bounds;
            Qa.Check(supBounds.Bottom < subBounds.Y,
                "512px 위/아래첨자 사이의 양수 세로 간격이 없음");
            Qa.Check(!RectanglesOverlap(supBounds, subBounds),
                "512px 위/아래첨자 bounds가 서로 겹침");
        }
    }

    private static void AssertOperatorLayout(OperatorFixtureData fixture, bool isDisplay)
    {
        var metrics = new DeterministicMetrics();
        var sumGlyph = Glyph(fixture.Layout, fixture.Sum.Id, "∑");
        var sumGlyphRect = GlyphBounds(sumGlyph, metrics);
        var sumLower = Node(fixture.Layout, fixture.Sum.LowerLimit!.Id).Bounds;
        var sumUpper = Node(fixture.Layout, fixture.Sum.UpperLimit!.Id).Bounds;
        var sumBody = Node(fixture.Layout, fixture.Sum.Body.Id).Bounds;
        var limitGlyph = Glyph(fixture.Layout, fixture.Limit.Id, "lim");
        var limitGlyphRect = GlyphBounds(limitGlyph, metrics);
        var approach = Node(fixture.Layout, fixture.Limit.Approach.Id).Bounds;
        var limitBody = Node(fixture.Layout, fixture.Limit.Body.Id).Bounds;
        var integralGlyph = Glyph(fixture.Layout, fixture.Integral.Id, "∫");
        var integralGlyphRect = GlyphBounds(integralGlyph, metrics);
        var integralLower = Node(fixture.Layout, fixture.Integral.LowerLimit!.Id).Bounds;
        var integralUpper = Node(fixture.Layout, fixture.Integral.UpperLimit!.Id).Bounds;
        var integralBody = Node(fixture.Layout, fixture.Integral.Integrand.Id).Bounds;

        if (isDisplay)
        {
            Qa.Check(CenterX(sumLower) < sumBody.X && CenterX(sumUpper) < sumBody.X,
                "Display sum 한계가 body 앞 operator column에 위치하지 않음");
            Qa.Near(CenterX(sumGlyphRect), CenterX(sumLower), "Display sum 하한의 operator 중앙 정렬", 5);
            Qa.Near(CenterX(sumGlyphRect), CenterX(sumUpper), "Display sum 상한의 operator 중앙 정렬", 5);
            Qa.Check(sumUpper.Bottom < sumGlyphRect.Y + 2 && sumLower.Y > sumGlyphRect.Bottom - 2,
                "Display sum 한계가 operator 위·아래에 놓이지 않음");
            Qa.Check(approach.X < limitBody.X && approach.Bottom > limitGlyphRect.Y,
                "Display lim 접근식이 operator 아래에 배치되지 않음");
        }
        else
        {
            Qa.Check(sumLower.X > sumGlyphRect.Right && sumUpper.X > sumGlyphRect.Right,
                "compact sum 한계가 operator 옆에 배치되지 않음");
            if (!fixture.StressBounds)
            {
                Qa.Check(Math.Abs(CenterY(sumGlyphRect) - CenterY(sumBody)) < 2,
                    "compact sum body가 operator의 inline baseline을 벗어남");
                Qa.Check(VerticalIntervalsOverlap(sumLower, sumGlyphRect) &&
                         VerticalIntervalsOverlap(sumUpper, sumGlyphRect),
                    "compact sum 한계의 baseline이 operator 높이와 겹치지 않음");
            }
            Qa.Check(approach.X > limitGlyphRect.Right && limitBody.X > approach.X,
                "compact lim 접근식이 operator 옆·본문 앞에 배치되지 않음");
        }

        Qa.Check(integralLower.X > integralGlyphRect.Right && integralUpper.X > integralGlyphRect.Right,
            "적분 한계가 Display/compact 모두 operator 옆에 배치되지 않음");
        Qa.Check(integralBody.X > integralLower.X && integralBody.X > integralUpper.X,
            "적분 integrand가 limit column 뒤에 배치되지 않음");
        var sumBounds = Node(fixture.Layout, fixture.Sum.Id).Bounds;
        Qa.Check(ContainsRect(sumBounds, Node(fixture.Layout, fixture.Sum.LowerLimit!.Id).Bounds) &&
                 ContainsRect(sumBounds, Node(fixture.Layout, fixture.Sum.UpperLimit!.Id).Bounds) &&
                 ContainsRect(sumBounds, Node(fixture.Layout, fixture.Sum.Body.Id).Bounds) &&
                 ContainsRect(sumBounds, sumGlyphRect),
            "큰 합 연산자/한계/본문 bounds가 합 전체 bounds에 포함되지 않음");
        Qa.Check(!RectanglesOverlap(sumGlyphRect, sumLower) && !RectanglesOverlap(sumGlyphRect, sumUpper) &&
                 !RectanglesOverlap(sumLower, sumBody) && !RectanglesOverlap(sumUpper, sumBody),
            "합 연산자·큰 한계·본문의 기하 bounds가 서로 겹침");
        foreach (var node in fixture.Layout.Nodes)
            Qa.Check(node.Bounds.Width >= 0 && node.Bounds.Height > 0, "연산자 layout node bounds 유효성 " + node.NodeId);
    }

    private static MathRect GlyphBounds(GlyphPrimitive glyph, IMathTextMetrics metrics)
    {
        var measured = metrics.Measure(glyph.Text, glyph.FontFamily, glyph.FontSize, glyph.Italic);
        return new MathRect(glyph.BaselineOrigin.X, glyph.BaselineOrigin.Y - measured.Ascent,
            measured.Width, measured.Ascent + measured.Descent);
    }

    private static GlyphPrimitive Glyph(LayoutResult layout, Guid nodeId, string text) =>
        layout.Primitives.OfType<GlyphPrimitive>().Single(glyph => glyph.NodeId == nodeId && glyph.Text == text);

    private static double GlyphSize(LayoutResult layout, Guid nodeId)
    {
        var glyphs = layout.Primitives.OfType<GlyphPrimitive>().Where(glyph => glyph.NodeId == nodeId).ToArray();
        if (glyphs.Length == 0)
            throw new InvalidOperationException("node glyph primitive 없음: " + nodeId);
        var sizes = glyphs.Select(glyph => glyph.FontSize).Distinct().ToArray();
        if (sizes.Length != 1)
            throw new InvalidOperationException("단일 TextNode에 서로 다른 glyph size가 있음: " + nodeId);
        return sizes[0];
    }

    private static NodeLayout Node(LayoutResult layout, Guid id) => layout.Nodes.Single(node => node.NodeId == id);
    private static MathPoint Center(MathRect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    private static double CenterX(MathRect rect) => rect.X + rect.Width / 2;
    private static double CenterY(MathRect rect) => rect.Y + rect.Height / 2;
    private static bool VerticalIntervalsOverlap(MathRect left, MathRect right) =>
        left.Y < right.Bottom && right.Y < left.Bottom;

    private static bool RectanglesOverlap(MathRect left, MathRect right) =>
        left.X < right.Right && right.X < left.Right && left.Y < right.Bottom && right.Y < left.Bottom;

    private static bool ContainsRect(MathRect outer, MathRect inner, double epsilon = 0.01) =>
        inner.X >= outer.X - epsilon && inner.Y >= outer.Y - epsilon &&
        inner.Right <= outer.Right + epsilon && inner.Bottom <= outer.Bottom + epsilon;

    private static MathPropertyTargetSnapshot Capture(EditingSession session, MathPropertyScope scope)
    {
        Qa.Check(session.TryCaptureMathPropertyTarget(scope, out var target) && target is not null,
            "조판 대상 캡처 실패: " + scope);
        return target!;
    }

    private static MathStyleOverride Override(MathTypesettingStyle style) => new() { TypesettingStyle = style };

    private static EquationRow Row(MathNode root) => new(Guid.NewGuid(), root);

    private static EquationGroup Group(EquationRow[] rows) =>
        new(Guid.NewGuid(), [.. rows], AlignmentMode.None, 20);

    private static EquationWorkspace Workspace(EquationGroup[] groups, MathStyle? style = null) =>
        new(Guid.NewGuid(), 1, "조판 QA", [.. groups], style ?? new MathStyle(30, "Test Face", "#203040"));

    private static void RemovePropertyRecursively(JsonNode? node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(propertyName);
            foreach (var property in obj.ToArray())
                RemovePropertyRecursively(property.Value, propertyName);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                RemovePropertyRecursively(child, propertyName);
        }
    }

    private sealed record OperatorFixtureData(LayoutResult Layout, EditingSession Session,
        SummationNode Sum, LimitNode Limit, IntegralNode Integral, bool StressBounds, TextNode[] TextNodes);

    private sealed class DeterministicMetrics : IMathTextMetrics
    {
        public TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic)
        {
            var count = Qa.Elements(text);
            var advance = fontSize * (italic ? 0.53 : 0.50);
            var caretX = ImmutableArray.CreateBuilder<double>(count + 1);
            for (var index = 0; index <= count; index++)
                caretX.Add(index * advance);
            return new TextMeasurement(count * advance, fontSize * 0.72, fontSize * 0.23, caretX.MoveToImmutable());
        }
    }
}
