using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace TaskbarVisualizer
{
    public class TaskbarVisualizerForm : Form
    {
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int LWA_ALPHA = 0x2;
        
        private static string _logPath = Path.Combine(Path.GetTempPath(), "taskbar_visualizer_debug.log");
        private static string _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarVisualizer",
            "settings.json"
        );

        private void DebugLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logLine = $"[{timestamp}] {message}";
            Console.WriteLine(logLine);
            try
            {
                File.AppendAllText(_logPath, logLine + Environment.NewLine);
            }
            catch { /* Silently fail if can't write */ }
        }

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private int _numBars = 48;
        private float[] _spectrum = new float[48];
        private float[] _prevSpectrum = new float[48];
        private float _smoothing = 0.5f;
        private float _decayRate = 0.95f;
        private int _idleTimeoutMs = 2000;
        private long _lastAudioTime = 0;
        private float _peakLevel = 0.1f;  // Track the highest recent level for dynamic scaling
        private float _peakDecayRate = 0.98f;  // How fast peaks fade (per frame)
        private Color _barColorLow = Color.Cyan;  // Color at low audio levels
        private Color _barColorHigh = Color.Magenta;  // Color at high audio levels
        private int _colorMode = 1;  // 0=Default, 1=Custom, 2=RGBGradient, 3=RainbowWave
        private int _rainbowDirection = 0;  // 0=Left-to-Right, 1=Right-to-Left
        private float _waveSpeed = 1.0f;  // Speed multiplier for animation
        private byte _backgroundAlpha = 255;  // 0-255 transparency
        private bool _clickThrough = false;  // Enable window click-through
        private Timer? _renderTimer;
        private WasapiLoopbackCapture? _audioCapture;
        private int _animationCounter = 0;
        private NotifyIcon? _notifyIcon;
        private SettingsForm? _settingsForm;
        private string _selectedDeviceName = "";
        private bool _isReceivingAudio = false;
        private bool _isDraggingForm = false;
        private int _dragOffsetX = 0;
        private int _dragOffsetY = 0;
        private bool _settingsLoaded = false;  // Track if settings were successfully loaded

        public TaskbarVisualizerForm()
        {
            // Basic form setup
            this.DoubleBuffered = true;
            this.BackColor = Color.Black;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;  // Always on top of taskbar
            this.Text = "Taskbar Visualizer";
            this.Enabled = true;

            DebugLog("=== TaskbarVisualizerForm Constructor ===");
            DebugLog($"Form created - Visible: {this.Visible}, TopMost: {this.TopMost}");

            // Make window transparent and click-through
            int exStyle = (int)NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE);
            DebugLog($"Initial exStyle: {exStyle:X}");
            
            // Add layered window style AND click-through
            NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE, 
                (IntPtr)(exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT));
            
            DebugLog("Set window to WS_EX_LAYERED | WS_EX_TRANSPARENT");
            
            // Use ALPHA blending - respects per-pixel alpha from drawing
            SetLayeredWindowAttributes(Handle, 0, 255, 2);  // LWA_ALPHA with 255 (fully opaque)
            
            DebugLog("Set window alpha to 255 (fully opaque, per-pixel alpha enabled)");

            // Setup render timer
            _renderTimer = new Timer();
            _renderTimer.Interval = 16;  // Fixed 60 FPS
            _renderTimer.Tick += (s, e) => this.Invalidate();

            // Setup system tray icon
            SetupTrayIcon();

            this.Load += Form_Load;
            this.FormClosing += Form_FormClosing;
            this.Paint += Form_Paint;
            this.MouseDown += (s, e) => { _isDraggingForm = true; _dragOffsetX = e.X; _dragOffsetY = e.Y; };
            this.MouseUp += (s, e) => { _isDraggingForm = false; };
            this.MouseMove += (s, e) => 
            { 
                if (_isDraggingForm && e.Button == MouseButtons.Left)
                {
                    this.Left += e.X - _dragOffsetX;
                    this.Top += e.Y - _dragOffsetY;
                }
            };
        }

        private void Form_Load(object sender, EventArgs e)
        {
            DebugLog("Form_Load starting...");
            LoadSettings();  // Load saved settings before positioning
            DebugLog($"Settings loaded flag: {_settingsLoaded}");
            DebugLog($"Initial form state - Location: ({this.Left}, {this.Top}), Size: {this.Width}x{this.Height}");
            
            // Only reposition if settings weren't loaded (first run)
            if (!_settingsLoaded)
            {
                // Position window at taskbar on load
                IntPtr taskbarHandle = FindWindow("Shell_TryWnd", null);
                DebugLog($"Taskbar handle: {taskbarHandle}");
                
                if (taskbarHandle != IntPtr.Zero)
                {
                    GetWindowRect(taskbarHandle, out RECT taskbarRect);
                    int width = taskbarRect.Right - taskbarRect.Left;
                    int height = taskbarRect.Bottom - taskbarRect.Top;

                    DebugLog($"Found taskbar - Rect: ({taskbarRect.Left}, {taskbarRect.Top}, {taskbarRect.Right}, {taskbarRect.Bottom})");
                    DebugLog($"Taskbar size: {width}x{height}");

                    this.Width = width;
                    this.Height = height;
                    this.Left = taskbarRect.Left;
                    this.Top = taskbarRect.Top;
                    
                    // Position on top using HWND_TOPMOST
                    IntPtr HWND_TOPMOST = new IntPtr(-1);
                    NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, taskbarRect.Left, taskbarRect.Top, width, height, 0x0010);
                }
                else
                {
                    // Default to top of screen if taskbar not found
                    DebugLog("Taskbar not found, using screen positioning...");
                    try
                    {
                        Screen? primaryScreen = Screen.PrimaryScreen;
                        if (primaryScreen != null)
                        {
                            // Use actual working area width (accounts for actual display resolution, not max capability)
                            int screenWidth = primaryScreen.WorkingArea.Width;
                            int screenHeight = 60;
                            
                            DebugLog($"Primary screen size: {primaryScreen.Bounds.Width}x{primaryScreen.Bounds.Height}");
                            DebugLog($"Primary screen working area: {screenWidth}x{screenHeight}");
                            DebugLog($"Setting window to: 0, 0, {screenWidth}x{screenHeight}");
                            
                            this.Width = screenWidth;
                            this.Height = screenHeight;
                            this.Left = 0;
                            this.Top = 0;
                            
                            IntPtr HWND_TOPMOST = new IntPtr(-1);
                            NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, 0, 0, screenWidth, screenHeight, 0x0010);
                        }
                        else
                        {
                            // Fallback if screen is null
                            DebugLog("PrimaryScreen is null, using fallback 800x60");
                            this.Width = 800;
                            this.Height = 60;
                            this.Left = 0;
                            this.Top = 0;
                            
                            IntPtr HWND_TOPMOST = new IntPtr(-1);
                            NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 800, 60, 0x0010);
                        }
                    }
                catch (Exception ex)
                {
                    // Safety fallback
                    DebugLog($"Exception during screen detection: {ex.Message}");
                    this.Width = 800;
                    this.Height = 60;
                    this.Left = 0;
                    this.Top = 0;
                    
                    IntPtr HWND_TOPMOST = new IntPtr(-1);
                    NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 800, 60, 0x0010);
                }
            }
            }  // End if (!_settingsLoaded)

            DebugLog($"Final window position: ({this.Left}, {this.Top}), Size: {this.Width}x{this.Height}");
            DebugLog($"Visible before Show: {this.Visible}");
            DebugLog("Starting render timer and audio capture...");
            DebugLog($"Render timer is null: {_renderTimer == null}");
            
            // Explicitly show the window
            this.Visible = true;
            this.Show();
            this.BringToFront();
            
            DebugLog($"Visible after Show: {this.Visible}");
            DebugLog($"Window Handle: {this.Handle}");
            DebugLog($"Window Title: {this.Text}");
            
            _renderTimer?.Start();
            InitializeAudio();
            
            DebugLog("Form_Load complete");
        }

        private void Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            _renderTimer?.Stop();
            _audioCapture?.Dispose();
            _notifyIcon?.Dispose();
        }

        private void InitializeAudio()
        {
            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();
                MMDevice device;

                if (!string.IsNullOrEmpty(_selectedDeviceName))
                {
                    // Find device by name
                    var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    device = null;
                    foreach (var d in devices)
                    {
                        if (d.FriendlyName == _selectedDeviceName)
                        {
                            device = d;
                            break;
                        }
                    }
                    if (device == null)
                        device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
                else
                {
                    device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }

                _audioCapture?.Dispose();
                _audioCapture = new WasapiLoopbackCapture(device);
                _audioCapture.DataAvailable += AudioCapture_DataAvailable;
                _audioCapture.StartRecording();
                
                Console.WriteLine($"Audio initialized with device: {device.FriendlyName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Audio init error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void SetupTrayIcon()
        {
            _notifyIcon = new NotifyIcon();
            
            // Try to load custom image and create circular icon
            try
            {
                string iconPath = "RedChase07_v2.png";
                if (File.Exists(iconPath))
                {
                    using (var sourceBitmap = new Bitmap(iconPath))
                    {
                        // Create a circular version of the image (16x16 for tray icon)
                        Bitmap circularIcon = new Bitmap(16, 16);
                        using (Graphics g = Graphics.FromImage(circularIcon))
                        {
                            g.Clear(Color.Transparent);
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            
                            // Draw the image cropped to a circle
                            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                            {
                                path.AddEllipse(0, 0, 15, 15);
                                g.SetClip(path);
                                g.DrawImage(sourceBitmap, 0, 0, 16, 16);
                            }
                        }
                        _notifyIcon.Icon = Icon.FromHandle(circularIcon.GetHicon());
                    }
                }
                else
                {
                    // Fallback to simple circle if image not found
                    Bitmap iconBitmap = new Bitmap(16, 16);
                    using (Graphics g = Graphics.FromImage(iconBitmap))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        using (Pen pen = new Pen(Color.Cyan, 2))
                        {
                            g.DrawEllipse(pen, 1, 1, 14, 14);
                        }
                    }
                    _notifyIcon.Icon = Icon.FromHandle(iconBitmap.GetHicon());
                }
            }
            catch
            {
                // Fallback to simple circle on error
                Bitmap iconBitmap = new Bitmap(16, 16);
                using (Graphics g = Graphics.FromImage(iconBitmap))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (Pen pen = new Pen(Color.Cyan, 2))
                    {
                        g.DrawEllipse(pen, 1, 1, 14, 14);
                    }
                }
                _notifyIcon.Icon = Icon.FromHandle(iconBitmap.GetHicon());
            }

            _notifyIcon.Text = "Taskbar Visualizer";
            _notifyIcon.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Bring to Front", null, (s, e) => BringWindowToFront());
            menu.Items.Add("Settings", null, (s, e) => ShowSettings());
            menu.Items.Add("Exit", null, (s, e) => this.Close());

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (s, e) => ShowSettings();
        }

        private Color LerpColor(Color from, Color to, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * t),
                (int)(from.G + (to.G - from.G) * t),
                (int)(from.B + (to.B - from.B) * t)
            );
        }

        private void BringWindowToFront()
        {
            // Force window to top with HWND_TOPMOST
            IntPtr HWND_TOPMOST = new IntPtr(-1);
            NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, this.Left, this.Top, this.Width, this.Height, 0x0010);
            base.BringToFront();
        }

        private Color GetBarColor(int barIndex, float normalizedHeight)
        {
            switch (_colorMode)
            {
                case 0: // Default - hard-coded cyan-to-white gradient
                    {
                        int r = Math.Min(140 + (int)(normalizedHeight * 100), 255);
                        int g = Math.Min(200 + (int)(normalizedHeight * 50), 255);
                        int b = Math.Min(255 + (int)(normalizedHeight * 20), 255);
                        return Color.FromArgb(r, g, b);
                    }
                case 1: // Custom - interpolate between user-selected colors
                    return LerpColor(_barColorLow, _barColorHigh, normalizedHeight);
                
                case 2: // RGB Gradient - STATIC, bars cycle through RGB based on position (left to right)
                    {
                        float hue = ((float)barIndex / _numBars) * 360;  // No animation
                        return HSVToRGB(hue, 1.0f, normalizedHeight);
                    }
                
                case 3: // Rainbow Wave - ANIMATED, colors move with speed control
                    {
                        float offset = (_rainbowDirection == 0) ? _animationCounter * 0.01f * _waveSpeed : -_animationCounter * 0.01f * _waveSpeed;
                        float hue = (((float)barIndex / _numBars + offset) * 360) % 360;
                        if (hue < 0) hue += 360;  // Handle negative hue values
                        return HSVToRGB(hue, 1.0f, normalizedHeight);
                    }
                
                default:
                    return Color.Cyan;
            }
        }

        private Color HSVToRGB(float h, float s, float v)
        {
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            float m = v - c;

            float r = 0, g = 0, b = 0;

            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromArgb(
                (int)((r + m) * 255),
                (int)((g + m) * 255),
                (int)((b + m) * 255)
            );
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new SettingsData
                {
                    SelectedDevice = _selectedDeviceName,
                    NumberOfBars = _numBars,
                    Smoothing = _smoothing,
                    DecayRate = _decayRate,
                    IdleTimeoutMs = _idleTimeoutMs,
                    WindowX = this.Left,
                    WindowY = this.Top,
                    WindowWidth = this.Width,
                    WindowHeight = this.Height,
                    ColorMode = _colorMode,
                    RainbowDirection = _rainbowDirection,
                    WaveSpeed = _waveSpeed,
                    BackgroundAlpha = _backgroundAlpha,
                    ClickThrough = _clickThrough,
                    BarColorLowARGB = _barColorLow.ToArgb(),
                    BarColorHighARGB = _barColorHigh.ToArgb()
                };

                // Create directory if it doesn't exist
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? "");
                
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                DebugLog($"Settings saved to {_settingsPath}");
            }
            catch (Exception ex)
            {
                DebugLog($"Error saving settings: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    DebugLog("No settings file found, using defaults");
                    return;
                }

                string json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                if (settings == null)
                {
                    DebugLog("Failed to deserialize settings");
                    return;
                }

                _selectedDeviceName = settings.SelectedDevice ?? "";
                _numBars = settings.NumberOfBars;
                _spectrum = new float[_numBars];
                _prevSpectrum = new float[_numBars];
                _smoothing = settings.Smoothing;
                _decayRate = settings.DecayRate;
                _idleTimeoutMs = settings.IdleTimeoutMs;
                _colorMode = settings.ColorMode;
                _rainbowDirection = settings.RainbowDirection;
                _waveSpeed = settings.WaveSpeed;
                _backgroundAlpha = settings.BackgroundAlpha;
                _clickThrough = settings.ClickThrough;
                _barColorLow = Color.FromArgb(settings.BarColorLowARGB);
                _barColorHigh = Color.FromArgb(settings.BarColorHighARGB);

                // Apply window position/size
                this.Left = settings.WindowX;
                this.Top = settings.WindowY;
                this.Width = settings.WindowWidth;
                this.Height = settings.WindowHeight;

                DebugLog($"Settings loaded from {_settingsPath}");
                ApplyClickThroughSetting();
                _settingsLoaded = true;  // Mark that settings were successfully loaded
            }
            catch (Exception ex)
            {
                DebugLog($"Error loading settings: {ex.Message}");
            }
        }

        private class SettingsData
        {
            public string? SelectedDevice { get; set; }
            public int NumberOfBars { get; set; }
            public float Smoothing { get; set; }
            public float DecayRate { get; set; }
            public int IdleTimeoutMs { get; set; }
            public int WindowX { get; set; }
            public int WindowY { get; set; }
            public int WindowWidth { get; set; }
            public int WindowHeight { get; set; }
            public int ColorMode { get; set; }
            public int RainbowDirection { get; set; }
            public float WaveSpeed { get; set; }
            public byte BackgroundAlpha { get; set; }
            public bool ClickThrough { get; set; }
            public int BarColorLowARGB { get; set; }
            public int BarColorHighARGB { get; set; }
        }

        private void ApplyClickThroughSetting()
        {
            // Toggle WS_EX_TRANSPARENT flag based on _clickThrough setting
            IntPtr currentStyle = NativeMethods.GetWindowLongPtr(this.Handle, NativeMethods.GWL_EXSTYLE);
            
            if (_clickThrough)
            {
                // Enable click-through
                IntPtr newStyle = new IntPtr(currentStyle.ToInt64() | WS_EX_TRANSPARENT);
                NativeMethods.SetWindowLongPtr(this.Handle, NativeMethods.GWL_EXSTYLE, newStyle);
                DebugLog($"Click-through enabled: WS_EX_TRANSPARENT flag set");
            }
            else
            {
                // Disable click-through
                IntPtr newStyle = new IntPtr(currentStyle.ToInt64() & ~WS_EX_TRANSPARENT);
                NativeMethods.SetWindowLongPtr(this.Handle, NativeMethods.GWL_EXSTYLE, newStyle);
                DebugLog($"Click-through disabled: WS_EX_TRANSPARENT flag removed");
            }
        }

        private void ApplySettingsFromForm()
        {
            // Apply new settings from form
            _smoothing = _settingsForm.Smoothing;
            _selectedDeviceName = _settingsForm.SelectedDevice;
            _decayRate = _settingsForm.DecayRate;
            _idleTimeoutMs = _settingsForm.IdleTimeoutMs;
            _barColorLow = _settingsForm.BarColorLow;
            _barColorHigh = _settingsForm.BarColorHigh;
            _colorMode = _settingsForm.ColorMode;
            _rainbowDirection = _settingsForm.RainbowDirection;
            _waveSpeed = _settingsForm.WaveSpeed;
            _backgroundAlpha = _settingsForm.BackgroundAlpha;
            _clickThrough = _settingsForm.ClickThrough;
            // No need to update window alpha - it's handled per-frame in Form_Paint
            
            // Apply click-through setting
            ApplyClickThroughSetting();
            
            // Update bar count and resize arrays if changed
            int newBarCount = _settingsForm.NumberOfBars;
            if (newBarCount != _numBars)
            {
                _numBars = newBarCount;
                _spectrum = new float[_numBars];
                _prevSpectrum = new float[_numBars];
                Console.WriteLine($"Updated bar count to {_numBars}");
            }

            // Apply window position and size if changed
            // When height changes, grow upward (decrease Y) to keep bottom edge fixed
            if (this.Height != _settingsForm.WindowHeight)
            {
                int heightDifference = _settingsForm.WindowHeight - this.Height;
                int newY = this.Top - heightDifference;
                this.Height = _settingsForm.WindowHeight;
                this.Top = newY;
                Console.WriteLine($"Updated window height to {_settingsForm.WindowHeight}, adjusted Y to {newY} to grow upward");
            }

            if (this.Left != _settingsForm.WindowX || this.Top != _settingsForm.WindowY)
            {
                this.Left = _settingsForm.WindowX;
                this.Top = _settingsForm.WindowY;
                Console.WriteLine($"Updated window position to {_settingsForm.WindowX}, {_settingsForm.WindowY}");
            }

            if (this.Width != _settingsForm.WindowWidth)
            {
                this.Width = _settingsForm.WindowWidth;
                Console.WriteLine($"Updated window width to {_settingsForm.WindowWidth}");
            }

            // Reinitialize with new device if changed
            if (!string.IsNullOrEmpty(_selectedDeviceName))
            {
                InitializeAudio();
            }
        }

        private void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm();
            }
            
            // Load current settings into form
            _settingsForm.NumberOfBars = _numBars;
            _settingsForm.Smoothing = _smoothing;
            _settingsForm.DecayRate = _decayRate;
            _settingsForm.IdleTimeoutMs = _idleTimeoutMs;
            _settingsForm.WindowX = this.Left;
            _settingsForm.WindowY = this.Top;
            _settingsForm.WindowWidth = this.Width;
            _settingsForm.WindowHeight = this.Height;
            _settingsForm.ColorMode = _colorMode;
            _settingsForm.RainbowDirection = _rainbowDirection;
            _settingsForm.WaveSpeed = _waveSpeed;
            _settingsForm.BackgroundAlpha = _backgroundAlpha;
            _settingsForm.BarColorLow = _barColorLow;
            _settingsForm.BarColorHigh = _barColorHigh;
            _settingsForm.ClickThrough = _clickThrough;
            _settingsForm.UpdateWindowPositionDisplay(this.Left, this.Top, this.Width, this.Height);
            
            // Handle dialog result
            DialogResult result = _settingsForm.ShowDialog();

            // Apply settings regardless of how dialog closed
            ApplySettingsFromForm();
            SaveSettings();  // Persist settings after applying
        }

        private void AudioCapture_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            int sampleCount = e.BytesRecorded / 4;
            float[] samples = new float[sampleCount];
            Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

            // Use logarithmic frequency binning for more natural spectrum visualization
            // This groups low frequencies together (where most music energy is) and spreads out high frequencies
            for (int barIndex = 0; barIndex < _numBars; barIndex++)
            {
                // Map bar index to exponential frequency range (20Hz to ~10kHz)
                // Lower bars = lower frequencies (bass), higher bars = higher frequencies (treble)
                float freqLow = 20f * (float)Math.Pow(10, barIndex / (float)_numBars * 2.7f);
                float freqHigh = 20f * (float)Math.Pow(10, (barIndex + 1) / (float)_numBars * 2.7f);
                
                // Convert frequency to sample index (assuming 44100 Hz sample rate)
                int sampleLow = (int)(freqLow / 44100f * sampleCount);
                int sampleHigh = (int)(freqHigh / 44100f * sampleCount);
                
                // Clamp to valid range
                sampleLow = Math.Max(0, Math.Min(sampleCount - 1, sampleLow));
                sampleHigh = Math.Max(sampleLow + 1, Math.Min(sampleCount, sampleHigh));
                
                // Average samples in this frequency range
                float sum = 0;
                int count = 0;
                for (int j = sampleLow; j < sampleHigh; j++)
                {
                    sum += Math.Abs(samples[j]);
                    count++;
                }
                
                float average = count > 0 ? sum / count : 0;
                _spectrum[barIndex] = average * 2.5f;  // Amplification factor
                
                // Invert smoothing so lower values = faster reaction (more responsive)
                float invertedSmoothing = 1f - _smoothing;
                _prevSpectrum[barIndex] = _prevSpectrum[barIndex] * (1 - invertedSmoothing) + _spectrum[barIndex] * invertedSmoothing;

                // Update peak level (track the highest level seen)
                if (_spectrum[barIndex] > _peakLevel)
                {
                    _peakLevel = _spectrum[barIndex];
                }
            }
            
            // Mark that we're receiving audio data
            _isReceivingAudio = true;
            _lastAudioTime = DateTime.Now.Ticks;
        }

        private void Form_Paint(object sender, PaintEventArgs e)
        {
            // Increment animation counter every frame for wave effects
            _animationCounter++;
            
            if (_animationCounter % 120 == 0)  // Log every 120 frames to avoid spam
            {
                DebugLog($"Form_Paint called - Counter: {_animationCounter}, BgAlpha: {_backgroundAlpha}, Visible: {this.Visible}");
            }
            
            // Clear entire surface first
            e.Graphics.Clear(Color.Transparent);
            
            // Draw semi-transparent background based on opacity setting
            // When opacity is 0, no background; when 255, fully opaque dark background
            if (_backgroundAlpha > 0)
            {
                // Use fixed dark gray color with variable alpha
                using (Brush bgBrush = new SolidBrush(Color.FromArgb(_backgroundAlpha, 25, 25, 25)))
                {
                    e.Graphics.FillRectangle(bgBrush, 0, 0, Width, Height);
                }
            }

            int barWidth = Math.Max(1, Width / _numBars);
            int maxHeight = Height;

            // Check if we're in idle timeout
            long currentTicks = DateTime.Now.Ticks;
            long millisecondsSinceAudio = (_lastAudioTime > 0) ? (currentTicks - _lastAudioTime) / 10000 : long.MaxValue;
            bool isIdle = millisecondsSinceAudio > _idleTimeoutMs;

            // Apply decay to spectrum when not receiving audio
            if (!_isReceivingAudio)
            {
                for (int i = 0; i < _numBars; i++)
                {
                    _prevSpectrum[i] *= _decayRate;
                }
            }
            
            // Decay the peak level over time for dynamic range compression
            _peakLevel *= _peakDecayRate;
            if (_peakLevel < 0.1f)
                _peakLevel = 0.1f;  // Minimum threshold to prevent division issues
            
            _isReceivingAudio = false;  // Reset flag each frame

            // Check if there's any audio to display
            bool hasAudio = false;
            for (int i = 0; i < _numBars; i++)
            {
                if (_prevSpectrum[i] > 0.001f)
                {
                    hasAudio = true;
                    break;
                }
            }

            if (hasAudio && !isIdle)
            {
                // Draw actual audio bars with dynamic scaling based on peak level
                for (int i = 0; i < _numBars; i++)
                {
                    // Normalize the bar height relative to the peak level
                    // This creates gradual scaling instead of hard clamp
                    float normalizedHeight = Math.Min(1.0f, _prevSpectrum[i] / _peakLevel);
                    int height = (int)(normalizedHeight * maxHeight);

                    if (height > 0)
                    {
                        // Get color based on current color mode
                        Color barColor = GetBarColor(i, normalizedHeight);

                        using (Brush brush = new SolidBrush(barColor))
                        {
                            e.Graphics.FillRectangle(brush, i * barWidth, maxHeight - height, barWidth - 1, height);
                        }
                    }
                }
            }
            else
            {
                // Idle animation - subtle wave
                for (int i = 0; i < _numBars; i++)
                {
                    float wave = (float)Math.Sin((i + _animationCounter) * 0.1) * 0.5f + 0.5f;
                    int height = (int)(wave * maxHeight);
                    using (Brush b = new SolidBrush(Color.FromArgb(0, 100 + (int)(wave * 100), 200)))
                    {
                        e.Graphics.FillRectangle(b, i * barWidth, maxHeight - height, barWidth - 1, height);
                    }
                }
            }
        }
    }

    static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
