// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Qourex.FasterWhisper.NET;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<BlazorWhisperService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<Qourex.FasterWhisper.NET.Samples.Blazor.Gpu.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

public class BlazorWhisperService : IDisposable
{
    private WhisperModel? _model;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _isInitialized;

    public async Task EnsureModelLoadedAsync(string modelName, Action<string, double>? onDownloadProgress = null)
    {
        if (_isInitialized) return;

        await _initializationLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            var downloader = new ModelDownloader();
            var progress = new Progress<(string FileName, long BytesRead, long TotalBytes)>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    double percent = (double)p.BytesRead / p.TotalBytes * 100;
                    onDownloadProgress?.Invoke(p.FileName, percent);
                }
                else
                {
                    onDownloadProgress?.Invoke(p.FileName, 0);
                }
            });

            string modelPath = await downloader.GetModelPathAsync(modelName, progress);

            // Configure GPU/CUDA execution
            var modelBuilder = WhisperModelBuilder.Create(modelPath)
                .WithDevice("cuda")
                .WithComputeType("default")
                .WithMemoryMapping()
                .WithNumReplicas(1);

            _model = modelBuilder.Build();
            _isInitialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<List<TextSegment>> TranscribeAsync(string wavFilePath, string modelName, Action<string, double>? onDownloadProgress = null)
    {
        await EnsureModelLoadedAsync(modelName, onDownloadProgress);

        if (_model == null)
        {
            throw new InvalidOperationException("Model was not loaded correctly.");
        }

        var audioProcessor = new AudioProcessor(_model.NMels);
        float[] pcm = await Task.Run(() => audioProcessor.LoadWav(wavFilePath));

        var options = new WhisperOptions
        {
            BeamSize = 5
        };

        var rawSegments = await Task.Run(() => _model.Transcribe(pcm, language: "en", options: options));

        var result = new List<TextSegment>();
        foreach (var segment in rawSegments)
        {
            result.Add(new TextSegment(segment.Start, segment.End, segment.Text.Trim()));
        }

        return result;
    }

    public void Dispose()
    {
        _model?.Dispose();
        _initializationLock.Dispose();
    }
}

public record TextSegment(double Start, double End, string Text);
