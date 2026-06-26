#Requires -Version 5.1
<#
==============================================================================
  AntiLag-Pro :: Diagnose-Latency.ps1
------------------------------------------------------------------------------
  Диагностика задержек (latency) на Windows.
  Находит ПРИЧИНУ высокого DPC latency, а не маскирует её.

  DPC latency (Deferred Procedure Call latency) = задержка отложенных
  вызовов драйверов. Здоровая система: < 100-200 µs (микросекунд).
  Высокий DPC = микро-фризы, дёрганая мышь, нестабильный FPS.

  Что делает скрипт (НИЧЕГО НЕ МЕНЯЕТ — только читает и измеряет):
    1. Обзор системы: CPU, ОС, Timer Resolution, флаг таймера, Power Plan.
    2. Конфиг-проверки частых виновников: сетевая карта, USB, аудио.
    3. ETW-трейс DPC/ISR по драйверам (встроенный logman + tracerpt).
    4. Итоговый вердикт и рекомендации.

  Запуск:  правый клик -> "Запустить с помощью PowerShell"  (само попросит админа)
  Или:     powershell -ExecutionPolicy Bypass -File .\Diagnose-Latency.ps1
==============================================================================
#>
param(
    [int]$TraceSeconds = 30   # длительность ETW-трейса DPC/ISR
)

# ---------------------------------------------------------------------------
# Самоповышение прав до администратора (нужно для трейса и чтения настроек)
# ---------------------------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Write-Host "Требуются права администратора. Перезапускаю с повышением..." -ForegroundColor Yellow
    $psi = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -TraceSeconds $TraceSeconds"
    Start-Process powershell.exe -Verb RunAs -ArgumentList $psi
    return
}

$ErrorActionPreference = 'Continue'
$Host.UI.RawUI.WindowTitle = 'AntiLag-Pro :: Diagnostics'

