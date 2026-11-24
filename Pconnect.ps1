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
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    }
}
'@
[Native.Win32Base]::ShowWindow([Native.Win32Base]::GetConsoleWindow(), 0) | Out-Null

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# ---------- каталог приложения ----------
$appDir = $PSScriptRoot
if (-not $appDir) {
    try { $appDir = [System.IO.Path]::GetDirectoryName([System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName) } catch { $appDir = (Get-Location).Path }
}

# ---------- загрузка конфигурации ----------
$configPath = Join-Path $appDir 'config.json'
$config = $null
if (Test-Path $configPath) {
    try { $config = Get-Content -Raw -Encoding UTF8 $configPath | ConvertFrom-Json } catch { $config = $null }
}
if (-not $config) { $config = [pscustomobject]@{ defaultProfile = ''; profiles = @() } }

$script:profiles = @($config.profiles)
$script:profile  = $null
$defaultKeywords = @('zapret','winws','goodbyedpi','spoofdpi','byedpi','nfqws','tpws','dpi_bypass','dpi-bypass','ciadpi','zaborona','anticenz','greentunnel')

# ---------- пути/ресурсы ----------
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

# ---------- всплывающее уведомление (само закрывается) ----------
function Show-Toast([string]$title, [string]$message, [string]$dotHex = '#16A34A') {
    try {
        $toastXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="330" SizeToContent="Height" WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        WindowStartupLocation="Manual" Topmost="True" ShowInTaskbar="False" ShowActivated="False" ResizeMode="NoResize">
  <Border CornerRadius="14" Margin="6" Background="#F2FFFFFF">
    <Border.Effect>
      <DropShadowEffect BlurRadius="16" ShadowDepth="1" Opacity="0.18" Color="#000000"/>
    </Border.Effect>
    <StackPanel Margin="16,12,16,12" Orientation="Horizontal">
      <Ellipse x:Name="Dot" Width="10" Height="10" VerticalAlignment="Center" Fill="#16A34A"/>
      <StackPanel Margin="12,0,0,0">
        <TextBlock x:Name="TTitle" Text="Pconnect" FontSize="11" FontWeight="Bold" Foreground="#1B2430"/>
        <TextBlock x:Name="TMsg" Text="..." FontSize="11" Foreground="#4B5563" TextWrapping="Wrap" MaxWidth="250"/>
      </StackPanel>
    </StackPanel>
  </Border>
</Window>
'@
        $toast = [System.Windows.Markup.XamlReader]::Parse($toastXaml)
        $toast.FindName('TTitle').Text = $title
        $toast.FindName('TMsg').Text = $message
        $toast.FindName('Dot').Fill = New-Brush $dotHex
        $toast.Add_Loaded({
            $wa = [System.Windows.SystemParameters]::WorkArea
            $toast.Left = $wa.Right - $toast.ActualWidth - 12
            $toast.Top = $wa.Bottom - $toast.ActualHeight - 12
        })
        $toast.Show()
        $script:currentToast = $toast
        $t = New-Object System.Windows.Threading.DispatcherTimer
        $t.Interval = [TimeSpan]::FromSeconds(3)
        $t.Add_Tick({
            param($sender, $e)
            try { $sender.Stop() } catch { }
            try { if ($script:currentToast) { $script:currentToast.Close() }; $script:currentToast = $null } catch { }
        })
        $t.Start()
    } catch { }
}

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
          <Button x:Name="SettingsBtn" Content="⚙" Style="{StaticResource WinButton}"/>
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
$SettingsBtn= $window.FindName('SettingsBtn')
$HeaderPanel= $window.FindName('HeaderPanel')

# ---------- окно настроек (XAML) ----------
$settingsXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Настройки" Width="540" Height="660" WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        WindowStartupLocation="CenterScreen" Topmost="True" ResizeMode="NoResize">
  <Border CornerRadius="20" Margin="10">
    <Border.Background>
      <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
        <GradientStop Color="#F6F7FC" Offset="0"/>
        <GradientStop Color="#E8EBF5" Offset="1"/>
      </LinearGradientBrush>
    </Border.Background>
    <Border.Effect><DropShadowEffect BlurRadius="22" ShadowDepth="0" Opacity="0.20" Color="#000000"/></Border.Effect>
    <Grid Margin="24,20,24,20">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
        <RowDefinition Height="Auto"/>
      </Grid.RowDefinitions>
      <Grid Grid.Row="0">
        <TextBlock Text="Настройки профилей" FontSize="18" FontWeight="Bold" Foreground="#1B2430"/>
        <Button x:Name="SWClose" Content="✕" Width="30" Height="26" HorizontalAlignment="Right" VerticalAlignment="Top" Background="#00FFFFFF" BorderThickness="0" Foreground="#7B8496" Cursor="Hand"/>
      </Grid>
      <StackPanel Grid.Row="1" Margin="0,14,0,0">
        <TextBlock Text="Профиль" FontSize="11" Foreground="#7B8496"/>
        <Grid Margin="0,5,0,0">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <ComboBox x:Name="ProfileCombo" Height="32" FontSize="12"/>
          <Button x:Name="AddBtn" Content="＋ Добавить" Grid.Column="1" Height="32" Margin="8,0,0,0" Padding="12,0"/>
          <Button x:Name="DelBtn" Content="🗑" Grid.Column="2" Width="40" Height="32" Margin="8,0,0,0"/>
        </Grid>
      </StackPanel>
      <StackPanel Grid.Row="2" Margin="0,12,0,0">
        <TextBlock Text="Название" FontSize="11" Foreground="#7B8496"/>
        <TextBox x:Name="FName" Height="28" Margin="0,3,0,8" FontSize="12"/>
        <TextBlock Text="Команда подключения" FontSize="11" Foreground="#7B8496"/>
        <TextBox x:Name="FConnect" Height="28" Margin="0,3,0,8" FontSize="12"/>
        <TextBlock Text="Команда отключения" FontSize="11" Foreground="#7B8496"/>
        <TextBox x:Name="FDisconnect" Height="28" Margin="0,3,0,8" FontSize="12"/>
        <TextBlock Text="Адаптер (для статуса)" FontSize="11" Foreground="#7B8496"/>
        <Grid Margin="0,3,0,8">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <TextBox x:Name="FAdapter" Height="28" FontSize="12"/>
          <Button x:Name="DetectBtn" Content="Подобрать" Grid.Column="1" Height="28" Margin="8,0,0,0" Padding="14,0"/>
        </Grid>
        <TextBlock Text="Путь к приложению" FontSize="11" Foreground="#7B8496"/>
        <TextBox x:Name="FClientPath" Height="28" Margin="0,3,0,8" FontSize="12"/>
        <CheckBox x:Name="FFixRouting" Content="Исправлять маршрутизацию (IgnoreDefaultRoutes)" FontSize="12" Margin="0,0,0,8"/>
        <TextBlock Text="Службы для отключения (через запятую)" FontSize="11" Foreground="#7B8496"/>
        <TextBox x:Name="FServices" Height="28" Margin="0,3,0,8" FontSize="12"/>
        <TextBlock Text="Процессы для завершения (через запятую)" FontSize="11" Foreground="#7B8496"/>
        <TextBox x:Name="FProcesses" Height="28" Margin="0,3,0,8" FontSize="12"/>
      </StackPanel>
      <StackPanel Grid.Row="3" Margin="0,6,0,0">
        <CheckBox x:Name="FKillSwitch" Content="Защита от обрыва — авто-переподключение при падении VPN" FontSize="12"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
          <Button x:Name="SaveBtn" Content="Сохранить" Width="120" Height="36" Background="#16A34A" Foreground="White" FontWeight="Bold" BorderThickness="0" Cursor="Hand"/>
          <Button x:Name="CancelBtn" Content="Закрыть" Width="110" Height="36" Margin="10,0,0,0" Background="#FFFFFFFF" BorderThickness="0" Cursor="Hand"/>
        </StackPanel>
      </StackPanel>
    </Grid>
  </Border>
</Window>
'@

function Save-Config {
    try {
        $obj = [ordered]@{}
        $obj.defaultProfile = if ($config.defaultProfile) { [string]$config.defaultProfile } else { '' }
        $obj.profiles = @()
        foreach ($p in $script:profiles) {
            $obj.profiles += [ordered]@{
                name                 = [string]$p.name
                connect              = [string]$p.connect
                disconnect           = [string]$p.disconnect
                adapter              = [string]$p.adapter
                clientPath           = [string]$p.clientPath
                fixRouting           = [bool]$p.fixRouting
                interferingServices  = @($p.interferingServices)
                interferingProcesses = @($p.interferingProcesses)
                interferingKeywords  = @($p.interferingKeywords)
            }
        }
        $json = $obj | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText($configPath, $json, (New-Object System.Text.UTF8Encoding($true)))
        return $true
    } catch { return $false }
}

function Get-CandidateAdapters {
    return Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object {
        $_.Virtual -or $_.InterfaceDescription -match '(?i)tun|tap|wireguard|wintun|vpn|happ|utun|openvpn'
    }
}

# ---------- состояние ----------
$script:state = 'off'
$script:animPhase = 0
$script:stoppedServices = @()

# ---------- настройки (реестр) ----------
$prefKey = 'HKCU:\Software\Pconnect'
function Get-Pref([string]$name) {
    return (Get-ItemProperty -Path $prefKey -Name $name -ErrorAction SilentlyContinue).$name
}
function Set-Pref([string]$name, [bool]$value) {
    if (-not (Test-Path $prefKey)) { New-Item -Path $prefKey -Force | Out-Null }
    Set-ItemProperty -Path $prefKey -Name $name -Value ([int]$value) -Type DWord
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'Pconnect'
function Set-Autostart([bool]$enable) {
    if ($enable) {
        $exe = Join-Path $appDir 'Pconnect.exe'
        if (Test-Path $exe) { Set-ItemProperty -Path $runKey -Name $runValueName -Value ('"' + $exe + '"') }
        else { Set-ItemProperty -Path $runKey -Name $runValueName -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $appDir 'Pconnect.ps1') + '"') }
    } else {
        Remove-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue
    }
}
function Get-Autostart {
    return [bool](Get-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue)
}

