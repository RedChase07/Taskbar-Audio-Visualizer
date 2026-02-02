using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace TaskbarVisualizer
{
    public class SettingsForm : Form
    {
        private TabControl tabControl;
        private TabPage visualizationTab;
        private TabPage windowTab;
        private TabPage colorTab;
        
        // Visualization Tab Controls
        private Label deviceLabel;
        private ComboBox deviceComboBox;
        private Label barsLabel;
        private NumericUpDown barsNumeric;
        private Label smoothingLabel;
        private TrackBar smoothingTrackBar;
        private Label smoothingValueLabel;
        private Label decayLabel;
        private NumericUpDown decayNumeric;
        private Label idleTimeoutLabel;
        private NumericUpDown idleTimeoutNumeric;
        
        // Window Tab Controls
        private Label windowPosLabel;
        private Label windowPosValueLabel;
        private Label windowXLabel;
        private NumericUpDown windowXNumeric;
        private Label windowYLabel;
        private NumericUpDown windowYNumeric;
        private Label windowWidthLabel;
        private NumericUpDown windowWidthNumeric;
        private Label windowHeightLabel;
        private NumericUpDown windowHeightNumeric;
        private Button resetWindowButton;
        
        // Color Tab Controls
        private Label colorModeLabel;
        private ComboBox colorModeCombo;
        private Label rainbowDirectionLabel;
        private ComboBox rainbowDirectionCombo;
        private Label waveSpeedLabel;
        private TrackBar waveSpeedTrack;
        private Label waveSpeedValueLabel;
        private Label backgroundAlphaLabel;
        private TrackBar backgroundAlphaTrack;
        private Label backgroundAlphaValueLabel;
        private Label colorLowLabel;
        private Button colorLowButton;
        private Label colorHighLabel;
        private Button colorHighButton;
        
        // Window Tab Click-Through Control
        private CheckBox clickThroughCheckBox;
        
        // OK button for saving settings
        private Button okButton;

        public string SelectedDevice { get; set; } = "";
        public int NumberOfBars { get; set; } = 48;
        public float Smoothing { get; set; } = 0.5f;
        public float DecayRate { get; set; } = 0.95f;
        public int IdleTimeoutMs { get; set; } = 2000;
        public int WindowX { get; set; } = 0;
        public int WindowY { get; set; } = 721;
        public int WindowWidth { get; set; } = 1920;
        public int WindowHeight { get; set; } = 45;
        public int ColorMode { get; set; } = 1;  // 0=Default, 1=Custom, 2=RGBGradient, 3=RainbowWave
        public int RainbowDirection { get; set; } = 0;  // 0=Left-to-Right, 1=Right-to-Left
        public float WaveSpeed { get; set; } = 1.0f;  // 0.5-2.0 animation speed
        public byte BackgroundAlpha { get; set; } = 255;  // 0-255 transparency
        public Color BarColorLow { get; set; } = Color.Cyan;
        public Color BarColorHigh { get; set; } = Color.Magenta;
        public bool ClickThrough { get; set; } = false;  // Enable window click-through

        public SettingsForm()
        {
            InitializeComponent();
            LoadAudioDevices();
        }

        private void InitializeComponent()
        {
            this.Text = "Taskbar Visualizer Settings";
            this.Width = 450;
            this.Height = 620;  // Increased from 550 to prevent clipping
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Create Tab Control
            tabControl = new TabControl();
            tabControl.Location = new Point(10, 10);
            tabControl.Width = 420;
            tabControl.Height = 470;
            tabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(tabControl);

            // ===== VISUALIZATION TAB =====
            visualizationTab = new TabPage("Visualization");
            int y = 20;

            // Audio Device
            deviceLabel = new Label { Text = "Audio Device:", Location = new Point(20, y), Width = 100 };
            visualizationTab.Controls.Add(deviceLabel);
            deviceComboBox = new ComboBox { Location = new Point(130, y), Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            visualizationTab.Controls.Add(deviceComboBox);
            y += 40;

            // Number of Bars
            barsLabel = new Label { Text = "Number of Bars:", Location = new Point(20, y), Width = 100 };
            visualizationTab.Controls.Add(barsLabel);
            barsNumeric = new NumericUpDown { Location = new Point(130, y), Width = 100, Minimum = 16, Maximum = 128, Value = 48 };
            visualizationTab.Controls.Add(barsNumeric);
            y += 40;

            // Smoothing
            smoothingLabel = new Label { Text = "Smoothing:", Location = new Point(20, y), Width = 100 };
            visualizationTab.Controls.Add(smoothingLabel);
            smoothingTrackBar = new TrackBar { Location = new Point(130, y), Width = 150, Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 10 };
            smoothingTrackBar.Scroll += (s, e) => UpdateSmoothingLabel();
            visualizationTab.Controls.Add(smoothingTrackBar);
            smoothingValueLabel = new Label { Location = new Point(290, y), Width = 60 };
            visualizationTab.Controls.Add(smoothingValueLabel);
            UpdateSmoothingLabel();
            y += 40;

            // Decay Rate
            decayLabel = new Label { Text = "Decay Rate:", Location = new Point(20, y), Width = 100 };
            visualizationTab.Controls.Add(decayLabel);
            decayNumeric = new NumericUpDown { Location = new Point(130, y), Width = 100, Minimum = 0, Maximum = 100, Value = 95, DecimalPlaces = 0 };
            visualizationTab.Controls.Add(decayNumeric);
            y += 40;

            // Idle Timeout
            idleTimeoutLabel = new Label { Text = "Idle Timeout (ms):", Location = new Point(20, y), Width = 100 };
            visualizationTab.Controls.Add(idleTimeoutLabel);
            idleTimeoutNumeric = new NumericUpDown { Location = new Point(130, y), Width = 100, Minimum = 100, Maximum = 10000, Value = 2000 };
            visualizationTab.Controls.Add(idleTimeoutNumeric);

            tabControl.TabPages.Add(visualizationTab);

            // ===== WINDOW TAB =====
            windowTab = new TabPage("Window");
            y = 20;

            windowPosLabel = new Label { Text = "Window Pos:", Location = new Point(20, y), Width = 100 };
            windowTab.Controls.Add(windowPosLabel);
            windowPosValueLabel = new Label { Location = new Point(130, y), Width = 250, Text = "X:0, Y:0, W:0, H:0" };
            windowTab.Controls.Add(windowPosValueLabel);
            y += 30;

            windowXLabel = new Label { Text = "Window X:", Location = new Point(20, y), Width = 100 };
            windowTab.Controls.Add(windowXLabel);
            windowXNumeric = new NumericUpDown { Location = new Point(130, y), Width = 100, Minimum = -3840, Maximum = 3840, Value = 0 };
            windowTab.Controls.Add(windowXNumeric);
            y += 40;

            windowYLabel = new Label { Text = "Window Y:", Location = new Point(20, y), Width = 100 };
            windowTab.Controls.Add(windowYLabel);
            windowYNumeric = new NumericUpDown { Location = new Point(130, y), Width = 100, Minimum = -2160, Maximum = 2160, Value = 721 };
            windowTab.Controls.Add(windowYNumeric);
            y += 40;

            windowWidthLabel = new Label { Text = "Width:", Location = new Point(20, y), Width = 100 };
            windowTab.Controls.Add(windowWidthLabel);
            windowWidthNumeric = new NumericUpDown { Location = new Point(130, y), Width = 100, Minimum = 100, Maximum = 3840, Value = 1920 };
            windowTab.Controls.Add(windowWidthNumeric);
            y += 40;

            windowHeightLabel = new Label { Text = "Height:", Location = new Point(20, y), Width = 100 };
            windowTab.Controls.Add(windowHeightLabel);
            windowHeightNumeric = new NumericUpDown { Location = new Point(130, y), Width = 100, Minimum = 10, Maximum = 200, Value = 45 };
            windowTab.Controls.Add(windowHeightNumeric);
            y += 40;

            resetWindowButton = new Button { Text = "Reset Window", Location = new Point(130, y), Width = 100 };
            resetWindowButton.Click += (s, e) => { WindowX = 0; WindowY = 721; WindowWidth = 1920; WindowHeight = 45; LoadWindowValues(); };
            windowTab.Controls.Add(resetWindowButton);
            y += 40;

            clickThroughCheckBox = new CheckBox { Text = "Enable Click-Through", Location = new Point(20, y), Width = 200, Checked = false };
            windowTab.Controls.Add(clickThroughCheckBox);

            tabControl.TabPages.Add(windowTab);

            // ===== COLOR TAB =====
            colorTab = new TabPage("Colors");
            y = 20;

            colorModeLabel = new Label { Text = "Color Mode:", Location = new Point(20, y), Width = 150 };
            colorTab.Controls.Add(colorModeLabel);
            colorModeCombo = new ComboBox { Location = new Point(180, y), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            colorModeCombo.Items.AddRange(new string[] { "Default (Gradient)", "Custom Colors", "RGB Gradient", "Rainbow Wave" });
            colorModeCombo.SelectedIndex = 1;
            colorTab.Controls.Add(colorModeCombo);
            y += 40;

            rainbowDirectionLabel = new Label { Text = "Rainbow Direction:", Location = new Point(20, y), Width = 150 };
            colorTab.Controls.Add(rainbowDirectionLabel);
            rainbowDirectionCombo = new ComboBox { Location = new Point(180, y), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            rainbowDirectionCombo.Items.AddRange(new string[] { "Left → Right", "Right → Left" });
            rainbowDirectionCombo.SelectedIndex = 0;
            colorTab.Controls.Add(rainbowDirectionCombo);
            y += 40;

            waveSpeedLabel = new Label { Text = "Wave Speed:", Location = new Point(20, y), Width = 150 };
            colorTab.Controls.Add(waveSpeedLabel);
            waveSpeedTrack = new TrackBar { Location = new Point(180, y), Width = 150, Minimum = 5, Maximum = 50, Value = 10, TickFrequency = 1 };
            waveSpeedTrack.Scroll += (s, e) => UpdateWaveSpeedLabel();
            colorTab.Controls.Add(waveSpeedTrack);
            waveSpeedValueLabel = new Label { Location = new Point(340, y), Width = 50 };
            colorTab.Controls.Add(waveSpeedValueLabel);
            UpdateWaveSpeedLabel();
            y += 40;

            backgroundAlphaLabel = new Label { Text = "Background Opacity:", Location = new Point(20, y), Width = 150 };
            colorTab.Controls.Add(backgroundAlphaLabel);
            backgroundAlphaTrack = new TrackBar { Location = new Point(180, y), Width = 150, Minimum = 0, Maximum = 255, Value = 255, TickFrequency = 25 };
            backgroundAlphaTrack.Scroll += (s, e) => UpdateBackgroundAlphaLabel();
            colorTab.Controls.Add(backgroundAlphaTrack);
            backgroundAlphaValueLabel = new Label { Location = new Point(340, y), Width = 50 };
            colorTab.Controls.Add(backgroundAlphaValueLabel);
            UpdateBackgroundAlphaLabel();
            y += 40;

            colorLowLabel = new Label { Text = "Low Audio Color:", Location = new Point(20, y), Width = 150 };
            colorTab.Controls.Add(colorLowLabel);
            colorLowButton = new Button { Location = new Point(180, y - 5), Width = 100, Height = 30, BackColor = Color.Cyan, Text = "Change" };
            colorLowButton.Click += (s, e) => { ColorDialog cd = new ColorDialog { Color = BarColorLow }; if (cd.ShowDialog() == DialogResult.OK) { BarColorLow = cd.Color; colorLowButton.BackColor = cd.Color; } };
            colorTab.Controls.Add(colorLowButton);
            y += 50;

            colorHighLabel = new Label { Text = "High Audio Color:", Location = new Point(20, y), Width = 150 };
            colorTab.Controls.Add(colorHighLabel);
            colorHighButton = new Button { Location = new Point(180, y - 5), Width = 100, Height = 30, BackColor = Color.Magenta, Text = "Change" };
            colorHighButton.Click += (s, e) => { ColorDialog cd = new ColorDialog { Color = BarColorHigh }; if (cd.ShowDialog() == DialogResult.OK) { BarColorHigh = cd.Color; colorHighButton.BackColor = cd.Color; } };
            colorTab.Controls.Add(colorHighButton);

            tabControl.TabPages.Add(colorTab);

            // Save Settings and Close buttons
            okButton = new Button { Text = "Save Settings", Location = new Point(300, 490), Width = 110 };
            okButton.Click += (s, e) => { ApplySettings(); this.DialogResult = DialogResult.OK; this.Close(); };
            this.Controls.Add(okButton);
        }

        private void UpdateSmoothingLabel()
        {
            smoothingValueLabel.Text = $"{smoothingTrackBar.Value}%";
        }

        private void UpdateBackgroundAlphaLabel()
        {
            int percent = (int)((backgroundAlphaTrack.Value / 255.0f) * 100);
            backgroundAlphaValueLabel.Text = $"{percent}%";
        }
        private void UpdateWaveSpeedLabel()
        {
            float speed = (waveSpeedTrack.Value / 10.0f);
            waveSpeedValueLabel.Text = speed.ToString("F1") + "x";
        }
        private void LoadWindowValues()
        {
            windowXNumeric.Value = WindowX;
            windowYNumeric.Value = WindowY;
            windowWidthNumeric.Value = WindowWidth;
            windowHeightNumeric.Value = WindowHeight;
            clickThroughCheckBox.Checked = ClickThrough;
            UpdateWindowPositionDisplay(WindowX, WindowY, WindowWidth, WindowHeight);
        }

        public void UpdateWindowPositionDisplay(int x, int y, int w, int h)
        {
            windowPosValueLabel.Text = $"X:{x}, Y:{y}, W:{w}, H:{h}";
        }

        private void LoadAudioDevices()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                
                foreach (var device in devices)
                {
                    deviceComboBox.Items.Add(device.FriendlyName);
                }
                
                if (deviceComboBox.Items.Count > 0)
                    deviceComboBox.SelectedIndex = 0;
            }
            catch { }
        }

        private void ApplySettings()
        {
            SelectedDevice = deviceComboBox.SelectedItem?.ToString() ?? "";
            NumberOfBars = (int)barsNumeric.Value;
            Smoothing = smoothingTrackBar.Value / 100.0f;
            DecayRate = (float)decayNumeric.Value / 100.0f;
            IdleTimeoutMs = (int)idleTimeoutNumeric.Value;
            WindowX = (int)windowXNumeric.Value;
            WindowY = (int)windowYNumeric.Value;
            WindowWidth = (int)windowWidthNumeric.Value;
            WindowHeight = (int)windowHeightNumeric.Value;
            ColorMode = colorModeCombo.SelectedIndex;
            RainbowDirection = rainbowDirectionCombo.SelectedIndex;
            WaveSpeed = waveSpeedTrack.Value / 10.0f;
            BackgroundAlpha = (byte)backgroundAlphaTrack.Value;
            ClickThrough = clickThroughCheckBox.Checked;
        }
    }
}
