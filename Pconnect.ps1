# ============================================================
#  Pconnect  —  универсальный помощник VPN (WPF)
#  Настройки — в config.json рядом с программой.
# ============================================================

# ---------- само-повышение прав ----------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden `
            -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"')
    } catch { }
    exit
}

# ---------- скрыть консоль ----------
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Native {
    public static class Win32Base {
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    }
}
'@
[Native.Win32Base]::ShowWindow([Native.Win32Base]::GetConsoleWindow(), 0) | Out-Null

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

# ---------- загрузка конфигурации ----------
$configPath = Join-Path $PSScriptRoot 'config.json'
$config = $null
if (Test-Path $configPath) {
    try { $config = Get-Content -Raw -Encoding UTF8 $configPath | ConvertFrom-Json } catch { $config = $null }
}
if (-not $config) { $config = [pscustomobject]@{ defaultProfile = ''; profiles = @() } }

$script:profiles = @($config.profiles)
$script:profile  = $null

# ---------- пути/ресурсы ----------
$appDir  = $PSScriptRoot
$pngPath = Join-Path $appDir 'assets\Pconnect.png'
$logoUri = if (Test-Path $pngPath) { New-Object System.Uri($pngPath) } else { $null }

# ---------- звуки ----------
function New-Wav([double[]]$freqs, [double]$dur = 0.16, [double]$vol = 0.4) {
    $rate = 32000; $nPer = [int]($rate * $dur); $gap = [int]($rate * 0.015); $total = ($nPer + $gap) * $freqs.Count
    $dataLen = $total * 2
    $buf = New-Object byte[] (44 + $dataLen)
    [Text.Encoding]::ASCII.GetBytes('RIFF').CopyTo($buf, 0)
    [BitConverter]::GetBytes([int](36 + $dataLen)).CopyTo($buf, 4)
    [Text.Encoding]::ASCII.GetBytes('WAVE').CopyTo($buf, 8)
    [Text.Encoding]::ASCII.GetBytes('fmt ').CopyTo($buf, 12)
    [BitConverter]::GetBytes([int]16).CopyTo($buf, 16)
    [BitConverter]::GetBytes([int16]1).CopyTo($buf, 20)
    [BitConverter]::GetBytes([int16]1).CopyTo($buf, 22)
    [BitConverter]::GetBytes([int]$rate).CopyTo($buf, 24)
    [BitConverter]::GetBytes([int]($rate * 2)).CopyTo($buf, 28)
    [BitConverter]::GetBytes([int16]2).CopyTo($buf, 32)
    [BitConverter]::GetBytes([int16]16).CopyTo($buf, 34)
    [Text.Encoding]::ASCII.GetBytes('data').CopyTo($buf, 36)
    [BitConverter]::GetBytes([int]$dataLen).CopyTo($buf, 40)
    $idx = 44
    foreach ($f in $freqs) {
        for ($j = 0; $j -lt $nPer; $j++) {
            $t = $j / $rate
            $a = [Math]::Min(1.0, $t / 0.004)
            $d = [Math]::Exp(-3.0 * $t / $dur)
            $v = [Math]::Sin(2 * [Math]::PI * $f * $t) * $a * $d * $vol
            $s = [int16]([Math]::Max(-32767, [Math]::Min(32767, $v * 32767)))
            [BitConverter]::GetBytes($s).CopyTo($buf, $idx)
            $idx += 2
        }
        $idx += $gap * 2
    }
    return ,$buf
}

function Ensure-Sounds {
    $dir = Join-Path $appDir 'sounds'
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $script:connectWav = Join-Path $dir 'connect.wav'
    $script:disconnectWav = Join-Path $dir 'disconnect.wav'
    if (-not (Test-Path $script:connectWav)) { [IO.File]::WriteAllBytes($script:connectWav, (New-Wav @(659.25, 783.99, 1046.50) 0.14 0.35)) }
    if (-not (Test-Path $script:disconnectWav)) { [IO.File]::WriteAllBytes($script:disconnectWav, (New-Wav @(783.99, 523.25) 0.16 0.30)) }
}