# ---------- трей ----------
function New-TrayIcon([string]$hex) {
    $bmp = New-Object System.Drawing.Bitmap(32, 32)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $brush = New-Object System.Drawing.SolidBrush(([System.Drawing.ColorTranslator]::FromHtml($hex)))
    $g.FillEllipse($brush, 2, 2, 28, 28)
    $font = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString('P', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, 0, 32, 32)), $sf)
    $g.Dispose()
    $hIcon = $bmp.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hIcon).Clone()
    $bmp.Dispose()
    return $icon
}
$script:iconGreen = New-TrayIcon '#16A34A'
$script:iconGray  = New-TrayIcon '#9CA3AF'

$script:notify = New-Object System.Windows.Forms.NotifyIcon
$script:notify.Icon = $script:iconGray
$script:notify.Text = 'Pconnect — не подключено'
$script:notify.Visible = $true

$trayMenu = New-Object System.Windows.Forms.ContextMenuStrip
$script:miStatus = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miStatus.Text = 'Не подключено'
$script:miStatus.Enabled = $false
$script:miToggle = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miToggle.Text = 'Включить VPN'
$script:miOpen = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miOpen.Text = 'Открыть Pconnect'
$script:miSettings = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miSettings.Text = 'Настройки'
$script:miSep1 = New-Object System.Windows.Forms.ToolStripSeparator
$script:miAutoStart = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miAutoStart.Text = 'Запускать при входе в систему'
$script:miAutoStart.Checked = (Get-Autostart)
$script:miAutoConnect = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miAutoConnect.Text = 'Подключаться автоматически'
$script:miAutoConnect.Checked = [bool](Get-Pref 'AutoConnect')
$script:miSep2 = New-Object System.Windows.Forms.ToolStripSeparator
$script:miExit = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miExit.Text = 'Выход'

