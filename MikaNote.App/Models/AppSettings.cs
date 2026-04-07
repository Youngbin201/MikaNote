namespace MikaNote.App;

public sealed class AppSettings
{
    public bool KeepVisibleOnShowDesktop { get; set; } = true;
    public double NoteCornerRadius { get; set; } = 4;
    public double TitleFontSize { get; set; } = 12;
    public double ContentFontSize { get; set; } = 14;
    public double TitleLineSpacing { get; set; } = 1.2;
    public double ContentLineSpacing { get; set; } = 1.3;
    public string DefaultBackgroundColor { get; set; } = "#FFF4A0";
}
