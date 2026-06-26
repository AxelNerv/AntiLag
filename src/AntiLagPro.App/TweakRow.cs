using System.ComponentModel;
using System.Runtime.CompilerServices;
using AntiLagPro.Core;

namespace AntiLagPro.App;

/// <summary>
/// Обёртка над ITweak для привязки к UI (одна строка-галочка в списке).
/// IsSelected = чего хочет пользователь, IsApplied = что есть в системе сейчас.
/// </summary>
public sealed class TweakRow : INotifyPropertyChanged
{
    private readonly ITweak _tweak;

    public TweakRow(ITweak tweak, bool applied)
    {
        _tweak = tweak;
        _isApplied = applied;
        _isSelected = applied; // стартовое состояние галочки = текущее в системе
    }

    public string Id => _tweak.Id;
    public string Name => _tweak.Name;
    public string Description => _tweak.Description;
    public bool RequiresReboot => _tweak.RequiresReboot;

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

    public string StatusText => _isApplied ? "включено" : "выключено";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
