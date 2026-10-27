using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace UGreenCapViewer;

/// <summary>Hosts the live video, or the "no signal" artwork with a status message.</summary>
sealed class VideoSurface : Control
{
    readonly Image background = Image.FromStream(Program.Resource("nosignal.jpg"));
    readonly Image logo = Image.FromStream(Program.Resource("app.png"));
    CaptureSession? session;
    string status = "Starting…";

    public VideoSurface()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque |
                 ControlStyles.ResizeRedraw | ControlStyles.StandardDoubleClick, true);
        BackColor = Color.Black;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CaptureSession? Session
    {
        get => session;
        set { session = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Status
    {
        get => status;
        set { status = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (session != null)
        {
            session.Repaint(Handle, e.Graphics);
            return;
        }

        using var buffer = BufferedGraphicsManager.Current.Allocate(e.Graphics, ClientRectangle);
        var g = buffer.Graphics;
        int w = ClientSize.Width, h = ClientSize.Height;
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // Cover-scale the backdrop.
        float scale = Math.Max((float)w / background.Width, (float)h / background.Height);
        float bw = background.Width * scale, bh = background.Height * scale;
        g.DrawImage(background, (w - bw) / 2, (h - bh) / 2, bw, bh);

        int logoSize = Math.Clamp(Math.Min(w, h) / 5, 32, 128);
        var lines = status.Split('\n', 2);
        using var titleFont = new Font("Segoe UI Semibold", Math.Clamp(logoSize / 5f, 10, 20));
        using var subFont = new Font("Segoe UI", Math.Clamp(logoSize / 8f, 8, 12));
        var titleSize = g.MeasureString(lines[0], titleFont);
        var subSize = lines.Length > 1 ? g.MeasureString(lines[1], subFont, w - 40) : SizeF.Empty;
        float total = logoSize + 16 + titleSize.Height + subSize.Height;
        float y = (h - total) / 2;

        g.DrawImage(logo, (w - logoSize) / 2f, y, logoSize, logoSize);
        y += logoSize + 16;
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(lines[0], titleFont, Brushes.White, new RectangleF(0, y, w, titleSize.Height), format);
        y += titleSize.Height;
        if (lines.Length > 1)
        {
            using var dim = new SolidBrush(Color.FromArgb(170, 200, 190));
            g.DrawString(lines[1], subFont, dim, new RectangleF(20, y, w - 40, subSize.Height + 4), format);
        }
        buffer.Render(e.Graphics);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            background.Dispose();
            logo.Dispose();
        }
        base.Dispose(disposing);
    }
}
