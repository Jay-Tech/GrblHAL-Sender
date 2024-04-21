using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Xaml.Interactivity;

namespace GrbLHAL_Sender.Behaviors;

public class ScrollToEndBehavior : Behavior<ListBox>
{
    private bool _shouldScrollToEnd = true;
    private bool _scrolling;
    
    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is { })
        {
            AssociatedObject.SizeChanged += AssociatedObjectOnSizeChanged;
            AssociatedObject.TemplateApplied += AssociatedObjectOnTemplateApplied;
        }
    }

    private void AssociatedObjectOnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        var sw = e.NameScope.Get<ScrollViewer>("PART_ScrollViewer");
        sw.ScrollChanged += SwOnScrollChanged;
        
        // scroll into view when loaded
        AssociatedObject.ScrollIntoView(AssociatedObject.Items.Count - 1);
    }

    private void SwOnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sw && !_scrolling)
        {
            _shouldScrollToEnd = Math.Abs(sw.Offset.Y - sw.Extent.Height + sw.Viewport.Height) < 5; // need to define some px of tolerance here
        }
    }
    
    private void AssociatedObjectOnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_shouldScrollToEnd && AssociatedObject?.Items.Count > 0)
        {
            _scrolling = true;
            AssociatedObject.ScrollIntoView(AssociatedObject.Items.Count - 1);
            _scrolling = false;
        }
    }
}