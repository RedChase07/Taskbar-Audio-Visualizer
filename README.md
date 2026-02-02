# TaskbarVisualizer - C# Audio Visualizer

A Windows taskbar audio visualizer overlay similar to TranslucentTB with real-time frequency visualization.

Owner: "github seems to have auto filled in the README.md so i edited some things"

## Features
- Real-time audio spectrum visualization
- Transparent overlay on the Windows taskbar
- Click-through design (taskbar remains fully interactive - disabled by default)
- Gradient color effects
- System audio capture via WASAPI

## Requirements
- .NET 6.0 or later
- Windows 10/11
- Visual Studio 2022 (recommended) or VS Code with C# extension

## Building

```bash
dotnet build
```

## Running

```bash
dotnet run
```

Or run the compiled executable directly:

```bash
.\bin\Release\net6.0-windows\TaskbarVisualizer.exe
```

## How It Works

1. **Audio Capture**: Uses WASAPI loopback capture to capture system audio output
2. **Frequency Analysis**: Analyzes the audio frequency spectrum
3. **Visualization**: Displays frequency bars that react to audio in real-time

## Dependencies

- NAudio 2.2.1 - For audio capture and processing
- Windows Forms - For UI rendering
- Windows API - For taskbar integration and window manipulation

## License

MIT License



