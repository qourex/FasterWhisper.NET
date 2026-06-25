// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Qourex.FasterWhisper.NET;
using System.Diagnostics;

namespace Qourex.FasterWhisper.NET.Samples.Maui.Gpu
{
    public partial class MainPage : ContentPage
    {
        public ObservableCollection<SegmentModel> Segments { get; } = new();
        private string _selectedAudioPath = "harvard.wav";
        private string? _pickedFilePath;
        
        private WhisperModel? _model;
        private string? _currentModelName;

        public MainPage()
        {
            InitializeComponent();
            SegmentsListView.ItemsSource = Segments;
        }

        private void OnSelectPreset1(object? sender, EventArgs e)
        {
            _pickedFilePath = null;
            _selectedAudioPath = "harvard.wav";
            SelectedFileLabel.Text = "Selected: harvard.wav (Preset)";
        }

        private void OnSelectPreset2(object? sender, EventArgs e)
        {
            _pickedFilePath = null;
            _selectedAudioPath = "harvard2.wav";
            SelectedFileLabel.Text = "Selected: harvard2.wav (Preset)";
        }

        private async void OnPickFileClicked(object? sender, EventArgs e)
        {
            try
            {
                var customFileType = new FilePickerFileType(
                    new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.iOS, new[] { "public.audio" } },
                        { DevicePlatform.Android, new[] { "audio/wav", "audio/x-wav" } },
                        { DevicePlatform.WinUI, new[] { ".wav" } },
                        { DevicePlatform.MacCatalyst, new[] { "public.audio" } },
                    });

                var options = new PickOptions
                {
                    PickerTitle = "Please select a WAV audio file",
                    FileTypes = customFileType
                };
                var result = await FilePicker.Default.PickAsync(options);
                if (result != null)
                {
                    _pickedFilePath = result.FullPath;
                    _selectedAudioPath = result.FileName;
                    SelectedFileLabel.Text = $"Selected: {result.FileName}";
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"Failed to pick file: {ex.Message}", "OK");
            }
        }

        private async void OnTranscribeClicked(object? sender, EventArgs e)
        {
            TranscribeBtn.IsEnabled = false;
            Segments.Clear();
            TimeLabel.Text = "";
            StatusLabel.Text = "Initializing...";
            ProgressLayout.IsVisible = true;
            ProgressIndicator.Progress = 0;

            try
            {
                string modelName = ModelPicker.SelectedItem?.ToString() ?? "tiny";
                string wavFilePath;

                if (_pickedFilePath != null)
                {
                    wavFilePath = _pickedFilePath;
                }
                else
                {
                    StatusLabel.Text = "Extracting preset audio asset...";
                    wavFilePath = await PrepareTempFileFromAssetAsync(_selectedAudioPath);
                }

                // Load/Download Model
                if (_model == null || _currentModelName != modelName)
                {
                    _model?.Dispose();
                    _model = null;

                    StatusLabel.Text = "Downloading Whisper model weights...";
                    var downloader = new ModelDownloader();
                    var progress = new Progress<(string FileName, long BytesRead, long TotalBytes)>(p =>
                    {
                        if (p.TotalBytes > 0)
                        {
                            double pct = (double)p.BytesRead / p.TotalBytes;
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                ProgressIndicator.Progress = pct;
                                ProgressLabel.Text = $"Downloading: {pct * 100:F0}%";
                            });
                        }
                    });

                    string modelPath = await Task.Run(() => downloader.GetModelPathAsync(modelName, progress));

                    StatusLabel.Text = "Loading model on GPU (CUDA)...";
                    var builder = WhisperModelBuilder.Create(modelPath)
                        .WithDevice("cuda")
                        .WithComputeType("default")
                        .WithMemoryMapping()
                        .WithNumReplicas(1);

                    _model = await Task.Run(() => builder.Build());
                    _currentModelName = modelName;
                }

                StatusLabel.Text = "Transcribing audio (CUDA)...";
                ProgressLayout.IsVisible = false;

                var stopwatch = Stopwatch.StartNew();

                var audioProcessor = new AudioProcessor(_model.NMels);
                float[] pcm = await Task.Run(() => audioProcessor.LoadWav(wavFilePath));

                var options = new WhisperOptions
                {
                    BeamSize = 5
                };

                var results = await Task.Run(() => _model.Transcribe(pcm, language: "en", options: options));

                stopwatch.Stop();
                TimeLabel.Text = $"({stopwatch.Elapsed.TotalSeconds:F2}s)";

                foreach (var s in results)
                {
                    Segments.Add(new SegmentModel
                    {
                        TimeRange = $"{FormatTime(s.Start)} - {FormatTime(s.End)}",
                        Text = s.Text.Trim()
                    });
                }

                StatusLabel.Text = "Success!";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Error occurred.";
                await DisplayAlertAsync("Transcription Error", ex.Message, "OK");
            }
            finally
            {
                TranscribeBtn.IsEnabled = true;
                ProgressLayout.IsVisible = false;
            }
        }

        private async Task<string> PrepareTempFileFromAssetAsync(string assetName)
        {
            string targetPath = Path.Combine(FileSystem.CacheDirectory, assetName);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            using var stream = await FileSystem.OpenAppPackageFileAsync(assetName);
            using var outStream = File.OpenWrite(targetPath);
            await stream.CopyToAsync(outStream);
            return targetPath;
        }

        private string FormatTime(double seconds)
        {
            return TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss\.f");
        }
    }

    public class SegmentModel
    {
        public string TimeRange { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
