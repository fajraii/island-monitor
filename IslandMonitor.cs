using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Xml.Serialization;

namespace IslandMonitor {
public class MonitorSettings {
    public bool ShowRam = true;
    public bool ShowDisk = true;
    public bool DiskTransfer = false;
    public string Drive = Path.GetPathRoot(Environment.SystemDirectory);
    public int IntervalSeconds = 1;
    public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IslandMonitor", "settings.xml"); } }
    public static MonitorSettings Load() {
        try { using(var stream=File.OpenRead(FilePath)) { var s=(MonitorSettings)new XmlSerializer(typeof(MonitorSettings)).Deserialize(stream); s.IntervalSeconds=Math.Max(1,Math.Min(10,s.IntervalSeconds)); if(!s.ShowRam && !s.ShowDisk) s.ShowDisk=true; if(string.IsNullOrEmpty(s.Drive)) s.Drive=Path.GetPathRoot(Environment.SystemDirectory); return s; } }
        catch { return new MonitorSettings(); }
    }
    public void Save() {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        var temporary=FilePath+".tmp";
        using(var stream=File.Create(temporary)) new XmlSerializer(typeof(MonitorSettings)).Serialize(stream,this);
        if(File.Exists(FilePath)) File.Replace(temporary,FilePath,null); else File.Move(temporary,FilePath);
    }
}
public class App : Application {
    [STAThread] public static void Main() { new App().Run(new Island()); }
}
public class Island : Window {
    [StructLayout(LayoutKind.Sequential)] class MemoryStatus {
        public uint length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
        public uint load;
        public ulong totalPhysical, availablePhysical, totalPage, availablePage, totalVirtual, availableVirtual, extended;
    }
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
    readonly TextBlock compactRam = Label("RAM —", 13, "#E8EBF1");
    readonly TextBlock compactDisk = Label("DISK —", 13, "#E8EBF1");
    readonly TextBlock ramValue = Label("—", 24, "#FFFFFF");
    readonly TextBlock ramDetail = Label("Reading memory…", 12, "#9298A8");
    readonly TextBlock diskValue = Label("—", 24, "#FFFFFF");
    readonly TextBlock diskDetail = Label("Reading disk…", 12, "#9298A8");
    readonly TextBlock spaceDetail = Label("—", 12, "#9298A8");
    readonly Border ramFill = new Border { Background = Brush("#A6F5C5"), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border spaceFill = new Border { Background = Brush("#A5BFFF"), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border ramDot = new Border { Width=7, Height=7, CornerRadius=new CornerRadius(4), Background=Brush("#9298A8"), Margin=new Thickness(0,0,8,0) };
    readonly StackPanel details = new StackPanel { Margin = new Thickness(24, 0, 24, 20), Visibility = Visibility.Collapsed };
    readonly DispatcherTimer timer = new DispatcherTimer();
    readonly System.Windows.Forms.NotifyIcon tray = new System.Windows.Forms.NotifyIcon();
    bool quitting;
    MonitorSettings settings=MonitorSettings.Load();
    StackPanel ramHeader;
    Border headerSeparator;
    StackPanel headerContent;
    Window settingsWindow;
    PerformanceCounter reads, writes, diskIdle;
    readonly Stopwatch diskSampleAge=Stopwatch.StartNew();
    bool expanded;
    Point down;
    bool moved;
    static SolidColorBrush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
    static SolidColorBrush UsageColor(double percent) { return Brush(double.IsNaN(percent)?"#9298A8":percent>=90?"#FF7B86":percent>=70?"#FFD166":"#A6F5C5"); }
    const string ThresholdHint="Green: below 70% · Yellow: 70–89% · Red: 90% or higher";
    static TextBlock Label(string text, double size, string color) { return new TextBlock { Text = text, FontSize = size, Foreground = Brush(color), FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center }; }
    static Button SettingsButton() {
        var button=new Button {
            Content="\u2699  Settings", FontFamily=new FontFamily("Segoe UI"), FontSize=12,
            FontWeight=FontWeights.SemiBold, Foreground=Brush("#C3D2FF"),
            Background=Brush("#202530"), BorderBrush=Brush("#394359"), BorderThickness=new Thickness(1),
            HorizontalAlignment=HorizontalAlignment.Right, Padding=new Thickness(14,7,14,7),
            Margin=new Thickness(0,14,0,0), Cursor=Cursors.Hand,
            ToolTip="Configure the island"
        };
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(12));
        border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(content); template.VisualTree=border;
        var hover=new Trigger { Property=Button.IsMouseOverProperty, Value=true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#2D374B")));
        hover.Setters.Add(new Setter(Control.BorderBrushProperty,Brush("#718BC4"))); template.Triggers.Add(hover);
        var focus=new Trigger { Property=Button.IsKeyboardFocusedProperty, Value=true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty,Brush("#A6F5C5"))); template.Triggers.Add(focus);
        var pressed=new Trigger { Property=Button.IsPressedProperty, Value=true };
        pressed.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#172030")));
        pressed.Setters.Add(new Setter(Control.ForegroundProperty,Brush("#FFFFFF"))); template.Triggers.Add(pressed);
        button.Template=template;
        return button;
    }
    public Island() {
        Title = "Island Monitor"; Width = 410; Height = 62; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
        Left = (SystemParameters.WorkArea.Width - Width) / 2; Top = SystemParameters.WorkArea.Top + 12;
        var shell = new Border { Background = Brush("#101115"), BorderBrush = Brush("#303239"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(28), Margin = new Thickness(5), ClipToBounds = true,
            Effect = new DropShadowEffect { BlurRadius = 9, ShadowDepth = 2, Opacity = .35 } };
        Content = shell;
        var root = new StackPanel(); shell.Child = root;
        var header = new Grid { Height = 50, Background = Brushes.Transparent, Margin = new Thickness(20,0,20,0), Cursor = Cursors.Hand };
        headerContent=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Center };
        header.Children.Add(headerContent);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        ramHeader=left;
        left.Children.Add(ramDot); left.Children.Add(compactRam); headerContent.Children.Add(left);
        var separator = new Border { Width=1, Height=16, Background=Brush("#393B44"), Margin=new Thickness(14,0,14,0) }; headerContent.Children.Add(separator);
        headerSeparator=separator;
        headerContent.Children.Add(compactDisk);
        root.Children.Add(header); root.Children.Add(details);
        header.ToolTip = "Click to expand · Drag to move · Right-click for options";
        header.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { down = PointToScreen(e.GetPosition(this)); moved=false; header.CaptureMouse(); };
        header.MouseMove += delegate(object s, MouseEventArgs e) { if(e.LeftButton == MouseButtonState.Pressed && header.IsMouseCaptured) { var p=PointToScreen(e.GetPosition(this)); var dx=p.X-down.X; var dy=p.Y-down.Y; if(Math.Abs(dx)+Math.Abs(dy)>3 || moved) { var scale=PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice; var delta=scale.Transform(new Vector(dx,dy)); Left+=delta.X; Top+=delta.Y; down=p; moved=true; } } };
        header.MouseLeftButtonUp += delegate { header.ReleaseMouseCapture(); if(!moved) Toggle(); };
        details.Children.Add(new Border { Height=1, Background=Brush("#292B33"), Margin=new Thickness(0,0,0,16) });
        details.Children.Add(Label("MEMORY",10,"#A6F5C5")); details.Children.Add(ramValue); details.Children.Add(ramDetail); details.Children.Add(Track(ramFill));
        details.Children.Add(new Border { Height=16 }); details.Children.Add(Label("DISK TRANSFER · ALL DRIVES",10,"#A5BFFF")); details.Children.Add(diskValue); details.Children.Add(diskDetail);
        details.Children.Add(new Border { Height=16 }); details.Children.Add(spaceDetail); details.Children.Add(Track(spaceFill));
        var settingsButton=SettingsButton();
        settingsButton.Click+=delegate { OpenSettings(); }; details.Children.Add(settingsButton);
        var menu=new ContextMenu(); var expand=new MenuItem { Header="Expand / collapse" }; expand.Click+=delegate { Toggle(); }; menu.Items.Add(expand);
        var configure=new MenuItem { Header="Settings…" }; configure.Click+=delegate { OpenSettings(); }; menu.Items.Add(configure);
        var pin=new MenuItem { Header="Always on top", IsCheckable=true, IsChecked=true }; pin.Click+=delegate { Topmost=pin.IsChecked; }; menu.Items.Add(pin);
        var center=new MenuItem { Header="Return to top center" }; center.Click+=delegate { Left=SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-Width)/2; Top=SystemParameters.WorkArea.Top+12; }; menu.Items.Add(center);
        var hide=new MenuItem { Header="Hide to tray" }; hide.Click+=delegate { Hide(); }; menu.Items.Add(hide);
        menu.Items.Add(new Separator()); var quit=new MenuItem { Header="Quit Island Monitor" }; quit.Click+=delegate { Quit(); }; menu.Items.Add(quit); shell.ContextMenu=menu;
        var trayMenu=new System.Windows.Forms.ContextMenuStrip();
        trayMenu.Items.Add("Show / hide monitor", null, delegate { ToggleVisibility(); });
        trayMenu.Items.Add("Expand / collapse", null, delegate { Show(); Toggle(); });
        trayMenu.Items.Add("Settings…", null, delegate { OpenSettings(); });
        var trayPin=new System.Windows.Forms.ToolStripMenuItem("Always on top") { CheckOnClick=true, Checked=true };
        trayPin.Click+=delegate { Topmost=trayPin.Checked; pin.IsChecked=Topmost; };
        pin.Click+=delegate { trayPin.Checked=Topmost; };
        trayMenu.Items.Add(trayPin);
        trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        trayMenu.Items.Add("Quit Island Monitor", null, delegate { Quit(); });
        using(var iconStream=typeof(Island).Assembly.GetManifestResourceStream("IslandMonitor.ico")) {
            tray.Icon=new System.Drawing.Icon(iconStream, 32, 32);
        }
        Icon=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(tray.Icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        tray.Text="Island Monitor";
        tray.ContextMenuStrip=trayMenu;
        tray.DoubleClick+=delegate { ToggleVisibility(); };
        tray.Visible=true;
        try { reads=new PerformanceCounter("PhysicalDisk","Disk Read Bytes/sec","_Total"); writes=new PerformanceCounter("PhysicalDisk","Disk Write Bytes/sec","_Total"); reads.NextValue(); writes.NextValue(); } catch { DisposeCounters(); }
        try { diskIdle=new PerformanceCounter("PhysicalDisk","% Idle Time","_Total"); diskIdle.NextValue(); } catch { if(diskIdle!=null) diskIdle.Dispose(); diskIdle=null; }
        diskSampleAge.Restart();
        timer.Tick+=delegate { Refresh(); }; ApplySettings(); timer.Start();
        Closing+=delegate(object sender, System.ComponentModel.CancelEventArgs e) { if(!quitting) { e.Cancel=true; Hide(); } };
        Closed+=delegate { if(settingsWindow!=null) settingsWindow.Close(); timer.Stop(); DisposeCounters(); tray.Visible=false; var icon=tray.Icon; tray.Dispose(); icon.Dispose(); trayMenu.Dispose(); };
    }
    void ApplySettings() {
        ramHeader.Visibility=settings.ShowRam?Visibility.Visible:Visibility.Collapsed;
        compactDisk.Visibility=settings.ShowDisk?Visibility.Visible:Visibility.Collapsed;
        headerSeparator.Visibility=settings.ShowRam && settings.ShowDisk?Visibility.Visible:Visibility.Collapsed;
        timer.Interval=TimeSpan.FromSeconds(settings.IntervalSeconds);
        Refresh();
    }
    void ResizeIsland() {
        headerContent.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        double target=Math.Ceiling(headerContent.DesiredSize.Width+52);
        target=Math.Max(expanded?410:110,target);
        if(Math.Abs(Width-target)<1) return;
        double center=Left+Width/2;
        Width=target;
        var area=SystemParameters.WorkArea;
        Left=Math.Max(area.Left,Math.Min(area.Right-Width,center-Width/2));
    }
    void OpenSettings() {
        if(settingsWindow!=null) { settingsWindow.Activate(); return; }
        var original=settings;
        bool saved=false;
        var window=new Window { Title="Island Monitor settings", Width=420, SizeToContent=SizeToContent.Height, MaxHeight=SystemParameters.WorkArea.Height, ResizeMode=ResizeMode.NoResize, WindowStyle=WindowStyle.None, AllowsTransparency=true, WindowStartupLocation=WindowStartupLocation.CenterScreen, Background=Brushes.Transparent, Foreground=Brush("#E8EBF1"), FontFamily=new FontFamily("Segoe UI"), FontSize=13, Topmost=true, Icon=Icon };
        using(var theme=typeof(Island).Assembly.GetManifestResourceStream("SettingsTheme.xaml")) window.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Load(theme);
        settingsWindow=window;
        var frame=new Border { Background=Brush("#101115"), BorderBrush=Brush("#343C4C"), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(22), Margin=new Thickness(8), Effect=new DropShadowEffect { BlurRadius=12, ShadowDepth=2, Opacity=.4 } };
        window.Content=frame;
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; frame.Child=scroll;
        var form=new StackPanel { Margin=new Thickness(24) }; scroll.Content=form;
        var titlebar=new DockPanel { Margin=new Thickness(0,0,0,18), Background=Brushes.Transparent };
        var close=new Button { Content="\u00D7", Width=32, MinHeight=30, Padding=new Thickness(0), ToolTip="Close settings", IsCancel=true };
        close.Click+=delegate { window.Close(); }; DockPanel.SetDock(close,Dock.Right); titlebar.Children.Add(close);
        titlebar.Children.Add(new TextBlock { Text="ISLAND MONITOR", FontSize=11, Foreground=Brush("#A5BFFF"), VerticalAlignment=VerticalAlignment.Center });
        titlebar.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) { if(e.ButtonState==MouseButtonState.Pressed) window.DragMove(); }; form.Children.Add(titlebar);
        form.Children.Add(new TextBlock { Text="Make the island yours", FontSize=24, FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,0,0,6) });
        form.Children.Add(new TextBlock { Text="Choose what stays in view.", Foreground=Brush("#9298A8"), Margin=new Thickness(0,0,0,22) });
        var ram=new CheckBox { Content="Show RAM in the pill", IsChecked=settings.ShowRam, Margin=new Thickness(0,0,0,12) }; form.Children.Add(ram);
        var disk=new CheckBox { Content="Show disk in the pill", IsChecked=settings.ShowDisk, Margin=new Thickness(0,0,0,18) }; form.Children.Add(disk);
        form.Children.Add(new TextBlock { Text="Disk display", Margin=new Thickness(0,0,0,6) });
        var mode=new ComboBox { Margin=new Thickness(0,0,0,16) }; mode.Items.Add("Storage used (%) — selected drive"); mode.Items.Add("Transfer speed — all drives"); mode.SelectedIndex=settings.DiskTransfer?1:0; form.Children.Add(mode);
        form.Children.Add(new TextBlock { Text="Drive for storage usage", Margin=new Thickness(0,0,0,6) });
        var drive=new ComboBox { Margin=new Thickness(0,0,0,16) };
        foreach(var d in DriveInfo.GetDrives()) { try { if(d.IsReady && d.DriveType==DriveType.Fixed) drive.Items.Add(d.Name); } catch(IOException) {} }
        if(!drive.Items.Contains(settings.Drive)) drive.Items.Add(settings.Drive);
        drive.SelectedItem=settings.Drive; form.Children.Add(drive);
        form.Children.Add(new TextBlock { Text="Refresh interval", Margin=new Thickness(0,0,0,6) });
        var interval=new ComboBox { Margin=new Thickness(0,0,0,16) }; foreach(int seconds in new int[] {1,2,5,10}) interval.Items.Add(seconds); if(!interval.Items.Contains(settings.IntervalSeconds)) interval.Items.Add(settings.IntervalSeconds); interval.SelectedItem=settings.IntervalSeconds; form.Children.Add(interval);
        form.Children.Add(new TextBlock { Text="Changes preview immediately. Save to keep them. Interval is in seconds; expanded view shows all metrics.", TextWrapping=TextWrapping.Wrap, Foreground=Brush("#9298A8"), FontSize=12, Margin=new Thickness(0,0,0,20) });
        var error=new TextBlock { Foreground=Brush("#FF9EAD"), TextWrapping=TextWrapping.Wrap }; form.Children.Add(error);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=new Button { Content="Cancel", Padding=new Thickness(14,6,14,6), Margin=new Thickness(0,0,8,0) }; cancel.Click+=delegate { window.Close(); }; buttons.Children.Add(cancel);
        var save=new Button { Content="Save changes", Padding=new Thickness(18,8,18,8), IsDefault=true, Background=Brush("#A6F5C5"), Foreground=Brush("#12261B"), BorderBrush=Brush("#A6F5C5") }; buttons.Children.Add(save); form.Children.Add(buttons);
        Action preview=delegate {
            if(ram.IsChecked!=true && disk.IsChecked!=true) { error.Text="Choose at least one metric to display."; save.IsEnabled=false; return; }
            error.Text=""; save.IsEnabled=true;
            settings=new MonitorSettings { ShowRam=ram.IsChecked==true, ShowDisk=disk.IsChecked==true, DiskTransfer=mode.SelectedIndex==1, Drive=(string)drive.SelectedItem, IntervalSeconds=(int)interval.SelectedItem };
            ApplySettings();
        };
        ram.Checked+=delegate { preview(); }; ram.Unchecked+=delegate { preview(); };
        disk.Checked+=delegate { preview(); }; disk.Unchecked+=delegate { preview(); };
        mode.SelectionChanged+=delegate { preview(); }; drive.SelectionChanged+=delegate { preview(); }; interval.SelectionChanged+=delegate { preview(); };
        save.Click+=delegate {
            if(ram.IsChecked!=true && disk.IsChecked!=true) { error.Text="Choose at least one metric to display."; return; }
            var updated=new MonitorSettings { ShowRam=ram.IsChecked==true, ShowDisk=disk.IsChecked==true, DiskTransfer=mode.SelectedIndex==1, Drive=(string)drive.SelectedItem, IntervalSeconds=(int)interval.SelectedItem };
            try { updated.Save(); } catch(Exception ex) { error.Text="Could not save settings: "+ex.Message; return; }
            settings=updated; saved=true; ApplySettings(); window.Close();
        };
        window.Closed+=delegate { settingsWindow=null; if(!saved && !quitting) { settings=original; ApplySettings(); } }; window.Show();
    }
    void ToggleVisibility() { if(IsVisible) Hide(); else { Show(); Activate(); } }
    void Quit() { quitting=true; Close(); }
    Border Track(Border fill) { return new Border { Height=5, Background=Brush("#292D36"), CornerRadius=new CornerRadius(3), Margin=new Thickness(0,9,0,0), Child=fill }; }
    void Toggle() { expanded=!expanded; details.Visibility=expanded?Visibility.Visible:Visibility.Collapsed; Height=expanded?390:62; ResizeIsland(); }
    void DisposeCounters() { if(reads!=null) reads.Dispose(); if(writes!=null) writes.Dispose(); if(diskIdle!=null) diskIdle.Dispose(); reads=null; writes=null; diskIdle=null; }
    static string Rate(double bytes) { return bytes>=1048576 ? (bytes/1048576).ToString("0.0")+" MB/s" : (bytes/1024).ToString("0")+" KB/s"; }
    void Refresh() {
        var memory=new MemoryStatus();
        if(GlobalMemoryStatusEx(memory)) { double used=(memory.totalPhysical-memory.availablePhysical)/1073741824.0; compactRam.Text="RAM "+memory.load+"%"; ramValue.Text=used.ToString("0.0")+" GB"; ramDetail.Text="of "+(memory.totalPhysical/1073741824.0).ToString("0.0")+" GB  ·  "+memory.load+"% used"; ramFill.Width=260*memory.load/100.0; var color=UsageColor(memory.load); compactRam.Foreground=color; ramValue.Foreground=color; ramFill.Background=color; ramDot.Background=color; }
        else { compactRam.Text="RAM —"; compactRam.Foreground=ramValue.Foreground=UsageColor(double.NaN); ramDot.Background=ramFill.Background=UsageColor(double.NaN); }
        compactRam.ToolTip="Physical memory used. "+ThresholdHint;
        ramValue.ToolTip=compactRam.ToolTip;
        double active=double.NaN;
        if(diskIdle!=null && diskSampleAge.ElapsedMilliseconds>=500) { try { active=Math.Max(0,Math.Min(100,100-diskIdle.NextValue())); } catch { diskIdle.Dispose(); diskIdle=null; } }
        var transferColor=UsageColor(active);
        diskValue.Foreground=transferColor;
        diskValue.ToolTip=(double.IsNaN(active)?"Disk activity unavailable":active.ToString("0")+"% disk active time (all drives)")+". "+ThresholdHint;
        string transfer="DISK —";
        if(reads!=null) { try { double r=reads.NextValue(), w=writes.NextValue(); transfer="DISK "+Rate(r+w); diskValue.Text=Rate(r+w); diskDetail.Text="↓ Read "+Rate(r)+"   ↑ Write "+Rate(w); } catch { DisposeCounters(); } }
        if(reads==null) { diskValue.Text="Unavailable"; diskDetail.Text="Windows disk counters are unavailable"; transferColor=UsageColor(double.NaN); diskValue.Foreground=transferColor; }
        string storage="DISK —";
        var storageColor=UsageColor(double.NaN);
        try { var drive=new DriveInfo(settings.Drive); double free=drive.AvailableFreeSpace/1073741824.0, total=drive.TotalSize/1073741824.0; double used=1-free/total; double percent=Math.Round(used*100,MidpointRounding.AwayFromZero); storageColor=UsageColor(percent); storage="DISK "+drive.Name.TrimEnd('\\')+" "+percent.ToString("0")+"%"; spaceDetail.Text=drive.Name+"   "+free.ToString("0.0")+" GB free of "+total.ToString("0")+" GB"; spaceFill.Width=260*used; } catch { spaceDetail.Text=settings.Drive+" Drive space unavailable"; spaceFill.Width=0; }
        spaceFill.Background=storageColor; spaceDetail.Foreground=storageColor;
        spaceDetail.ToolTip="Storage used on "+settings.Drive+". "+ThresholdHint;
        compactDisk.Text=settings.DiskTransfer?transfer:storage;
        compactDisk.Foreground=settings.DiskTransfer?transferColor:storageColor;
        compactDisk.ToolTip=settings.DiskTransfer?diskValue.ToolTip:spaceDetail.ToolTip;
        tray.Text="Island Monitor | "+compactRam.Text+" | "+compactDisk.Text;
        ResizeIsland();
    }
}
}
