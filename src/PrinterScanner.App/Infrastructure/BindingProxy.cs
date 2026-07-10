using System.Windows;

namespace PrinterScanner.App.Infrastructure;

// Proxy Freezable: permite referenciar o DataContext do Window dentro de
// contextos com árvore visual separada (ContextMenu, Popup, DataTemplate).
// Uso: <infra:BindingProxy x:Key="Proxy" Data="{Binding}" />
//      Command="{Binding Source={StaticResource Proxy}, Path=Data.MinhaCommand}"
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
