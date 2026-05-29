# Noter
A beautiful, macOS/Apple Notes-inspired modern WPF notes application designed for Windows. It provides a sleek three-pane interface, rich text editing, interactive checklists, real-time deep search with highlighting, theme switching, and acrylic window transparency with custom window chrome.

---
<img width="1920" height="1200" alt="image" src="https://github.com/user-attachments/assets/25e797ae-b111-4775-b102-8d8e6a571a82" />

## ✨ Features

- **Apple Notes Aesthetics**: Premium interface featuring a three-pane layout (Folders, Notes List, and Editor) with fluid resizing and clean visual hierarchy.
- **Acrylic Backdrop & Rounded Corners**: Utilizes Windows native P/Invoke APIs to deliver a high-quality acrylic glassmorphism blur effect and rounded corners that look stunning on modern Windows.
- **Interactive Checklists**: Fully functional checklist support inside the RichTextBox editor with smart continuous list creation on Enter.
- **Dynamic Deep Search**: Scan and search all notes instantly. Search matches are highlighted in a readable soft red that automatically clears when searching ends, ensuring zero file-saving pollution.
- **Apple-inspired Custom Toolbar**: Built-in SVG-path toolbar mimicking the Apple Notes look, offering formatting options, checklist insertions, folder/sidebar toggling, and note creation/deletion.
- **Robust Settings Popup**: Customize the application experience with:
  - Theme toggler (Light, Dark, and System Sync).
  - Editor opacity adjustments.
- **Self-Contained Portable App**: Can be packaged into a standalone installer (`NoterSetup.exe`) that runs on other machines without needing .NET installation.

---

## 🛠️ Tech Stack

- **Framework**: .NET 10.0 WPF (Windows Presentation Foundation)
- **Language**: C# / XAML
- **Styling & Effects**: Native Windows APIs (Dwmapi.dll / uxtheme.dll) via P/Invoke for acrylic effects and custom window chrome.
- **Packaging**: Inno Setup 6 (for `.exe` installers)

---

## 📁 Project Structure

- `MainWindow.xaml` / `MainWindow.xaml.cs`: The core interface, pane layouts, toolbar events, search highlights, and file handling.
- `App.xaml` / `App.xaml.cs`: Application-wide resources, themes, and life-cycle management.
- `Note.cs`: Model representation of notes and folder directories.
- `installer_script.iss`: Setup script configuration for Inno Setup.
- **Local Storage Path**: Notes are stored in `%APPDATA%\Noter\` (isolated from the binary files so your personal notes aren't packaged into builds).

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows 10/11 (for Acrylic blur and rounded window corners support)

## ⚙️ How It Works

### Note Saving & Serialization
All notes are automatically saved locally in RTF (Rich Text Format) inside the `%APPDATA%\Noter` directory. The application handles real-time saving. To keep notes free from temporary search highlights, the RTF text is stripped of any custom highlight formatting right before saving, and restored immediately afterward.


I have fully vibe coded this project but in a few months i will code this app entirely by myself , built it because i needed a good note taking app on windows which isnt an electron app. 
