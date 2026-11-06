using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.App;

/// <summary>Desktop HTTP speed test against Cloudflare's public speed endpoints.</summary>
internal sealed class NetworkSpeedTestForm : Form
{
    private const int DOWNLOAD_BYTES = 10_000_000;
    private const int UPLOAD_BYTES = 2_000_000;
    private const int LATENCY_SAMPLES = 5;
    private const string DOWNLOAD_URL = "https://speed.cloudflare.com/__down?bytes=";
    private const string UPLOAD_URL = "https://speed.cloudflare.com/__up";

    private readonly Translator _t;
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Label _status;
    private readonly Label _download;
    private readonly Label _upload;
    private readonly Label _latency;
    private readonly Label _jitter;
    private readonly ProgressBar _progress;
    private CancellationTokenSource? _cancellation;

    public NetworkSpeedTestForm(Translator t, Font font)
    {
        _t = t;
        Text = t["networkTest.title"];
        Icon = Ui.AppIcon;
        Font = font;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Ui.SPACING * 2);

        var notice = new Label
        {
            Text = t["networkTest.notice"],
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(520), 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, Ui.SPACING),
        };

        _download = ValueLabel();
        _upload = ValueLabel();
        _latency = ValueLabel();
        _jitter = ValueLabel();
        var metrics = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0) };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddMetric(metrics, 0, 0, t["networkTest.download"], _download);
        AddMetric(metrics, 1, 0, t["networkTest.upload"], _upload);
        AddMetric(metrics, 0, 1, t["networkTest.latency"], _latency);
        AddMetric(metrics, 1, 1, t["networkTest.jitter"], _jitter);

        _status = new Label { AutoSize = true, Text = t["networkTest.ready"], Margin = new Padding(0, Ui.SPACING, 0, Ui.SPACING) };
        _progress = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 20, Height = LogicalToDeviceUnits(8), Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, Ui.SPACING), Visible = false };
        _start = Ui.Button();
        _start.Text = t["networkTest.start"];
        _start.Click += (_, _) => _ = RunTestAsync();
        _stop = Ui.Button();
        _stop.Text = t["ui.stop"];
        _stop.Enabled = false;
        _stop.Click += (_, _) => _cancellation?.Cancel();
        var buttons = Ui.Flow();
        buttons.Controls.AddRange([_start, _stop]);

        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0) };
        root.Controls.Add(notice, 0, 0);
        root.Controls.Add(metrics, 0, 1);
        root.Controls.Add(_status, 0, 2);
        root.Controls.Add(_progress, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        Controls.Add(root);
        Ui.ScaleButtons(this);
        FormClosed += (_, _) => _cancellation?.Cancel();
    }

    private static Label ValueLabel() => new()
    {
        AutoSize = true,
        Text = "—",
        Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? Control.DefaultFont.FontFamily, 18f, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, Ui.SPACING),
    };

    private static void AddMetric(TableLayoutPanel layout, int column, int row, string caption, Label value)
    {
        var cell = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, Ui.SPACING, 0) };
        cell.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = SystemColors.GrayText }, 0, 0);
        cell.Controls.Add(value, 0, 1);
        layout.Controls.Add(cell, column, row);
    }

    private async Task RunTestAsync()
    {
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _start.Enabled = false;
        _stop.Enabled = true;
        _progress.Visible = true;
        _download.Text = _upload.Text = _latency.Text = _jitter.Text = "—";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _status.Text = _t["networkTest.latency"];
            var latencies = new List<double>();
            for (var index = 0; index < LATENCY_SAMPLES; index++)
            {
                token.ThrowIfCancellationRequested();
                var watch = Stopwatch.StartNew();
                using var response = await client.GetAsync($"{DOWNLOAD_URL}0", token);
                response.EnsureSuccessStatusCode();
                watch.Stop();
                latencies.Add(watch.Elapsed.TotalMilliseconds);
            }

            var average = latencies.Average();
            var jitter = Math.Sqrt(latencies.Sum(value => Math.Pow(value - average, 2)) / latencies.Count);
            _latency.Text = $"{average.ToString("0", CultureInfo.InvariantCulture)} ms";
            _jitter.Text = $"{jitter.ToString("0", CultureInfo.InvariantCulture)} ms";

            _status.Text = _t["networkTest.download"];
            var downloadWatch = Stopwatch.StartNew();
            using (var response = await client.GetAsync($"{DOWNLOAD_URL}{DOWNLOAD_BYTES}", HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                {
                    total += read;
                    if (total >= DOWNLOAD_BYTES)
                    {
                        break;
                    }
                }

                downloadWatch.Stop();
                _download.Text = $"{ToMbps(total, downloadWatch.Elapsed):0.#} Mbps";
            }

            _status.Text = _t["networkTest.upload"];
            using (var content = new ByteArrayContent(new byte[UPLOAD_BYTES]))
            {
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                var uploadWatch = Stopwatch.StartNew();
                using var response = await client.PostAsync(UPLOAD_URL, content, token);
                response.EnsureSuccessStatusCode();
                uploadWatch.Stop();
                _upload.Text = $"{ToMbps(UPLOAD_BYTES, uploadWatch.Elapsed):0.#} Mbps";
            }

            _status.Text = _t["networkTest.done"];
        }
        catch (OperationCanceledException)
        {
            _status.Text = _t["networkTest.cancelled"];
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            if (!IsDisposed)
            {
                _status.Text = _t["networkTest.failed", exception.Message];
            }
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            if (!IsDisposed)
            {
                _progress.Visible = false;
                _start.Enabled = true;
                _stop.Enabled = false;
            }
        }
    }

    private static double ToMbps(long bytes, TimeSpan elapsed) =>
        elapsed.TotalSeconds <= 0 ? 0 : bytes * 8d / 1_000_000d / elapsed.TotalSeconds;
}