$trayMenu.Items.AddRange(@($script:miStatus, $script:miToggle, $script:miOpen, $script:miSettings, $script:miSep1, $script:miAutoStart, $script:miAutoConnect, $script:miSep2, $script:miExit))
$script:notify.ContextMenuStrip = $trayMenu

$script:miToggle.Add_Click({ if (Test-TunnelUp) { Stop-VpnAction } else { Start-VpnAction } })
$script:miOpen.Add_Click({ $window.Show(); if ($window.WindowState -eq 'Minimized') { $window.WindowState = 'Normal' }; $window.Activate() })
$script:miSettings.Add_Click({ $window.Show(); if ($window.WindowState -eq 'Minimized') { $window.WindowState = 'Normal' }; $window.Activate(); Show-Settings })
$script:miAutoStart.Add_Click({ Set-Autostart $script:miAutoStart.Checked; Set-Pref 'AutoStart' $script:miAutoStart.Checked })
$script:miAutoConnect.Add_Click({ Set-Pref 'AutoConnect' $script:miAutoConnect.Checked })
$script:miExit.Add_Click({ $script:notify.Visible = $false; $script:notify.Dispose(); $window.Close() })
$script:notify.Add_DoubleClick({ $window.Show(); if ($window.WindowState -eq 'Minimized') { $window.WindowState = 'Normal' }; $window.Activate() })

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
    $on = ($script:profile -and (Test-TunnelUp))
    if ($on) {
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
    # трей
    if ($script:notify) {
        $script:notify.Icon = if ($on) { $script:iconGreen } else { $script:iconGray }
        $script:notify.Text = if ($on) { 'Pconnect — подключено' } else { 'Pconnect — не подключено' }
        $script:miStatus.Text = if ($on) { 'Подключено' } else { 'Не подключено' }
        $script:miToggle.Text = if ($on) { 'Выключить VPN' } else { 'Включить VPN' }
    }
    Wpf-DoEvents
}

