# Lite Media Viewer

Lite Media Viewer is a lightweight Windows desktop app for browsing and editing images, with playback for common video and audio formats.

## Features

- Browse images in a folder, with slideshow, zoom, pan, rotate, and flip.
- Crop and resize images; inspect image dimensions and available EXIF details.
- Save edited images as PNG, JPEG, BMP, or TIFF using **Save as**.
- Play media using the Windows Media Player engine installed on the computer.
- Trim media and crop video with FFmpeg.

Supported file extensions are defined in `Viewer.cs`. Playback and image-format support can also depend on the Windows components and codecs installed on your PC.

## Requirements

- Windows.
- A compatible .NET Framework C# compiler to build the app (the build script checks the .NET Framework 3.5 and 4.x compiler locations).
- Windows Media Player installed for in-app audio/video playback.
- `ffmpeg.exe` for video/audio trimming and video cropping. FFmpeg is a separate project and is not included in this repository.

## Build

Run `build.bat` from the project directory on Windows. The script compiles `Viewer.cs` into `LiteMediaViewer.exe`, using `icon.ico`.

To build the installer, open `installer.iss` with Inno Setup and compile it. If an `ffmpeg.exe` is present beside the installer script, the installer script includes it; only distribute FFmpeg after checking its applicable license and distribution requirements.

`make_icon.py` is an optional helper for regenerating `icon.ico`. It expects a `logo.png` in the project directory and requires Pillow (`python -m pip install Pillow`).

## Use

Launch the executable and open a file, or pass a media file path as an argument. Use **F1** in the app for keyboard and mouse shortcuts.

The current application requires an activation token obtained from its configured online service before the viewer opens. During activation, the app sends the token and a Windows machine identifier (the Windows MachineGuid, or the computer name as a fallback) to that service through the browser-based activation flow. The app also contacts its configured update endpoint when you choose **Check for updates**. Publishing this source does not remove those requirements; review and update the service configuration and privacy disclosures if needed.

## Open-source project files

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidance, [SECURITY.md](SECURITY.md) for reporting security issues, and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for community expectations.

## License

This project is distributed under the MIT License; see [LICENSE](LICENSE). FFmpeg, Windows, .NET Framework, and Windows Media Player are separate third-party products and are not relicensed by this project.
