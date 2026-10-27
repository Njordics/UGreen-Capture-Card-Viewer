using DirectShowLib;

namespace UGreenCapViewer;

sealed class MainForm : Form
{
    const string AppName = "UGreen Capture Viewer";
    const int WM_DEVICECHANGE = 0x0219;
    const int WM_DISPLAYCHANGE = 0x007E;

    readonly VideoSurface surface = new() { Dock = DockStyle.Fill };
    readonly ContextMenuStrip menu = new();
    readonly System.Windows.Forms.Timer pollTimer = new() { Interval = 500 };
    readonly System.Windows.Forms.Timer retryTimer = new() { Interval = 2000 };
    readonly System.Windows.Forms.Timer cursorTimer = new() { Interval = 2500 };
    readonly Settings settings = Settings.Load();
    CaptureSession? session;
    string? videoName;

    bool fullscreen;
    bool cursorHidden;
    FormWindowState preFullscreenState;
    Rectangle preFullscreenBounds;

    public MainForm()
    {
        Text = AppName;
        Icon = new Icon(Program.Resource("app.ico"));
        BackColor = Color.Black;
        ClientSize = new Size(1280, 720);
        MinimumSize = new Size(320, 220);
        TopMost = settings.TopMost;
        RestoreWindowBounds();
        Controls.Add(surface);

        surface.DoubleClick += (_, _) => ToggleFullscreen();
        surface.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            BuildMenu();
            ShowCursorNow();
            menu.Show(surface, e.Location);
        };
        surface.MouseMove += (_, _) => { ShowCursorNow(); if (fullscreen) { cursorTimer.Stop(); cursorTimer.Start(); } };
        surface.Resize += (_, _) => session?.UpdatePosition(surface.ClientRectangle);

