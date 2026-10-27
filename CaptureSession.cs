using System.Runtime.InteropServices;
using System.Text;
using DirectShowLib;

namespace UGreenCapViewer;

sealed record VideoFormat(int Index, Guid SubType, int Width, int Height, long Interval)
{
    public double Fps => Interval > 0 ? 10_000_000.0 / Interval : 0;
    public string Key => $"{FourCC}:{Width}x{Height}@{Interval}";

    public string FourCC
    {
        get
        {
            var b = SubType.ToByteArray();
            if (b.Take(4).All(c => c is >= 0x20 and < 0x7F)) return Encoding.ASCII.GetString(b, 0, 4).Trim();
            if (SubType == MediaSubType.RGB24) return "RGB24";
            if (SubType == MediaSubType.RGB32) return "RGB32";
            return "RGB";
        }
    }

    public override string ToString() => $"{Width}×{Height} @ {Fps:0.##} fps ({FourCC})";
}

/// <summary>
/// A live DirectShow preview of one capture device: video into a windowless VMR9 drawn on a host
/// control, plus an optional separate audio graph so audio buffering never holds back the picture.
/// </summary>
sealed class CaptureSession : IDisposable
{
    readonly List<object> comObjects = [];
    IMediaControl? control;
    IMediaEvent? events;
    IVMRWindowlessControl9? windowless;
    IMediaControl? audioControl;
    IBasicAudio? basicAudio;

    public IReadOnlyList<VideoFormat> Formats { get; private set; } = [];
    public VideoFormat? Format { get; private set; }
    public bool HasAudio => basicAudio != null;
    public string? AudioError { get; private set; }

    public bool Muted
    {
        set => basicAudio?.put_Volume(value ? -10000 : 0);
    }

    public void Start(DsDevice video, string? formatKey, DsDevice? audio, Control host)
    {
        var graph = (IFilterGraph2)new FilterGraph();
        comObjects.Add(graph);
        var builder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
        try
        {
            DsError.ThrowExceptionForHR(builder.SetFiltergraph(graph));
            DsError.ThrowExceptionForHR(graph.AddSourceFilterForMoniker(video.Mon, null, video.Name, out IBaseFilter source));
            comObjects.Add(source);

            if (builder.FindInterface(PinCategory.Capture, MediaType.Video, source, typeof(IAMStreamConfig).GUID, out object pin) == 0)
            {
                try
                {
                    var config = (IAMStreamConfig)pin;
                    Formats = EnumerateFormats(config);
                    Format = Formats.FirstOrDefault(f => f.Key == formatKey) ?? PickBest(Formats);
                    if (Format != null) ApplyFormat(config, Format);
                }
                finally { Marshal.ReleaseComObject(pin); }
            }

            var renderer = (IBaseFilter)new VideoMixingRenderer9();
            comObjects.Add(renderer);
            DsError.ThrowExceptionForHR(graph.AddFilter(renderer, "Video Renderer"));
            DsError.ThrowExceptionForHR(((IVMRFilterConfig9)renderer).SetRenderingMode(VMR9Mode.Windowless));
            windowless = (IVMRWindowlessControl9)renderer;
            windowless.SetVideoClippingWindow(host.Handle);
            windowless.SetAspectRatioMode(VMR9AspectRatioMode.LetterBox);
            windowless.SetBorderColor(0);

            DsError.ThrowExceptionForHR(builder.RenderStream(PinCategory.Capture, MediaType.Video, source, null, renderer));

            // No reference clock: frames are drawn the moment they arrive instead of being scheduled.
            ((IMediaFilter)graph).SetSyncSource(null);

            control = (IMediaControl)graph;
            events = (IMediaEvent)graph;
            UpdatePosition(host.ClientRectangle);
            DsError.ThrowExceptionForHR(control.Run());
        }
        finally { Marshal.ReleaseComObject(builder); }

        if (audio != null)
        {
            try { StartAudio(audio); }
            catch (Exception ex)
            {
                AudioError = ex.Message;
                audioControl = null;
                basicAudio = null;
            }
        }
    }

    void StartAudio(DsDevice device)
    {
        var graph = (IFilterGraph2)new FilterGraph();
        comObjects.Add(graph);
        var builder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
        try
        {
            DsError.ThrowExceptionForHR(builder.SetFiltergraph(graph));
            DsError.ThrowExceptionForHR(graph.AddSourceFilterForMoniker(device.Mon, null, device.Name, out IBaseFilter source));
            comObjects.Add(source);

            // Ask for ~20 ms capture buffers so the audio stays close to the picture.
            if (builder.FindInterface(PinCategory.Capture, MediaType.Audio, source, typeof(IAMBufferNegotiation).GUID, out object pin) == 0)
            {
                try
                {
                    if (pin is IAMStreamConfig config && config.GetFormat(out AMMediaType mt) == 0)
                    {
                        try
                        {
                            if (mt.formatType == FormatType.WaveEx && mt.formatPtr != IntPtr.Zero)
                            {
                                var wave = Marshal.PtrToStructure<WaveFormatEx>(mt.formatPtr)!;
                                ((IAMBufferNegotiation)pin).SuggestAllocatorProperties(new AllocatorProperties
                                {
                                    cBuffers = -1,
                                    cbBuffer = Math.Max(wave.nBlockAlign, wave.nAvgBytesPerSec / 50 / wave.nBlockAlign * wave.nBlockAlign),
                                    cbAlign = -1,
                                    cbPrefix = -1,
                                });
                            }
                        }
                        finally { DsUtils.FreeAMMediaType(mt); }
                    }
                }
                finally { Marshal.ReleaseComObject(pin); }
            }

            DsError.ThrowExceptionForHR(builder.RenderStream(PinCategory.Capture, MediaType.Audio, source, null, null));
            audioControl = (IMediaControl)graph;
            basicAudio = (IBasicAudio)graph;
            DsError.ThrowExceptionForHR(audioControl.Run());
        }
        finally { Marshal.ReleaseComObject(builder); }
    }