function Set-Working([string]$label) {
    $script:state = $label
    $Prog.Visibility = 'Visible'
    $StatusText.Foreground = $bViolet
    $StatusText.Text = if ($label -eq 'connecting') { 'Подключение' } else { 'Отключение' }
    Wpf-DoEvents
}

function Get-KwPattern {
    $kw = @(Get-ProfileProp 'interferingKeywords') + $defaultKeywords
    $kw = @($kw | Where-Object { $_ } | Select-Object -Unique)
    if ($kw.Count -eq 0) { return $null }
    return '(?i)' + (($kw | ForEach-Object { [regex]::Escape([string]$_) }) -join '|')
}

function Get-InterferingServices {
    $names = @(Get-ProfileProp 'interferingServices')
    $re = Get-KwPattern
    if ($re) {
        $names += @(Get-Service -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match $re -or $_.DisplayName -match $re } |
            ForEach-Object { $_.Name })
    }
    return @($names | Where-Object { $_ } | Select-Object -Unique)
}

function Get-InterferingProcesses {
    $names = @(Get-ProfileProp 'interferingProcesses')
    $re = Get-KwPattern
    if ($re) {
        $names += @(Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.ProcessName -match $re } |
            ForEach-Object { $_.ProcessName } | Select-Object -Unique)
    }
    return @($names | Where-Object { $_ } | Select-Object -Unique)
}

