using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Application = System.Windows.Application;
using ListView = System.Windows.Controls.ListView;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Binding = System.Windows.Data.Binding;
using SelectionMode = System.Windows.Controls.SelectionMode;

namespace IgezziGuard.Shell;

/// <summary>One column of a <see cref="DataList{T}"/>: a header, a width and how a row's text is read.</summary>
internal sealed record Column<T>(string Header, double Width, Func<T, string> Text);

/// <summary>
/// A dark, sortable, virtualized table. The page owns the sorting (it knows what each column means); the list reports which
/// header was clicked and shows an arrow on the sorted column. Selection and double-click are plain events.
/// </summary>
internal sealed class DataList<T> where T : class
{
    private sealed class Text(Func<T, string> read) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is T item ? read(item) : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private readonly IReadOnlyList<Column<T>> columns;
    private readonly GridView grid = new();
    internal ListView View { get; } = new();
    /// <summary>The header that was clicked (index into the columns).</summary>
    internal event Action<int>? HeaderClicked;
    internal event Action? SelectionChanged;
    internal event Action<T>? Activated;
    internal IReadOnlyList<T> Selected => View.SelectedItems.Cast<T>().ToList();
    internal int Count => View.Items.Count;

    internal DataList(IReadOnlyList<Column<T>> columns, bool multiSelect, string name)
    {
        this.columns = columns;
        grid.ColumnHeaderContainerStyle = (Style)Application.Current.FindResource("DataHeader");
        foreach (var column in columns)
            grid.Columns.Add(new GridViewColumn { Header = column.Header, Width = column.Width, DisplayMemberBinding = new Binding { Converter = new Text(column.Text) } });
        View.View = grid; View.Style = (Style)Application.Current.FindResource("DataListView");
        View.SelectionMode = multiSelect ? SelectionMode.Extended : SelectionMode.Single;
        System.Windows.Automation.AutomationProperties.SetName(View, name);
        VirtualizingPanel.SetIsVirtualizing(View, true); VirtualizingPanel.SetVirtualizationMode(View, VirtualizationMode.Recycling);
        View.SelectionChanged += (_, _) => SelectionChanged?.Invoke();
        View.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((_, e) => {
            if (e.OriginalSource is GridViewColumnHeader { Column: { } column } header && header.Role != GridViewColumnHeaderRole.Padding) HeaderClicked?.Invoke(grid.Columns.IndexOf(column));
        }));
        View.MouseDoubleClick += (_, e) => { if (e.OriginalSource is FrameworkElement { DataContext: T item }) Activated?.Invoke(item); };
    }

    /// <summary>Replaces the rows. Large lists stay fast because only the visible rows are drawn.</summary>
    internal void SetItems(IReadOnlyList<T> items) { View.ItemsSource = items; }

    /// <summary>Shows a sort arrow on one column (or none when <paramref name="column"/> is negative).</summary>
    internal void ShowSort(int column, bool descending)
    {
        for (int i = 0; i < columns.Count; i++) grid.Columns[i].Header = columns[i].Header + (i == column ? descending ? "  ↓" : "  ↑" : "");
    }
}
