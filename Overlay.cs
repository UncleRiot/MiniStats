using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Windows.Forms;

namespace MiniStats
{
    public sealed partial class Overlay : Form
    {
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly HardwareMonitorService hardwareMonitorService;
        private readonly ContextMenuStrip overlayMenu;
        private readonly ContextMenuStrip trayMenu;
        private readonly NotifyIcon notifyIcon;
        private readonly string settingsFilePath = Path.Combine(AppContext.BaseDirectory, "overlay.config.json");
        private readonly string trayBaseIconPath = Path.Combine(AppContext.BaseDirectory, "ministats_icon.ico");

        private bool dragging;
        private Point dragCursorPoint;
        private Point dragFormPoint;

        private int fontSize = 16;
        private int backgroundOpacityPercent = 70;

        private bool showFps = true;
        private bool showCpu = true;
        private bool showGpu = true;
        private bool showCpuInTrayIcon;
        private bool showGpuInTrayIcon;
        private bool startInSystray;
        private bool displayHorizontal;
        private readonly bool isRunningAsAdministrator;
        private bool trayIconShowCpuNext = true;

        private Color cpuOverlayValueColor = Color.White;
        private Color gpuOverlayValueColor = Color.White;
        private Color cpuTrayValueColor = Color.FromArgb(255, 0, 255, 0);
        private Color gpuTrayValueColor = Color.FromArgb(255, 255, 160, 40);

        private string fpsText = "-";
        private string cpuTemperatureText = "-";
        private string gpuTemperatureText = "-";

        private sealed class OverlaySettings
        {
            public int FontSize { get; set; } = 16;
            public int BackgroundOpacityPercent { get; set; } = 70;
            public int LocationX { get; set; } = 50;
            public int LocationY { get; set; } = 50;
            public bool ShowFps { get; set; } = true;
            public bool ShowCpu { get; set; } = true;
            public bool ShowGpu { get; set; } = true;
            public bool ShowCpuInTrayIcon { get; set; }
            public bool ShowGpuInTrayIcon { get; set; }
            public bool StartInSystray { get; set; }
            public bool DisplayHorizontal { get; set; }
            public int BoxWidth { get; set; } = 170;
            public int BoxSpacing { get; set; } = 8;
            public int CpuOverlayValueColorArgb { get; set; } = unchecked((int)0xFFFFFFFF);
            public int GpuOverlayValueColorArgb { get; set; } = unchecked((int)0xFFFFFFFF);
            public int CpuTrayValueColorArgb { get; set; } = unchecked((int)0xFF00FF00);
            public int GpuTrayValueColorArgb { get; set; } = unchecked((int)0xFFFFA028);
        }

