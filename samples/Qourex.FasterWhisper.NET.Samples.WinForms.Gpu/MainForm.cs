// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using Qourex.FasterWhisper.NET;

namespace Qourex.FasterWhisper.NET.Samples.WinForms.Gpu
{
    public class MainForm : Form
    {
        private ComboBox _modelCombo = null!;
        private TextBox _audioPathText = null!;
        private Button _browseBtn = null!;
        private Button _transcribeBtn = null!;
        private ProgressBar _progressBar = null!;
        private Label _statusLabel = null!;
        private RichTextBox _outputBox = null!;
        
        private WhisperModel? _model;
        private string? _currentModelName;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "FasterWhisper.NET - GPU (CUDA) Windows Forms Sample";
            this.Size = new Size(800, 600);
            this.MinimumSize = new Size(600, 450);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Layout using a TableLayoutPanel for simplicity and resizing
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(15)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100F)); // Input fields
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));  // Progress & Status
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Output box
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));  // Bottom Action buttons

            // 1. TOP ROW: Configuration Controls
            var configPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };

            var modelLabel = new Label
            {
                Text = "Model:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            _modelCombo = new ComboBox
            {
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9)
            };
            _modelCombo.Items.AddRange(new object[] { "tiny", "base", "small" });
            _modelCombo.SelectedIndex = 0;

            var audioLabel = new Label
            {
                Text = "Audio File:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(15, 0, 0, 0),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            _audioPathText = new TextBox
            {
                Width = 250,
                ReadOnly = true,
                Font = new Font("Segoe UI", 9)
            };
            // Set default preset file path if it exists
            string presetPath = Path.Combine(AppContext.BaseDirectory, "harvard.wav");
            if (File.Exists(presetPath))
            {
                _audioPathText.Text = presetPath;
            }

            _browseBtn = new Button
            {
                Text = "Browse...",
                Width = 80,
                Font = new Font("Segoe UI", 9)
            };
            _browseBtn.Click += BrowseBtn_Click;

            configPanel.Controls.Add(modelLabel);
            configPanel.Controls.Add(_modelCombo);
            configPanel.Controls.Add(audioLabel);
            configPanel.Controls.Add(_audioPathText);
            configPanel.Controls.Add(_browseBtn);

            // 2. SECOND ROW: Progress Bar & Status Label
            var progressPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Height = 20,
                Style = ProgressBarStyle.Continuous
            };

            _statusLabel = new Label
            {
                Text = "Ready",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9, FontStyle.Italic)
            };

            progressPanel.Controls.Add(_progressBar, 0, 0);
            progressPanel.Controls.Add(_statusLabel, 1, 0);

            // 3. THIRD ROW: Rich Text Box
            _outputBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Consolas", 10.5F),
                BorderStyle = BorderStyle.FixedSingle
            };

            // 4. FOURTH ROW: Transcribe Button
            _transcribeBtn = new Button
            {
                Text = "Transcribe Audio (CUDA) ⚡",
                Dock = DockStyle.Right,
                Width = 220,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Height = 35
            };
            _transcribeBtn.Click += TranscribeBtn_Click;

            mainLayout.Controls.Add(configPanel, 0, 0);
            mainLayout.Controls.Add(progressPanel, 0, 1);
            mainLayout.Controls.Add(_outputBox, 0, 2);
            mainLayout.Controls.Add(_transcribeBtn, 0, 3);

            this.Controls.Add(mainLayout);
        }

        private void BrowseBtn_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "WAV Audio Files (*.wav)|*.wav";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _audioPathText.Text = ofd.FileName;
                }
            }
        }

        private async void TranscribeBtn_Click(object? sender, EventArgs e)
        {
            string audioPath = _audioPathText.Text;
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
            {
                MessageBox.Show("Please select a valid WAV audio file first.", "Audio File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string selectedModel = _modelCombo.SelectedItem?.ToString() ?? "tiny";
            
            // Disable UI
            _transcribeBtn.Enabled = false;
            _browseBtn.Enabled = false;
            _modelCombo.Enabled = false;
            _outputBox.Clear();
            _progressBar.Value = 0;

            try
            {
                await RunTranscriptionAsync(audioPath, selectedModel);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Transcription error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = "Error occurred.";
            }
            finally
            {
                // Re-enable UI
                _transcribeBtn.Enabled = true;
                _browseBtn.Enabled = true;
                _modelCombo.Enabled = true;
            }
        }

        private async Task RunTranscriptionAsync(string audioPath, string modelName)
        {
            // Load/download model if not cached
            if (_model == null || _currentModelName != modelName)
            {
                _model?.Dispose();
                _model = null;

                _statusLabel.Text = "Downloading/Resolving model...";
                _progressBar.Style = ProgressBarStyle.Marquee;

                var downloader = new ModelDownloader();
                var progress = new Progress<(string FileName, long BytesRead, long TotalBytes)>(p =>
                {
                    if (p.TotalBytes > 0)
                    {
                        int percent = (int)((double)p.BytesRead / p.TotalBytes * 100);
                        this.BeginInvoke(() =>
                        {
                            _progressBar.Style = ProgressBarStyle.Continuous;
                            _progressBar.Value = Math.Min(percent, 100);
                            _statusLabel.Text = $"Downloading weights: {percent}%";
                        });
                    }
                });

                string modelPath = await Task.Run(() => downloader.GetModelPathAsync(modelName, progress));

                _statusLabel.Text = "Loading model on GPU (CUDA)...";
                _progressBar.Style = ProgressBarStyle.Marquee;

                // Build for GPU/CUDA execution
                var builder = WhisperModelBuilder.Create(modelPath)
                    .WithDevice("cuda")
                    .WithComputeType("default")
                    .WithMemoryMapping()
                    .WithNumReplicas(1);

                _model = await Task.Run(() => builder.Build());
                _currentModelName = modelName;
            }

            _statusLabel.Text = "Transcribing audio (CUDA)...";
            _progressBar.Style = ProgressBarStyle.Marquee;

            var audioProcessor = new AudioProcessor(_model.NMels);
            float[] pcm = await Task.Run(() => audioProcessor.LoadWav(audioPath));

            var options = new WhisperOptions
            {
                BeamSize = 5
            };

            var segments = await Task.Run(() => _model.Transcribe(pcm, language: "en", options: options));

            _outputBox.Clear();
            foreach (var segment in segments)
            {
                string timeStr = $"[{TimeSpan.FromSeconds(segment.Start):hh\\:mm\\:ss} -> {TimeSpan.FromSeconds(segment.End):hh\\:mm\\:ss}] ";
                
                // Color formatting for timestamps
                _outputBox.SelectionStart = _outputBox.TextLength;
                _outputBox.SelectionLength = 0;
                _outputBox.SelectionColor = Color.LightSkyBlue;
                _outputBox.AppendText(timeStr);

                // Normal formatting for text
                _outputBox.SelectionStart = _outputBox.TextLength;
                _outputBox.SelectionLength = 0;
                _outputBox.SelectionColor = Color.White;
                _outputBox.AppendText(segment.Text.Trim() + "\n");
            }

            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 100;
            _statusLabel.Text = "Success!";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _model?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
