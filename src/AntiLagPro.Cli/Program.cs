using AntiLagPro.Core;

// Маленький тестовый CLI для проверки движка БЕЗ интерфейса.
// Команды:
//   antilag status   - показать таймер + состояние твиков
//   antilag apply     - применить универсальные твики (флаг таймера + питание)
//   antilag undo      - откатить всё назад из бэкапа

Console.OutputEncoding = System.Text.Encoding.UTF8;
var engine = new TweakEngine();

string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

try
{
    switch (cmd)
    {
        case "status": ShowStatus(); break;
        case "apply":  DoApply();    break;
        case "undo":   DoUndo();     break;
        default:
            Console.WriteLine("Команды: status | apply | undo");
            break;
    }
}
catch (UnauthorizedAccessException ex)
{
    Console.WriteLine($"[ОШИБКА] {ex.Message} Запусти от администратора.");
    return 1;
}
catch (Exception ex)
{
    Console.WriteLine($"[ОШИБКА] {ex.Message}");
    return 1;
}
return 0;

void ShowStatus()
{
    var (cur, min, _) = (engine.Timer.CurrentMs, engine.Timer.MinMs, 0.0);
    Console.WriteLine("=== AntiLag-Pro :: статус ===");
    Console.WriteLine($"Админ-права: {(TweakEngine.IsElevated() ? "да" : "НЕТ")}");
    Console.WriteLine($"Timer Resolution: {cur:N4} ms (минимум возможный {min:N4} ms)");
    Console.WriteLine($"Бэкап: {BackupStore.Location}");
    Console.WriteLine();
    Console.WriteLine("Твики:");
    foreach (var s in engine.GetStatus())
    {
        string mark = s.IsApplied ? "[x]" : "[ ]";
        string reboot = s.Tweak.RequiresReboot ? " (нужна перезагрузка)" : "";
        Console.WriteLine($"  {mark} {s.Tweak.Name}{reboot}");
        Console.WriteLine($"        {s.Tweak.Description}");
    }
}

void DoApply()
{
    var ids = engine.Tweaks
        .Where(t => t.Tier == TweakTier.Universal)
        .Select(t => t.Id);

    engine.Apply(ids);
    engine.Timer.Start(); // демонстрация: держим 0.5 ms пока процесс жив
    Console.WriteLine("Универсальные твики применены.");
    Console.WriteLine($"Timer Resolution сейчас: {engine.Timer.CurrentMs:N4} ms");
    if (engine.RequiresRebootAfter)
        Console.WriteLine(">> Нужна ПЕРЕЗАГРУЗКА, чтобы флаг таймера заработал системно.");
    Console.WriteLine();
    ShowStatus();
}

void DoUndo()
{
    engine.Restore();
    Console.WriteLine("Откат выполнен — все изменения возвращены.");
    Console.WriteLine();
    ShowStatus();
}
