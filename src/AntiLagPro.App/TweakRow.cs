using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AntiLagPro.Core;

namespace AntiLagPro.App;

/// <summary>
/// Обёртка над ITweak для привязки к UI (одна строка списка).
/// IsSelected = чего хочет пользователь, IsApplied = что есть в системе сейчас.
/// Витрина (блок, ключ реестра, бейдж, предупреждение) берётся из TweakCatalog.
/// </summary>
public sealed class TweakRow : INotifyPropertyChanged
{
    private readonly ITweak _tweak;
    private readonly TweakInfo _info;

    public TweakRow(ITweak tweak, bool applied)
    {
        _tweak = tweak;
        _info = TweakCatalog.For(tweak.Id);
        _isApplied = applied;
        _isSelected = applied; // стартовое состояние = текущее в системе
    }

    public string Id => _tweak.Id;
    public string Name => _tweak.Name;
    public string Description => _tweak.Description;
    public bool RequiresReboot => _tweak.RequiresReboot;

    /// <summary>Блок, в котором показывается твик (см. TweakCatalog).</summary>
    public string GroupId => _info.GroupId;

    /// <summary>Что именно меняется — ключ реестра или команда.</summary>
    public string Target => _info.Target;

    /// <summary>Короткая подпись под названием: первое предложение описания.</summary>
    public string Summary
    {
        get
        {
            var d = Description;
            int dot = d.IndexOf(". ", StringComparison.Ordinal);
            return dot > 0 ? d[..dot] : d;
        }
    }

    public string? Tag => TweakCatalog.TagFor(_tweak);
    public Visibility TagVisibility => Tag is null ? Visibility.Collapsed : Visibility.Visible;

    public string? Warning => _info.Warning;
    public Visibility WarningVisibility => Warning is null ? Visibility.Collapsed : Visibility.Visible;

    private bool _isApplied;
    public bool IsApplied
    {
        get => _isApplied;
        set { if (_isApplied != value) { _isApplied = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    /// <summary>Развёрнуто ли подробное описание.</summary>
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetailVisibility));
            OnPropertyChanged(nameof(Chevron));
        }
    }

    public Visibility DetailVisibility => _isExpanded ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Стрелка разворота (Segoe Fluent Icons).</summary>
    public string Chevron => _isExpanded ? "\uE70E" : "\uE70D";

    /// <summary>Изменено ли относительно системы — для счётчика «N изменений не применено».</summary>
    public bool IsPending => _isSelected != _isApplied;

    public string StatusText => _isApplied ? "включено" : "выключено";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