        pollTimer.Tick += (_, _) =>
        {
            if (session?.PollLost() == true)
            {
                StopCapture();
                surface.Status = $"Signal lost\nWaiting for {videoName} to come back…";
                retryTimer.Start();
            }
        };
        retryTimer.Tick += (_, _) => StartCapture();
        cursorTimer.Tick += (_, _) =>
        {
            cursorTimer.Stop();
            if (fullscreen && !cursorHidden && !menu.Visible) { Cursor.Hide(); cursorHidden = true; }
        };
        Shown += (_, _) => StartCapture();
    }

    void StartCapture()
    {
        retryTimer.Stop();
        StopCapture();

        var videos = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        var video = settings.VideoDevice != null
            ? videos.FirstOrDefault(d => d.Name == settings.VideoDevice)
            : videos.FirstOrDefault(d => d.Name.Contains("UGREEN", StringComparison.OrdinalIgnoreCase));
        if (video == null)
        {
            surface.Status = settings.VideoDevice != null
                ? $"Waiting for {settings.VideoDevice}…\nRight-click to pick a different device."
                : "Waiting for a UGREEN capture card…\nPlug it in, or right-click to pick a different device.";
            Text = AppName;
            retryTimer.Start();
            return;
        }

        videoName = video.Name;
        session = new CaptureSession();
        try
        {
            session.Start(video, settings.Format, PickAudio(video.Name), surface);
            session.Muted = settings.Muted;
            surface.Session = session;
            pollTimer.Start();
            Text = $"{AppName} — {video.Name}" + (session.Format is { } f ? $" · {f}" : "") +
                   (settings.Muted && session.HasAudio ? " · muted" : "");
        }
        catch (Exception ex)
        {
            StopCapture();
            surface.Status = $"Couldn't open {video.Name}\n{ex.Message.Trim()}\nIs another app (OBS, Discord…) using it? Retrying…";
            Text = AppName;
            retryTimer.Start();
        }
    }

    void StopCapture()
    {
        pollTimer.Stop();
        surface.Session = null;
        session?.Dispose();
        session = null;
    }

    DsDevice? PickAudio(string video)
    {
        if (settings.AudioDevice == "") return null;
        var audios = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice);
        if (settings.AudioDevice != null) return audios.FirstOrDefault(a => a.Name == settings.AudioDevice);
        return audios.FirstOrDefault(a => a.Name.Contains(video, StringComparison.OrdinalIgnoreCase))
            ?? (video.Contains("UGREEN", StringComparison.OrdinalIgnoreCase)
                ? audios.FirstOrDefault(a => a.Name.Contains("UGREEN", StringComparison.OrdinalIgnoreCase))
                : null);
    }

    void BuildMenu()
    {
        menu.Items.Clear();

        var videoMenu = new ToolStripMenuItem("Video device");
        foreach (var d in DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice))
        {
            string name = d.Name;
            videoMenu.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) =>
            {
                settings.VideoDevice = name;
                settings.Format = null;
                StartCapture();
            }) { Checked = name == videoName && session != null });
        }
        if (videoMenu.DropDownItems.Count == 0) videoMenu.DropDownItems.Add(new ToolStripMenuItem("(none found)") { Enabled = false });

        var audioMenu = new ToolStripMenuItem("Audio device");
        audioMenu.DropDownItems.Add(new ToolStripMenuItem("Automatic (match video)", null, (_, _) => { settings.AudioDevice = null; StartCapture(); })
            { Checked = settings.AudioDevice == null });
        audioMenu.DropDownItems.Add(new ToolStripMenuItem("None", null, (_, _) => { settings.AudioDevice = ""; StartCapture(); })
            { Checked = settings.AudioDevice == "" });
        audioMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var d in DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice))
        {
            string name = d.Name;
            audioMenu.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => { settings.AudioDevice = name; StartCapture(); })
                { Checked = settings.AudioDevice == name });
        }

        var formatMenu = new ToolStripMenuItem("Resolution / FPS") { Enabled = session?.Formats.Count > 0 };
        foreach (var f in session?.Formats ?? [])
        {
            var fmt = f;
            formatMenu.DropDownItems.Add(new ToolStripMenuItem(fmt.ToString(), null, (_, _) => { settings.Format = fmt.Key; StartCapture(); })
                { Checked = fmt == session!.Format });
        }

        menu.Items.AddRange(new ToolStripItem[]
        {
            videoMenu,
            audioMenu,
            formatMenu,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Mute", null, (_, _) => ToggleMute()) { Checked = settings.Muted, ShortcutKeyDisplayString = "M", Enabled = session?.HasAudio == true },
            new ToolStripMenuItem("Always on top", null, (_, _) => ToggleTopMost()) { Checked = TopMost, ShortcutKeyDisplayString = "T" },
            new ToolStripMenuItem("Fullscreen", null, (_, _) => ToggleFullscreen()) { Checked = fullscreen, ShortcutKeyDisplayString = "F11 / Double-click" },
            new ToolStripMenuItem("Restart capture", null, (_, _) => StartCapture()) { ShortcutKeyDisplayString = "R" },
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => Close()),
        });
        if (session?.AudioError is { } err)
            menu.Items.Insert(2, new ToolStripMenuItem($"Audio failed: {err}") { Enabled = false });
    }

    void ToggleMute()
    {
        settings.Muted = !settings.Muted;
        if (session != null)
        {
            session.Muted = settings.Muted;
            Text = settings.Muted ? Text + " · muted" : Text.Replace(" · muted", "");
        }
    }

    void ToggleTopMost() => settings.TopMost = TopMost = !TopMost;

    void ToggleFullscreen()
    {
        if (!fullscreen)
        {
            preFullscreenState = WindowState;
            preFullscreenBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            fullscreen = true;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
            cursorTimer.Start();
        }
        else
        {
            fullscreen = false;
            cursorTimer.Stop();
            ShowCursorNow();
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = preFullscreenBounds;
            WindowState = preFullscreenState;
        }
    }

    void ShowCursorNow()
    {
        if (!cursorHidden) return;
        Cursor.Show();
        cursorHidden = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F11:
            case Keys.Alt | Keys.Enter:
                ToggleFullscreen();
                return true;
            case Keys.Escape when fullscreen:
                ToggleFullscreen();
                return true;
            case Keys.M:
                ToggleMute();
                return true;
            case Keys.T:
                ToggleTopMost();
                return true;
            case Keys.R:
                StartCapture();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_DEVICECHANGE && session == null)
        {
            // Something was plugged in; give the driver a moment, then retry soon.
            retryTimer.Stop();
            retryTimer.Start();
        }
        else if (m.Msg == WM_DISPLAYCHANGE)
        {
            session?.DisplayModeChanged();
        }
    }

    void RestoreWindowBounds()
    {
        if (settings.Bounds is not [var x, var y, var w, var h]) return;
        var rect = new Rectangle(x, y, w, h);
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect))) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = rect;
        if (settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var bounds = fullscreen ? preFullscreenBounds : WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        settings.Bounds = [bounds.X, bounds.Y, bounds.Width, bounds.Height];
        settings.Maximized = fullscreen ? preFullscreenState == FormWindowState.Maximized : WindowState == FormWindowState.Maximized;
        settings.Save();
        StopCapture();
        base.OnFormClosing(e);
    }
}