function Play-Sound([string]$which) {
    try {
        $f = if ($which -eq 'connect') { $script:connectWav } else { $script:disconnectWav }
        if ($f -and (Test-Path $f)) { (New-Object System.Media.SoundPlayer($f)).Play() }
    } catch { }
}

Ensure-Sounds

# ---------- кисти ----------
function New-Brush([string]$hex) {
    New-Object System.Windows.Media.SolidColorBrush(([System.Windows.Media.ColorConverter]::ConvertFromString($hex)))
}
$bGreen  = New-Brush '#16A34A'
$bGreenL = New-Brush '#5EE08A'
$bRed    = New-Brush '#DC2626'
$bGray   = New-Brush '#9CA3AF'
$bGrayL  = New-Brush '#D7DBE2'
$bViolet = New-Brush '#6D5CF7'
$bVioletL= New-Brush '#B4A6FF'
$bDark   = New-Brush '#1B2430'

# ---------- XAML ----------
$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Pconnect" Width="460" Height="660"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize">
  <Window.Resources>
    <Style x:Key="PillButton" TargetType="Button">
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Foreground" Value="White"/>
      <Setter Property="FontWeight" Value="Bold"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border CornerRadius="26" Background="{TemplateBinding Background}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
          <Setter Property="Opacity" Value="0.88"/>
        </Trigger>
        <Trigger Property="IsPressed" Value="True">
          <Setter Property="Opacity" Value="0.72"/>
        </Trigger>
      </Style.Triggers>
    </Style>
    <Style x:Key="GhostButton" TargetType="Button">
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Background" Value="#FFFFFFFF"/>
      <Setter Property="Foreground" Value="#1B2430"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border CornerRadius="18" Background="{TemplateBinding Background}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
          <Setter Property="Background" Value="#E9EDF6"/>
        </Trigger>
        <Trigger Property="IsPressed" Value="True">
          <Setter Property="Background" Value="#DCE2F0"/>
        </Trigger>
      </Style.Triggers>
    </Style>
    <Style x:Key="WinButton" TargetType="Button">
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Width" Value="30"/>
      <Setter Property="Height" Value="26"/>
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="Foreground" Value="#7B8496"/>
      <Setter Property="Background" Value="#00FFFFFF"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border CornerRadius="12" Background="{TemplateBinding Background}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
          <Setter Property="Background" Value="#E4E8F1"/>
        </Trigger>
      </Style.Triggers>
    </Style>
    <Style x:Key="CloseButton" TargetType="Button">
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Width" Value="30"/>
      <Setter Property="Height" Value="26"/>
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="Foreground" Value="#7B8496"/>
      <Setter Property="Background" Value="#00FFFFFF"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border CornerRadius="12" Background="{TemplateBinding Background}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
          <Setter Property="Background" Value="#DC2626"/>
          <Setter Property="Foreground" Value="White"/>
        </Trigger>
      </Style.Triggers>
    </Style>
  </Window.Resources>
  <Grid Margin="12">
    <Border CornerRadius="24">
      <Border.Background>
        <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
          <GradientStop Color="#F6F7FC" Offset="0"/>
          <GradientStop Color="#E8EBF5" Offset="1"/>
        </LinearGradientBrush>
      </Border.Background>
      <Border.Effect>
        <DropShadowEffect BlurRadius="26" ShadowDepth="0" Opacity="0.20" Color="#000000"/>
      </Border.Effect>
      <Grid>
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="0.2*"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="*" MinHeight="60"/>
        </Grid.RowDefinitions>

        <StackPanel x:Name="HeaderPanel" Grid.Row="0" Margin="0,20,0,14" Background="Transparent">
          <Image x:Name="Logo" Width="58" Height="58" HorizontalAlignment="Center"/>
          <TextBlock Text="Pconnect" FontSize="21" FontWeight="Bold" Foreground="#1B2430" HorizontalAlignment="Center" Margin="0,10,0,0"/>
          <TextBlock Text="Универсальный помощник VPN" FontSize="12" Foreground="#7B8496" HorizontalAlignment="Center" Margin="0,3,0,0"/>
        </StackPanel>

        <Border Grid.Row="1" Margin="16,0,16,0" CornerRadius="20" Background="#FFFFFFFF">
          <Border.Effect>
            <DropShadowEffect BlurRadius="16" ShadowDepth="2" Opacity="0.10" Color="#000000"/>
          </Border.Effect>
          <StackPanel Margin="22,18,22,18">
            <ComboBox x:Name="ProfileBox" Height="34" FontSize="12" Padding="10,0" Margin="0,0,0,12" Background="#FFFFFFFF"/>
            <StackPanel Orientation="Horizontal">
              <Ellipse x:Name="Dot" Width="16" Height="16" VerticalAlignment="Center" Fill="#9CA3AF"/>
              <TextBlock x:Name="StatusText" Text="Не подключено" FontSize="16" FontWeight="Bold" Foreground="#1B2430" Margin="12,0,0,0" VerticalAlignment="Center"/>
            </StackPanel>
            <TextBlock x:Name="IpText" Text="Ваш IP: —" FontSize="12" Foreground="#7B8496" Margin="0,8,0,0"/>
            <Button x:Name="PowerBtn" Content="Включить VPN" Style="{StaticResource PillButton}" Height="52" Margin="0,18,0,0" FontSize="15" Background="#16A34A"/>
            <ProgressBar x:Name="Prog" Height="5" Margin="0,12,0,0" IsIndeterminate="True" Visibility="Collapsed" Foreground="#6D5CF7"/>
          </StackPanel>
        </Border>

        <TextBlock Grid.Row="2" Text="После подключения просто откройте любой браузер" FontSize="11" Foreground="#7B8496" HorizontalAlignment="Center" Margin="0,12,0,0"/>

        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,14,0,0">
          <Button x:Name="HappBtn" Content="Открыть VPN" Style="{StaticResource GhostButton}" Width="150" Height="40" FontSize="12"/>
          <Button x:Name="IpBtn" Content="Проверить IP" Style="{StaticResource GhostButton}" Width="150" Height="40" FontSize="12" Margin="12,0,0,0"/>
        </StackPanel>

        <GridSplitter Grid.Row="5" Height="8" HorizontalAlignment="Stretch" VerticalAlignment="Center"
                      ResizeDirection="Rows" ResizeBehavior="PreviousAndNext" Background="Transparent" Cursor="SizeNS"/>

        <Border Grid.Row="6" Margin="16,4,16,16" CornerRadius="14" Background="#FFFFFFFF">
          <Grid Margin="14,10,14,10">
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="*"/>
            </Grid.RowDefinitions>
            <TextBlock Text="Журнал" FontSize="10" Foreground="#9AA3B2"/>
            <TextBox x:Name="LogBox" Grid.Row="1" IsReadOnly="True" BorderThickness="0" Background="Transparent" Foreground="#1B2430" FontFamily="Consolas" FontSize="10" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto" Margin="0,4,0,0"/>
          </Grid>
        </Border>

        <StackPanel Grid.Row="0" Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,6,16,0">
          <Button x:Name="MinBtn" Content="—" Style="{StaticResource WinButton}"/>
          <Button x:Name="CloseBtn" Content="✕" Style="{StaticResource CloseButton}" Margin="2,0,0,0"/>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Window>
