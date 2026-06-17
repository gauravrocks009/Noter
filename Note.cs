using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AppleNotesWpf;

public class Note : INotifyPropertyChanged
{
    private string _title = "New Note";
    private string _preview = "No additional text";
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public string Title 
    { 
        get => _title; 
        set { if (_title != value) { _title = value; OnPropertyChanged(); } } 
    }

    public string Preview 
    { 
        get => _preview; 
        set { if (_preview != value) { _preview = value; OnPropertyChanged(); } } 
    }

    public string Content { get; set; } = "";
    public string FolderName { get; set; } = "";
    public DateTime LastModified { get; set; } = DateTime.Now;

    public string DisplayDate => LastModified.Date == DateTime.Now.Date ? LastModified.ToString("t") : LastModified.ToString("d");

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