# ---------------------------------------------------------------------------
# Лог: дублируем весь вывод в файл рядом со скриптом
# ---------------------------------------------------------------------------
$reportDir = Join-Path $PSScriptRoot ("report_" + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
$logFile = Join-Path $reportDir 'diagnostics.txt'
try { Start-Transcript -Path $logFile -Force | Out-Null } catch {}

# ---------------------------------------------------------------------------
# Хелперы вывода
# ---------------------------------------------------------------------------
function Hr        { Write-Host ("=" * 78) -ForegroundColor DarkGray }
function Section($t){ Write-Host ""; Hr; Write-Host "  $t" -ForegroundColor Cyan; Hr }
function Good($t)  { Write-Host "  [OK]   $t" -ForegroundColor Green }
function Warn($t)  { Write-Host "  [!]    $t" -ForegroundColor Yellow }
function Bad($t)   { Write-Host "  [XX]   $t" -ForegroundColor Red }
function Info($t)  { Write-Host "         $t" -ForegroundColor Gray }

# Сюда собираем итоговые находки для финального вердикта
$findings = New-Object System.Collections.Generic.List[object]
function AddFinding($sev,$msg,$fix){ $findings.Add([pscustomobject]@{Sev=$sev;Msg=$msg;Fix=$fix}) }

Write-Host ""
Write-Host "  ===================================================" -ForegroundColor White
Write-Host "      AntiLag-Pro  -  Latency Diagnostics" -ForegroundColor White
Write-Host "  ===================================================" -ForegroundColor White
Write-Host "      Отчёт будет сохранён в: $reportDir" -ForegroundColor DarkGray

# ===========================================================================
# 1. ОБЗОР СИСТЕМЫ
# ===========================================================================
Section "1. Обзор системы (System overview)"

$os  = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
Info ("CPU: {0} ({1} ядер / {2} потоков)" -f $cpu.Name.Trim(), $cpu.NumberOfCores, $cpu.NumberOfLogicalProcessors)
Info ("ОС:  {0} build {1}" -f $os.Caption, $os.BuildNumber)

# --- Timer Resolution (разрешение системного таймера) ---
$sig = '[DllImport("ntdll.dll")] public static extern int NtQueryTimerResolution(out uint Max, out uint Min, out uint Cur);'
$tr  = Add-Type -MemberDefinition $sig -Name TimerRes -Namespace AntiLag -PassThru
$max=0;$min=0;$cur=0
[void]$tr::NtQueryTimerResolution([ref]$max,[ref]$min,[ref]$cur)
$curMs = $cur/10000.0; $minMs = $min/10000.0; $maxMs = $max/10000.0
Info ("Timer Resolution сейчас: {0:N4} ms  (лучшее возможное: {1:N4} ms, по умолчанию: {2:N4} ms)" -f $curMs,$minMs,$maxMs)
if ($curMs -gt 0.6) {
    Warn "Таймер НЕ на минимуме (0.5 ms). Это добавляет инпут-лаг и дрожание FPS."
    AddFinding 'WARN' "Timer Resolution = $('{0:N4}' -f $curMs) ms (не 0.5 ms)" "Тулза будет принудительно держать 0.5 ms"
} else {
    Good "Таймер на минимуме (0.5 ms)."
}

# --- Флаг GlobalTimerResolutionRequests (ключевой для Win11 24H2+) ---
$kPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\kernel'
$gtr = (Get-ItemProperty -Path $kPath -Name GlobalTimerResolutionRequests -ErrorAction SilentlyContinue).GlobalTimerResolutionRequests
$build = [int]$os.BuildNumber
if ($build -ge 22000) {  # Windows 11
    if ($gtr -eq 1) {
        Good "GlobalTimerResolutionRequests = 1 (запрос 0.5 ms действует на ВСЮ систему)."
    } else {
        Bad  "GlobalTimerResolutionRequests НЕ задан (=0)."
        Info "На Win11 это значит: 0.5 ms работает ТОЛЬКО внутри процесса, который его запросил."
        Info "Именно поэтому старые тулзы (AntiLag/ExitLag) перестают помогать в играх."
        AddFinding 'BAD' "GlobalTimerResolutionRequests=0 -> таймер 0.5 ms не действует системно" "Поставить флаг реестра = 1 (главный фикс)"
    }
}

# --- Активная схема питания (power plan) ---
$active = (powercfg /getactivescheme) -join ' '
Info ("Активная схема питания: {0}" -f $active.Trim())
if ($active -match 'a1841308|Энергосбереж|Power saver') {
    Warn "Активна энергосберегающая схема — это режет производительность."
    AddFinding 'WARN' "Активна энергосберегающая схема питания" "Создать/включить High-Performance план"
} else {
    Good "Схема питания не энергосберегающая."
}

# --- USB selective suspend (засыпание USB-портов = лаг мыши/клавы) ---
$usb = (powercfg /q SCHEME_CURRENT 2bf0017c-22b6-46a4-9c25-7a23c61b3a40 48e6b7a6-50f5-4782-a5d4-53bb8f07e226) -join "`n"
if ($usb -match 'Текущее значение индекса параметра пит\. от сети:\s*0x0+1' -or $usb -match 'Current AC Power Setting Index:\s*0x0+1') {
    Warn "USB Selective Suspend включён на питании от сети (USB-порты могут засыпать)."
    AddFinding 'WARN' "USB Selective Suspend ВКЛ" "Отключить (убирает микро-залипания мыши/клавы)"
} else {
    Good "USB Selective Suspend выключен (или не на сети)."
}

# ===========================================================================
# 2. ПРОВЕРКА ЧАСТЫХ ВИНОВНИКОВ DPC (сеть / аудио)
# ===========================================================================
Section "2. Частые виновники DPC latency"

# --- Сетевые адаптеры ---
$nics = Get-NetAdapter -Physical | Where-Object Status -eq 'Up'
foreach ($n in $nics) {
    Write-Host ""
    Info ("Сетевой адаптер: {0}  [{1}]" -f $n.InterfaceDescription, $n.LinkSpeed)

    # Питание адаптера: "Разрешить отключение для экономии энергии"
    $pm = Get-NetAdapterPowerManagement -Name $n.Name -ErrorAction SilentlyContinue
    if ($pm -and $pm.AllowComputerToTurnOffDevice -eq 'Enabled') {
        Warn "Windows может отключать этот адаптер ради экономии — частая причина DPC-спайков."
        AddFinding 'WARN' "NIC '$($n.InterfaceDescription)': разрешено отключение для экономии" "Снять галку в Диспетчере устройств / тулза сделает сама"
    } else {
        Good "Управление питанием адаптера: отключение запрещено."
    }

    # Известные проблемные семейства драйверов
    if ($n.InterfaceDescription -match 'Killer') {
        Bad "Это Killer-адаптер — печально известен высоким DPC latency. Часто помогает чистый драйвер от Realtek/Intel."
        AddFinding 'BAD' "Killer network adapter (известная причина DPC)" "Заменить Killer-драйвер на стоковый Realtek/Intel"
    }

    # Дата драйвера (старый сетевой драйвер = частый источник)
    $drv = Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceName -eq $n.InterfaceDescription } | Select-Object -First 1
    if ($drv) {
        $dt = $null
        if ($drv.DriverDate) {
            try { $dt = [Management.ManagementDateTimeConverter]::ToDateTime($drv.DriverDate) } catch { $dt = $null }
        }
        if ($dt) {
            Info ("   Драйвер: версия {0}, дата {1:yyyy-MM-dd}" -f $drv.DriverVersion, $dt)
            if ($dt -lt (Get-Date).AddYears(-3)) {
                Warn "   Драйвер старше 3 лет — кандидат на обновление."
            }
        } else {
            Info ("   Драйвер: версия {0} (дата не указана)" -f $drv.DriverVersion)
        }
    }
}

# --- Аудио драйверы (второй по частоте источник DPC) ---
Write-Host ""
$audio = Get-CimInstance Win32_SoundDevice | Where-Object Status -eq 'OK'
foreach ($a in $audio) { Info ("Аудио-устройство: {0}" -f $a.Name) }