'@

$window = [System.Windows.Markup.XamlReader]::Parse($xaml)

if ($logoUri) {
    try {
        $bmp = New-Object System.Windows.Media.Imaging.BitmapImage($logoUri)
        $window.FindName('Logo').Source = $bmp
        $window.Icon = $bmp
    } catch { }
}

$Logo       = $window.FindName('Logo')
$ProfileBox = $window.FindName('ProfileBox')
$Dot        = $window.FindName('Dot')
$StatusText = $window.FindName('StatusText')
$IpText     = $window.FindName('IpText')
$PowerBtn   = $window.FindName('PowerBtn')
$Prog       = $window.FindName('Prog')
$LogBox     = $window.FindName('LogBox')
$HappBtn    = $window.FindName('HappBtn')
$IpBtn      = $window.FindName('IpBtn')
$MinBtn     = $window.FindName('MinBtn')
$CloseBtn   = $window.FindName('CloseBtn')
$HeaderPanel= $window.FindName('HeaderPanel')

# ---------- состояние ----------
$script:state = 'off'
$script:animPhase = 0

function Wpf-DoEvents {
    try { [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([System.Windows.Threading.DispatcherPriority]::Background, [Action]{}) } catch { }
}

function Write-Log([string]$msg) {
    $line = '{0:HH:mm:ss}  {1}' -f (Get-Date), $msg
    $LogBox.AppendText($line + "`r`n")
    $LogBox.ScrollToEnd()
    Wpf-DoEvents
}

function Get-ProfileProp([string]$name) {
    if (-not $script:profile) { return $null }
    return $script:profile.$name
}

function Test-TunnelUp {
    $ad = Get-ProfileProp 'adapter'
    if (-not $ad) { return $false }
    $a = Get-NetAdapter -Name $ad -ErrorAction SilentlyContinue
    return ($a -and $a.Status -eq 'Up')
}

function Invoke-Action([string]$a) {
    if (-not $a) { return }
    if ($a -match '://') { Start-Process $a }
    else { Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $a -WindowStyle Hidden }
}

function Set-Profile([string]$name) {
    $script:profile = $script:profiles | Where-Object { $_.name -eq $name } | Select-Object -First 1
    if (-not $script:profile -and $script:profiles.Count -gt 0) { $script:profile = $script:profiles[0] }
    Update-Status
}

function Update-Status {
    if ($script:profile -and (Test-TunnelUp)) {
        $script:state = 'on'
        $StatusText.Text = 'Подключено'
        $StatusText.Foreground = $bGreen
        $PowerBtn.Content = 'Выключить VPN'
        $PowerBtn.Background = $bRed
    } else {
        $script:state = 'off'
        $StatusText.Text = 'Не подключено'
        $StatusText.Foreground = $bDark
        $PowerBtn.Content = 'Включить VPN'
        $PowerBtn.Background = $bGreen
    }
    $Prog.Visibility = 'Collapsed'
    Wpf-DoEvents
}

function Set-Working([string]$label) {
    $script:state = $label
    $Prog.Visibility = 'Visible'
    $StatusText.Foreground = $bViolet
    $StatusText.Text = if ($label -eq 'connecting') { 'Подключение' } else { 'Отключение' }
    Wpf-DoEvents
}

function Stop-Interfering {
    $svcs = @(Get-ProfileProp 'interferingServices')
    $procs = @(Get-ProfileProp 'interferingProcesses')
    if (-not $svcs -and -not $procs) { Write-Log 'В профиле нет «мешающих» программ.'; return }
    Write-Log 'Отключаю мешающие программы…'
    foreach ($name in $svcs) {
        if (-not $name) { continue }
        $s = Get-Service -Name $name -ErrorAction SilentlyContinue
        if (-not $s) { Write-Log "Служба $name не найдена."; continue }
        Write-Log "Служба: $($s.DisplayName) ($($s.Name)) — $($s.Status)"
        try { Stop-Service -Name $name -Force -ErrorAction Stop; Write-Log '   остановлена' }
        catch { Write-Log "   не удалось остановить: $($_.Exception.Message)" }
        try { Set-Service -Name $name -StartupType Disabled -ErrorAction Stop; Write-Log '   автозапуск отключён' }
        catch { Write-Log "   не удалось отключить автозапуск: $($_.Exception.Message)" }
    }
    foreach ($name in $procs) {
        if (-not $name) { continue }
        $p = Get-Process -Name $name -ErrorAction SilentlyContinue
        if ($p) { $p | Stop-Process -Force -ErrorAction SilentlyContinue; Write-Log "Процесс $name завершён" }
    }
}

function Restore-Interfering {
    $svcs = @(Get-ProfileProp 'interferingServices')
    if (-not $svcs) { Write-Log 'Нет служб для восстановления.'; return }
    Write-Log 'Возвращаю мешающие программы…'
    foreach ($name in $svcs) {
        if (-not $name) { continue }
        $s = Get-Service -Name $name -ErrorAction SilentlyContinue
        if (-not $s) { continue }
        Write-Log "Служба: $($s.DisplayName) ($($s.Name))"
        try { Set-Service -Name $name -StartupType Automatic -ErrorAction Stop; Write-Log '   автозапуск включён' }
        catch { Write-Log "   не удалось включить автозапуск: $($_.Exception.Message)" }
        try { Start-Service -Name $name -ErrorAction Stop; Write-Log '   запущена' }
        catch { Write-Log "   не удалось запустить: $($_.Exception.Message)" }
    }
}

function Get-ExitIP {
    for ($i = 0; $i -lt 12; $i++) {
        try {
            $ip = (Invoke-RestMethod -Uri 'https://api.ipify.org?format=json' -TimeoutSec 10 -ErrorAction Stop).ip
            if ($ip) { return $ip }
        } catch { }
        Start-Sleep -Milliseconds 800
        Wpf-DoEvents
    }
    return $null
}

function Get-Country($ip) {
    try {
        $g = Invoke-RestMethod -Uri "https://ipinfo.io/$ip/json" -TimeoutSec 10 -ErrorAction Stop
        return "$($g.country) — $($g.city) ($($g.org))"
    } catch { return 'страна не определена' }
}

function Set-SystemProxyOff {
    $k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    try { Set-ItemProperty -Path $k -Name ProxyEnable -Value 0 -Type DWord -ErrorAction Stop } catch { }
}

function Update-IPLabel {
    $ip = Get-ExitIP
    if ($ip) { $IpText.Text = "Ваш IP: $ip — $(Get-Country $ip)" }
    else { $IpText.Text = 'Ваш IP: не определён' }
    Wpf-DoEvents
}

function Start-VpnAction {
    if (-not $script:profile) { Write-Log 'Не выбран профиль VPN.'; return }
    Write-Log "================ ЗАПУСК VPN ($($script:profile.name)) ================"
    Set-Working 'connecting'
    Stop-Interfering

    $cmd = Get-ProfileProp 'connect'
    Write-Log "Команда подключения: $cmd"
    try { Invoke-Action $cmd } catch { Write-Log "Ошибка подключения: $($_.Exception.Message)" }

    $ad = Get-ProfileProp 'adapter'
    if ($ad) {
        for ($i = 0; $i -lt 50; $i++) {
            if (Test-TunnelUp) { break }
            Start-Sleep -Milliseconds 750
            Wpf-DoEvents
        }
        if (-not (Test-TunnelUp)) {
            Write-Log 'Не удалось подключиться — проверьте настройки профиля и повторите.'
            Update-Status
            return
        }
        Write-Log "Туннель «$ad» поднят."
        if (Get-ProfileProp 'fixRouting') {
            try {
                Set-NetIPInterface -InterfaceAlias $ad -AddressFamily IPv4 -IgnoreDefaultRoutes Disabled -ErrorAction Stop
                Write-Log 'Маршрутизация всего трафика — включена.'
            } catch { Write-Log "Не удалось настроить маршрут: $($_.Exception.Message)" }
        }
    } else {
        Write-Log 'В профиле не указан адаптер — статус не отслеживается.'
        Start-Sleep -Seconds 3
    }

    Set-SystemProxyOff
    Update-IPLabel
    Update-Status
    Play-Sound 'connect'
    Write-Log 'Готово.'
}

function Stop-VpnAction {
    if (-not $script:profile) { Write-Log 'Не выбран профиль VPN.'; return }
    Write-Log "================ ОСТАНОВКА VPN ($($script:profile.name)) ================"
    if (Test-TunnelUp) {
        Set-Working 'disconnecting'
        $cmd = Get-ProfileProp 'disconnect'
        Write-Log "Команда отключения: $cmd"
        try { Invoke-Action $cmd } catch { Write-Log "Ошибка отключения: $($_.Exception.Message)" }

        for ($i = 0; $i -lt 25; $i++) {
            if (-not (Test-TunnelUp)) { break }
            Start-Sleep -Milliseconds 500
            Wpf-DoEvents
        }
    } else {
        Write-Log 'VPN уже не подключён.'
    }

    Set-SystemProxyOff
    Restore-Interfering
    $IpText.Text = 'Ваш IP: —'
    Update-Status
    Play-Sound 'disconnect'
    Write-Log 'Готово.'
}

# ---------- анимация ----------
$anim = New-Object System.Windows.Threading.DispatcherTimer
$anim.Interval = [TimeSpan]::FromMilliseconds(320)
$anim.Add_Tick({
    $script:animPhase++
    $base = $bGray; $light = $bGrayL
    if ($script:state -eq 'on') { $base = $bGreen; $light = $bGreenL }
    elseif ($script:state -eq 'connecting' -or $script:state -eq 'disconnecting') { $base = $bViolet; $light = $bVioletL }
    $Dot.Fill = if (($script:animPhase % 2) -eq 0) { $light } else { $base }
    if ($script:state -eq 'connecting') { $StatusText.Text = 'Подключение' + ('.' * ($script:animPhase % 4)) }
    if ($script:state -eq 'disconnecting') { $StatusText.Text = 'Отключение' + ('.' * ($script:animPhase % 4)) }
})

# ---------- обработчики ----------
$PowerBtn.Add_Click({
    $PowerBtn.IsEnabled = $false
    try {
        if (Test-TunnelUp) { Stop-VpnAction } else { Start-VpnAction }
    } catch { Write-Log "Ошибка: $($_.Exception.Message)"; Update-Status }
    $PowerBtn.IsEnabled = $true
})

$HappBtn.Add_Click({
    $client = Get-ProfileProp 'clientPath'
    if ($client -and (Test-Path $client)) { Start-Process -FilePath $client }
    elseif ($client) { Write-Log "Не найден $client" }
    else { Write-Log 'В профиле не указан clientPath.' }
})

$IpBtn.Add_Click({
    Write-Log 'Проверяю IP…'
    Update-IPLabel
    Write-Log "Текущий IP: $($IpText.Text)"
})

$MinBtn.Add_Click({ $window.WindowState = 'Minimized' })
$CloseBtn.Add_Click({ $window.Close() })
$HeaderPanel.Add_MouseLeftButtonDown({ try { $window.DragMove() } catch { } })

$ProfileBox.Add_SelectionChanged({
    if ($ProfileBox.SelectedItem -ne $null) { Set-Profile ([string]$ProfileBox.SelectedItem) }
})

$window.Add_Loaded({
    # профили
    $ProfileBox.ItemsSource = @($script:profiles | ForEach-Object { $_.name })
    $idx = 0
    $def = $config.defaultProfile
    for ($i = 0; $i -lt $script:profiles.Count; $i++) {
        if ($script:profiles[$i].name -eq $def) { $idx = $i; break }
    }
    if ($script:profiles.Count -gt 0) {
        $ProfileBox.SelectedIndex = $idx
        Set-Profile ($script:profiles[$idx].name)
    } else {
        Write-Log 'Не найден config.json или в нём нет профилей.'
        Write-Log 'Создайте config.json рядом с программой (см. README).'
        $PowerBtn.IsEnabled = $false
    }

    $anim.Start()
    Write-Log 'Pconnect готов.'
    Write-Log 'Настройки: config.json рядом с программой.'
})

[void] $window.ShowDialog()