        public Overlay()
        {
            isRunningAsAdministrator = IsRunningAsAdmin();

            TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(50, 50);
            ShowInTaskbar = true;
            DoubleBuffered = true;
            BackColor = Color.Black;
            TransparencyKey = Color.Empty;

            Icon? appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (appIcon != null)
            {
                Icon = (Icon)appIcon.Clone();
                appIcon.Dispose();
            }

            LoadSettings();
            UpdateOverlayBounds();

            hardwareMonitorService = new HardwareMonitorService();

            overlayMenu = new ContextMenuStrip();
            overlayMenu.Items.Add("Settings", null, OpenConfig);
            overlayMenu.Items.Add("Exit", null, CloseOverlay);

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show", null, ShowOverlayFromTray);
            trayMenu.Items.Add("Settings", null, OpenConfig);
            trayMenu.Items.Add("Exit", null, CloseOverlay);

            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = CreateTrayIcon(null, null);
            notifyIcon.Text = "MiniStats";
            notifyIcon.Visible = false;
            notifyIcon.ContextMenuStrip = trayMenu;
            notifyIcon.DoubleClick += ShowOverlayFromTray;

            MouseDown += StartDrag;
            MouseMove += DoDrag;
            MouseUp += EndDragOrMenu;
            DoubleClick += CloseOverlay;
            FormClosing += Overlay_FormClosing;

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 1000;
            refreshTimer.Tick += RefreshTimer_Tick;
            refreshTimer.Start();

            if (!isRunningAsAdministrator)
            {
                MessageBox.Show(
                    "Some Features do only work, if started as admin!",
                    "MiniStats",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            RefreshOverlay();

            if (startInSystray)
            {
                HideToTray();
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams createParams = base.CreateParams;
                createParams.ExStyle |= NativeMethods.WS_EX_LAYERED;
                return createParams;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RenderLayered();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            RenderLayered();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RenderLayered();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            RefreshOverlay();
        }

        private void Overlay_FormClosing(object? sender, FormClosingEventArgs e)
        {
            refreshTimer.Stop();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            trayMenu.Dispose();
            SaveSettingsSafe();
            hardwareMonitorService.Dispose();
        }

        private void RefreshOverlay()
        {
            HardwareSnapshot hardwareSnapshot = hardwareMonitorService.ReadSnapshot();

            fpsText = hardwareSnapshot.Fps.HasValue
                ? $"{Math.Round(hardwareSnapshot.Fps.Value)}"
                : "-";

            cpuTemperatureText = hardwareSnapshot.CpuTemperature.HasValue
                ? $"{Math.Round(hardwareSnapshot.CpuTemperature.Value)} °C"
                : "-";

            gpuTemperatureText = hardwareSnapshot.GpuTemperature.HasValue
                ? $"{Math.Round(hardwareSnapshot.GpuTemperature.Value)} °C"
                : "-";

            UpdateTrayIcon(hardwareSnapshot);
            UpdateOverlayBounds();
            RenderLayered();
        }

        private void CloseOverlay(object? sender, EventArgs e)
        {
            Close();
        }

        private void StartDrag(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            dragging = true;
            dragCursorPoint = Cursor.Position;
            dragFormPoint = Location;
        }

        private void DoDrag(object? sender, MouseEventArgs e)
        {
            if (!dragging)
            {
                return;
            }

            Point diff = Point.Subtract(Cursor.Position, new Size(dragCursorPoint));
            Location = Point.Add(dragFormPoint, new Size(diff));
        }

        private void EndDragOrMenu(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = false;
                SaveSettingsSafe();
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                overlayMenu.Show(this, e.Location);
            }
        }

        private void OpenConfig(object? sender, EventArgs e)
        {
            int originalFontSize = fontSize;
            int originalBackgroundOpacityPercent = backgroundOpacityPercent;
            bool originalShowFps = showFps;
            bool originalShowCpu = showCpu;
            bool originalShowGpu = showGpu;
            bool originalShowCpuInTrayIcon = showCpuInTrayIcon;
            bool originalShowGpuInTrayIcon = showGpuInTrayIcon;
            bool originalStartInSystray = startInSystray;
            bool originalDisplayHorizontal = displayHorizontal;
            int originalBoxWidth = boxWidth;
            int originalBoxSpacing = boxSpacing;
            Color originalCpuOverlayValueColor = cpuOverlayValueColor;
            Color originalGpuOverlayValueColor = gpuOverlayValueColor;
            Color originalCpuTrayValueColor = cpuTrayValueColor;
            Color originalGpuTrayValueColor = gpuTrayValueColor;

            using Form configForm = new Form();
            configForm.Text = "MiniStats settings - © D. Capilla 2026";
            configForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            configForm.StartPosition = FormStartPosition.Manual;
            configForm.ClientSize = new Size(300, 390);
            configForm.MaximizeBox = false;
            configForm.MinimizeBox = false;
            configForm.ShowInTaskbar = false;
            configForm.AutoScaleMode = AutoScaleMode.Font;
            configForm.Location = new Point(Right + 12, Top);

            Label fontLabel = new Label
            {
                Text = "Font Size",
                Left = 12,
                Top = 16,
                Width = 120
            };

            NumericUpDown fontUpDown = new NumericUpDown
            {
                Left = 140,
                Top = 13,
                Width = 120,
                Minimum = 10,
                Maximum = 32,
                Value = fontSize
            };

            fontUpDown.ValueChanged += (_, _) =>
            {
                fontSize = (int)fontUpDown.Value;
                UpdateOverlayBounds();
                RenderLayered();
            };

            Label opacityLabel = new Label
            {
                Text = "Opacity (%)",
                Left = 12,
                Top = 54,
                Width = 120
            };

            TrackBar opacityTrackBar = new TrackBar
            {
                Left = 140,
                Top = 48,
                Width = 90,
                Height = 24,
                AutoSize = false,
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 1,
                LargeChange = 10,
                Value = Math.Max(0, Math.Min(100, backgroundOpacityPercent))
            };

            Label opacityValueLabel = new Label
            {
                Text = $"{opacityTrackBar.Value}%",
                Left = 235,
                Top = 54,
                Width = 35
            };

            opacityTrackBar.ValueChanged += (_, _) =>
            {
                backgroundOpacityPercent = opacityTrackBar.Value;
                opacityValueLabel.Text = $"{opacityTrackBar.Value}%";
                RenderLayered();
            };

            Label fpsLabel = new Label
            {
                Text = "FPS",
                Left = 12,
                Top = 92,
                Width = 120
            };

            CheckBox fpsCheckBox = new CheckBox
            {
                Left = 140,
                Top = 90,
                Width = 18,
                Height = 24,
                Checked = showFps
            };

            fpsCheckBox.CheckedChanged += (_, _) =>
            {
                showFps = fpsCheckBox.Checked;
                UpdateOverlayBounds();
                RenderLayered();
            };

            Label cpuLabel = new Label
            {
                Text = "CPU",
                Left = 12,
                Top = 120,
                Width = 120
            };

            CheckBox cpuCheckBox = new CheckBox
            {
                Left = 140,
                Top = 118,
                Width = 18,
                Height = 24,
                Checked = showCpu,
                Enabled = isRunningAsAdministrator
            };

            cpuCheckBox.CheckedChanged += (_, _) =>
            {
                showCpu = cpuCheckBox.Checked;
                UpdateOverlayBounds();
                RenderLayered();
            };

            CheckBox cpuTrayCheckBox = new CheckBox
            {
                Left = 165,
                Top = 118,
                Width = 18,
                Height = 24,
                Checked = showCpuInTrayIcon
            };

            cpuTrayCheckBox.CheckedChanged += (_, _) =>
            {
                showCpuInTrayIcon = cpuTrayCheckBox.Checked;
            };

            Panel cpuOverlayColorPanel = CreateColorPanel(190, 119, cpuOverlayValueColor);
            Panel cpuTrayColorPanel = CreateColorPanel(215, 119, cpuTrayValueColor);

            cpuOverlayColorPanel.Click += (_, _) => SelectCpuOverlayValueColor(cpuOverlayColorPanel);
            cpuTrayColorPanel.Click += (_, _) => SelectCpuTrayValueColor(cpuTrayColorPanel);

            Label gpuLabel = new Label
            {
                Text = "GPU",
                Left = 12,
                Top = 148,
                Width = 120
            };

            CheckBox gpuCheckBox = new CheckBox
            {
                Left = 140,
                Top = 146,
                Width = 18,
                Height = 24,
                Checked = showGpu
            };

            gpuCheckBox.CheckedChanged += (_, _) =>
            {
                showGpu = gpuCheckBox.Checked;
                UpdateOverlayBounds();
                RenderLayered();
            };

            CheckBox gpuTrayCheckBox = new CheckBox
            {
                Left = 165,
                Top = 146,
                Width = 18,
                Height = 24,
                Checked = showGpuInTrayIcon
            };

            gpuTrayCheckBox.CheckedChanged += (_, _) =>
            {
                showGpuInTrayIcon = gpuTrayCheckBox.Checked;
            };

            Panel gpuOverlayColorPanel = CreateColorPanel(190, 147, gpuOverlayValueColor);
            Panel gpuTrayColorPanel = CreateColorPanel(215, 147, gpuTrayValueColor);

            gpuOverlayColorPanel.Click += (_, _) => SelectGpuOverlayValueColor(gpuOverlayColorPanel);
            gpuTrayColorPanel.Click += (_, _) => SelectGpuTrayValueColor(gpuTrayColorPanel);

            Label startInSystrayLabel = new Label
            {
                Text = "Start in Systray",
                Left = 12,
                Top = 176,
                Width = 120
            };

            CheckBox startInSystrayCheckBox = new CheckBox
            {
                Left = 140,
                Top = 174,
                Width = 18,
                Height = 24,
                Checked = startInSystray
            };

            startInSystrayCheckBox.CheckedChanged += (_, _) =>
            {
                startInSystray = startInSystrayCheckBox.Checked;
            };

            Label displayLabel = new Label
            {
                Text = "Display",
                Left = 12,
                Top = 208,
                Width = 120
            };

            ComboBox displayComboBox = new ComboBox
            {
                Left = 140,
                Top = 205,
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            displayComboBox.Items.Add("Vertical");
            displayComboBox.Items.Add("Horizontal");
            displayComboBox.SelectedItem = displayHorizontal ? "Horizontal" : "Vertical";

            displayComboBox.SelectedIndexChanged += (_, _) =>
            {
                displayHorizontal = string.Equals(displayComboBox.SelectedItem?.ToString(), "Horizontal", StringComparison.Ordinal);
                UpdateOverlayBounds();
                RenderLayered();
            };

            Label widthLabel = new Label
            {
                Text = "Box Width",
                Left = 12,
                Top = 240,
                Width = 120
            };

            NumericUpDown widthUpDown = new NumericUpDown
            {
                Left = 140,
                Top = 237,
                Width = 120,
                Minimum = 80,
                Maximum = 400,
                Value = boxWidth
            };

            widthUpDown.ValueChanged += (_, _) =>
            {
                boxWidth = (int)widthUpDown.Value;
                UpdateOverlayBounds();
                RenderLayered();
            };

            Label spacingLabel = new Label
            {
                Text = "Table Spacing",
                Left = 12,
                Top = 272,
                Width = 120
            };

            NumericUpDown spacingUpDown = new NumericUpDown
            {
                Left = 140,
                Top = 269,
                Width = 120,
                Minimum = 0,
                Maximum = 50,
                Value = boxSpacing
            };

            spacingUpDown.ValueChanged += (_, _) =>
            {
                boxSpacing = (int)spacingUpDown.Value;
                UpdateOverlayBounds();
                RenderLayered();
            };

            Button okButton = new Button
            {
                Text = "OK",
                Left = 130,
                Top = 340,
                Width = 60,
                Height = 26,
                DialogResult = DialogResult.OK
            };

            Button cancelButton = new Button
            {
                Text = "Cancel",
                Left = 200,
                Top = 340,
                Width = 60,
                Height = 26,
                DialogResult = DialogResult.Cancel
            };

            configForm.Controls.Add(fontLabel);
            configForm.Controls.Add(fontUpDown);
            configForm.Controls.Add(opacityLabel);
            configForm.Controls.Add(opacityTrackBar);
            configForm.Controls.Add(opacityValueLabel);
            configForm.Controls.Add(fpsLabel);
            configForm.Controls.Add(fpsCheckBox);
            configForm.Controls.Add(cpuLabel);
            configForm.Controls.Add(cpuCheckBox);
            configForm.Controls.Add(cpuTrayCheckBox);
            configForm.Controls.Add(cpuOverlayColorPanel);
            configForm.Controls.Add(cpuTrayColorPanel);
            configForm.Controls.Add(gpuLabel);
            configForm.Controls.Add(gpuCheckBox);
            configForm.Controls.Add(gpuTrayCheckBox);
            configForm.Controls.Add(gpuOverlayColorPanel);
            configForm.Controls.Add(gpuTrayColorPanel);
            configForm.Controls.Add(startInSystrayLabel);
            configForm.Controls.Add(startInSystrayCheckBox);
            configForm.Controls.Add(displayLabel);
            configForm.Controls.Add(displayComboBox);
            configForm.Controls.Add(widthLabel);
            configForm.Controls.Add(widthUpDown);
            configForm.Controls.Add(spacingLabel);
            configForm.Controls.Add(spacingUpDown);
            configForm.Controls.Add(okButton);
            configForm.Controls.Add(cancelButton);

            configForm.AcceptButton = okButton;
            configForm.CancelButton = cancelButton;

            DialogResult result = configForm.ShowDialog();

            if (result == DialogResult.OK)
            {
                fontSize = (int)fontUpDown.Value;
                backgroundOpacityPercent = opacityTrackBar.Value;
                showFps = fpsCheckBox.Checked;
                showCpu = cpuCheckBox.Checked;
                showGpu = gpuCheckBox.Checked;
                showCpuInTrayIcon = cpuTrayCheckBox.Checked;
                showGpuInTrayIcon = gpuTrayCheckBox.Checked;
                startInSystray = startInSystrayCheckBox.Checked;
                displayHorizontal = string.Equals(displayComboBox.SelectedItem?.ToString(), "Horizontal", StringComparison.Ordinal);
                boxWidth = (int)widthUpDown.Value;
                boxSpacing = (int)spacingUpDown.Value;

                SaveSettingsSafe();
                RefreshOverlay();
                return;
            }

            fontSize = originalFontSize;
            backgroundOpacityPercent = originalBackgroundOpacityPercent;
            showFps = originalShowFps;
            showCpu = originalShowCpu;
            showGpu = originalShowGpu;
            showCpuInTrayIcon = originalShowCpuInTrayIcon;
            showGpuInTrayIcon = originalShowGpuInTrayIcon;
            startInSystray = originalStartInSystray;
            displayHorizontal = originalDisplayHorizontal;
            boxWidth = originalBoxWidth;
            boxSpacing = originalBoxSpacing;
            cpuOverlayValueColor = originalCpuOverlayValueColor;
            gpuOverlayValueColor = originalGpuOverlayValueColor;
            cpuTrayValueColor = originalCpuTrayValueColor;
            gpuTrayValueColor = originalGpuTrayValueColor;

            RefreshOverlay();
        }

        private Panel CreateColorPanel(int left, int top, Color color)
        {
            Panel panel = new Panel
            {
                Left = left,
                Top = top,
                Width = 18,
                Height = 18,
                BackColor = color,
                Cursor = Cursors.Hand
            };

            panel.Paint += (_, paintEventArgs) =>
            {
                paintEventArgs.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using SolidBrush brush = new SolidBrush(panel.BackColor);
                using Pen pen = new Pen(Color.DimGray);
                paintEventArgs.Graphics.FillEllipse(brush, 1, 1, 15, 15);
                paintEventArgs.Graphics.DrawEllipse(pen, 1, 1, 15, 15);
            };

            return panel;
        }

        private void SelectCpuOverlayValueColor(Panel panel)
        {
            using ColorDialog colorDialog = new ColorDialog();
            colorDialog.FullOpen = true;
            colorDialog.Color = cpuOverlayValueColor;

            if (colorDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            cpuOverlayValueColor = colorDialog.Color;
            panel.BackColor = cpuOverlayValueColor;
            panel.Invalidate();
            RenderLayered();
        }

        private void SelectGpuOverlayValueColor(Panel panel)
        {
            using ColorDialog colorDialog = new ColorDialog();
            colorDialog.FullOpen = true;
            colorDialog.Color = gpuOverlayValueColor;

            if (colorDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            gpuOverlayValueColor = colorDialog.Color;
            panel.BackColor = gpuOverlayValueColor;
            panel.Invalidate();
            RenderLayered();
        }

        private void SelectCpuTrayValueColor(Panel panel)
        {
            using ColorDialog colorDialog = new ColorDialog();
            colorDialog.FullOpen = true;
            colorDialog.Color = cpuTrayValueColor;

            if (colorDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            cpuTrayValueColor = colorDialog.Color;
            panel.BackColor = cpuTrayValueColor;
            panel.Invalidate();
            RefreshOverlay();
        }

        private void SelectGpuTrayValueColor(Panel panel)
        {
            using ColorDialog colorDialog = new ColorDialog();
            colorDialog.FullOpen = true;
            colorDialog.Color = gpuTrayValueColor;

            if (colorDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            gpuTrayValueColor = colorDialog.Color;
            panel.BackColor = gpuTrayValueColor;
            panel.Invalidate();
            RefreshOverlay();
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsFilePath))
                {
                    return;
                }

                string json = File.ReadAllText(settingsFilePath);
                OverlaySettings? settings = JsonSerializer.Deserialize<OverlaySettings>(json);

                if (settings == null)
                {
                    return;
                }

                fontSize = Math.Max(10, Math.Min(32, settings.FontSize));
                backgroundOpacityPercent = Math.Max(0, Math.Min(100, settings.BackgroundOpacityPercent));
                showFps = settings.ShowFps;
                showCpu = settings.ShowCpu;
                showGpu = settings.ShowGpu;
                showCpuInTrayIcon = settings.ShowCpuInTrayIcon;
                showGpuInTrayIcon = settings.ShowGpuInTrayIcon;
                startInSystray = settings.StartInSystray;
                displayHorizontal = settings.DisplayHorizontal;
                boxWidth = settings.BoxWidth;
                boxSpacing = settings.BoxSpacing;
                cpuOverlayValueColor = Color.FromArgb(settings.CpuOverlayValueColorArgb);
                gpuOverlayValueColor = Color.FromArgb(settings.GpuOverlayValueColorArgb);
                cpuTrayValueColor = Color.FromArgb(settings.CpuTrayValueColorArgb);
                gpuTrayValueColor = Color.FromArgb(settings.GpuTrayValueColorArgb);
                Location = new Point(settings.LocationX, settings.LocationY);
            }
            catch
            {
                fontSize = 16;
                backgroundOpacityPercent = 70;
                showFps = true;
                showCpu = true;
                showGpu = true;
                showCpuInTrayIcon = false;
                showGpuInTrayIcon = false;
                startInSystray = false;
                displayHorizontal = false;
                boxWidth = 170;
                boxSpacing = 8;
                cpuOverlayValueColor = Color.White;
                gpuOverlayValueColor = Color.White;
                cpuTrayValueColor = Color.FromArgb(255, 0, 255, 0);
                gpuTrayValueColor = Color.FromArgb(255, 255, 160, 40);
                Location = new Point(50, 50);
            }
        }

        private void SaveSettingsSafe()
        {
            try
            {
                OverlaySettings settings = new OverlaySettings
                {
                    FontSize = fontSize,
                    BackgroundOpacityPercent = backgroundOpacityPercent,
                    LocationX = Left,
                    LocationY = Top,
                    ShowFps = showFps,
                    ShowCpu = showCpu,
                    ShowGpu = showGpu,
                    ShowCpuInTrayIcon = showCpuInTrayIcon,
                    ShowGpuInTrayIcon = showGpuInTrayIcon,
                    StartInSystray = startInSystray,
                    DisplayHorizontal = displayHorizontal,
                    BoxWidth = boxWidth,
                    BoxSpacing = boxSpacing,
                    CpuOverlayValueColorArgb = cpuOverlayValueColor.ToArgb(),
                    GpuOverlayValueColorArgb = gpuOverlayValueColor.ToArgb(),
                    CpuTrayValueColorArgb = cpuTrayValueColor.ToArgb(),
                    GpuTrayValueColorArgb = gpuTrayValueColor.ToArgb()
                };

                JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(settings, jsonSerializerOptions);
                File.WriteAllText(settingsFilePath, json);
            }
            catch
            {
            }
        }

        private void RenderLayered()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
            {
                return;
            }

            using Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);

            graphics.Clear(Color.Transparent);
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int backgroundAlpha = (int)Math.Round(255.0 * backgroundOpacityPercent / 100.0);

            if (backgroundAlpha > 0)
            {
                using Brush backgroundBrush = new SolidBrush(Color.FromArgb(backgroundAlpha, 10, 10, 10));
                graphics.FillRectangle(backgroundBrush, ClientRectangle);
            }

            RenderDefaultLayout(graphics);
            ApplyBitmap(bitmap);
        }

        private void ApplyBitmap(Bitmap bitmap)
        {
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                NativeMethods.SIZE size = new NativeMethods.SIZE(bitmap.Width, bitmap.Height);
                NativeMethods.POINT sourcePoint = new NativeMethods.POINT(0, 0);
                NativeMethods.POINT topPos = new NativeMethods.POINT(Left, Top);

                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };

                NativeMethods.UpdateLayeredWindow(
                    Handle,
                    screenDc,
                    ref topPos,
                    ref size,
                    memDc,
                    ref sourcePoint,
                    0,
                    ref blend,
                    NativeMethods.ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldBitmap);
                }

                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                }

                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void ShowOverlayFromTray(object? sender, EventArgs e)
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            notifyIcon.Visible = false;
            Activate();
            RenderLayered();
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
            notifyIcon.Visible = true;
        }

