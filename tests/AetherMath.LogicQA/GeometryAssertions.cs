using AetherMath.Core;
using AetherMath.Layout;

namespace AetherMath.LogicQA;

internal static class GeometryAssertions
{
    public static void Structures(EquationWorkspace workspace, LayoutResult layout)
    {
        NodeLayout Node(MathNode n) => layout.Nodes.Single(item => item.NodeId == n.Id);
        foreach (var node in Qa.Nodes(workspace))
        {
            switch (node)
            {
                case FractionNode fraction:
                {
                    var numerator = Node(fraction.Numerator).Bounds; var denominator = Node(fraction.Denominator).Bounds;
                    var lines = layout.Primitives.OfType<LinePrimitive>().Where(line =>
                        Math.Abs(line.Start.Y - line.End.Y) < 0.001 && line.StrokeWidth > 0 &&
                        line.Start.Y >= numerator.Y + numerator.Height - 0.001 && line.Start.Y <= denominator.Y + 0.001 &&
                        Math.Min(line.Start.X, line.End.X) <= Math.Min(numerator.X, denominator.X) + 0.001 &&
                        Math.Max(line.Start.X, line.End.X) >= Math.Max(numerator.X + numerator.Width, denominator.X + denominator.Width) - 0.001).ToArray();
                    Qa.Equal(1, lines.Length, "분자/분모 사이에서 양쪽 폭을 덮는 분수선 " + fraction.Id);
                    break;
                }
                case RadicalNode radical:
                {
                    var radicand = Node(radical.Radicand).Bounds;
                    var structureBounds = Node(radical).Bounds;
                    var shapes = layout.Primitives.OfType<PolylinePrimitive>().Where(p => p.Points.Length >= 3 && p.StrokeWidth > 0 &&
                        p.Points.All(v => v.X >= structureBounds.X - 0.001 && v.X <= structureBounds.X + structureBounds.Width + 0.001 &&
                            v.Y >= structureBounds.Y - 0.001 && v.Y <= structureBounds.Y + structureBounds.Height + 0.001) &&
                        p.Points.Min(v => v.X) < radicand.X && p.Points.Max(v => v.X) >= radicand.X + radicand.Width - 0.001 &&
                        p.Points.Min(v => v.Y) <= radicand.Y + 0.001 && p.Points.Max(v => v.Y) >= Node(radical.Radicand).Baseline).ToArray();
                    Qa.Check(shapes.Length > 0, "피근수 범위를 감싸는 근호/윗선 없음 " + radical.Id);
                    break;
                }
                case ScriptNode script:
                {
                    var baseNode = Node(script.Base);
                    if (script.Superscript is not null)
                    {
                        var upper = Node(script.Superscript);
                        Qa.Check(upper.Baseline < baseNode.Baseline && upper.Bounds.X >= baseNode.Bounds.X + baseNode.Bounds.Width - 0.001,
                            "위첨자가 밑보다 위/오른쪽에 있지 않음");
                    }
                    if (script.Subscript is not null)
                    {
                        var lower = Node(script.Subscript);
                        Qa.Check(lower.Baseline > baseNode.Baseline && lower.Bounds.X >= baseNode.Bounds.X + baseNode.Bounds.Width - 0.001,
                            "아래첨자가 밑보다 아래/오른쪽에 있지 않음");
                    }
                    break;
                }
                case MatrixNode matrix:
                {
                    var cells = matrix.Cells.Select(Node).ToArray();
                    for (int r = 0; r < matrix.Rows; r++)
                        for (int c = 0; c < matrix.Columns; c++)
                        {
                            var cell = cells[r * matrix.Columns + c];
                            Qa.Near(cells[r * matrix.Columns].Baseline, cell.Baseline, "행렬 같은 행 baseline");
                            if (c > 0) Qa.Check(cell.Bounds.X >= cells[r * matrix.Columns + c - 1].Bounds.X + cells[r * matrix.Columns + c - 1].Bounds.Width, "행렬 열 겹침/순서");
                            if (r > 0) Qa.Check(cell.Bounds.Y >= cells[(r - 1) * matrix.Columns + c].Bounds.Y + cells[(r - 1) * matrix.Columns + c].Bounds.Height, "행렬 행 겹침/순서");
                        }
                    double left = cells.Min(c => c.Bounds.X), right = cells.Max(c => c.Bounds.X + c.Bounds.Width);
                    double top = cells.Min(c => c.Bounds.Y), bottom = cells.Max(c => c.Bounds.Y + c.Bounds.Height);
                    var structureBounds = Node(matrix).Bounds;
                    var brackets = layout.Primitives.OfType<PolylinePrimitive>().Where(p => p.Points.Length >= 3 && p.StrokeWidth > 0 &&
                        p.Points.All(v => v.X >= structureBounds.X - 0.001 && v.X <= structureBounds.X + structureBounds.Width + 0.001 &&
                            v.Y >= structureBounds.Y - 0.001 && v.Y <= structureBounds.Y + structureBounds.Height + 0.001) &&
                        p.Points.Min(v => v.Y) <= top + 0.001 && p.Points.Max(v => v.Y) >= bottom - 0.001).ToArray();
                    Qa.Check(brackets.Any(p => p.Points.Max(v => v.X) <= left + 0.001), "행렬 전 행을 감싸는 왼쪽 괄호 없음");
                    Qa.Check(brackets.Any(p => p.Points.Min(v => v.X) >= right - 0.001), "행렬 전 행을 감싸는 오른쪽 괄호 없음");
                    break;
                }
            }
        }
    }
}