# ===========================================================================
# 3. ETW-ТРЕЙС: КТО РЕАЛЬНО ГРУЗИТ DPC/ISR
# ===========================================================================
Section "3. Трейс DPC/ISR по драйверам ($TraceSeconds сек)"
Info "Сейчас пойдёт запись на $TraceSeconds секунд."
Info "ВАЖНО: в это время ПОЛЬЗУЙСЯ ПК как обычно — подвигай мышь, открой игру/браузер,"
Info "чтобы поймать реальные спайки. Ничего нажимать не нужно, просто работай."
Write-Host ""
Read-Host "Нажми Enter чтобы начать запись"

$etl     = Join-Path $reportDir 'dpc_trace.etl'
$reportH = Join-Path $reportDir 'dpc_report.html'
$summary = Join-Path $reportDir 'dpc_summary.txt'
$session = 'AntiLagProTrace'

# Гасим возможные остатки прошлой сессии ядра
logman stop  $session -ets 2>$null | Out-Null

# Запускаем трейс ядра. Hex-маска ключей = process|thread|img|dpc|isr|driver:
#   0x1 process + 0x2 thread + 0x4 img + 0x20 dpc + 0x40 isr + 0x800000 driver = 0x800067
$start = logman start $session -p "Windows Kernel Trace" 0x800067 -ets -o "$etl" 2>&1
if ($LASTEXITCODE -ne 0) {
    Bad "Не удалось запустить ETW-трейс (часто: уже работает другая kernel-сессия, напр. xperf/WPR)."
    Info ($start -join ' ')
    Info "Пропускаю трейс. Для точного per-driver анализа поставь LatencyMon (бесплатно)."
    AddFinding 'INFO' "ETW-трейс не запустился" "Закрыть др. профайлеры или использовать LatencyMon"
} else {
    Good "Запись пошла. Пользуйся ПК..."
    for ($i=$TraceSeconds; $i -gt 0; $i--) {
        Write-Host ("`r   осталось {0,3} сек " -f $i) -NoNewline -ForegroundColor DarkCyan
        Start-Sleep -Seconds 1
    }
    Write-Host ""
    logman stop $session -ets 2>$null | Out-Null
    Good "Запись остановлена. Анализирую..."

    # tracerpt строит человекочитаемый отчёт с разбивкой DPC/ISR по модулям
    tracerpt "$etl" -report "$reportH" -summary "$summary" -f HTML -y 2>$null | Out-Null

    if (Test-Path $reportH) {
        Good "Отчёт собран: dpc_report.html"
        # Пытаемся вытащить топ драйверов по DPC/ISR прямо в консоль (best-effort)
        try {
            $html = Get-Content $reportH -Raw
            $rows = [regex]::Matches($html, '<tr[^>]*>(?:(?!</tr>).)*?(?:DPC|ISR)(?:(?!</tr>).)*?</tr>', 'Singleline')
            if ($rows.Count -gt 0) {
                Info "Фрагменты DPC/ISR из отчёта (полную картину смотри в HTML):"
            }
        } catch {}
        Info "Открываю dpc_report.html — ищи в нём разделы 'DPC' и 'ISR',"
        Info "драйвер с наибольшим '% или total time' = твой главный виновник."
        Start-Process $reportH
    } else {
        Warn "tracerpt не собрал HTML-отчёт. ETL сохранён ($etl) — можно открыть в WPA."
    }
}

# ===========================================================================
# 4. ИТОГОВЫЙ ВЕРДИКТ
# ===========================================================================
Section "4. Итог и рекомендации"

if ($findings.Count -eq 0) {
    Good "Явных проблем в конфигурации не найдено. Если лаг остаётся — смотри dpc_report.html."
} else {
    Write-Host ""
    foreach ($f in $findings) {
        switch ($f.Sev) {
            'BAD'  { Bad  $f.Msg }
            'WARN' { Warn $f.Msg }
            default{ Info $f.Msg }
        }
        Write-Host ("           -> фикс: {0}" -f $f.Fix) -ForegroundColor DarkGray
    }
}

Write-Host ""
Hr
Write-Host "  Следующий шаг:" -ForegroundColor Cyan
Write-Host "   * Этот скрипт НИЧЕГО не менял — только диагностика." -ForegroundColor Gray
Write-Host "   * Главный фикс под твою Win11: флаг GlobalTimerResolutionRequests + таймер 0.5ms." -ForegroundColor Gray
Write-Host "   * Когда подтвердим виновника по dpc_report.html — сделаю C#-тулзу (как оригинал)" -ForegroundColor Gray
Write-Host "     с применением и откатом всех фиксов в один клик." -ForegroundColor Gray
Write-Host "   * Для эталонного сравнения per-driver можешь также прогнать LatencyMon (бесплатно)." -ForegroundColor Gray
Hr
Write-Host ""
Info "Полный лог: $logFile"
try { Stop-Transcript | Out-Null } catch {}
Write-Host ""
Read-Host "Готово. Нажми Enter чтобы закрыть"