        private Icon CreateTrayIcon(string? trayText, Color? trayColor)
        {
            if (string.IsNullOrWhiteSpace(trayText))
            {
                if (File.Exists(trayBaseIconPath))
                {
                    return new Icon(trayBaseIconPath, new Size(32, 32));
                }

                Icon? executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (executableIcon != null)
                {
                    return (Icon)executableIcon.Clone();
                }

                return (Icon)SystemIcons.Application.Clone();
            }

            using Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);

            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            string displayText = trayText.Length > 2 ? trayText.Substring(0, 2) : trayText;

            using Font font = new Font("Tahoma", 20f, FontStyle.Regular, GraphicsUnit.Pixel);
            using Brush textBrush = new SolidBrush(trayColor ?? Color.White);
            using StringFormat stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };

            Rectangle textRect = new Rectangle(0, 0, 32, 32);
            graphics.DrawString(displayText, font, textBrush, textRect, stringFormat);

            IntPtr hIcon = bitmap.GetHicon();

            try
            {
                using Icon icon = Icon.FromHandle(hIcon);
                return (Icon)icon.Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }

        private void UpdateTrayIcon(HardwareSnapshot hardwareSnapshot)
        {
            string? trayText = null;
            Color? trayColor = null;

            if (showCpuInTrayIcon && showGpuInTrayIcon)
            {
                if (trayIconShowCpuNext && hardwareSnapshot.CpuTemperature.HasValue)
                {
                    trayText = $"{Math.Round(hardwareSnapshot.CpuTemperature.Value):0}";
                    trayColor = cpuTrayValueColor;
                }
                else if (hardwareSnapshot.GpuTemperature.HasValue)
                {
                    trayText = $"{Math.Round(hardwareSnapshot.GpuTemperature.Value):0}";
                    trayColor = gpuTrayValueColor;
                }
                else if (hardwareSnapshot.CpuTemperature.HasValue)
                {
                    trayText = $"{Math.Round(hardwareSnapshot.CpuTemperature.Value):0}";
                    trayColor = cpuTrayValueColor;
                }

                trayIconShowCpuNext = !trayIconShowCpuNext;
            }
            else if (showCpuInTrayIcon && hardwareSnapshot.CpuTemperature.HasValue)
            {
                trayText = $"{Math.Round(hardwareSnapshot.CpuTemperature.Value):0}";
                trayColor = cpuTrayValueColor;
            }
            else if (showGpuInTrayIcon && hardwareSnapshot.GpuTemperature.HasValue)
            {
                trayText = $"{Math.Round(hardwareSnapshot.GpuTemperature.Value):0}";
                trayColor = gpuTrayValueColor;
            }

            Icon newIcon = CreateTrayIcon(trayText, trayColor);
            Icon? oldIcon = notifyIcon.Icon;
            notifyIcon.Icon = newIcon;

            if (oldIcon != null)
            {
                oldIcon.Dispose();
            }
        }

        private static bool IsRunningAsAdmin()
        {
            using WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent();
            WindowsPrincipal windowsPrincipal = new WindowsPrincipal(windowsIdentity);
            return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static class NativeMethods
        {
            public const int WS_EX_LAYERED = 0x00080000;
            public const int ULW_ALPHA = 0x00000002;
            public const byte AC_SRC_OVER = 0x00;
            public const byte AC_SRC_ALPHA = 0x01;

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int X;
                public int Y;

                public POINT(int x, int y)
                {
                    X = x;
                    Y = y;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct SIZE
            {
                public int cx;
                public int cy;

                public SIZE(int cx, int cy)
                {
                    this.cx = cx;
                    this.cy = cy;
                }
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct BLENDFUNCTION
            {
                public byte BlendOp;
                public byte BlendFlags;
                public byte SourceConstantAlpha;
                public byte AlphaFormat;
            }

            [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll", ExactSpelling = true)]
            public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

            [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern IntPtr CreateCompatibleDC(IntPtr hDc);

            [DllImport("gdi32.dll", ExactSpelling = true)]
            public static extern bool DeleteDC(IntPtr hDc);

            [DllImport("gdi32.dll", ExactSpelling = true)]
            public static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

            [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern bool DeleteObject(IntPtr hObject);

            [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern bool UpdateLayeredWindow(
                IntPtr hwnd,
                IntPtr hdcDst,
                ref POINT pptDst,
                ref SIZE psize,
                IntPtr hdcSrc,
                ref POINT pprSrc,
                int crKey,
                ref BLENDFUNCTION pblend,
                int dwFlags);

            [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern bool DestroyIcon(IntPtr hIcon);
        }
    }
}