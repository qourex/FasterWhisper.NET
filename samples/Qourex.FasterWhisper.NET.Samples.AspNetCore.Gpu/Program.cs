// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qourex.FasterWhisper.NET;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddSingleton<WhisperService>();

var app = builder.Build();

// Configure the HTTP request pipeline
app.MapPost("/api/transcribe", async (IFormFile file, WhisperService whisperService) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("No file uploaded.");
    }

    if (!file.FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("Only WAV files are supported in this basic sample.");
    }

    // Save uploaded file to a temporary location
    var tempFilePath = Path.GetTempFileName();
    try
    {
        using (var stream = new FileStream(tempFilePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var result = await whisperService.TranscribeAsync(tempFilePath);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Transcription failed");
        return Results.Problem($"Transcription failed: {ex.Message}");
    }
    finally
    {
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }
    }
});

app.MapGet("/", () => Results.Text("FasterWhisper.NET GPU API is running! Post a WAV file to /api/transcribe to transcribe using CUDA."));

app.Run();

// Singleton wrapper around FasterWhisper.NET model execution
public class WhisperService : IDisposable
{
    private readonly ILogger<WhisperService> _logger;
    private WhisperModel? _model;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _isInitialized;

    public WhisperService(ILogger<WhisperService> logger)
    {
        _logger = logger;
    }

    private async Task EnsureModelLoadedAsync()
    {
        if (_isInitialized) return;

        await _initializationLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            string modelName = "tiny";
            _logger.LogInformation("Resolving Whisper model: {Model}", modelName);

            var downloader = new ModelDownloader();
            string modelPath = await downloader.GetModelPathAsync(modelName);

            _logger.LogInformation("Loading model from path: {Path} on GPU (CUDA)", modelPath);
            var builder = WhisperModelBuilder.Create(modelPath)
                .WithDevice("cuda")
                .WithComputeType("default")
                .WithMemoryMapping()
                .WithNumReplicas(1);

            _model = builder.Build();
            _isInitialized = true;
            _logger.LogInformation("Model loaded successfully on CUDA.");
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<TranscriptionResponse> TranscribeAsync(string wavFilePath)
    {
        await EnsureModelLoadedAsync();

        if (_model == null)
        {
            throw new InvalidOperationException("Model was not loaded correctly.");
        }

        _logger.LogInformation("Reading WAV audio...");
        var audioProcessor = new AudioProcessor(_model.NMels);
        
        // LoadWav is synchronous, run it on a thread pool thread
        float[] pcm = await Task.Run(() => audioProcessor.LoadWav(wavFilePath));

        _logger.LogInformation("Running transcription on CUDA...");
        var options = new WhisperOptions
        {
            BeamSize = 5
        };

        // Transcribe is synchronous but internally serializes execution
        var rawSegments = await Task.Run(() => _model.Transcribe(pcm, language: "en", options: options));

        var responseSegments = new List<TextSegment>();
        foreach (var segment in rawSegments)
        {
            responseSegments.Add(new TextSegment(
                segment.Start,
                segment.End,
                segment.Text.Trim()
            ));
        }

        _logger.LogInformation("Transcription complete. Transcribed {Count} segments.", responseSegments.Count);
        return new TranscriptionResponse(responseSegments);
    }

    public void Dispose()
    {
        _model?.Dispose();
        _initializationLock.Dispose();
    }
}

public record TextSegment(double Start, double End, string Text);
public record TranscriptionResponse(List<TextSegment> Segments);
