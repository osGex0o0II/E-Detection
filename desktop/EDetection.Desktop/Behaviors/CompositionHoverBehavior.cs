using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.UI.ViewManagement;

namespace EDetection.Desktop.Behaviors;

public static class CompositionHoverBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(CompositionHoverBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly UISettings UiSettings = new();

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if (e.NewValue is true)
        {
            element.PointerEntered += OnPointerEntered;
            element.PointerExited += OnPointerExited;
            element.SizeChanged += OnSizeChanged;
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            ApplyVisual(element, isHovered: false);
            return;
        }

        element.PointerEntered -= OnPointerEntered;
        element.PointerExited -= OnPointerExited;
        element.SizeChanged -= OnSizeChanged;
        element.Loaded -= OnLoaded;
        element.Unloaded -= OnUnloaded;
        ResetVisual(element);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ApplyVisual(element, isHovered: false);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ResetVisual(element);
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            UpdateCenterPoint(element);
        }
    }

    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ApplyVisual(element, isHovered: true);
        }
    }

    private static void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ApplyVisual(element, isHovered: false);
        }
    }

    private static void ApplyVisual(FrameworkElement element, bool isHovered)
    {
        if (!UiSettings.AnimationsEnabled)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        UpdateCenterPoint(element);
        EnsureImplicitScaleAnimation(visual);
        visual.Scale = isHovered ? new Vector3(1.02f, 1.02f, 1f) : Vector3.One;
    }

    private static void ResetVisual(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.ImplicitAnimations = null;
        visual.Scale = Vector3.One;
    }

    private static void UpdateCenterPoint(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3(
            (float)(element.ActualWidth / 2),
            (float)(element.ActualHeight / 2),
            0f);
    }

    private static void EnsureImplicitScaleAnimation(Visual visual)
    {
        if (visual.ImplicitAnimations is not null)
        {
            return;
        }

        var compositor = visual.Compositor;
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Target = "Scale";
        animation.InsertExpressionKeyFrame(1f, "this.FinalValue");
        animation.Duration = TimeSpan.FromMilliseconds(140);

        var animations = compositor.CreateImplicitAnimationCollection();
        animations["Scale"] = animation;
        visual.ImplicitAnimations = animations;
    }
}