function Get-SvcStartValue([string]$name) {
    return (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$name" -ErrorAction SilentlyContinue).Start
}

function Set-SvcStartValue([string]$name, [int]$v) {
    switch ($v) {
        2 { Set-Service -Name $name -StartupType Automatic -ErrorAction SilentlyContinue }
        3 { Set-Service -Name $name -StartupType Manual -ErrorAction SilentlyContinue }
        default { Set-Service -Name $name -StartupType Automatic -ErrorAction SilentlyContinue }
    }
}

function Stop-Interfering {
    $svcs = Get-InterferingServices
    $procs = Get-InterferingProcesses
    if ($svcs.Count -eq 0 -and $procs.Count -eq 0) { Write-Log 'Мешающих программ не найдено.'; return }
    Write-Log 'Отключаю мешающие программы…'
    $script:stoppedServices = @()
    foreach ($name in $svcs) {
        $s = Get-Service -Name $name -ErrorAction SilentlyContinue
        if (-not $s) { continue }
        $orig = Get-SvcStartValue $name
        Write-Log "Служба: $($s.DisplayName) ($($s.Name)) — $($s.Status)"
        try { Stop-Service -Name $name -Force -ErrorAction Stop; Write-Log '   остановлена' }
        catch { Write-Log "   не удалось остановить: $($_.Exception.Message)" }
        try { Set-Service -Name $name -StartupType Disabled -ErrorAction Stop; Write-Log '   автозапуск отключён' }
        catch { Write-Log "   не удалось отключить автозапуск: $($_.Exception.Message)" }
        $script:stoppedServices += [pscustomobject]@{ Name = $name; OriginalStart = $orig }
    }
    foreach ($name in $procs) {
        $p = Get-Process -Name $name -ErrorAction SilentlyContinue
        if ($p) { $p | Stop-Process -Force -ErrorAction SilentlyContinue; Write-Log "Процесс $name завершён" }
    }
}

function Restore-Interfering {
    if (-not $script:stoppedServices -or $script:stoppedServices.Count -eq 0) {
        Write-Log 'Нет служб для восстановления.'
        return
    }
    Write-Log 'Возвращаю мешающие программы…'
    foreach ($svc in $script:stoppedServices) {
        $name = $svc.Name
        $s = Get-Service -Name $name -ErrorAction SilentlyContinue
        Write-Log "Служба: $($s.DisplayName) ($name)"
        Set-SvcStartValue $name ([int]$svc.OriginalStart)
        try { Start-Service -Name $name -ErrorAction Stop; Write-Log '   запущена' }
        catch { Write-Log "   не удалось запустить: $($_.Exception.Message)" }
    }
    $script:stoppedServices = @()
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
    Show-Toast 'Pconnect' ('VPN подключён — ' + ($IpText.Text -replace 'Ваш IP: ', '')) '#16A34A'
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
    Show-Toast 'Pconnect' 'VPN выключен' '#9CA3AF'
    Write-Log 'Готово.'
}

function Show-Settings {
    try {
        $sw = [System.Windows.Markup.XamlReader]::Parse($settingsXaml)
        $cb = $sw.FindName('ProfileCombo')
        $fName = $sw.FindName('FName')
        $fConnect = $sw.FindName('FConnect')
        $fDisconnect = $sw.FindName('FDisconnect')
        $fAdapter = $sw.FindName('FAdapter')
        $fClientPath = $sw.FindName('FClientPath')
        $fFixRouting = $sw.FindName('FFixRouting')
        $fServices = $sw.FindName('FServices')
        $fProcesses = $sw.FindName('FProcesses')
        $fKillSwitch = $sw.FindName('FKillSwitch')
        $addBtn = $sw.FindName('AddBtn')
        $delBtn = $sw.FindName('DelBtn')
        $detectBtn = $sw.FindName('DetectBtn')
        $saveBtn = $sw.FindName('SaveBtn')
        $cancelBtn = $sw.FindName('CancelBtn')
        $closeBtn = $sw.FindName('SWClose')

        $script:settingCurrent = $null

        $refreshList = {
            $cb.ItemsSource = @($script:profiles | ForEach-Object { $_.name })
            if ($script:profiles.Count -gt 0) { $cb.SelectedIndex = 0 }
        }

        $loadFields = {
            $nm = [string]$cb.SelectedItem
            $script:settingCurrent = $script:profiles | Where-Object { $_.name -eq $nm } | Select-Object -First 1
            if ($script:settingCurrent) {
                $fName.Text = [string]$script:settingCurrent.name
                $fConnect.Text = [string]$script:settingCurrent.connect
                $fDisconnect.Text = [string]$script:settingCurrent.disconnect
                $fAdapter.Text = [string]$script:settingCurrent.adapter
                $fClientPath.Text = [string]$script:settingCurrent.clientPath
                $fFixRouting.IsChecked = [bool]$script:settingCurrent.fixRouting
                $fServices.Text = (@($script:settingCurrent.interferingServices) -join ', ')
                $fProcesses.Text = (@($script:settingCurrent.interferingProcesses) -join ', ')
            } else {
                $fName.Text = ''; $fConnect.Text = ''; $fDisconnect.Text = ''; $fAdapter.Text = ''
                $fClientPath.Text = ''; $fFixRouting.IsChecked = $false; $fServices.Text = ''; $fProcesses.Text = ''
            }
        }

        $cb.Add_SelectionChanged({ & $loadFields })

        $detectBtn.Add_Click({
            $cands = @(Get-CandidateAdapters)
            if ($cands.Count -eq 0) {
                Write-Log 'VPN-адаптер не найден. Подключите VPN и повторите.'
            } else {
                $up = $cands | Where-Object { $_.Status -eq 'Up' } | Select-Object -First 1
                $pick = if ($up) { $up } else { $cands[0] }
                $fAdapter.Text = [string]$pick.Name
                Write-Log "Выбран адаптер: $($pick.Name) — $($pick.InterfaceDescription)"
            }
        })

        $addBtn.Add_Click({
            $np = [pscustomobject]@{ name = 'Новый профиль'; connect = ''; disconnect = ''; adapter = ''; clientPath = ''; fixRouting = $false; interferingServices = @(); interferingProcesses = @(); interferingKeywords = @() }
            $script:profiles += $np
            & $refreshList
            $cb.SelectedItem = 'Новый профиль'
            & $loadFields
        })

        $delBtn.Add_Click({
            if (-not $script:settingCurrent) { return }
            $nm = [string]$script:settingCurrent.name
            $script:profiles = @($script:profiles | Where-Object { $_.name -ne $nm })
            $script:settingCurrent = $null
            & $refreshList
            & $loadFields
        })

        $saveBtn.Add_Click({
            if (-not $script:settingCurrent) { Write-Log 'Нет профиля для сохранения.'; return }
            $script:settingCurrent.name = $fName.Text
            $script:settingCurrent.connect = $fConnect.Text
            $script:settingCurrent.disconnect = $fDisconnect.Text
            $script:settingCurrent.adapter = $fAdapter.Text
            $script:settingCurrent.clientPath = $fClientPath.Text
            $script:settingCurrent.fixRouting = [bool]$fFixRouting.IsChecked
            $script:settingCurrent.interferingServices = @(($fServices.Text -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            $script:settingCurrent.interferingProcesses = @(($fProcesses.Text -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            Set-Pref 'KillSwitch' ([bool]$fKillSwitch.IsChecked)
            if (Save-Config) { Write-Log 'Настройки сохранены в config.json.' } else { Write-Log 'Не удалось сохранить настройки.' }
            & $refreshList
        })

        $cancelBtn.Add_Click({ $sw.Close() })
        $closeBtn.Add_Click({ $sw.Close() })

        $fKillSwitch.IsChecked = [bool](Get-Pref 'KillSwitch')
        & $refreshList
        & $loadFields
        [void] $sw.ShowDialog()
    } catch { Write-Log "Ошибка настроек: $($_.Exception.Message)" }
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

# ---------- защита от обрыва (kill switch) ----------
$guard = New-Object System.Windows.Threading.DispatcherTimer
$guard.Interval = [TimeSpan]::FromSeconds(2)
$guard.Add_Tick({
    try {
        if ([bool](Get-Pref 'KillSwitch') -and $script:state -eq 'on' -and $script:profile) {
            if (-not (Test-TunnelUp)) {
                Write-Log 'Обнаружен обрыв VPN — переподключаю…'
                $script:state = 'off'
                Show-Toast 'Pconnect' 'VPN разорван — переподключение…' '#DC2626'
                [System.Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([Action]{ Start-VpnAction }) | Out-Null
            }
        }
    } catch { }
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

$script:hideHintShown = $false
function Hide-ToTray {
    $window.Hide()
    if (-not $script:hideHintShown) {
        $script:hideHintShown = $true
        Show-Toast 'Pconnect' 'Pconnect работает в трее. Правый клик по значку — меню.' '#6D5CF7'
    }
}
$MinBtn.Add_Click({ Hide-ToTray })
$CloseBtn.Add_Click({ Hide-ToTray })
$SettingsBtn.Add_Click({ Show-Settings })
$HeaderPanel.Add_PreviewMouseLeftButtonDown({
    try {
        $h = (New-Object System.Windows.Interop.WindowInteropHelper($window)).Handle
        [Native.Win32Base]::ReleaseCapture() | Out-Null
        [Native.Win32Base]::SendMessage($h, 0xA1, [IntPtr]2, [IntPtr]::Zero) | Out-Null
    } catch { }
})

$window.Add_Loaded({
    if ($script:profiles.Count -gt 0) {
        $def = $config.defaultProfile
        $p = $script:profiles | Where-Object { $_.name -eq $def } | Select-Object -First 1
        if (-not $p) { $p = $script:profiles[0] }
        Set-Profile $p.name
    } else {
        Write-Log 'Не найден config.json или в нём нет профилей.'
        Write-Log 'Создайте config.json рядом с программой (см. README).'
        $PowerBtn.IsEnabled = $false
    }

    $anim.Start()
    $guard.Start()
    Write-Log 'Pconnect готов.'
    Write-Log 'Настройки: config.json рядом с программой.'

    # глобальная горячая клавиша Ctrl+Shift+V
    try {
        $helper = New-Object System.Windows.Interop.WindowInteropHelper($window)
        $hwnd = $helper.Handle
        [Native.Win32Base]::RegisterHotKey($hwnd, 1, 0x0006, 0x56) | Out-Null
        $src = [System.Windows.Interop.HwndSource]::FromHwnd($hwnd)
        $src.AddHook({
            param($hwnd, $msg, $wParam, $lParam)
            try {
                if ($msg -eq 0x0312 -and $wParam -eq [IntPtr]1) {
                    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([Action]{
                        if (Test-TunnelUp) { Stop-VpnAction } else { Start-VpnAction }
                    }) | Out-Null
                }
            } catch { }
            return [IntPtr]::Zero
        })
        Write-Log 'Горячая клавиша Ctrl+Shift+V включена.'
    } catch { Write-Log 'Не удалось зарегистрировать горячую клавишу.' }

    # автоподключение
    if ([bool](Get-Pref 'AutoConnect')) {
        Write-Log 'Автоподключение включено.'
        [System.Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([Action]{ Start-VpnAction })
    }
})

# ---------- экран загрузки (splash) ----------
$splashXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Pconnect" Width="330" Height="230"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        WindowStartupLocation="CenterScreen" Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize">
  <Border CornerRadius="22" Margin="10">
    <Border.Background>
      <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
        <GradientStop Color="#F6F7FC" Offset="0"/>
        <GradientStop Color="#E4E7F4" Offset="1"/>
      </LinearGradientBrush>
    </Border.Background>
    <Border.Effect>
      <DropShadowEffect BlurRadius="24" ShadowDepth="0" Opacity="0.20" Color="#000000"/>
    </Border.Effect>
    <StackPanel VerticalAlignment="Center">
      <Image x:Name="SplashLogo" Width="66" Height="66" HorizontalAlignment="Center"/>
      <TextBlock Text="Pconnect" FontSize="20" FontWeight="Bold" Foreground="#1B2430" HorizontalAlignment="Center" Margin="0,12,0,0"/>
      <TextBlock Text="Welcome" FontSize="15" Foreground="#6D5CF7" HorizontalAlignment="Center" Margin="0,2,0,0"/>
      <ProgressBar Width="150" Height="4" IsIndeterminate="True" Margin="0,18,0,0" Foreground="#6D5CF7" Background="#E2E5EF"/>
    </StackPanel>
  </Border>
</Window>
'@
$splash = [System.Windows.Markup.XamlReader]::Parse($splashXaml)
if ($logoUri) {
    try { $splash.FindName('SplashLogo').Source = New-Object System.Windows.Media.Imaging.BitmapImage($logoUri) } catch { }
}

# показать заставку, затем основное окно
$splash.Show()
$splashTimer = New-Object System.Windows.Threading.DispatcherTimer
$splashTimer.Interval = [TimeSpan]::FromSeconds(2.5)
$splashTimer.Add_Tick({
    $splashTimer.Stop()
    $splash.Close()
    $window.Show()
    $window.Activate()
})
$splashTimer.Start()

# цикл сообщений; завершение при закрытии окна
$window.Add_Closed({ [System.Windows.Threading.Dispatcher]::CurrentDispatcher.InvokeShutdown() })
[System.Windows.Threading.Dispatcher]::Run()
