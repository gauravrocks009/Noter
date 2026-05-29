using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Documents;
using System.Windows.Media;
using System.Text.Json;
using System.IO;
using System.Linq;

namespace AppleNotesWpf;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("dwmapi.dll")]
    static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);

    [StructLayout(LayoutKind.Sequential)]
    struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fEnable;
        public IntPtr hRgnBlur;
        [MarshalAs(UnmanagedType.Bool)] public bool fTransitionOnMaximized;
    }

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const uint DWM_BB_ENABLE = 0x1;

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern Int32 ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProc(IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    private readonly string _appDataPath;
    private List<Note> _allNotes = new();
    private System.Collections.ObjectModel.ObservableCollection<Note> _notes = new();
    private string? _selectedFolder = null; // null = "All iCloud", "" = root "Notes", else = folder name
    private bool _isInternalChange = false;

    // Virtual Folder State
    private class VirtualFolderState
    {
        public bool AllICloudDeleted { get; set; } = false;
        public string AllICloudName { get; set; } = "All iCloud";
        public bool NotesDeleted { get; set; } = false;
        public string NotesName { get; set; } = "Notes";
    }
    private VirtualFolderState _folderState = new();

    public MainWindow()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(EditorBox, EditorBox_Pasting);

        _appDataPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Noter");
        
        // Migration from old name "AppleNotesClone"
        string oldPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppleNotesClone");
        if (System.IO.Directory.Exists(oldPath))
        {
            // If Noter doesn't exist OR is empty, move the files
            bool noterIsEmpty = !System.IO.Directory.Exists(_appDataPath) || (System.IO.Directory.GetFileSystemEntries(_appDataPath).Length == 0);
            if (noterIsEmpty)
            {
                try 
                { 
                    if (!System.IO.Directory.Exists(_appDataPath)) System.IO.Directory.CreateDirectory(_appDataPath);
                    foreach (var file in Directory.GetFiles(oldPath, "*.*", SearchOption.AllDirectories))
                    {
                        string relativePath = Path.GetRelativePath(oldPath, file);
                        string destFile = Path.Combine(_appDataPath, relativePath);
                        string? destDir = Path.GetDirectoryName(destFile);
                        if (destDir != null && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                        File.Copy(file, destFile, true);
                    }
                    // Optionally delete old path if migration was successful? 
                    // No, safer to leave it for now or just rename the whole dir if dest doesn't exist.
                    if (!Directory.Exists(_appDataPath)) Directory.Move(oldPath, _appDataPath);
                } 
                catch { }
            }
        }

        if (!System.IO.Directory.Exists(_appDataPath))
            System.IO.Directory.CreateDirectory(_appDataPath);

        LoadFolderState();

        NotesList.ItemsSource = _notes;
        LoadFolders();
        LoadNotes();

        // Select "All iCloud" by default
        if (FoldersListBox.Items.Count > 0)
            FoldersListBox.SelectedIndex = 0;

        var timer = new System.Windows.Threading.DispatcherTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.Tick += (s, e) => UpdateContrast();
        timer.Start();
    }

    // ======================== FOLDERS ========================

    private void LoadFolderState()
    {
        try
        {
            var path = Path.Combine(_appDataPath, "folders.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _folderState = JsonSerializer.Deserialize<VirtualFolderState>(json) ?? new VirtualFolderState();
            }
        }
        catch { }
    }

    private void SaveFolderState()
    {
        try
        {
            var path = Path.Combine(_appDataPath, "folders.json");
            var json = JsonSerializer.Serialize(_folderState);
            File.WriteAllText(path, json);
        }
        catch { }
    }

    private void LoadFolders()
    {
        FoldersListBox.Items.Clear();

        // "All iCloud" item (if not deleted)
        if (!_folderState.AllICloudDeleted)
            FoldersListBox.Items.Add(CreateFolderItem("\xED43", _folderState.AllICloudName, isCustomFolder: true));
        
        // "Notes" item (root folder, if not deleted)
        if (!_folderState.NotesDeleted)
            FoldersListBox.Items.Add(CreateFolderItem("\xED41", _folderState.NotesName, isCustomFolder: true));

        // Scan for subdirectories
        foreach (var dir in System.IO.Directory.GetDirectories(_appDataPath))
        {
            var name = System.IO.Path.GetFileName(dir);
            FoldersListBox.Items.Add(CreateFolderItem("\xED41", name, isCustomFolder: true));
        }
    }

    private void ToggleFolders_Click(object sender, RoutedEventArgs e)
    {
        var splitWidth = new GridLength(5);
        
        // Check if currently expanded (width > 44)
        if (RootCol0.Width.Value > 44)
        {
            // Collapse to 44px gutter
            var gutterWidth = new GridLength(44);
            var zeroWidth = new GridLength(0);

            // Update columns
            RootCol0.Width = gutterWidth;
            RootCol1.Width = zeroWidth;  // Hide primary splitter

            // Hide list and title, but KEEP the toggle and new folder icons
            FoldersTitle.Visibility = Visibility.Collapsed;
            FoldersListBox.Visibility = Visibility.Collapsed;
            
            // Re-align header panel and icons for the thin gutter to prevent cutting
            FoldersHeaderPanel.Margin = new Thickness(0, 5, 0, 5);
            FoldersHeaderPanel.HorizontalAlignment = HorizontalAlignment.Center;

            NewFolderBtn.HorizontalAlignment = HorizontalAlignment.Center;
            NewFolderBtn.Margin = new Thickness(0, 10, 0, 10);
            NewFolderBtn.Visibility = Visibility.Visible; 
            
            // Specifically for the Toggle button, ensure it's also centered
            ToggleFoldersBtn.HorizontalAlignment = HorizontalAlignment.Center;
            ToggleFoldersBtn.Margin = new Thickness(0, 0, 0, 0);
        }
        else
        {
            // Expand to defaults
            var sidebarWidth = new GridLength(200);

            RootCol0.Width = sidebarWidth;
            RootCol1.Width = splitWidth;

            // Restore header panel margin
            FoldersHeaderPanel.Margin = new Thickness(15, 5, 0, 5);
            FoldersHeaderPanel.HorizontalAlignment = HorizontalAlignment.Left;

            FoldersTitle.Visibility = Visibility.Visible;
            NewFolderBtn.Visibility = Visibility.Visible;
            NewFolderBtn.HorizontalAlignment = HorizontalAlignment.Left;
            NewFolderBtn.Margin = new Thickness(15, 10, 15, 10);
            
            ToggleFoldersBtn.HorizontalAlignment = HorizontalAlignment.Left;
            ToggleFoldersBtn.Margin = new Thickness(0, 0, 5, 0);

            FoldersListBox.Visibility = Visibility.Visible;
            FoldersBg.Visibility = Visibility.Visible;
        }
    }

    private System.Windows.Controls.ListBoxItem CreateFolderItem(string icon, string name, bool isCustomFolder = false)
    {
        var item = new System.Windows.Controls.ListBoxItem
        {
            Background = Brushes.Transparent,
            Foreground = EditorBox.Foreground,
            Padding = new Thickness(15, 5, 15, 5),
            Tag = name // Store folder name in Tag for lookup
        };

        if (isCustomFolder)
        {
            var ctx = new System.Windows.Controls.ContextMenu
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                Padding = new Thickness(4),
                HasDropShadow = false
            };

            ctx.Loaded += (s, e) => 
            {
                bool isLight = EditorBox.Foreground is SolidColorBrush b && b.Color == Colors.Black || EditorBox.Foreground == Brushes.Black;
                ctx.Background = isLight ? new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(100, 20, 20, 20));
                ctx.Foreground = isLight ? Brushes.Black : Brushes.White;
            };

            ctx.Opened += (s, ev) =>
            {
                if (PresentationSource.FromVisual(ctx) is HwndSource hwndSource)
                {
                    var hwnd = hwndSource.Handle;
                    
                    // Strip WS_EX_LAYERED to fix DWM white box bug on WPF popups
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_LAYERED);

                    int backdropType = 3; // Acrylic
                    DwmSetWindowAttribute(hwnd, 38, ref backdropType, sizeof(int));
                    
                    int corner = DWMWCP_ROUND;
                    DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));

                    var blur = new DWM_BLURBEHIND { dwFlags = DWM_BB_ENABLE, fEnable = true, hRgnBlur = IntPtr.Zero };
                    DwmEnableBlurBehindWindow(hwnd, ref blur);
                    
                    hwndSource.CompositionTarget!.BackgroundColor = Colors.Transparent;
                }
            };

            var renameItem = new System.Windows.Controls.MenuItem { Header = "Rename", Background = Brushes.Transparent };
            renameItem.Click += RenameFolder_Click;
            var deleteItem = new System.Windows.Controls.MenuItem { Header = "Delete", Background = Brushes.Transparent };
            deleteItem.Click += DeleteFolder_Click;
            ctx.Items.Add(renameItem);
            ctx.Items.Add(deleteItem);
            item.ContextMenu = ctx;
        }

        var stack = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = icon,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center
        });
        item.Content = stack;
        return item;
    }

    private void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && 
            menuItem.Parent is System.Windows.Controls.ContextMenu menu &&
            menu.PlacementTarget is System.Windows.Controls.ListBoxItem item &&
            item.Tag is string oldFolderName)
        {
            string newFolderName = oldFolderName;
            var dialog = CreateCustomDialog("Rename Folder", 300, 130);
            var root = (System.Windows.Controls.Grid)dialog.Content;

            var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
            var tb = new System.Windows.Controls.TextBox { Text = newFolderName, FontSize = 14 };
            tb.SelectAll();
            var btn = new System.Windows.Controls.Button { Content = "Rename", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(20, 5, 20, 5), HorizontalAlignment = HorizontalAlignment.Right };
            btn.Click += (s, ev) => { newFolderName = tb.Text; dialog.DialogResult = true; };
            sp.Children.Add(tb);
            sp.Children.Add(btn);
            
            root.Children.Add(sp);
            System.Windows.Controls.Grid.SetRow(sp, 1);

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(newFolderName) && newFolderName != oldFolderName)
            {
                // Special handling for virtual folders "All iCloud" and "Notes"
                if (oldFolderName == _folderState.AllICloudName || oldFolderName == _folderState.NotesName)
                {
                    if (oldFolderName == _folderState.AllICloudName)
                    {
                        _folderState.AllICloudName = newFolderName;
                    }
                    else if (oldFolderName == _folderState.NotesName)
                    {
                        _folderState.NotesName = newFolderName;
                    }
                    
                    SaveFolderState();

                    // Update UI label only
                    if (item.Content is System.Windows.Controls.StackPanel stack && stack.Children.Count >= 2 && stack.Children[1] is System.Windows.Controls.TextBlock textBlock)
                    {
                        textBlock.Text = newFolderName;
                    }
                    item.Tag = newFolderName;
                    
                    if (oldFolderName == _folderState.AllICloudName && _selectedFolder == null)
                        FilterNotes(); // Trigger UI update if selected, but keep internal _selectedFolder logic null
                    else if (oldFolderName == _folderState.NotesName && _selectedFolder == "")
                        FilterNotes(); // Internal logic uses "" for Notes

                    return;
                }

                // Normal physical subfolder logic
                var oldPath = System.IO.Path.Combine(_appDataPath, oldFolderName);
                var newPath = System.IO.Path.Combine(_appDataPath, newFolderName);
                
                if (System.IO.Directory.Exists(oldPath) && !System.IO.Directory.Exists(newPath))
                {
                    System.IO.Directory.Move(oldPath, newPath);
                    foreach (var note in _allNotes.Where(n => n.FolderName == oldFolderName))
                    {
                        note.FolderName = newFolderName;
                    }
                    if (_selectedFolder == oldFolderName) _selectedFolder = newFolderName;
                    
                    LoadFolders();
                    for (int i = 0; i < FoldersListBox.Items.Count; i++)
                    {
                        if (FoldersListBox.Items[i] is System.Windows.Controls.ListBoxItem lbi &&
                            lbi.Tag as string == _selectedFolder)
                        {
                            FoldersListBox.SelectedIndex = i;
                            break;
                        }
                    }
                    FilterNotes();
                }
                else if (System.IO.Directory.Exists(newPath))
                {
                    var msg = CreateCustomDialog("Error", 300, 140);
                    var rootMsg = (System.Windows.Controls.Grid)msg.Content;
                    
                    var msp = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
                    msp.Children.Add(new System.Windows.Controls.TextBlock { Text = "A folder with this name already exists.", TextWrapping = TextWrapping.Wrap });
                    var mbtn = new System.Windows.Controls.Button { Content = "OK", Margin = new Thickness(0, 15, 0, 0), Width = 60, HorizontalAlignment = HorizontalAlignment.Right };
                    mbtn.Click += (s, ev) => msg.DialogResult = true;
                    msp.Children.Add(mbtn);
                    
                    rootMsg.Children.Add(msp);
                    System.Windows.Controls.Grid.SetRow(msp, 1);
                    
                    msg.ShowDialog();
                }
            }
        }
    }

    private void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && 
            menuItem.Parent is System.Windows.Controls.ContextMenu menu &&
            menu.PlacementTarget is System.Windows.Controls.ListBoxItem item &&
            item.Tag is string folderName)
        {
            var dialog = CreateCustomDialog("Delete Folder", 350, 150);
            var root = (System.Windows.Controls.Grid)dialog.Content;
            
            var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new System.Windows.Controls.TextBlock { Text = $"Are you sure you want to delete '{folderName}' and all its notes?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,15) });
            
            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnYes = new System.Windows.Controls.Button { Content = "Yes", Padding = new Thickness(15, 5, 15, 5), Margin = new Thickness(0,0,10,0) };
            var btnNo = new System.Windows.Controls.Button { Content = "No", Padding = new Thickness(15, 5, 15, 5) };
            
            var result = false;
            btnYes.Click += (s, ev) => { result = true; dialog.DialogResult = true; };
            btnNo.Click += (s, ev) => { dialog.DialogResult = false; };
            
            btnPanel.Children.Add(btnYes);
            btnPanel.Children.Add(btnNo);
            sp.Children.Add(btnPanel);
            
            root.Children.Add(sp);
            System.Windows.Controls.Grid.SetRow(sp, 1);

            if (dialog.ShowDialog() == true && result)
            {
                // Special handling for virtual folders
                if (folderName == _folderState.AllICloudName || folderName == _folderState.NotesName || item.Tag as string == _folderState.AllICloudName || item.Tag as string == _folderState.NotesName)
                {
                    bool isNotes = folderName == _folderState.NotesName || item.Tag as string == _folderState.NotesName;
                    string? targetFolderStr = isNotes ? "" : null;

                    // Delete affected files from disk (root directory for Notes, all directories for All iCloud)
                    var notesToDelete = targetFolderStr == null ? _allNotes.ToList() : _allNotes.Where(n => n.FolderName == "").ToList();
                    foreach (var note in notesToDelete)
                    {
                        var path = GetNotePath(note);
                        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                    }

                    // Remove from memory
                    if (targetFolderStr == null) _allNotes.Clear();
                    else _allNotes.RemoveAll(n => n.FolderName == "");

                    // Save state
                    if (isNotes) _folderState.NotesDeleted = true;
                    else _folderState.AllICloudDeleted = true;
                    SaveFolderState();

                    FoldersListBox.Items.Remove(item); // Remove from UI
                    if (_selectedFolder == targetFolderStr) _selectedFolder = null; // Reset selection if we deleted what we were looking at
                    FilterNotes();
                    return;
                }

                // Normal physical subfolder logic
                var folderPath = System.IO.Path.Combine(_appDataPath, folderName);
                if (System.IO.Directory.Exists(folderPath))
                {
                    System.IO.Directory.Delete(folderPath, true);
                    _allNotes.RemoveAll(n => n.FolderName == folderName);
                    
                    if (_selectedFolder == folderName) _selectedFolder = null;
                    
                    LoadFolders();
                    for (int i = 0; i < FoldersListBox.Items.Count; i++)
                    {
                        var tag = (FoldersListBox.Items[i] as System.Windows.Controls.ListBoxItem)?.Tag as string;
                        if ((_selectedFolder == null && tag == "All iCloud") || tag == _selectedFolder)
                        {
                            FoldersListBox.SelectedIndex = i;
                            break;
                        }
                    }
                    FilterNotes();
                }
            }
        }
    }


    private void FoldersListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FoldersListBox.SelectedItem is System.Windows.Controls.ListBoxItem item)
        {
            var tag = item.Tag as string;
            if (tag == _folderState.AllICloudName)
                _selectedFolder = null;
            else if (tag == _folderState.NotesName)
                _selectedFolder = "";
            else
                _selectedFolder = tag ?? "";

            FilterNotes();
        }
    }

    private void FilterNotes()
    {
        _notes.Clear();
        var filtered = _selectedFolder == null
            ? _allNotes
            : _allNotes.Where(n => n.FolderName == _selectedFolder).ToList();

        foreach (var n in filtered.OrderByDescending(n => n.LastModified))
            _notes.Add(n);

        if (_notes.Count > 0)
            NotesList.SelectedIndex = 0;
        else
            EditorBox.Document.Blocks.Clear();
    }

    private string GetNotePath(Note note)
    {
        if (string.IsNullOrEmpty(note.FolderName))
            return System.IO.Path.Combine(_appDataPath, $"{note.Id}.rtf");
        return System.IO.Path.Combine(_appDataPath, note.FolderName, $"{note.Id}.rtf");
    }

    private void LoadNotes()
    {
        _allNotes.Clear();
        _notes.Clear();

        // Load notes from root folder
        LoadNotesFrom(_appDataPath, "");

        // Load notes from each subfolder
        foreach (var dir in System.IO.Directory.GetDirectories(_appDataPath))
        {
            var folderName = System.IO.Path.GetFileName(dir);
            LoadNotesFrom(dir, folderName);
        }

        _allNotes = _allNotes.OrderByDescending(n => n.LastModified).ToList();
    }

    private void LoadNotesFrom(string dirPath, string folderName)
    {
        var files = System.IO.Directory.GetFiles(dirPath, "*.rtf");
        foreach (var file in files)
        {
            var note = new Note
            {
                Id = System.IO.Path.GetFileNameWithoutExtension(file),
                FolderName = folderName,
                LastModified = System.IO.File.GetLastWriteTime(file)
            };
            try
            {
                var doc = new FlowDocument();
                var tr = new TextRange(doc.ContentStart, doc.ContentEnd);
                using var fs = new System.IO.FileStream(file, System.IO.FileMode.Open);
                tr.Load(fs, DataFormats.Rtf);
                UpdateNoteMetadata(doc, note);
            }
            catch { }
            _allNotes.Add(note);
        }
    }

    private void UpdateNoteMetadata(FlowDocument doc, Note note)
    {
        // Update content for search - ensure we get everything
        var tr = new TextRange(doc.ContentStart, doc.ContentEnd);
        note.Content = tr.Text;

        // Iterate through blocks to find the first and second non-empty paragraphs
        // This is more robust than splitting the whole text string which may have mixed line endings
        var lines = new List<string>();
        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph p)
            {
                string pText = new TextRange(p.ContentStart, p.ContentEnd).Text.Trim();
                // Filter out common control characters and whitespace
                pText = pText.Replace("\r", "").Replace("\n", "").Trim();
                
                if (!string.IsNullOrEmpty(pText))
                {
                    lines.Add(pText);
                    if (lines.Count >= 2) break;
                }
            }
            else if (block is List list)
            {
                foreach (var listItem in list.ListItems)
                {
                    string liText = new TextRange(listItem.ContentStart, listItem.ContentEnd).Text.Trim();
                    liText = liText.Replace("\r", "").Replace("\n", "").Trim();
                    if (!string.IsNullOrEmpty(liText))
                    {
                        lines.Add(liText);
                        if (lines.Count >= 2) break;
                    }
                }
                if (lines.Count >= 2) break;
            }
        }

        if (lines.Count > 0)
        {
            note.Title = lines[0];
            note.Preview = lines.Count > 1 ? lines[1] : "No additional text";
        }
        else
        {
            // Only reset to "New Note" if it's actually empty and we don't have a title yet
            if (string.IsNullOrEmpty(note.Title) || note.Title == "New Note")
            {
                note.Title = "New Note";
                note.Preview = "No additional text";
            }
        }
    }

    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        // Put new note in the currently selected folder (or root if "All iCloud")
        var folder = _selectedFolder ?? "";
        var note = new Note { FolderName = folder };

        // Ensure folder directory exists
        if (!string.IsNullOrEmpty(folder))
        {
            var folderPath = System.IO.Path.Combine(_appDataPath, folder);
            if (!System.IO.Directory.Exists(folderPath))
                System.IO.Directory.CreateDirectory(folderPath);
        }

        _allNotes.Insert(0, note);
        _notes.Insert(0, note);
        NotesList.SelectedItem = note;
        EditorBox.Focus();
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        string folderName = "New Folder";
        var dialog = CreateCustomDialog("New Folder", 300, 130);
        var root = (System.Windows.Controls.Grid)dialog.Content;

        var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
        var tb = new System.Windows.Controls.TextBox { Text = folderName, FontSize = 14 };
        tb.SelectAll();
        var btn = new System.Windows.Controls.Button { Content = "Create", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(20, 5, 20, 5), HorizontalAlignment = HorizontalAlignment.Right };
        btn.Click += (s, ev) => { folderName = tb.Text; dialog.DialogResult = true; };
        sp.Children.Add(tb);
        sp.Children.Add(btn);
        
        root.Children.Add(sp);
        System.Windows.Controls.Grid.SetRow(sp, 1);

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(folderName))
        {
            var folderPath = System.IO.Path.Combine(_appDataPath, folderName);
            if (!System.IO.Directory.Exists(folderPath))
                System.IO.Directory.CreateDirectory(folderPath);

            // Reload folders and select the new one
            LoadFolders();
            for (int i = 0; i < FoldersListBox.Items.Count; i++)
            {
                if (FoldersListBox.Items[i] is System.Windows.Controls.ListBoxItem item &&
                    item.Tag as string == folderName)
                {
                    FoldersListBox.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (NotesList.SelectedItem is not Note note) return;

        var file = GetNotePath(note);
        if (System.IO.File.Exists(file))
            System.IO.File.Delete(file);

        int idx = _notes.IndexOf(note);
        _allNotes.Remove(note);
        _notes.Remove(note);

        if (_notes.Count > 0)
            NotesList.SelectedIndex = Math.Min(idx, _notes.Count - 1);
        else
            EditorBox.Document.Blocks.Clear();
    }

    private void NotesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (NotesList.SelectedItem is Note note)
        {
            _isInternalChange = true;
            var file = GetNotePath(note);
            if (System.IO.File.Exists(file))
            {
                try
                {
                    var tr = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
                    using var fs = new System.IO.FileStream(file, System.IO.FileMode.Open);
                    tr.Load(fs, DataFormats.Rtf);
                }
                catch
                {
                    EditorBox.Document.Blocks.Clear();
                }
            }
            else
            {
                EditorBox.Document.Blocks.Clear();
            }
            _isInternalChange = false;

            if (SearchBox != null && !string.IsNullOrWhiteSpace(SearchBox.Text) && SearchBox.Text != "Search")
            {
                ApplySearchHighlights(SearchBox.Text);
            }
        }
    }

    private void EditorBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs? e)
    {
        if (_isInternalChange || NotesList.SelectedItem is not Note note) return;

        _isInternalChange = true;
        
        // --- AUTO-HEADING LOGIC ---
        // First block gets FontSize=48, others get FontSize=16
        var blocks = EditorBox.Document.Blocks.ToList();
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph p)
            {
                double targetSize = (i == 0) ? 48 : 16;
                // Only apply if different to avoid unnecessary overhead/flicker
                if (p.FontSize != targetSize)
                {
                    p.FontSize = targetSize;
                }
            }
        }

        string? activeQuery = null;
        if (SearchBox != null && !string.IsNullOrWhiteSpace(SearchBox.Text) && SearchBox.Text != "Search")
        {
            activeQuery = SearchBox.Text;
            ClearSearchHighlights();
        }

        var startVector = EditorBox.Document.ContentStart.GetOffsetToPosition(EditorBox.CaretPosition);

        var file = GetNotePath(note);
        // Ensure folder directory exists
        var dir = System.IO.Path.GetDirectoryName(file)!;
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);
        var tr = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
        using (var fs = new System.IO.FileStream(file, System.IO.FileMode.Create))
        {
            tr.Save(fs, DataFormats.Rtf);
        }

        if (activeQuery != null)
        {
            ApplySearchHighlights(activeQuery);
        }

        TextPointer newPos = EditorBox.Document.ContentStart.GetPositionAtOffset(startVector, LogicalDirection.Forward);
        if (newPos != null) EditorBox.CaretPosition = newPos;
        _isInternalChange = false;

        UpdateNoteMetadata(EditorBox.Document, note);
        note.LastModified = DateTime.Now;
        // InotifyPropertyChanged will handle Title/Preview updates, but Refresh sorts the list if needed
        NotesList.Items.Refresh();
    }

    private void EditorBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            var curBlock = EditorBox.Document.Blocks
                .Where(b => b.ContentStart.CompareTo(EditorBox.CaretPosition) <= 0 &&
                            b.ContentEnd.CompareTo(EditorBox.CaretPosition) >= 0)
                .FirstOrDefault();

            if (curBlock is Paragraph p)
            {
                var paraText = new TextRange(p.ContentStart, p.ContentEnd).Text;
                
                // 1. CHECKLIST HANDLING
                if (paraText.StartsWith("☐") || paraText.StartsWith("☑"))
                {
                    e.Handled = true;
                    var newRun = new Run("☐ ");
                    var newParagraph = new Paragraph(newRun);
                    EditorBox.Document.Blocks.InsertAfter(p, newParagraph);
                    EditorBox.CaretPosition = newRun.ContentEnd;
                    EditorBox.Focus();
                    EditorBox_TextChanged(this, null);
                    return;
                }

                // 2. NUMBERED LIST HANDLING
                var trimmed = paraText.TrimStart();
                int dotIdx = -1;
                int digitCount = 0;
                for (int i = 0; i < trimmed.Length; i++)
                {
                    if (char.IsDigit(trimmed[i]))
                    {
                        digitCount++;
                    }
                    else if (trimmed[i] == '.' && digitCount > 0)
                    {
                        dotIdx = i;
                        break;
                    }
                    else
                    {
                        break;
                    }
                }

                if (dotIdx != -1)
                {
                    bool isValidListPrefix = false;
                    string rest = trimmed.Substring(dotIdx + 1);
                    if (string.IsNullOrWhiteSpace(rest) || rest.StartsWith(" "))
                    {
                        isValidListPrefix = true;
                    }

                    if (isValidListPrefix)
                    {
                        string numberStr = trimmed.Substring(0, dotIdx);
                        if (int.TryParse(numberStr, out int currentNum))
                        {
                            int nextNum = currentNum + 1;
                            e.Handled = true;
                            
                            string leadingWhitespace = "";
                            for (int i = 0; i < paraText.Length; i++)
                            {
                                if (char.IsWhiteSpace(paraText[i]))
                                    leadingWhitespace += paraText[i];
                                else
                                    break;
                            }

                            var newRun = new Run(leadingWhitespace + nextNum + ". ");
                            var newParagraph = new Paragraph(newRun);
                            EditorBox.Document.Blocks.InsertAfter(p, newParagraph);
                            EditorBox.CaretPosition = newRun.ContentEnd;
                            EditorBox.Focus();
                            EditorBox_TextChanged(this, null);
                        }
                    }
                }
            }
        }
    }

    // ======================== SEARCH ========================

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SearchBox.Text == "Search") SearchBox.Text = "";
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            SearchBox.TextChanged -= SearchBox_TextChanged;
            SearchBox.Text = "Search";
            SearchBox.TextChanged += SearchBox_TextChanged;
            ClearSearchHighlights();
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (NotesList == null || SearchBox == null) return;
        if (SearchBox.Text == "Search")
        {
            ClearSearchHighlights();
            return;
        }

        string q = SearchBox.Text.ToLower();
        if (string.IsNullOrWhiteSpace(q))
        {
            ClearSearchHighlights();
            NotesList.ItemsSource = _notes;
            if (_notes.Count > 0 && NotesList.SelectedIndex == -1) NotesList.SelectedIndex = 0;
            return;
        }

        ApplySearchHighlights(q);

        var filtered = System.Linq.Enumerable.Where(_notes, n =>
            n.Title.ToLower().Contains(q) ||
            n.Preview.ToLower().Contains(q) ||
            (n.Content ?? "").ToLower().Contains(q));
        NotesList.ItemsSource = filtered;
    }

    // ======================== WINDOW ========================

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else if (WindowState != WindowState.Maximized)
            {
                DragMove();
            }
        }
    }

    private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            if (WindowState == WindowState.Maximized)
            {
                // Get absolute mouse position
                var point = PointToScreen(e.GetPosition(this));
                
                // Convert to device-independent pixels (DPI scaling)
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    var dpiPoint = source.CompositionTarget.TransformFromDevice.Transform(point);
                    
                    // Set normal window bounds so it stays under the cursor
                    Left = dpiPoint.X - (RestoreBounds.Width / 2);
                    Top = dpiPoint.Y - 15;
                }
                
                WindowState = WindowState.Normal;
                try
                {
                    DragMove();
                }
                catch { }
            }
        }
    }

    // ======================== CHECKLIST (Unicode ☐/☑) ========================

    private const string UNCHECKED = "☐ ";
    private const string CHECKED = "☑ ";

    private void AddChecklist_Click(object sender, RoutedEventArgs e)
    {
        if (NotesList.SelectedItem is not Note) return;

        var newRun = new Run(UNCHECKED);
        var paragraph = new Paragraph(newRun);
        EditorBox.Document.Blocks.Add(paragraph);
        EditorBox.CaretPosition = newRun.ContentEnd;
        EditorBox.Focus();
    }

    /// <summary>
    /// Handles clicking on ☐/☑ to toggle checklist state.
    /// When ☐ is clicked → becomes ☑ + strikethrough on the entire line.
    /// When ☑ is clicked → becomes ☐ + strikethrough removed.
    /// </summary>
    private void EditorBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Find which text position was clicked
        var pos = EditorBox.GetPositionFromPoint(e.GetPosition(EditorBox), false);
        if (pos == null) return;

        // Find the paragraph that was clicked
        var clickedBlock = EditorBox.Document.Blocks
            .Where(b => b.ContentStart.CompareTo(pos) <= 0 && b.ContentEnd.CompareTo(pos) >= 0)
            .FirstOrDefault();

        if (clickedBlock is not Paragraph para) return;

        var paraRange = new TextRange(para.ContentStart, para.ContentEnd);
        var text = paraRange.Text;

        if (!text.StartsWith("☐") && !text.StartsWith("☑")) return;

        // Check if the click is near the checkbox character (within first ~20 pixels)
        var paraStart = para.ContentStart;
        var charRect = paraStart.GetCharacterRect(LogicalDirection.Forward);
        var clickPoint = e.GetPosition(EditorBox);

        // Only toggle if clicking within the first 25 pixels (where the ☐/☑ character is)
        if (clickPoint.X - charRect.X > 25) return;

        e.Handled = true; // Prevent caret from moving

        _isInternalChange = true;

        if (text.StartsWith("☐"))
        {
            // Toggle to CHECKED: replace ☐ with ☑
            // Find the first Run containing ☐
            foreach (var inline in para.Inlines.ToList())
            {
                if (inline is Run run && run.Text.Contains("☐"))
                {
                    run.Text = run.Text.Replace("☐", "☑");
                }
            }
            // Apply strikethrough to entire paragraph text
            paraRange.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
            paraRange.ApplyPropertyValue(TextElement.ForegroundProperty,
                new SolidColorBrush(Color.FromArgb(160, 128, 128, 128)));
        }
        else if (text.StartsWith("☑"))
        {
            // Toggle to UNCHECKED: replace ☑ with ☐
            foreach (var inline in para.Inlines.ToList())
            {
                if (inline is Run run && run.Text.Contains("☑"))
                {
                    run.Text = run.Text.Replace("☑", "☐");
                }
            }
            // Remove strikethrough
            paraRange.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
            paraRange.ApplyPropertyValue(TextElement.ForegroundProperty, EditorBox.Foreground);
        }

        _isInternalChange = false;
    }

    private void EditorBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (NotesList.SelectedItem is not Note note) return;

        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            if (string.IsNullOrEmpty(text)) return;

            // Split into lines
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            // Check if there are multiple lines and any of them start with checklist or list prefixes
            // Or if we are currently inside a checklist block, we might want to convert all of them
            bool isChecklistPaste = false;
            
            // 1. Detect if the text contains checkbox indicators
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("☐") || trimmed.StartsWith("☑") ||
                    trimmed.StartsWith("[ ]") || trimmed.StartsWith("[x]") || trimmed.StartsWith("[X]") ||
                    trimmed.StartsWith("- [ ]") || trimmed.StartsWith("- [x]") || trimmed.StartsWith("- [X]") ||
                    trimmed.StartsWith("* [ ]") || trimmed.StartsWith("* [x]") || trimmed.StartsWith("* [X]"))
                {
                    isChecklistPaste = true;
                    break;
                }
            }

            // 2. Also check if the current line where caret is located is a checklist line
            if (!isChecklistPaste && lines.Length > 1)
            {
                var caretPara = EditorBox.CaretPosition.Paragraph;
                if (caretPara != null)
                {
                    var paraText = new TextRange(caretPara.ContentStart, caretPara.ContentEnd).Text;
                    if (paraText.StartsWith("☐") || paraText.StartsWith("☑"))
                    {
                        isChecklistPaste = true;
                    }
                }
            }

            if (isChecklistPaste)
            {
                e.CancelCommand(); // Prevent standard paste
                
                _isInternalChange = true;
                
                // Delete selected text if any
                if (!EditorBox.Selection.IsEmpty)
                {
                    EditorBox.Selection.Text = string.Empty;
                }

                // Get current paragraph
                var caretPara = EditorBox.CaretPosition.Paragraph;
                var currentBlock = (Block?)caretPara ?? EditorBox.Document.Blocks.FirstBlock;

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var cleanedText = line;
                    bool isChecked = false;
                    bool hasCheckbox = false;

                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("☐"))
                    {
                        cleanedText = trimmed.Substring(1).TrimStart();
                        hasCheckbox = true;
                    }
                    else if (trimmed.StartsWith("☑"))
                    {
                        cleanedText = trimmed.Substring(1).TrimStart();
                        isChecked = true;
                        hasCheckbox = true;
                    }
                    else if (trimmed.StartsWith("- [ ]") || trimmed.StartsWith("* [ ]"))
                    {
                        cleanedText = trimmed.Substring(5).TrimStart();
                        hasCheckbox = true;
                    }
                    else if (trimmed.StartsWith("- [x]") || trimmed.StartsWith("- [X]") || trimmed.StartsWith("* [x]") || trimmed.StartsWith("* [X]"))
                    {
                        cleanedText = trimmed.Substring(5).TrimStart();
                        isChecked = true;
                        hasCheckbox = true;
                    }
                    else if (trimmed.StartsWith("[ ]"))
                    {
                        cleanedText = trimmed.Substring(3).TrimStart();
                        hasCheckbox = true;
                    }
                    else if (trimmed.StartsWith("[x]") || trimmed.StartsWith("[X]"))
                    {
                        cleanedText = trimmed.Substring(3).TrimStart();
                        isChecked = true;
                        hasCheckbox = true;
                    }
                    else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
                    {
                        cleanedText = trimmed.Substring(2).TrimStart();
                        hasCheckbox = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(line))
                    {
                        hasCheckbox = true;
                    }

                    if (hasCheckbox)
                    {
                        string prefix = isChecked ? CHECKED : UNCHECKED;
                        
                        var newPara = new Paragraph();
                        var newRun = new Run(prefix + cleanedText);
                        newPara.Inlines.Add(newRun);

                        if (isChecked)
                        {
                            var range = new TextRange(newPara.ContentStart, newPara.ContentEnd);
                            range.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
                            range.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromArgb(160, 128, 128, 128)));
                        }
                        
                        if (i == 0 && caretPara != null)
                        {
                            var caretRange = new TextRange(caretPara.ContentStart, caretPara.ContentEnd);
                            string caretText = caretRange.Text.Trim();
                            if (string.IsNullOrEmpty(caretText) || caretText == "☐" || caretText == "☑" || caretText == "☐ " || caretText == "☑ ")
                            {
                                caretPara.Inlines.Clear();
                                caretPara.Inlines.Add(newRun);
                                if (isChecked)
                                {
                                    caretRange.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
                                    caretRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromArgb(160, 128, 128, 128)));
                                }
                                currentBlock = caretPara;
                            }
                            else
                            {
                                EditorBox.Document.Blocks.InsertAfter(caretPara, newPara);
                                currentBlock = newPara;
                            }
                        }
                        else if (currentBlock != null)
                        {
                            EditorBox.Document.Blocks.InsertAfter(currentBlock, newPara);
                            currentBlock = newPara;
                        }
                        
                        if (currentBlock != null)
                        {
                            EditorBox.CaretPosition = currentBlock.ContentEnd;
                        }
                    }
                }
                
                _isInternalChange = false;
                EditorBox_TextChanged(this, null);
                EditorBox.Focus();
            }
        }
    }

    // ======================== THEME ========================

    public enum AppTheme { SystemDefault, Light, Dark }
    private AppTheme _currentTheme = AppTheme.SystemDefault;

    private void ThemeSystem_Click(object sender, RoutedEventArgs e) { _currentTheme = AppTheme.SystemDefault; ApplyTheme(); }
    private void ThemeLight_Click(object sender, RoutedEventArgs e) { _currentTheme = AppTheme.Light; ApplyTheme(); }
    private void ThemeDark_Click(object sender, RoutedEventArgs e) { _currentTheme = AppTheme.Dark; ApplyTheme(); }

    private void GlassOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FoldersBg != null && NotesListBg != null && EditorBg != null)
        {
            byte baseList = (byte)(_currentTheme == AppTheme.Light ? 255 : 0);
            NotesListBg.Background = new SolidColorBrush(
                Color.FromArgb((byte)(e.NewValue * 0.2 * 255), baseList, baseList, baseList));
            FoldersBg.Background = new SolidColorBrush(
                Color.FromArgb((byte)(e.NewValue * 0.4 * 255), 128, 128, 128));
        }
    }

    private void ApplyTheme()
    {
        bool isLight = true;
        if (_currentTheme == AppTheme.SystemDefault)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var val = key.GetValue("AppsUseLightTheme");
                if (val is int i) isLight = i == 1;
            }
        }
        else isLight = _currentTheme == AppTheme.Light;

        Brush fg = isLight ? Brushes.Black : Brushes.White;
        EditorBox.Foreground = fg;
        SearchBox.Foreground = fg;
        NotesList.Background = isLight
            ? new SolidColorBrush(Color.FromArgb(0, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
    }

    // ======================== LUMINANCE ========================

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;

    private Window CreateCustomDialog(string title, double width, double height)
    {
        var dialog = new Window
        {
            Title = title, Width = width, Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };
        ApplyAcrylicToWindow(dialog);
        
        var root = new System.Windows.Controls.Grid();
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(28) });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        
        var titleBar = new System.Windows.Controls.Grid { Background = Brushes.Transparent };
        titleBar.MouseLeftButtonDown += (s, e) => dialog.DragMove();
        
        var closeBtn = new System.Windows.Controls.Button 
        { 
            Content = "\xE8BB", 
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 10,
            Width = 28, Height = 28,
            ToolTip = "Close", 
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = EditorBox.Foreground
        };
        closeBtn.Click += (s, e) => dialog.Close();
        titleBar.Children.Add(closeBtn);
        
        var titleText = new System.Windows.Controls.TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = EditorBox.Foreground, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        titleBar.Children.Add(titleText);
        
        root.Children.Add(titleBar);
        System.Windows.Controls.Grid.SetRow(titleBar, 0);
        
        dialog.Content = root;
        return dialog;
    }

    private void ApplyAcrylicToWindow(Window window)
    {
        window.SourceInitialized += (s, ev) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            
            // Remove WS_EX_LAYERED if present
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_LAYERED) == WS_EX_LAYERED)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_LAYERED);
            }
            
            // Apply DWM Blur Behind
            var blur = new DWM_BLURBEHIND
            {
                dwFlags = DWM_BB_ENABLE,
                fEnable = true,
                hRgnBlur = IntPtr.Zero,
                fTransitionOnMaximized = true
            };
            DwmEnableBlurBehindWindow(hwnd, ref blur);

            // Extend glass frame into client area
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // Enable Acrylic (transient window) backdrop
            int backdropType = 3; // DWMSBT_TRANSIENTWINDOW (Acrylic)
            DwmSetWindowAttribute(hwnd, 38, ref backdropType, sizeof(int)); // 38 = DWMWA_SYSTEMBACKDROP_TYPE
            
            // Rounded corners
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));

            // Set window fully transparent to let the blur show through
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
        };
    }

    private void UpdateContrast()
    {
        try
        {
            var hdc = GetDC(IntPtr.Zero);
            Point pt = new(this.Left + this.Width / 2, this.Top + this.Height / 2);
            if (WindowState == WindowState.Maximized)
                pt = new(SystemParameters.PrimaryScreenWidth / 2, SystemParameters.PrimaryScreenHeight / 2);

            if (pt.X < -10000 || pt.Y < -10000 || double.IsNaN(pt.X) || double.IsNaN(pt.Y)) return;

            uint pixel = GetPixel(hdc, (int)pt.X, (int)pt.Y);
            ReleaseDC(IntPtr.Zero, hdc);

            byte r = (byte)(pixel & 0xFF);
            byte g = (byte)((pixel & 0xFF00) >> 8);
            byte b = (byte)((pixel & 0xFF0000) >> 16);
            double luminance = 0.2126 * r / 255.0 + 0.7152 * g / 255.0 + 0.0722 * b / 255.0;
            Brush newBrush = luminance > 0.5 ? Brushes.Black : Brushes.White;

            if (EditorBox.Foreground != newBrush)
            {
                EditorBox.Foreground = newBrush;
                SearchBox.Foreground = newBrush;
            }
        }
        catch { }
    }

    private void ApplySearchHighlights(string searchText)
    {
        if (string.IsNullOrEmpty(searchText) || searchText == "Search") return;

        bool alreadyInternal = _isInternalChange;
        _isInternalChange = true;
        try
        {
            ClearSearchHighlights();

            var matches = new List<TextRange>();
            TextPointer navigator = EditorBox.Document.ContentStart;

            while (navigator != null && navigator.CompareTo(EditorBox.Document.ContentEnd) < 0)
            {
                if (navigator.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string textRun = navigator.GetTextInRun(LogicalDirection.Forward);
                    int index = 0;
                    while ((index = textRun.IndexOf(searchText, index, StringComparison.OrdinalIgnoreCase)) != -1)
                    {
                        TextPointer start = navigator.GetPositionAtOffset(index);
                        TextPointer end = start.GetPositionAtOffset(searchText.Length);
                        matches.Add(new TextRange(start, end));
                        
                        index += searchText.Length;
                    }
                }
                navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
            }

            var highlightBrush = new SolidColorBrush(Color.FromArgb(120, 255, 75, 75)); // Soft red highlight
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                matches[i].ApplyPropertyValue(TextElement.BackgroundProperty, highlightBrush);
            }
        }
        catch { }
        finally
        {
            if (!alreadyInternal) _isInternalChange = false;
        }
    }

    private void ClearSearchHighlights()
    {
        bool alreadyInternal = _isInternalChange;
        _isInternalChange = true;
        try
        {
            foreach (var block in EditorBox.Document.Blocks)
            {
                ClearBlockBackgrounds(block);
            }
        }
        catch { }
        finally
        {
            if (!alreadyInternal) _isInternalChange = false;
        }
    }

    private void ClearBlockBackgrounds(Block block)
    {
        if (block == null) return;
        block.ClearValue(TextElement.BackgroundProperty);

        if (block is Paragraph p)
        {
            foreach (var inline in p.Inlines)
            {
                ClearInlineBackgrounds(inline);
            }
        }
        else if (block is List list)
        {
            foreach (var item in list.ListItems)
            {
                foreach (var blockItem in item.Blocks)
                {
                    ClearBlockBackgrounds(blockItem);
                }
            }
        }
        else if (block is Section sec)
        {
            foreach (var blockItem in sec.Blocks)
            {
                ClearBlockBackgrounds(blockItem);
            }
        }
    }

    private void ClearInlineBackgrounds(Inline inline)
    {
        if (inline == null) return;
        inline.ClearValue(TextElement.BackgroundProperty);

        if (inline is Span span)
        {
            foreach (var childInline in span.Inlines)
            {
                ClearInlineBackgrounds(childInline);
            }
        }
    }

    // ======================== INIT ========================

    private IntPtr _hwnd;
    private HwndSource? _hwndSource;

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);

        // Make WPF render transparently over the DWM glass surface
        if (_hwndSource?.CompositionTarget != null)
            _hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;

        // Hook into WndProc to re-apply DWM effects when composition changes or window re-activates
        _hwndSource?.AddHook(WndProc);

        // Apply the DWM effects
        ApplyDwmBackdrop();
    }

    private void ApplyDwmBackdrop()
    {
        if (_hwnd == IntPtr.Zero) return;

        // Extend DWM glass frame into the entire client area
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);

        // Set DWM Acrylic system backdrop (Windows 11)
        int backdropType = 3; // DWMSBT_TRANSIENTWINDOW (Acrylic)
        DwmSetWindowAttribute(_hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

        // Rounded corners (Windows 11)
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
    private const int WM_ACTIVATE = 0x0006;
    private const int WM_NCACTIVATE = 0x0086;
    private const int WM_ACTIVATEAPP = 0x001C;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_DWMCOMPOSITIONCHANGED:
                ApplyDwmBackdrop();
                break;

            case WM_ACTIVATE:
                if (wParam == IntPtr.Zero)
                {
                    // Ignore deactivation message to prevent DWM from turning the Acrylic backdrop solid
                    handled = true;
                    return IntPtr.Zero;
                }
                else
                {
                    ApplyDwmBackdrop();
                }
                break;

            case WM_ACTIVATEAPP:
                if (wParam == IntPtr.Zero)
                {
                    // Ignore application deactivation message to keep Acrylic active
                    handled = true;
                    return IntPtr.Zero;
                }
                break;

            case WM_NCACTIVATE:
                // Force the window to remain visually active so Acrylic backdrop doesn't fall back to solid color when inactive
                // Pass the original lParam instead of -1 to ensure correct painting flags are propagated to DefWindowProc
                handled = true;
                return DefWindowProc(hwnd, WM_NCACTIVATE, new IntPtr(1), lParam);
        }
        return IntPtr.Zero;
    }
}