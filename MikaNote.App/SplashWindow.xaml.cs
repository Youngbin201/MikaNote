using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MikaNote.App;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        LoadBrandImage();
    }

    private void LoadBrandImage()
    {
        string imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "MikaIcon.png");
        if (!File.Exists(imagePath))
        {
            return;
        }

        BrandImage.Source = new BitmapImage(new Uri(imagePath, UriKind.Absolute));
    }
}
