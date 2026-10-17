using System;
using System.IO;
using System.Text;
using System.Windows;
using QuickLook.Common.Plugin;

namespace QuickLook.Plugin.AiViewer
{
    public class Plugin : IViewer
    {
        private AiPanel _panel;

        // Above the built-in PDFViewer (-1), which otherwise takes .ai files because they start with %PDF.
        public int Priority => 10;

        public void Init()
        {
        }

        public bool CanHandle(string path)
        {
            // Only the bundled x64 pdfium is shipped; anything else falls back to the stock viewers.
            if (!Environment.Is64BitProcess || Directory.Exists(path))
                return false;

            if (!path.EndsWith(".ai", StringComparison.OrdinalIgnoreCase) || !File.Exists(AiDocument.DllPath))
                return false;

            // Only PDF-compatible .ai files. Old PostScript ones (%!PS) go to other viewers.
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var head = new byte[1024];
                    var read = stream.Read(head, 0, head.Length);
                    return Encoding.ASCII.GetString(head, 0, read).Contains("%PDF");
                }
            }
            catch
            {
                return false;
            }
        }

        public void Prepare(string path, ContextObject context)
        {
            var size = new Size(1000, 750);
            try
            {
                using (var doc = new AiDocument(path))
                {
                    var page = doc.PageCount > 0 ? doc.GetPageSize(0) : Size.Empty;
                    if (!page.IsEmpty && page.Width > 0 && page.Height > 0)
                    {
                        // Small logos still get a big window: the long side becomes 1000, then fits the screen.
                        var scale = 1000 / Math.Max(page.Width, page.Height);
                        size = new Size(page.Width * scale + 48, page.Height * scale + 48);
                    }

                    context.Title = Path.GetFileName(path) +
                                    (doc.PageCount > 1 ? $"  ({doc.PageCount} artboards)" : "");
                }
            }
            catch
            {
                context.Title = Path.GetFileName(path);
            }

            context.SetPreferredSizeFit(size, 0.8);
        }

        public void View(string path, ContextObject context)
        {
            _panel = new AiPanel(path, context);
            context.ViewerContent = _panel;
            _panel.StartRendering(); // IsBusy goes false when the first artboard is shown
        }

        public void Cleanup()
        {
            _panel?.Dispose();
            _panel = null;
        }
    }
}
