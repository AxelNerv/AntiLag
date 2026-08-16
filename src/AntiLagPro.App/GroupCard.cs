using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AntiLagPro.Core;

namespace AntiLagPro.App;

/// <summary>
/// Карточка блока на экране раздела: название, описание и сколько твиков
/// внутри включено. Счётчик и полоса пересчитываются при каждом переключении.
/// </summary>
public sealed class GroupCard : INotifyPropertyChanged
{
    private readonly IReadOnlyList<TweakRow> _rows;

    public GroupCard(TweakGroup group, IReadOnlyList<TweakRow> rows)
    {
        Id = group.Id;
        Name = group.Name;
        Description = group.Description;
        _rows = rows;
        foreach (var r in rows) r.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TweakRow.IsSelected) or nameof(TweakRow.IsApplied)) Refresh();
        };
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public int Total => _rows.Count;

    private int Active => _rows.Count(r => r.IsSelected);

    public string Status => $"{Active} / {Total} ВКЛ";

    // Полоса — две колонки в долях, а не фиксированные пиксели: тогда при
    // «3 / 3 ВКЛ» она заполнена целиком независимо от ширины карточки.
    public GridLength FillStar => new(Active, GridUnitType.Star);
    public GridLength RestStar => new(Math.Max(Total - Active, 0), GridUnitType.Star);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(FillStar));
        OnPropertyChanged(nameof(RestStar));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
