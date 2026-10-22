param(
    [string]$Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'bin\IslandMonitor.exe'),
    [string]$PreviewDirectory
)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run this script using Windows PowerShell -STA' }
Add-Type -AssemblyName PresentationFramework
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$type = $assembly.GetType('IslandMonitor.Island')
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
function Assert($condition, [string]$message) { if (-not $condition) { throw $message } }
function Field([string]$name) { return $type.GetField($name, $flags).GetValue($island) }
function Invoke-Island([string]$name) { $type.GetMethod($name, $flags).Invoke($island, @()) | Out-Null }
function Export-Preview($window, [string]$name) {
    $window.UpdateLayout()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap([int][Math]::Ceiling($window.ActualWidth), [int][Math]::Ceiling($window.ActualHeight), 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($window)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $file = [IO.File]::Create((Join-Path $PreviewDirectory $name))
    try { $encoder.Save($file) } finally { $file.Dispose() }
}

# Test boundaries without relying on the runner's current load.
$color = $type.GetMethod('UsageColor', [Reflection.BindingFlags]'Static,NonPublic')
foreach ($case in @(@(0,'#FFA6F5C5'), @(69.9,'#FFA6F5C5'), @(70,'#FFFFD166'), @(89.9,'#FFFFD166'), @(90,'#FFFF7B86'), @(100,'#FFFF7B86'), @([double]::NaN,'#FF9298A8'))) {
    Assert ($color.Invoke($null, @([double]$case[0])).ToString() -eq $case[1]) "Wrong color at $($case[0])"
}

$settingsType = $assembly.GetType('IslandMonitor.MonitorSettings')
$settingsPath = $settingsType.GetProperty('FilePath').GetValue($null, $null)
$before = if (Test-Path -LiteralPath $settingsPath) { [Convert]::ToBase64String([IO.File]::ReadAllBytes($settingsPath)) } else { $null }
$island = $assembly.CreateInstance('IslandMonitor.Island')
try {
    Assert (-not $island.ShowInTaskbar) 'Widget must stay out of the taskbar'
    Assert ((Field 'tray').Visible) 'Tray icon missing'
    $settings = Field 'settings'
    $settings.ShowRam = $true; $settings.ShowDisk = $true; $settings.DiskTransfer = $false
    Invoke-Island 'ApplySettings'
    $bothWidth = $island.Width
    $center = $island.Left + $island.Width / 2
    Assert ((Field 'compactDisk').Text.StartsWith('DISK ')) 'Missing disk label'
    Assert ($null -ne (Field 'compactRam').ToolTip) 'Missing threshold explanation'

    Invoke-Island 'OpenSettings'
    $dialog = Field 'settingsWindow'
    $dialog.UpdateLayout()
    $form = $dialog.Content.Child.Content
    $checks = @($form.Children | Where-Object { $_ -is [System.Windows.Controls.CheckBox] })
    $combos = @($form.Children | Where-Object { $_ -is [System.Windows.Controls.ComboBox] })
    Assert ($checks.Count -eq 2 -and $combos.Count -eq 3) 'Settings controls missing'
    foreach ($combo in $combos) {
        $combo.ApplyTemplate() | Out-Null
        Assert ($null -ne $combo.Template.FindName('PART_Popup', $combo)) 'Dropdown template missing popup'
    }
    $checks[1].IsChecked = $false
    Assert ($island.Width -lt $bothWidth -and $island.Width -le 160) 'RAM-only pill not compact'
    Assert ([Math]::Abs(($island.Left + $island.Width / 2) - $center) -lt 1) 'Resizing moved the center'
    $checks[1].IsChecked = $true; $checks[0].IsChecked = $false
    Assert ($island.Width -lt $bothWidth) 'Disk-only pill not compact'
    $combos[0].SelectedIndex = 1
    $combos[2].SelectedItem = 5
    Assert ((Field 'settings').DiskTransfer) 'Transfer mode did not preview'
    Assert ((Field 'timer').Interval.TotalSeconds -eq 5) 'Interval did not preview'
    $checks[1].IsChecked = $false
    $buttons = $form.Children | Where-Object { $_ -is [System.Windows.Controls.StackPanel] }
    $save = $buttons.Children | Where-Object { $_ -is [System.Windows.Controls.Button] -and $_.IsDefault }
    Assert (-not $save.IsEnabled) 'Empty metric selection was allowed'
    $dialog.Close()
    Assert ($island.Width -eq $bothWidth) 'Cancel did not restore layout'
    Assert ((Field 'timer').Interval.TotalSeconds -eq $settings.IntervalSeconds) 'Cancel did not restore interval'

    # Persistence format roundtrip in memory; leave user preferences untouched.
    $serializer = New-Object System.Xml.Serialization.XmlSerializer($settingsType)
    $stream = New-Object IO.MemoryStream
    try {
        $settings.DiskTransfer = $true; $settings.IntervalSeconds = 5
        $serializer.Serialize($stream, $settings); $stream.Position = 0
        $roundtrip = $serializer.Deserialize($stream)
        Assert ($roundtrip.DiskTransfer -and $roundtrip.IntervalSeconds -eq 5 -and $roundtrip.Drive -eq $settings.Drive) 'Settings serialization failed'
    } finally { $stream.Dispose() }

    $island.Show()
    Invoke-Island 'Toggle'
    Assert ($island.Width -ge 410) 'Expanded details too narrow'
    $island.UpdateLayout()
    Assert ((Field 'details').DesiredSize.Height -le ($island.Height - 62)) 'Expanded details clipped vertically'
    if ($PreviewDirectory) {
        New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
        Export-Preview $island 'island.png'
        Invoke-Island 'OpenSettings'
        Export-Preview (Field 'settingsWindow') 'settings.png'
        (Field 'settingsWindow').Close()
    }
    Invoke-Island 'Toggle'
    Assert ($island.Width -lt 410) 'Collapse did not restore compact width'
    Invoke-Island 'ToggleVisibility'
    Assert (-not $island.IsVisible -and (Field 'tray').Visible) 'Hide must keep the tray icon'
    Invoke-Island 'ToggleVisibility'
    Assert ($island.IsVisible) 'Tray visibility restore failed'
} finally { Invoke-Island 'Quit' }
$after = if (Test-Path -LiteralPath $settingsPath) { [Convert]::ToBase64String([IO.File]::ReadAllBytes($settingsPath)) } else { $null }
Assert ($before -eq $after) 'Tests modified user preferences'
Write-Output 'PASS: thresholds, live preview, compact layout, settings resources, serialization, tray lifecycle, and preference preservation'
