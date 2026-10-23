using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using AetherMath.Layout;

namespace AetherMath.Desktop;

/// <summary>읽기 전용 레이아웃 미리보기 표면입니다.</summary>
public sealed class MathOutputPreviewControl : Control
{
    public static readonly StyledProperty<LayoutResult?> LayoutProperty =
        AvaloniaProperty.Register<MathOutputPreviewControl, LayoutResult?>(nameof(Layout));

    private LayoutRenderResources? _renderResources;

    static MathOutputPreviewControl()
    {
        AffectsMeasure<MathOutputPreviewControl>(LayoutProperty);
        AffectsRender<MathOutputPreviewControl>(LayoutProperty);
    }

    public LayoutResult? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutProperty)
            ReleaseRenderResources();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseRenderResources();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = Layout;
        return layout is null
            ? new Size(1, 1)
            : new Size(Math.Max(1, layout.Width), Math.Max(1, layout.Height));
    }

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Layout is not LayoutResult layout)
            return;
        _renderResources ??= new LayoutRenderResources(layout);
        _renderResources.Draw(context);
    }

    private void ReleaseRenderResources()
    {
        _renderResources?.Dispose();
        _renderResources = null;
    }
}