    public void UpdatePosition(Rectangle bounds) =>
        windowless?.SetVideoPosition(null, new DsRect(bounds));

    public void Repaint(IntPtr hwnd, Graphics g)
    {
        if (windowless == null) return;
        var hdc = g.GetHdc();
        try { windowless.RepaintVideo(hwnd, hdc); }
        finally { g.ReleaseHdc(hdc); }
    }

    public void DisplayModeChanged() => windowless?.DisplayModeChanged();

    /// <summary>Drains graph events; true when the device was unplugged or the stream died.</summary>
    public bool PollLost()
    {
        if (events == null) return false;
        bool lost = false;
        while (events.GetEvent(out EventCode code, out IntPtr p1, out IntPtr p2, 0) == 0)
        {
            events.FreeEventParams(code, p1, p2);
            if (code is EventCode.DeviceLost or EventCode.ErrorAbort) lost = true;
        }
        return lost;
    }

    public void Dispose()
    {
        try { control?.Stop(); } catch { }
        try { audioControl?.Stop(); } catch { }
        control = null;
        events = null;
        windowless = null;
        audioControl = null;
        basicAudio = null;
        foreach (var o in comObjects)
        {
            try { Marshal.FinalReleaseComObject(o); } catch { }
        }
        comObjects.Clear();
    }

    static List<VideoFormat> EnumerateFormats(IAMStreamConfig config)
    {
        var list = new List<VideoFormat>();
        if (config.GetNumberOfCapabilities(out int count, out int size) != 0) return list;
        IntPtr caps = Marshal.AllocCoTaskMem(size);
        try
        {
            for (int i = 0; i < count; i++)
            {
                if (config.GetStreamCaps(i, out AMMediaType mt, caps) != 0) continue;
                try
                {
                    if (!TryReadHeader(mt, out int w, out int h, out long avg)) continue;
                    var vscc = Marshal.PtrToStructure<VideoStreamConfigCaps>(caps)!;
                    long interval = vscc.MinFrameInterval > 0 ? vscc.MinFrameInterval : avg;
                    list.Add(new VideoFormat(i, mt.subType, w, Math.Abs(h), interval));
                }
                finally { DsUtils.FreeAMMediaType(mt); }
            }
        }
        finally { Marshal.FreeCoTaskMem(caps); }

        return list.GroupBy(f => f.Key).Select(g => g.First())
            .OrderByDescending(f => f.Width * f.Height).ThenByDescending(f => f.Fps).ThenBy(f => f.FourCC)
            .ToList();
    }

    /// <summary>Prefer up to 1080p at the highest frame rate, then uncompressed over MJPG.</summary>
    static VideoFormat? PickBest(IReadOnlyList<VideoFormat> formats) => formats
        .OrderByDescending(f => Math.Min((long)f.Width * f.Height, 1920L * 1080))
        .ThenByDescending(f => Math.Round(f.Fps))
        .ThenByDescending(f => (long)f.Width * f.Height)
        .ThenBy(f => f.SubType == MediaSubType.MJPG ? 1 : 0)
        .FirstOrDefault();

    static void ApplyFormat(IAMStreamConfig config, VideoFormat format)
    {
        config.GetNumberOfCapabilities(out _, out int size);
        IntPtr caps = Marshal.AllocCoTaskMem(size);
        try
        {
            DsError.ThrowExceptionForHR(config.GetStreamCaps(format.Index, out AMMediaType mt, caps));
            try
            {
                if (mt.formatType == FormatType.VideoInfo)
                {
                    var v = Marshal.PtrToStructure<VideoInfoHeader>(mt.formatPtr)!;
                    v.AvgTimePerFrame = format.Interval;
                    Marshal.StructureToPtr(v, mt.formatPtr, false);
                }
                else if (mt.formatType == FormatType.VideoInfo2)
                {
                    var v = Marshal.PtrToStructure<VideoInfoHeader2>(mt.formatPtr)!;
                    v.AvgTimePerFrame = format.Interval;
                    Marshal.StructureToPtr(v, mt.formatPtr, false);
                }
                DsError.ThrowExceptionForHR(config.SetFormat(mt));
            }
            finally { DsUtils.FreeAMMediaType(mt); }
        }
        finally { Marshal.FreeCoTaskMem(caps); }
    }

    static bool TryReadHeader(AMMediaType mt, out int width, out int height, out long avg)
    {
        width = height = 0;
        avg = 0;
        if (mt.formatPtr == IntPtr.Zero) return false;
        if (mt.formatType == FormatType.VideoInfo)
        {
            var v = Marshal.PtrToStructure<VideoInfoHeader>(mt.formatPtr)!;
            (width, height, avg) = (v.BmiHeader.Width, v.BmiHeader.Height, v.AvgTimePerFrame);
            return true;
        }
        if (mt.formatType == FormatType.VideoInfo2)
        {
            var v = Marshal.PtrToStructure<VideoInfoHeader2>(mt.formatPtr)!;
            (width, height, avg) = (v.BmiHeader.Width, v.BmiHeader.Height, v.AvgTimePerFrame);
            return true;
        }
        return false;
    }
}
